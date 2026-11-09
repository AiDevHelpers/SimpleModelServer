using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Contracts.Utilities;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Providers.Common.Utilities;

public sealed class OllamaHelpers : IOllamaHelpers
{
    private readonly Action<Exception>? _onStreamFault;

    // The read that is still in flight for each stream, carried across polls (see ReadTokenAsync).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StreamReader, Task<string?>> PendingReads = new();

    /// <param name="onStreamFault">Called when a token read ends the stream early on an I/O or parse fault.</param>
    public OllamaHelpers(Action<Exception>? onStreamFault = null)
    {
        _onStreamFault = onStreamFault;
    }

    public string BuildPrompt(IReadOnlyList<ChatMessage> history)
    {
        return string.Join('\n', history.Select(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
                ? $"User: {m.Content}"
                : $"Assistant: {m.Content}"));
    }

    public string BuildPrompt(JsonArray messages)
    {
        StringBuilder builder = new();
        foreach (JsonNode? message in messages)
        {
            string role = message?["role"]?.GetValue<string>() ?? "";
            string content = message?["content"]?.GetValue<string>() ?? "";
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
            {
                _ = builder.AppendLine(CultureInfo.InvariantCulture, $"User: {content}");
            }
            else if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                _ = builder.AppendLine(CultureInfo.InvariantCulture, $"Assistant: {content}");
            }
        }

        return builder.ToString();
    }

    public async Task<TokenResult> ReadTokenAsync(StreamReader reader, TimeSpan timeout)
    {
        try
        {
            string? line;
            // Keep one read in flight per reader and wait on it for up to the poll timeout. Cancelling
            // the read on timeout (the old approach) makes the HTTP connection drop its response stream,
            // so the next poll failed with ObjectDisposedException and the reply was cut short. Only one
            // read is ever issued per reader, so StreamReader's "in use" restriction is not hit.
            Task<string?> pending = PendingReads.GetValue(reader, static r => r.ReadLineAsync());
            Task finished = await Task.WhenAny(pending, Task.Delay(timeout));
            if (!ReferenceEquals(finished, pending))
            {
                return new TokenResult("", false);
            }

            _ = PendingReads.Remove(reader);
            line = await pending;

            if (string.IsNullOrWhiteSpace(line))
            {
                return new TokenResult("", line is null);
            }

            JsonNode? json = JsonNode.Parse(line);
            string text = json?["response"]?.GetValue<string>() ?? "";
            bool done = json?["done"]?.GetValue<bool>() ?? false;
            return new TokenResult(text, done);
        }
        catch (Exception ex) when (ex is JsonException or IOException or ObjectDisposedException)
        {
            // Malformed line from the model server, or the underlying stream faulted/closed
            // mid-read. Both are indistinguishable from "the stream ended" to TokenResult's
            // current (Text, Done) shape, so we still report Done=true to stop the polling
            // loop — but unlike a bare catch, this only swallows genuinely expected failure
            // modes. Anything else (e.g. an OutOfMemoryException, a bug in caller-supplied
            // code) propagates instead of silently masquerading as a clean stream end.
            // The fault is reported so an early end-of-stream is not silent (the caller can
            // otherwise fall back to draft text with no trace of the failure).
            _onStreamFault?.Invoke(ex);
            return new TokenResult("", true);
        }
    }
}
