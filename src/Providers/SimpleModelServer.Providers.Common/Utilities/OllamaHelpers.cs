using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Contracts.Utilities;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Providers.Common.Utilities;

public sealed class OllamaHelpers : IOllamaHelpers
{
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
            try
            {
                // Must actually cancel the read on timeout, not just stop awaiting it (the
                // previous implementation raced ReadLineAsync() against Task.Delay via
                // Task.WhenAny and returned on timeout without touching the losing task).
                // StreamReader permits only one in-flight async read at a time; an abandoned-
                // but-still-running ReadLineAsync left the reader "in use" until it eventually
                // completed on its own (whenever the next line arrived, or never), so the very
                // next poll iteration's fresh ReadLineAsync() call collided with it and threw
                // InvalidOperationException: "The stream is currently in use by a previous
                // operation on the stream." A real CancellationToken properly unwinds the
                // pending read and clears that in-progress state instead of orphaning it.
                using CancellationTokenSource cts = new(timeout);
                line = await reader.ReadLineAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return new TokenResult("", false);
            }

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
            return new TokenResult("", true);
        }
    }
}
