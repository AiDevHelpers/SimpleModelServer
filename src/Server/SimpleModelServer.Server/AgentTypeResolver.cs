using SimpleModelServer.Config.Configuration;

using Microsoft.AspNetCore.Http;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server;

/// <summary>
///     Determines which agent type (code, debug, planning, research, ...) a given HTTP request
///     is targeting, by inspecting headers, query string, request body, and (as a last resort)
///     keyword matching against the prompt text. Pure request-inspection logic with no
///     dependency on Ollama, the router, or generation — testable in isolation.
/// </summary>
internal sealed class AgentTypeResolver(IAppConfig config)
{
    public string Resolve(HttpRequest request, JsonNode? body, string prompt, string currentAgentType)
    {
        // Internal pipeline calls (PromptAssessor, ConversationSummarization, etc.) set
        // X-Agent-Type: passthrough to suppress agent switching — their prompts contain
        // agent-type keywords (e.g. "Planning" in a categories list) that would otherwise
        // trigger a spurious reload on every internal model call.
        string? header = request.Headers["X-Agent-Type"];
        if (string.Equals(header, "passthrough", StringComparison.OrdinalIgnoreCase))
        {
            return currentAgentType;
        }

        foreach (string? candidate in AgentTypeCandidates(request, body, prompt))
        {
            string? normalized = NormalizeAgentType(candidate);
            if (normalized is not null && config.IsKnownAgentType(normalized))
            {
                return normalized;
            }
        }

        return config.DefaultAgentType.ToUpperInvariant();
    }

    private IEnumerable<string?> AgentTypeCandidates(HttpRequest request, JsonNode? body, string prompt)
    {
        yield return request.Headers["X-Agent-Type"];
        yield return request.Headers["X-CP-Chat-Agent"];
        yield return request.Headers["X-CP-Chat-Mode"];
        yield return request.Query["agent_type"];
        yield return request.Query["agent"];
        yield return request.Query["mode"];
        yield return GetJsonString(body, "agentType", "agent_type", "agent", "mode");
        yield return GetJsonString(body?["metadata"], "agentType", "agent_type", "agent", "mode");
        yield return AgentTypeFromModel(GetJsonString(body, "model"));
        yield return AgentTypeFromSystemMessages(body?["messages"] as JsonArray);
        yield return AgentTypeFromPrompt(prompt);
    }

    private string? AgentTypeFromModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        foreach (string agentType in KnownAgentTypes())
        {
            if (model.EndsWith($":{agentType}", StringComparison.OrdinalIgnoreCase) ||
                model.EndsWith($"-{agentType}", StringComparison.OrdinalIgnoreCase) ||
                model.EndsWith($"/{agentType}", StringComparison.OrdinalIgnoreCase))
            {
                return agentType;
            }
        }

        return null;
    }

    private string? AgentTypeFromSystemMessages(JsonArray? messages)
    {
        if (messages is null)
        {
            return null;
        }

        foreach (JsonNode? message in messages)
        {
            string role = message?["role"]?.GetValue<string>() ?? "";
            if (!string.Equals(role, "system", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "developer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? agentType = AgentTypeFromPrompt(SafeGetString(message?["content"]));
            if (agentType is not null)
            {
                return agentType;
            }
        }

        return null;
    }

    private static string? AgentTypeFromPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        string lower = prompt.ToUpperInvariant();
        // Research/search keywords — checked before others so "search for code X" routes to research.
        // The draft model then classifies WEB vs LOCAL before acting.
        return lower.StartsWith("SEARCH FOR ", StringComparison.Ordinal) || lower.StartsWith("SEARCH THE WEB", StringComparison.Ordinal) ||
            lower.StartsWith("SEARCH ", StringComparison.Ordinal) || lower.StartsWith("LOOK UP ", StringComparison.Ordinal) ||
            lower.StartsWith("FIND DOCS", StringComparison.Ordinal) || lower.StartsWith("FIND DOCUMENTATION", StringComparison.Ordinal) ||
            lower.StartsWith("FIND INFORMATION", StringComparison.Ordinal) || lower.StartsWith("RESEARCH ", StringComparison.Ordinal) ||
            lower.StartsWith("RESEARCH:", StringComparison.Ordinal) || ContainsAgentWord(lower, "RESEARCH")
            ? "research"
            : ContainsAgentWord(lower, "PLANNING") || ContainsAgentWord(lower, "PLANNER")
            ? "planning"
            : ContainsAgentWord(lower, "DEBUG") || ContainsAgentWord(lower, "DEBUGGING")
            ? "debug"
            : ContainsAgentWord(lower, "CODE") || ContainsAgentWord(lower, "CODING") ? "code" : null;
    }

    private IEnumerable<string> KnownAgentTypes()
    {
        foreach (string agentType in config.Agents.Keys)
        {
            yield return agentType.ToUpperInvariant();
        }

        yield return "RESEARCH";
        yield return "DEBUG";
        yield return "CODE";
        yield return "PLANNING";
    }

    private static string? GetJsonString(JsonNode? node, params string[] names)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        foreach (string name in names)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in obj)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return SafeGetString(pair.Value);
                }
            }
        }

        return null;
    }

    private static string SafeGetString(JsonNode? node)
    {
        if (node is null)
        {
            return "";
        }

        try
        {
            return node.GetValue<string>();
        }
        catch
        {
            return "";
        }
    }

    private static string? NormalizeAgentType(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private static bool ContainsAgentWord(string text, string word)
    {
        int index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            bool before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            int afterIndex = index + word.Length;
            bool after = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);
            if (before && after)
            {
                return true;
            }

            index = text.IndexOf(word, index + word.Length, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
