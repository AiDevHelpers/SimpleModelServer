namespace SimpleModelServer.Server;

/// <summary>
///     Performs web research via Jina AI's free reader endpoints — no API key required.
///     s.jina.ai/{query} returns search results as clean markdown.
///     r.jina.ai/{url}   converts a page to clean readable text.
/// </summary>
internal sealed class WebResearchService
{
    private static readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    private static readonly string[] QueryPrefixes =
    [
        "search the web for ",
        "search the web ",
        "search for ",
        "search ",
        "look up ",
        "find docs on ",
        "find docs for ",
        "find documentation on ",
        "find documentation for ",
        "find information about ",
        "find information on ",
        "research ",
        "research: "
    ];

    // Static initializer — set headers once on the shared client
    static WebResearchService()
    {
        http.DefaultRequestHeaders.Add("Accept", "text/plain");
        http.DefaultRequestHeaders.Add("User-Agent", "DraftModel/1.0 (local dev assistant)");
        // Ask Jina to return clean text instead of markdown with images
        http.DefaultRequestHeaders.Add("X-Return-Format", "text");
    }

    public static async Task<WebResearchResult> ResearchAsync(string prompt)
    {
        string query = ExtractQuery(prompt);

        try
        {
            string encoded = Uri.EscapeDataString(query);
            Uri url = new($"https://s.jina.ai/{encoded}");
            HttpResponseMessage response = await http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return WebResearchResult.Failure(query,
                    $"search returned HTTP {(int)response.StatusCode}");
            }

            string content = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(content))
            {
                return WebResearchResult.Failure(query, "no results returned");
            }

            // Cap at ~6 000 chars — enough for 2–3 solid results without overwhelming context
            if (content.Length > 6_000)
            {
                content = content[..6_000] + "\n\n[results truncated]";
            }

            return WebResearchResult.Success(query, content);
        }
        catch (TaskCanceledException)
        {
            return WebResearchResult.Failure(query, "search timed out after 25s");
        }
        catch (HttpRequestException ex)
        {
            return WebResearchResult.Failure(query, $"network error: {ex.Message}");
        }
        catch (Exception ex) when (ex is UriFormatException or IOException or ObjectDisposedException)
        {
            // Scoped to the failure modes actually reachable from this method's body:
            // Uri.EscapeDataString/the Uri constructor (UriFormatException on a pathological
            // query), response.Content.ReadAsStringAsync (IOException on a stream fault), and
            // the shared static HttpClient (ObjectDisposedException, effectively unreachable
            // since it's never disposed, but cheap to guard). This previously caught
            // Exception broadly, which would have also silently swallowed bugs (e.g. an
            // IndexOutOfRangeException from the content-length truncation above) as if they
            // were routine search failures.
            return WebResearchResult.Failure(query, ex.Message);
        }
    }

    public static string ExtractQuery(string prompt)
    {
        string trimmed = prompt.Trim();

        foreach (string prefix in QueryPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }

        return trimmed;
    }
}

internal sealed record WebResearchResult(
    bool Found,
    string Query,
    string? Content,
    string? ErrorMessage)
{
    public static WebResearchResult Success(string query, string content)
    {
        return new WebResearchResult(true, query, content, null);
    }

    public static WebResearchResult Failure(string query, string error)
    {
        return new WebResearchResult(false, query, null, error);
    }
}
