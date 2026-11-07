using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Providers.Common.Utilities;

using System.Text;
using System.Text.Json.Nodes;

using Xunit;

namespace SimpleModelServer.Providers.Common.Tests;

/// <summary>
///     Covers OllamaHelpers: prompt construction from chat history (both the ChatMessage-list
///     overload and the raw JsonArray overload used for the OpenAI-shaped endpoints) and streaming
///     token-line parsing, including the timeout/malformed-line/stream-ended edge cases. Zero
///     coverage before this — these are pure functions with no I/O dependency beyond a StreamReader
///     for ReadTokenAsync, so a StringReader-backed stream is enough to exercise them fully.
/// </summary>
public sealed class OllamaHelpersTests
{
	[Fact]
	public void BuildPrompt_ChatMessageList_LabelsUserAndAssistantTurns()
	{
		var ollamaHelpers = new OllamaHelpers();
		List<ChatMessage> history =
		[
			new ChatMessage("user", "hi"),
			new ChatMessage("assistant", "hello"),
			new ChatMessage("User", "how are you") // case-insensitive role match
        ];

		string prompt = ollamaHelpers.BuildPrompt(history);

		Assert.Equal("User: hi\nAssistant: hello\nUser: how are you", prompt);
	}

	[Fact]
	public void BuildPrompt_ChatMessageList_Empty_ReturnsEmptyString()
	{
		var ollamaHelpers = new OllamaHelpers();
		List<ChatMessage> empty = [];
		Assert.Equal("", ollamaHelpers.BuildPrompt(empty));
	}

	[Fact]
	public void BuildPrompt_JsonArray_LabelsUserAndAssistantTurns()
	{
		var ollamaHelpers = new OllamaHelpers();
		JsonArray messages =
		[
			new JsonObject { ["role"] = "user", ["content"] = "hi" },
			new JsonObject { ["role"] = "assistant", ["content"] = "hello" }
		];

		string prompt = ollamaHelpers.BuildPrompt(messages);

		Assert.Equal("User: hi\r\nAssistant: hello\r\n", prompt);
	}

	[Fact]
	public void BuildPrompt_JsonArray_SystemRole_IsSkipped()
	{
		var ollamaHelpers = new OllamaHelpers();
		// Only "user" and "assistant" roles are rendered — "system"/"developer" messages are
		// handled elsewhere (AgentTypeResolver), not folded into the draft-pair prompt itself.
		JsonArray messages =
		[
			new JsonObject { ["role"] = "system", ["content"] = "You are helpful." },
			new JsonObject { ["role"] = "user", ["content"] = "hi" }
		];

		string prompt = ollamaHelpers.BuildPrompt(messages);

		Assert.Equal("User: hi\r\n", prompt);
	}

	[Fact]
	public void BuildPrompt_JsonArray_MissingRoleOrContent_TreatedAsEmpty()
	{
		var ollamaHelpers = new OllamaHelpers();
		JsonArray messages = [new JsonObject { ["content"] = "orphan content, no role" }];

		string prompt = ollamaHelpers.BuildPrompt(messages);

		// No role means neither branch matches, so nothing is appended.
		Assert.Equal("", prompt);
	}

	private static StreamReader ReaderFor(string content)
	{
		MemoryStream stream = new(Encoding.UTF8.GetBytes(content));
		return new StreamReader(stream);
	}

	[Fact]
	public async Task ReadTokenAsync_ValidLine_ReturnsTextAndDoneFlag()
	{
		var ollamaHelpers = new OllamaHelpers();
		using StreamReader reader = ReaderFor("""{"response":"hi","done":false}\n""".Replace("\\n", "\n"));

		TokenResult result = await ollamaHelpers.ReadTokenAsync(reader, TimeSpan.FromSeconds(5));

		Assert.Equal("hi", result.Text);
		Assert.False(result.Done);
	}

	[Fact]
	public async Task ReadTokenAsync_DoneTrue_IsReported()
	{
		var ollamaHelpers = new OllamaHelpers();
		using StreamReader reader = ReaderFor("""{"response":"","done":true}""" + "\n");

		TokenResult result = await ollamaHelpers.ReadTokenAsync(reader, TimeSpan.FromSeconds(5));

		Assert.True(result.Done);
	}

	[Fact]
	public async Task ReadTokenAsync_StreamEnded_ReportsDoneTrue()
	{
		var ollamaHelpers = new OllamaHelpers();
		using StreamReader reader = ReaderFor("");

		TokenResult result = await ollamaHelpers.ReadTokenAsync(reader, TimeSpan.FromSeconds(5));

		Assert.Equal("", result.Text);
		Assert.True(result.Done);
	}

	[Fact]
	public async Task ReadTokenAsync_BlankLine_NotDone()
	{
		var ollamaHelpers = new OllamaHelpers();
		// A blank (but non-null) line means "line ended without content, but stream is still
		// open" — distinct from a genuinely closed stream (ReadLineAsync returning null).
		using StreamReader reader = ReaderFor("\n" + """{"response":"after blank","done":false}""" + "\n");

		TokenResult first = await ollamaHelpers.ReadTokenAsync(reader, TimeSpan.FromSeconds(5));
		Assert.Equal("", first.Text);
		Assert.False(first.Done);

		TokenResult second = await ollamaHelpers.ReadTokenAsync(reader, TimeSpan.FromSeconds(5));
		Assert.Equal("after blank", second.Text);
	}

	[Fact]
	public async Task ReadTokenAsync_MalformedJson_ReportsDoneTrueRatherThanThrowing()
	{
		var ollamaHelpers = new OllamaHelpers();
		using StreamReader reader = ReaderFor("this is not json\n");

		TokenResult result = await ollamaHelpers.ReadTokenAsync(reader, TimeSpan.FromSeconds(5));

		Assert.Equal("", result.Text);
		Assert.True(result.Done);
	}

	/// <summary>A Stream whose ReadAsync never completes on its own — only cancellation unblocks it — used to simulate "the model server hasn't sent anything yet" for the timeout test below.</summary>
	private sealed class NeverRespondingStream : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException("Test double only supports the async path.");
		}

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			// Blocks until the caller gives up — mirrors a socket with nothing written to it yet.
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return 0;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}
	}

	[Fact]
	public async Task ReadTokenAsync_TimesOut_ReportsNotDone()
	{
		var ollamaHelpers = new OllamaHelpers();
		// ReadLineAsync over a stream that never produces data blocks forever, so
		// ReadTokenAsync's internal CancellationTokenSource must actually cancel the read once
		// the timeout elapses (NeverRespondingStream only unblocks via cancellation — see its
		// own doc comment) and return the "not done" placeholder, rather than hanging
		// indefinitely or leaving an orphaned read in progress on the reader.
		using StreamReader reader = new(new NeverRespondingStream());

		TokenResult result = await ollamaHelpers.ReadTokenAsync(reader, TimeSpan.FromMilliseconds(100));

		Assert.Equal("", result.Text);
		Assert.False(result.Done);
	}

	[Fact]
	public async Task ReadTokenAsync_ReadAfterATimeout_DoesNotThrow()
	{
		var ollamaHelpers = new OllamaHelpers();
		// Regression test for the real-world failure this reproduced: a poll loop calls
		// ReadTokenAsync repeatedly on the SAME reader. If a timed-out read isn't actually
		// cancelled (the original Task.WhenAny-based implementation raced it against
		// Task.Delay and abandoned the loser instead of cancelling it), the orphaned
		// ReadLineAsync stays "in progress" on the StreamReader, and the very next poll
		// iteration's fresh ReadLineAsync() call throws InvalidOperationException: "The stream
		// is currently in use by a previous operation on the stream." This is exactly what hit
		// SimpleModelPairGenerationService's draft/main polling loops in production.
		using StreamReader reader = new(new DelayedThenRespondingStream());

		TokenResult timedOut = await ollamaHelpers.ReadTokenAsync(reader, TimeSpan.FromMilliseconds(50));
		Assert.False(timedOut.Done);

		TokenResult afterTimeout = await ollamaHelpers.ReadTokenAsync(reader, TimeSpan.FromSeconds(5));
		Assert.Equal("after timeout", afterTimeout.Text);
	}

	/// <summary>
	///     A Stream whose first read blocks until cancelled (like <see cref="NeverRespondingStream" />)
	///     but whose second read returns real data immediately — simulates a poll loop's first
	///     iteration timing out before the model has produced anything, followed by a later
	///     iteration that successfully reads the next line on the same reader.
	/// </summary>
	private sealed class DelayedThenRespondingStream : Stream
	{
		private static readonly byte[] Payload = Encoding.UTF8.GetBytes("""{"response":"after timeout","done":false}""" + "\n");
		private int callCount;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException("Test double only supports the async path.");
		}

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			if (Interlocked.Increment(ref callCount) == 1)
			{
				await Task.Delay(Timeout.Infinite, cancellationToken);
				return 0; // unreachable — Task.Delay(Infinite, ct) only ever throws or hangs
			}

			int toCopy = Math.Min(count, Payload.Length);
			Payload.AsSpan(0, toCopy).CopyTo(buffer.AsSpan(offset, toCopy));
			return toCopy;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}
	}
}
