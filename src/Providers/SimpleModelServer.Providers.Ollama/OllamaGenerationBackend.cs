using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Providers.Common.Generation;
using SimpleModelServer.Providers.Common.Streaming;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Providers.Ollama;

/// <summary>
///     <see cref="IGenerationBackend" /> implementation talking Ollama's native <c>/api/generate</c>
///     protocol — the exact request/response handling <see cref="SimpleModelServer.Server.SimpleModelPairGenerationService" />
///     used to do inline, now behind the backend-agnostic interface so it can be swapped for
///     <c>LMStudioGenerationBackend</c>. Token parsing is unchanged: still <see cref="IOllamaHelpers.ReadTokenAsync" />
///     over an <see cref="OllamaGenerationHandle" />.
/// </summary>
public sealed class OllamaGenerationBackend(IOllamaProcessManager process, HttpClient http, IOllamaHelpers ollamaHelpers) : IGenerationBackend
{
    public async Task<IGenerationHandle> StartStreamAsync(string model, string prompt, GenerationOptions options, CancellationToken ct = default)
    {
        using JsonContent generateContent = JsonContent.Create(BuildRequestBody(model, prompt, options, stream: true));
        using HttpRequestMessage request = new(HttpMethod.Post, $"http://127.0.0.1:{process.ServerPort}/api/generate")
        {
            Content = generateContent
        };

        // HttpCompletionOption.ResponseHeadersRead is load-bearing here: PostAsync's default
        // (ResponseContentRead) buffers the *entire* streamed response body before the call even
        // returns, silently turning every "stream" request into a blocking one.
        HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        _ = response.EnsureSuccessStatusCode();

        // CA2000 false positive: ownership of `reader` transfers into the OllamaGenerationHandle
        // constructed below — its DisposeAsync() disposes both `reader` and `response` together.
#pragma warning disable CA2000
        StreamReader reader = new(await response.Content.ReadAsStreamAsync(ct));
#pragma warning restore CA2000
        return new OllamaGenerationHandleAdapter(new OllamaGenerationHandle(response, reader), ollamaHelpers);
    }

    public async Task<string> GenerateAsync(string model, string prompt, GenerationOptions options, CancellationToken ct = default)
    {
        using JsonContent content = JsonContent.Create(BuildRequestBody(model, prompt, options, stream: false));
        using HttpResponseMessage response = await http.PostAsync(
            new Uri($"http://127.0.0.1:{process.ServerPort}/api/generate"), content, ct);

        if (!response.IsSuccessStatusCode)
        {
            return "";
        }

        string body = await response.Content.ReadAsStringAsync(ct);
        JsonNode? json = JsonNode.Parse(body);
        return json?["response"]?.GetValue<string>()?.Trim() ?? "";
    }

    public async Task<JsonArray?> GenerateEmbeddingsAsync(string model, JsonNode? input, CancellationToken ct = default)
    {
        JsonObject requestBody = new() { ["model"] = model, ["input"] = input?.DeepClone() };
        using JsonContent content = JsonContent.Create(requestBody);
        using HttpResponseMessage response = await http.PostAsync(
            new Uri($"http://127.0.0.1:{process.ServerPort}/api/embed"), content, ct);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        JsonNode? json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return json?["embeddings"]?.AsArray();
    }

    private static JsonObject BuildRequestBody(string model, string prompt, GenerationOptions options, bool stream)
    {
        JsonObject requestBody = new()
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = stream
        };

        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
        {
            requestBody["system"] = options.SystemPrompt;
        }

        if (options.KeepAlive is not null)
        {
            requestBody["keep_alive"] = options.KeepAlive;
        }

        JsonObject modelOptions = [];

        if (options.ContextSize is not null)
        {
            modelOptions["num_ctx"] = options.ContextSize.Value;
        }

        if (options.Temperature is not null)
        {
            modelOptions["temperature"] = options.Temperature.Value;
        }

        if (options.NumGpu is not null)
        {
            modelOptions["num_gpu"] = options.NumGpu.Value;
        }

        if (options.NumThread is not null)
        {
            modelOptions["num_thread"] = options.NumThread.Value;
        }

        if (options.ExtraSampling is not null)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in options.ExtraSampling)
            {
                modelOptions[pair.Key] = pair.Value?.DeepClone();
            }
        }

        if (options.Stop is { Count: > 0 })
        {
            modelOptions["stop"] = new JsonArray([.. options.Stop.Select(s => (JsonNode)s)]);
        }

        if (modelOptions.Count > 0)
        {
            requestBody["options"] = modelOptions;
        }

        return requestBody;
    }
}

/// <summary>Adapts the existing <see cref="OllamaGenerationHandle" /> to <see cref="IGenerationHandle" />.</summary>
internal sealed class OllamaGenerationHandleAdapter(IOllamaGenerationHandle inner, IOllamaHelpers ollamaHelpers) : IGenerationHandle
{
    public Task<TokenResult> ReadTokenAsync(TimeSpan timeout) => ollamaHelpers.ReadTokenAsync(inner.Reader, timeout);

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
