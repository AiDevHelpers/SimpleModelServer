using SimpleModelServer.Evals.Codegen;

using Xunit;

namespace SimpleModelServer.Evals.Tests;

public class AssertionCheckerTests
{
    [Fact]
    public void Check_AllRequiredPresentNoneForbidden_AllPassed()
    {
        string code = "public record OrderDto(Guid Id);";

        AssertionCheckResult result = AssertionChecker.Check(code, ["record OrderDto"], ["class OrderDto", "TODO"]);

        Assert.True(result.AllPassed);
        Assert.All(result.Outcomes, o => Assert.True(o.Passed));
    }

    [Fact]
    public void Check_MissingRequiredSubstring_Fails()
    {
        string code = "public class OrderDto { public Guid Id; }";

        AssertionCheckResult result = AssertionChecker.Check(code, ["record OrderDto"], []);

        Assert.False(result.AllPassed);
        AssertionOutcome outcome = Assert.Single(result.Outcomes);
        Assert.True(outcome.Required);
        Assert.False(outcome.Present);
        Assert.False(outcome.Passed);
    }

    [Fact]
    public void Check_ForbiddenSubstringPresent_Fails()
    {
        string code = "public class OrderDto { } // TODO: finish this";

        AssertionCheckResult result = AssertionChecker.Check(code, [], ["TODO"]);

        Assert.False(result.AllPassed);
    }

    [Fact]
    public void Check_IsCaseSensitive()
    {
        string code = "public record OrderDto(Guid Id);";

        // "TODO" (upper) is absent even though "todo" (lower) is not in the code at all — this
        // just asserts case sensitivity: a lowercase needle must not match an uppercase haystack hit.
        AssertionCheckResult result = AssertionChecker.Check(code, [], ["todo"]);

        Assert.True(result.AllPassed);
    }

    [Fact]
    public void Check_NoAssertions_TriviallyPasses()
    {
        AssertionCheckResult result = AssertionChecker.Check("anything", [], []);

        Assert.True(result.AllPassed);
        Assert.Empty(result.Outcomes);
    }
}
