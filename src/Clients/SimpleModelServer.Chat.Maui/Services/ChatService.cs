using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SimpleModelServer.Discovery;

namespace SimpleModelServer.Chat.Maui.Services;

/// <summary>
/// Service for sending chat messages to the SimpleModelServer and streaming responses.
/// Handles connection resolution, authentication, and streaming response parsing.
/// </summary>
public class ChatService
{
    private readonly IServerLocator _locator;
    private string? _cachedModelName;

    // TODO: Wire dual-model (draft+main handoff) support when config infrastructure is available.
    // For now, we ask the server for its available models via /api/tags and use the first one.

    public ChatService(IServerLocator locator)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
    }

    /// <summary>
    /// Asynchronously sends a chat message and streams the response.
    /// The response text is delivered incrementally via the onToken callback.
    /// </summary>
    /// <param name="history">The chat message history.</param>
    /// <param name="prompt">The user's message prompt.</param>
    /// <param name="onToken">Callback invoked for each token received. Called on the main thread.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public async Task SendAsync(
        IReadOnlyList<ChatMessage> history,
        string prompt,
        Action<string> onToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(onToken);

        try
        {
            // Resolve the server connection
            var connection = await _locator.ResolveAsync(cancellationToken);

            // Create an authenticated HTTP client
            using var httpClient = AuthenticatedHttpClientFactory.Create(connection);

            // Build the prompt from history
            var builtPrompt = BuildPrompt(history, prompt);

            // Resolve the model to use from the server's own /api/tags listing
            var modelName = await ResolveModelNameAsync(httpClient, cancellationToken);

            // Build the generate request
            var requestBody = BuildGenerateRequest(modelName, builtPrompt);

            // Post the request to the server's /api/generate endpoint
            var generateUri = new Uri(connection.BaseUrl).AppendPath("/api/generate");
            using var jsonContent = JsonContent.Create(requestBody);
            using var response = await httpClient.PostAsync(generateUri, jsonContent, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Stream the NDJSON response
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await ProcessStreamAsync(responseStream, onToken, cancellationToken);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            // Deliver error as a token so the UI can display it
            MainThread.BeginInvokeOnMainThread(() =>
                onToken($"\n[Error: {ex.Message}]")
            );
        }
    }

    /// <summary>
    /// Asks the server which models it has available (Ollama-compatible <c>/api/tags</c>) and
    /// caches the first one for reuse — the router picks the actual draft/main pairing
    /// server-side, so any listed model name is a valid <c>/api/generate</c> target.
    /// </summary>
    private async Task<string> ResolveModelNameAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (_cachedModelName is not null)
        {
            return _cachedModelName;
        }

        try
        {
            var tagsResponse = await httpClient.GetFromJsonAsync<JsonObject>("/api/tags", cancellationToken);
            var firstModel = tagsResponse?["models"]?.AsArray()
                .Select(m => m?["name"]?.GetValue<string>())
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

            if (!string.IsNullOrWhiteSpace(firstModel))
            {
                return _cachedModelName = firstModel;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Fall through to the placeholder — the request below will surface a clearer
            // error from the server than failing the tags lookup would.
        }

        return _cachedModelName = "default";
    }

    /// <summary>
    /// Builds a prompt from the chat history and the new user message.
    /// This is a simplified implementation that concatenates history in a basic format.
    /// </summary>
    private static string BuildPrompt(IReadOnlyList<ChatMessage> history, string userMessage)
    {
        if (history.Count == 0)
        {
            return userMessage;
        }

        var sb = new System.Text.StringBuilder();

        // Add conversation history
        foreach (var msg in history)
        {
            if (msg.Role == ChatMessageRole.User)
            {
                sb.AppendLine($"User: {msg.Text}");
            }
            else
            {
                sb.AppendLine($"Assistant: {msg.Text}");
            }
        }

        // Add the new user message
        sb.AppendLine($"User: {userMessage}");
        sb.Append("Assistant: ");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a generate request body for the Ollama-compatible /api/generate endpoint.
    /// </summary>
    private static JsonObject BuildGenerateRequest(string model, string prompt)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = true
        };

        return body;
    }

    /// <summary>
    /// Processes the streaming NDJSON response from the server.
    /// Each line is a JSON object with "response" and "done" fields.
    /// Invokes onToken for each token, ensuring calls are on the main thread.
    /// </summary>
    private static async Task ProcessStreamAsync(
        Stream responseStream,
        Action<string> onToken,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(responseStream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                // Parse the JSON line
                var jsonNode = JsonNode.Parse(line);
                if (jsonNode is not JsonObject obj)
                {
                    continue;
                }

                // Extract the response token
                if (obj["response"] is JsonValue responseValue)
                {
                    var token = responseValue.GetValue<string>();
                    if (!string.IsNullOrEmpty(token))
                    {
                        // Invoke the callback on the main thread
                        MainThread.BeginInvokeOnMainThread(() => onToken(token));
                    }
                }

                // Check if streaming is done
                if (obj["done"]?.GetValue<bool>() == true)
                {
                    break;
                }
            }
            catch (JsonException)
            {
                // Skip malformed JSON lines
                continue;
            }
        }
    }
}
