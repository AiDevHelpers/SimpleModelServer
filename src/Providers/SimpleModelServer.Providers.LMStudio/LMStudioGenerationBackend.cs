using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Providers.Common.Generation;

using System.Linq;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Providers.LMStudio;

/// <summary>
///     <see cref="IGenerationBackend" /> implementation talking LM Studio's OpenAI-compatible
///     <c>/v1/chat/completions</c> endpoint via <see cref="ILMStudioProvider" />. LM Studio has no
///     per-request equivalent of Ollama's <c>num_gpu</c>/<c>num_thread</c>/<c>keep_alive</c>/
///     <c>num_ctx</c> — those are ignored (LM Studio's own model-level settings control them).
/// </summary>
public sealed class LMStudioGenerationBackend(ILMStudioProvider provider) : IGenerationBackend
{
    public async Task<IGenerationHandle> StartStreamAsync(string model, string prompt, GenerationOptions options, CancellationToken ct = default)
    {
        // ResponseHeadersRead is load-bearing here — the default buffers the entire streamed
        // response before returning, which would silently turn every "stream" request blocking
        // (see ILMStudioProvider.PostChatCompletionAsync's completionOption doc).
        HttpResponseMessage response = await provider.PostChatCompletionAsync(
            BuildRequestBody(model, prompt, options, stream: true), ct, HttpCompletionOption.ResponseHeadersRead);
        _ = response.EnsureSuccessStatusCode();

        // CA2000 false positive: ownership of `reader` transfers into the LMStudioGenerationHandle
        // constructed below — its DisposeAsync() disposes both `reader` and `response` together.
#pragma warning disable CA2000
        StreamReader reader = new(await response.Content.ReadAsStreamAsync(ct));
#pragma warning restore CA2000
        return new LMStudioGenerationHandle(response, reader);
    }

    public async Task<string> GenerateAsync(string model, string prompt, GenerationOptions options, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await provider.PostChatCompletionAsync(BuildRequestBody(model, prompt, options, stream: false), ct);

        if (!response.IsSuccessStatusCode)
        {
            return "";
        }

        string body = await response.Content.ReadAsStringAsync(ct);
        JsonNode? json = JsonNode.Parse(body);
        return json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()?.Trim() ?? "";
    }

    public async Task<JsonArray?> GenerateEmbeddingsAsync(string model, JsonNode? input, CancellationToken ct = default)
    {
        JsonObject requestBody = new() { ["model"] = model, ["input"] = input?.DeepClone() };
        using HttpResponseMessage response = await provider.PostEmbeddingsAsync(requestBody, ct);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        JsonNode? json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        JsonArray? data = json?["data"]?.AsArray();
        if (data is null)
        {
            return null;
        }

        // The OpenAI embeddings response doesn't strictly guarantee ordering — each item carries
        // its own "index", so sort defensively rather than trusting array order.
        JsonArray embeddings = [];
        foreach (JsonNode? item in data.OrderBy(entry => entry?["index"]?.GetValue<int>() ?? 0))
        {
            embeddings.Add(item?["embedding"]?.DeepClone());
        }

        return embeddings;
    }

    private static JsonObject BuildRequestBody(string model, string prompt, GenerationOptions options, bool stream)
    {
        JsonArray messages = [];
        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = options.SystemPrompt });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = prompt });

        JsonObject requestBody = new()
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = stream
        };

        if (options.Temperature is not null)
        {
            requestBody["temperature"] = options.Temperature.Value;
        }

        if (options.Stop is { Count: > 0 })
        {
            requestBody["stop"] = new JsonArray([.. options.Stop.Select(s => (JsonNode)s)]);
        }

        if (options.ExtraSampling is not null)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in options.ExtraSampling)
            {
                requestBody[pair.Key] = pair.Value?.DeepClone();
            }
        }

        return requestBody;
    }
}
