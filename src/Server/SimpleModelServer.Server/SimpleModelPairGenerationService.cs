using SimpleModelServer.Common.Infrastructure;
using SimpleModelServer.Common.Interfaces;
using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Hardware;
using SimpleModelServer.Providers.Common.Generation;

using Microsoft.Extensions.Logging;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server;

/// <summary>
///     Owns dual-stream ("draft-pair") generation: starting a draft and a main model generation
///     in parallel, merging their token streams, building per-request generation options, and
///     resolving research (web vs local) queries. This is the "business logic" of what a request
///     actually produces — separate from how it arrived (HTTP routing), which backend actually
///     generates the tokens (<see cref="IGenerationBackend" /> — Ollama or LM Studio), or whether
///     the backend is currently healthy (process/health management).
/// </summary>
internal sealed partial class SimpleModelPairGenerationService(
    IAppConfig config,
    ILogger logger,
    IGenerationBackend backend,
    Func<ResolvedHardwareSettings> hardwareSettings,
    Func<string, int?> gpuLayersForModel,
    Func<string, Task> ensureAgentLoaded,
    IOllamaHelpers ollamaHelpers,
    IConsoleHelper consoleHelper)
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Research classifier: {Classification} (raw: \"{RawText}\")")]
    private static partial void LogResearchClassification(ILogger logger, string classification, string rawText);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Research classifier failed ({Message}), defaulting to WEB.")]
    private static partial void LogResearchClassifierFailed(ILogger logger, Exception ex, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Research: results found for: {Query}")]
    private static partial void LogResearchResultsFound(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "\nDraft generating (handoff preview)...")]
    private static partial void LogDraftGenerating(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "\nMain generating...")]
    private static partial void LogMainGenerating(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Draft prompt-cleanup call failed or timed out — using the original prompt.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "\nResearch: classifying query...")]
    private static partial void LogResearchClassifying(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Research: local codebase search — delegating to caller.")]
    private static partial void LogResearchLocal(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Research: web search via Jina...")]
    private static partial void LogResearchWebSearch(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Research: Jina failed ({ErrorMessage}) — asking caller to search.")]
    private static partial void LogResearchJinaFailed(ILogger logger, string errorMessage);

    private static readonly TimeSpan TokenPollTimeout = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan DraftPairPollYield = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan StreamPollYield = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan ResearchClassifyTimeout = TimeSpan.FromSeconds(10);

    public string DraftModelName => !string.IsNullOrWhiteSpace(config.Global.DraftModel)
        ? config.Global.DraftModel
        : config.ModelRoles.TryGetValue("draft", out string? d)
            ? d
            : "qwen2.5-coder-0.5b-chat";

    public string MainModelName => !string.IsNullOrWhiteSpace(config.Global.MainModel)
        ? config.Global.MainModel
        : config.ModelRoles.TryGetValue("main", out string? m)
            ? m
            : "qwen2.5-coder-3b-chat";

    public string EmbedModelName => !string.IsNullOrWhiteSpace(config.Global.EmbedModel)
        ? config.Global.EmbedModel
        : config.ModelRoles.TryGetValue("embed", out string? e)
            ? e
            : "nomic-embed-text";

    /// <summary>
    ///     Generates embeddings via whichever backend is configured (Ollama's <c>/api/embed</c> or
    ///     LM Studio's <c>/v1/embeddings</c>) — see <see cref="IGenerationBackend.GenerateEmbeddingsAsync" />.
    /// </summary>
    public Task<JsonArray?> GetEmbeddingsAsync(string model, JsonNode? input, CancellationToken ct = default)
        => backend.GenerateEmbeddingsAsync(model, input, ct);

    private int? EffectiveContextSize => config.Global.ContextSize;

    /// <summary>
    ///     How long the draft (0.5b) model gets to stream before <see cref="PrepareMainPromptAsync" />
    ///     hands off to main (3b), whatever the draft has produced by then. See
    ///     <see cref="GlobalConfig.DraftHandoffTimeoutMs" />.
    /// </summary>
    private TimeSpan DraftHandoffTimeout => TimeSpan.FromMilliseconds(config.Global.DraftHandoffTimeoutMs);

    private static readonly TimeSpan MaxDraftDrainTime = TimeSpan.FromSeconds(30);

    public async Task<string> GetDraftPairResponseAsync(IReadOnlyList<ChatMessage> history, string agentType, CancellationToken ct = default)
    {
        string prompt = ollamaHelpers.BuildPrompt(history);
        LogDraftGenerating(logger);

        (string mainPrompt, string fallbackText, Task draftCleanupTask) =
            await PrepareMainPromptAsync(prompt, agentType, ct);

        LogMainGenerating(logger);
        await using IGenerationHandle main = await StartStreamAsync(MainModelName, mainPrompt, agentType, ct);

        string mainText = "";
        bool mainDone = false;
        int lastLineLength = 0;

        while (!mainDone)
        {
            ct.ThrowIfCancellationRequested();

            TokenResult token = await main.ReadTokenAsync(TokenPollTimeout);
            mainDone = token.Done;
            if (!string.IsNullOrEmpty(token.Text))
            {
                mainText += token.Text;
                lastLineLength = Math.Max(lastLineLength, mainText.Length);
                consoleHelper.WriteOverwrite(mainText, lastLineLength);
            }

            if (!mainDone)
            {
                await Task.Delay(DraftPairPollYield, ct);
            }
        }

        Console.WriteLine();
        await draftCleanupTask;
        return ChooseFinalText(fallbackText, mainText);
    }

    public async Task StreamDraftPairAsync(string prompt, string agentType, Func<string, Task> onToken, CancellationToken ct = default)
    {
        (string mainPrompt, _, Task draftCleanupTask) = await PrepareMainPromptAsync(prompt, agentType, ct);

        await using IGenerationHandle main = await StartStreamAsync(MainModelName, mainPrompt, agentType, ct);

        bool mainDone = false;
        while (!mainDone)
        {
            ct.ThrowIfCancellationRequested();

            TokenResult token = await main.ReadTokenAsync(TokenPollTimeout);
            mainDone = token.Done;
            if (!string.IsNullOrEmpty(token.Text))
            {
                await onToken(token.Text);
            }

            if (!mainDone)
            {
                await Task.Delay(StreamPollYield, ct);
            }
        }

        await draftCleanupTask;
    }

    // ── Draft → main handoff (DraftMode.Preview / Cleanup / Both) ───────────────────────────

    /// <summary>
    ///     Builds the prompt main (3b) actually generates against, and the text to fall back to if
    ///     main comes back empty — see <see cref="DraftMode" /> for what the draft model
    ///     contributes in each mode. <c>DraftCleanupTask</c> is a background task draining and
    ///     disposing any draft stream that was still running at handoff time; callers should await
    ///     it after they're done with main so its resources get cleaned up, but it never blocks
    ///     main from starting.
    /// </summary>
    private Task<(string MainPrompt, string FallbackText, Task DraftCleanupTask)> PrepareMainPromptAsync(
        string prompt, string agentType, CancellationToken ct)
    {
        return config.Global.DraftMode switch
        {
            DraftMode.Cleanup => PrepareCleanupOnlyAsync(prompt, agentType, ct),
            DraftMode.Both => PrepareCleanupThenPreviewAsync(prompt, agentType, ct),
            _ => PreparePreviewAsync(prompt, agentType, ct)
        };
    }

    private async Task<(string, string, Task)> PreparePreviewAsync(string prompt, string agentType, CancellationToken ct)
    {
        (string draftText, Task cleanupTask) = await StreamDraftWithHandoffAsync(prompt, agentType, ct);
        return (BuildPreviewHandoffPrompt(prompt, draftText), draftText, cleanupTask);
    }

    private async Task<(string, string, Task)> PrepareCleanupOnlyAsync(string prompt, string agentType, CancellationToken ct)
    {
        string cleaned = await RunCleanupRewriteAsync(prompt, agentType, ct);
        string mainPrompt = string.IsNullOrWhiteSpace(cleaned) ? prompt : cleaned;
        // No draft *answer* was ever produced in this mode, so there's nothing meaningful to
        // fall back to if main comes back empty — an empty main response stays empty
        // (ChooseFinalText("", "") => "").
        return (mainPrompt, "", Task.CompletedTask);
    }

    private async Task<(string, string, Task)> PrepareCleanupThenPreviewAsync(string prompt, string agentType, CancellationToken ct)
    {
        string cleaned = await RunCleanupRewriteAsync(prompt, agentType, ct);
        string basePrompt = string.IsNullOrWhiteSpace(cleaned) ? prompt : cleaned;
        (string draftText, Task cleanupTask) = await StreamDraftWithHandoffAsync(basePrompt, agentType, ct);
        return (BuildPreviewHandoffPrompt(basePrompt, draftText), draftText, cleanupTask);
    }

    /// <summary>
    ///     Streams the draft model against <paramref name="prompt" /> for up to
    ///     <see cref="DraftHandoffTimeout" />, then returns whatever text has accumulated by then
    ///     (full text if the draft finished first) plus a background task that drains/disposes the
    ///     draft stream handle if it was still running at cutoff.
    /// </summary>
    private async Task<(string DraftText, Task CleanupTask)> StreamDraftWithHandoffAsync(
        string prompt, string agentType, CancellationToken ct)
    {
        IGenerationHandle draft = await StartStreamAsync(DraftModelName, prompt, agentType, ct);
        string draftText = "";
        bool draftDone = false;
        int lastLineLength = 0;
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        while (!draftDone && sw.Elapsed < DraftHandoffTimeout)
        {
            ct.ThrowIfCancellationRequested();

            TokenResult token = await draft.ReadTokenAsync(TokenPollTimeout);
            draftDone = token.Done;
            if (!string.IsNullOrEmpty(token.Text))
            {
                draftText += token.Text;
                lastLineLength = Math.Max(lastLineLength, draftText.Length);
                consoleHelper.WriteOverwrite(draftText, lastLineLength);
            }

            if (!draftDone && sw.Elapsed < DraftHandoffTimeout)
            {
                await Task.Delay(DraftPairPollYield, ct);
            }
        }

        Console.WriteLine();
        Task cleanupTask = draftDone ? draft.DisposeAsync().AsTask() : DrainAndDisposeDraftAsync(draft);
        return (draftText, cleanupTask);
    }

    /// <summary>
    ///     Best-effort background cleanup for a draft stream that was still running at handoff
    ///     time: drains remaining tokens (so the fake/real HTTP response body gets fully read
    ///     rather than abandoned mid-stream) and disposes the handle. Bounded by
    ///     <see cref="MaxDraftDrainTime" /> so a draft that never sends "done" can't leak this task
    ///     forever; failures here are swallowed since main has already taken over and this is pure
    ///     cleanup, not something callers can act on.
    /// </summary>
    private async Task DrainAndDisposeDraftAsync(IGenerationHandle draft)
    {
        await using IGenerationHandle handle = draft;
        try
        {
            using CancellationTokenSource cts = new(MaxDraftDrainTime);
            bool done = false;
            while (!done && !cts.IsCancellationRequested)
            {
                TokenResult token = await handle.ReadTokenAsync(TokenPollTimeout);
                done = token.Done;
            }
        }
        catch
        {
            // Best-effort; nothing left to propagate this to.
        }
    }

    private static string BuildPreviewHandoffPrompt(string originalPrompt, string draftText)
    {
        if (string.IsNullOrWhiteSpace(draftText))
        {
            return originalPrompt;
        }

        return $"""
                {originalPrompt}

                [Draft answer from a faster preview model — verify, correct, and complete it accurately. Do not repeat it if it's wrong or incomplete.]
                {draftText}
                """;
    }

    private static string BuildCleanupInstructionPrompt(string originalPrompt)
    {
        return $"""
                Rewrite the following request to be clear, complete, and unambiguous for another AI to act on. Fix typos and grammar, expand shorthand, and make any implicit ask explicit. Output ONLY the rewritten request — no preamble, no explanation, no quotes.

                Request: {originalPrompt}

                Rewritten request:
                """;
    }

    /// <summary>
    ///     Single non-streaming draft-model call used by <see cref="DraftMode.Cleanup" /> and
    ///     <see cref="DraftMode.Both" /> to rewrite the user's prompt before main sees it. Bounded
    ///     by <see cref="DraftHandoffTimeout" /> like the preview-streaming path; on any failure or
    ///     timeout, returns "" and callers fall back to the original prompt unchanged.
    /// </summary>
    private async Task<string> RunCleanupRewriteAsync(string originalPrompt, string agentType, CancellationToken ct)
    {
        await ensureAgentLoaded(agentType);

        try
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DraftHandoffTimeout);
            return await backend.GenerateAsync(
                DraftModelName, BuildCleanupInstructionPrompt(originalPrompt), GenerationOptions.None, cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or JsonException)
        {
            LogCleanupFailed(logger, ex);
            return "";
        }
    }

    private async Task<IGenerationHandle> StartStreamAsync(string model, string prompt, string agentType, CancellationToken ct = default)
    {
        await ensureAgentLoaded(agentType);
        IAgentConfig agentConfig = config.Agent(agentType);
        GenerationOptions options = BuildGenerationOptions(agentConfig, model);
        return await backend.StartStreamAsync(model, prompt, options, ct);
    }

    private GenerationOptions BuildGenerationOptions(IAgentConfig agentConfig, string model)
    {
        ResolvedHardwareSettings hw = hardwareSettings();

        int? contextSize = EffectiveContextSize ??
            (config.ModelSources.TryGetValue(model, out IModelSourceConfig? ctxSrc) ? ctxSrc.NumCtx : null);

        // Prefer the per-model VRAM-aware plan (GpuBudgetPlanner) over the single global
        // gpuLayers setting — the global value assumes one model has the whole GPU to itself,
        // which isn't true once draft/main/embed all need to stay resident together.
        int? numGpu = gpuLayersForModel(model) ?? hw.GpuLayers;

        JsonObject? extraSampling = null;
        if (agentConfig.Sampling is JsonObject sampling)
        {
            extraSampling = [];
            foreach (KeyValuePair<string, JsonNode?> pair in sampling)
            {
                extraSampling[pair.Key] = pair.Value?.DeepClone();
            }
        }
        else if (agentConfig.Sampling is JsonValue samplingValue && samplingValue.TryGetValue<double>(out double topP))
        {
            extraSampling = new JsonObject { ["top_p"] = topP };
        }

        IReadOnlyList<string>? stop =
            config.ModelSources.TryGetValue(model, out IModelSourceConfig? modelSrc) && modelSrc.Stop.Count > 0
                ? modelSrc.Stop
                : null;

        return new GenerationOptions
        {
            ContextSize = contextSize,
            Temperature = agentConfig.Temperature,
            NumGpu = numGpu,
            NumThread = hw.CpuThreads,
            Stop = stop,
            SystemPrompt = agentConfig.SystemPrompt,
            ExtraSampling = extraSampling
        };
    }

    private static string ChooseFinalText(string draftText, string mainText)
    {
        return string.IsNullOrWhiteSpace(mainText) && !string.IsNullOrWhiteSpace(draftText)
            ? draftText
            : mainText;
    }

    // ── Research routing ────────────────────────────────────────────────────────

    /// <summary>
    ///     Uses the draft model to decide whether a research query needs the web
    ///     or the local codebase. Calls Ollama directly (bypassing the router) to
    ///     avoid a circular request loop. Defaults to WEB on any failure.
    /// </summary>
    public async Task<bool> IsWebSearchAsync(string query, CancellationToken ct = default)
    {
        string classifyPrompt =
            $"""
             Classify this search query. Does it need the web (external docs, tutorials, APIs, tools, news) or the local codebase (files, classes, functions, variables, project-specific code)?
             Reply with exactly one word: WEB or LOCAL.

             Query: {query}

             Classification:
             """;

        try
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ResearchClassifyTimeout);
            string raw = await backend.GenerateAsync(DraftModelName, classifyPrompt, GenerationOptions.None, cts.Token);

            if (string.IsNullOrEmpty(raw))
            {
                return true; // backend call failed — default to web, matching the pre-abstraction !IsSuccessStatusCode path
            }

            string text = raw.Trim().ToUpperInvariant();
            bool isLocal = text.StartsWith("LOCAL", StringComparison.Ordinal);
            LogResearchClassification(logger, isLocal ? "LOCAL" : "WEB", text);
            return !isLocal;
        }
        catch (Exception ex)
        {
            LogResearchClassifierFailed(logger, ex, ex.Message);
            return true; // default to web on any failure
        }
    }

    private static string BuildResearchPrompt(WebResearchResult result)
    {
        return $"""
                Using the following web research results, answer the question clearly and concisely.

                ### Web Research: {result.Query}
                {result.Content}

                ### Question
                {result.Query}
                """;
    }

    private static string BuildResearchFallback(WebResearchResult result)
    {
        return $"I was unable to search the web for \"{result.Query}\" ({result.ErrorMessage}). " +
               "Please search for this information and share the results — I can then help analyze and summarize them.";
    }

    private static string BuildLocalSearchMessage(string query)
    {
        return $"This looks like a local codebase search. Please search your workspace for: {query}\n\n" +
               "Use grep, file search, or your available code tools to locate the relevant files and symbols, " +
               "then share what you find.";
    }

    /// <summary>
    ///     Classifies <paramref name="prompt" /> as a web or local-codebase research query, runs
    ///     the appropriate search, and returns the resulting response text — the part of the
    ///     research path shared identically by both the Ollama-native and OpenAI-compatible
    ///     handlers.
    ///     <paramref name="onWebResultsFound" /> is an optional hook invoked right after a
    ///     successful web search, before the (potentially slow) draft-pair call that turns the
    ///     enriched prompt into response text. Ollama's handler uses this to stream the
    ///     draft-pair response directly instead of waiting for the full text, by returning
    ///     <c>true</c> to signal it already wrote the response itself — in that case this method
    ///     returns an empty string and the caller must not write anything further. Returning
    ///     <c>false</c> (or passing no hook) proceeds with the normal non-streaming text-returning
    ///     path. This indirection exists specifically to avoid calling the classify/search steps
    ///     twice for the streaming case.
    /// </summary>
    public async Task<string> ResolveResearchResponseAsync(
        string prompt,
        Func<WebResearchResult, Task<bool>>? onWebResultsFound = null,
        CancellationToken ct = default)
    {
        LogResearchClassifying(logger);
        bool isWeb = await IsWebSearchAsync(prompt, ct);

        if (!isWeb)
        {
            LogResearchLocal(logger);
            return BuildLocalSearchMessage(WebResearchService.ExtractQuery(prompt));
        }

        LogResearchWebSearch(logger);
        WebResearchResult result = await WebResearchService.ResearchAsync(prompt);
        if (!result.Found)
        {
            LogResearchJinaFailed(logger, result.ErrorMessage);
            return BuildResearchFallback(result);
        }

        LogResearchResultsFound(logger, result.Query);

        if (onWebResultsFound is not null && await onWebResultsFound(result))
        {
            // Caller already streamed/wrote the response itself.
            return "";
        }

        string enriched = BuildResearchPrompt(result);
        return await GetDraftPairResponseAsync([new ChatMessage("user", enriched)], "research", ct);
    }

    /// <summary>Exposes <see cref="BuildResearchPrompt" /> to the router for its streaming fast-path.</summary>
    public static string BuildResearchPromptFor(WebResearchResult result)
    {
        return BuildResearchPrompt(result);
    }
}


