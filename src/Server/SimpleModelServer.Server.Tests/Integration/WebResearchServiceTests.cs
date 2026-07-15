using SimpleModelServer.Server;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers WebResearchService.ExtractQuery — stripping the trigger phrase
///     (AgentTypeResolver.AgentTypeFromPrompt's "research"-classified prefixes) off a prompt to
///     get the actual search query. Pure string logic, zero coverage before this.
///     WebResearchService.ResearchAsync itself calls the real Jina AI endpoint over the network,
///     so it's deliberately not exercised here — that's an integration concern, not a unit one.
/// </summary>
public sealed class WebResearchServiceTests
{
    [Theory]
    [InlineData("search the web for cats", "cats")]
    [InlineData("search the web cats", "cats")]
    [InlineData("search for cats", "cats")]
    [InlineData("search cats", "cats")]
    [InlineData("look up cats", "cats")]
    [InlineData("find docs on cats", "cats")]
    [InlineData("find docs for cats", "cats")]
    [InlineData("find documentation on cats", "cats")]
    [InlineData("find documentation for cats", "cats")]
    [InlineData("find information about cats", "cats")]
    [InlineData("find information on cats", "cats")]
    [InlineData("research cats", "cats")]
    [InlineData("research: cats", "cats")]
    public void ExtractQuery_StripsKnownPrefixes(string prompt, string expected)
    {
        Assert.Equal(expected, WebResearchService.ExtractQuery(prompt));
    }

    [Fact]
    public void ExtractQuery_PrefixMatching_IsCaseInsensitive()
    {
        Assert.Equal("Cats", WebResearchService.ExtractQuery("SEARCH FOR Cats"));
    }

    [Fact]
    public void ExtractQuery_NoKnownPrefix_ReturnsTrimmedPromptUnchanged()
    {
        Assert.Equal("just a plain prompt", WebResearchService.ExtractQuery("  just a plain prompt  "));
    }

    [Fact]
    public void ExtractQuery_TrimsWhitespaceAroundQuery()
    {
        Assert.Equal("cats", WebResearchService.ExtractQuery("search for   cats   "));
    }

    [Fact]
    public void ExtractQuery_OnlyFirstMatchingPrefix_IsStripped()
    {
        // "search for search for cats" should only strip the first "search for ", leaving the
        // second occurrence as part of the query — not stripped repeatedly.
        Assert.Equal("search for cats", WebResearchService.ExtractQuery("search for search for cats"));
    }

    [Fact]
    public void ExtractQuery_EmptyPrompt_ReturnsEmptyString()
    {
        Assert.Equal("", WebResearchService.ExtractQuery(""));
    }
}
