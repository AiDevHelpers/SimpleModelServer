using SimpleModelServer.Providers.Common.Streaming;
using SimpleModelServer.Providers.Common.Utilities;

using System.Net;

using Xunit;

namespace SimpleModelServer.Providers.Common.Tests;

/// <summary>
///     Covers OllamaGenerationHandle.DisposeAsync — a small class, but its whole reason to exist
///     is bundling a StreamReader and its owning HttpResponseMessage so callers can't dispose one
///     without the other (a real past failure mode: disposing the response without the reader, or
///     vice versa, leaks the other). Zero coverage before this.
/// </summary>
public sealed class OllamaGenerationHandleTests
{
	[Fact]
	public async Task DisposeAsync_DisposesBothReaderAndResponse()
	{
		HttpResponseMessage response = new(HttpStatusCode.OK)
		{
			Content = new StringContent("hello")
		};
		StreamReader reader = new(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
		OllamaGenerationHandle handle = new(response, reader);

		await handle.DisposeAsync();

		// A disposed StreamReader throws ObjectDisposedException on further use — the cleanest
		// externally-observable proof that Dispose actually ran on it.
		_ = await Assert.ThrowsAsync<ObjectDisposedException>(() => reader.ReadLineAsync(TestContext.Current.CancellationToken).AsTask());

		// HttpResponseMessage.Dispose() disposes its Content; a disposed StringContent throws
		// ObjectDisposedException from ReadAsStreamAsync, which is the analogous proof for the
		// response half of the pair.
		_ = await Assert.ThrowsAsync<ObjectDisposedException>(() => response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public void Reader_Property_ExposesTheSameInstancePassedIn()
	{
		HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StringContent("x") };
		StreamReader reader = new(Stream.Null);
		OllamaGenerationHandle handle = new(response, reader);

		Assert.Same(reader, handle.Reader);
	}
}
