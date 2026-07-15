using SimpleModelServer.Common.Infrastructure;
using SimpleModelServer.Common.Interfaces;
using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Providers.Common.Streaming;

using System.Net.Http.Json;
using System.Text.Json.Nodes;

internal sealed class ChatApp(IAppConfig config, IOllamaHelpers ollamaHelpers, IConsoleHelper consoleHelper) : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private bool disposed;

    private string DraftModel => !string.IsNullOrWhiteSpace(config.Global.DraftModel)
        ? config.Global.DraftModel
        : config.ModelRoles.TryGetValue("draft", out string? d)
            ? d
            : "qwen2.5-coder-0.5b-chat";

    private string MainModel => !string.IsNullOrWhiteSpace(config.Global.MainModel)
        ? config.Global.MainModel
        : config.ModelRoles.TryGetValue("main", out string? m)
            ? m
            : "qwen2.5-coder-3b-chat";

    private int RouterPort => config.Global.RouterPort;

    public async Task<int> RunAsync()
    {
        Console.Clear();
        consoleHelper.WriteLine("=== SIMPLE MODEL SERVER CHAT ===", ConsoleColor.Cyan);

        if (!await IsOllamaReadyAsync())
        {
            consoleHelper.WriteLine($"ERROR: Simple Model Server router is not running on port {RouterPort}.", ConsoleColor.Red);
            consoleHelper.WriteLine("Start SimpleModelServer.Loader (--service or --router) first, then retry.",
                ConsoleColor.DarkYellow);
            return 1;
        }

        string agentType = !string.IsNullOrWhiteSpace(config.Global.AgentType)
            ? config.Global.AgentType
            : config.DefaultAgentType;

        await RunInteractiveAsync(agentType);
        return 0;
    }

    private async Task RunInteractiveAsync(string agentType)
    {
        consoleHelper.WriteLine($"Agent: {agentType}  |  Type 'exit' to quit, 'agent <type>' to switch.",
            ConsoleColor.DarkCyan);

        List<ChatMessage> history = [];
        string currentAgent = agentType;

        while (true)
        {
            Console.Write("\nYou: ");
            string? user = Console.ReadLine();

            if (string.Equals(user, "exit", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(user))
            {
                continue;
            }

            if (user.StartsWith("agent ", StringComparison.OrdinalIgnoreCase))
            {
                string requested = user[6..].Trim();
                if (config.IsKnownAgentType(requested))
                {
                    currentAgent = requested;
                    consoleHelper.WriteLine($"Switched to agent: {currentAgent}", ConsoleColor.DarkCyan);
                }
                else
                {
                    consoleHelper.WriteLine($"Unknown agent type '{requested}'.", ConsoleColor.DarkYellow);
                }

                continue;
            }

            history.Add(new ChatMessage("user", user));
            consoleHelper.WriteLine("\nAssistant:", ConsoleColor.Cyan);
            string response = await GetResponseAsync(history, currentAgent);
            history.Add(new ChatMessage("assistant", response));
        }
    }

    private async Task<string> GetResponseAsync(IReadOnlyList<ChatMessage> history, string agentType)
    {
        string prompt = ollamaHelpers.BuildPrompt(history);
        consoleHelper.WriteLine("\nGenerating...", ConsoleColor.DarkCyan);

        IAgentConfig agentConfig = config.Agent(agentType);
        Uri generateUri = new($"http://localhost:{RouterPort}/api/generate");

        using JsonContent draftReq = JsonContent.Create(BuildRequest(DraftModel, prompt, agentConfig));
        HttpResponseMessage draftResp = await http.PostAsync(generateUri, draftReq);
        _ = draftResp.EnsureSuccessStatusCode();

        using JsonContent mainReq = JsonContent.Create(BuildRequest(MainModel, prompt, agentConfig));
        HttpResponseMessage mainResp = await http.PostAsync(generateUri, mainReq);
        _ = mainResp.EnsureSuccessStatusCode();

        await using OllamaGenerationHandle draftStream =
            new(draftResp, new StreamReader(await draftResp.Content.ReadAsStreamAsync()));
        await using OllamaGenerationHandle mainStream =
            new(mainResp, new StreamReader(await mainResp.Content.ReadAsStreamAsync()));

        string draftText = "";
        string mainText = "";
        bool mainStarted = false;
        bool draftDone = false;
        bool mainDone = false;
        int draftLastLen = 0;
        int mainLastLen = 0;

        while (!draftDone || !mainDone)
        {
            if (!draftDone)
            {
                TokenResult token = await ollamaHelpers.ReadTokenAsync(draftStream.Reader, TimeSpan.FromMilliseconds(30));
                draftDone = token.Done;
                if (!string.IsNullOrEmpty(token.Text) && !mainStarted)
                {
                    draftText += token.Text;
                    draftLastLen = Math.Max(draftLastLen, draftText.Length);
                    consoleHelper.WriteOverwrite(draftText, draftLastLen);
                }
            }

            if (!mainDone)
            {
                TokenResult token = await ollamaHelpers.ReadTokenAsync(mainStream.Reader, TimeSpan.FromMilliseconds(30));
                mainDone = token.Done;
                if (!string.IsNullOrEmpty(token.Text))
                {
                    if (!mainStarted)
                    {
                        // Handover from draft to main: clear whatever the draft left on this
                        // line (including any leftover characters longer than the first main
                        // token) before main output starts, so the two never visually overlap.
                        consoleHelper.WriteOverwrite("", draftLastLen);
                        mainStarted = true;
                    }

                    mainText += token.Text;
                    mainLastLen = Math.Max(mainLastLen, mainText.Length);
                    consoleHelper.WriteOverwrite(mainText, mainLastLen);
                }
            }

            if (!draftDone || !mainDone)
            {
                await Task.Delay(10);
            }
        }

        Console.WriteLine();
        return string.IsNullOrWhiteSpace(mainText) && !string.IsNullOrWhiteSpace(draftText) ? draftText : mainText;
    }

    private JsonObject BuildRequest(string model, string prompt, IAgentConfig agentConfig)
    {
        JsonObject body = new()
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = true
        };

        if (!string.IsNullOrWhiteSpace(agentConfig.SystemPrompt))
        {
            body["system"] = agentConfig.SystemPrompt;
        }

        JsonObject modelOptions = [];
        if (config.Global.ContextSize is not null)
        {
            modelOptions["num_ctx"] = config.Global.ContextSize.Value;
        }

        if (agentConfig.Temperature is not null)
        {
            modelOptions["temperature"] = agentConfig.Temperature.Value;
        }

        if (agentConfig.Sampling is JsonObject sampling)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in sampling)
            {
                modelOptions[pair.Key] = pair.Value?.DeepClone();
            }
        }
        else if (agentConfig.Sampling is JsonValue samplingValue && samplingValue.TryGetValue<double>(out double topP))
        {
            modelOptions["top_p"] = topP;
        }

        if (config.ModelSources.TryGetValue(model, out IModelSourceConfig? src) && src.Stop.Count > 0)
        {
            JsonArray arr = [.. src.Stop];

            modelOptions["stop"] = arr;
        }

        if (modelOptions.Count > 0)
        {
            body["options"] = modelOptions;
        }

        return body;
    }

    private async Task<bool> IsOllamaReadyAsync()
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri($"http://localhost:{RouterPort}/api/tags"));
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        http.Dispose();
        disposed = true;
    }
}
