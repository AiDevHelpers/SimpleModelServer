using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Evals.Http;

/// <summary>
/// Thin wrapper over the OpenAI-compatible <c>/v1/chat/completions</c> endpoint llama-server
/// exposes. The harness does not manage server processes (per the eval-harness prompt) — it only
/// ever talks to whatever base URL it is given and assumes the server is already running.
/// </summary>
public sealed class LlamaChatClient : ILlamaChatClient
{
    private readonly HttpClient http;

    public LlamaChatClient(TimeSpan timeout)
    {
        http = new HttpClient { Timeout = timeout };
    }

    public async Task<IChatCompletionResult> CompleteAsync(
        string baseUrl,
        IReadOnlyList<IChatMessage> messages,
        double temperature,
        int maxTokens,
        JsonNode? jsonSchema = null,
        CancellationToken cancellationToken = default)
    {
        JsonObject body = new()
        {
            ["messages"] = new JsonArray([.. messages.Select(m => JsonSerializer.SerializeToNode(m))]),
            ["temperature"] = temperature,
            // llama-server's OpenAI-compatible route accepts both names; sending both keeps this
            // client working whether the server maps --n-predict style limits via max_tokens or
            // n_predict internally.
            ["max_tokens"] = maxTokens,
            ["n_predict"] = maxTokens,
            ["stream"] = false
        };

        if (jsonSchema is not null)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "router_response",
                    ["schema"] = jsonSchema.DeepClone(),
                    ["strict"] = false
                }
            };
        }

        Uri uri = new(new Uri(baseUrl), "/v1/chat/completions");
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            using JsonContent content = JsonContent.Create(body);
            using HttpResponseMessage response = await http.PostAsync(uri, content, cancellationToken);
            sw.Stop();

            string raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ChatCompletionResult("", null, null, null, sw.Elapsed.TotalMilliseconds,
                    Error: $"HTTP {(int)response.StatusCode}: {raw}");
            }

            using JsonDocument doc = JsonDocument.Parse(raw);
            JsonElement root = doc.RootElement;

            string text = "";
            string? finishReason = null;
            if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
            {
                JsonElement choice = choices[0];
                if (choice.TryGetProperty("message", out JsonElement message) &&
                    message.TryGetProperty("content", out JsonElement contentEl))
                {
                    text = contentEl.GetString() ?? "";
                }

                if (choice.TryGetProperty("finish_reason", out JsonElement finishEl))
                {
                    finishReason = finishEl.GetString();
                }
            }

            int? promptTokens = null;
            int? completionTokens = null;
            if (root.TryGetProperty("usage", out JsonElement usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out JsonElement pt))
                {
                    promptTokens = pt.GetInt32();
                }

                if (usage.TryGetProperty("completion_tokens", out JsonElement ct))
                {
                    completionTokens = ct.GetInt32();
                }
            }

            return new ChatCompletionResult(text, finishReason, promptTokens, completionTokens, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ChatCompletionResult("", null, null, null, sw.Elapsed.TotalMilliseconds, Error: "timeout");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ChatCompletionResult("", null, null, null, sw.Elapsed.TotalMilliseconds, Error: ex.Message);
        }
    }

    /// <summary>Best-effort fetch of <c>/v1/models</c> for the report's model-info field. Returns
    /// null rather than throwing if the server doesn't expose it or is unreachable.</summary>
    public async Task<string?> TryGetModelInfoAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            Uri uri = new(new Uri(baseUrl), "/v1/models");
            using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string raw = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.GetArrayLength() > 0 &&
                data[0].TryGetProperty("id", out JsonElement id))
            {
                return id.GetString();
            }

            return raw;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => http.Dispose();
}
