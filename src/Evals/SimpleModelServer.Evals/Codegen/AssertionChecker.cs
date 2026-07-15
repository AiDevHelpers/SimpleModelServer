namespace SimpleModelServer.Evals.Codegen;

public sealed record AssertionOutcome(string Text, bool Required, bool Present) : IAssertionOutcome
{
    /// <summary>True when this assertion holds: a required substring is present, or a forbidden
    /// one is absent.</summary>
    public bool Passed => Required == Present;
}

public sealed record AssertionCheckResult(IReadOnlyList<AssertionOutcome> Outcomes) : IAssertionCheckResult
{
    public bool AllPassed => Outcomes.All(o => o.Passed);

    IReadOnlyList<IAssertionOutcome> IAssertionCheckResult.Outcomes => Outcomes;
}

/// <summary>
/// Case-sensitive substring checks for <c>must_contain</c>/<c>must_not_contain</c>, run against the
/// extracted code only (not surrounding prose) — per the harness spec, these assert spec compliance
/// the compiler can't check, e.g. "record Foo" vs "class Foo" both compile but only one is correct.
/// </summary>
public static class AssertionChecker
{
    public static AssertionCheckResult Check(string code, IReadOnlyList<string> mustContain, IReadOnlyList<string> mustNotContain)
    {
        List<AssertionOutcome> outcomes = [];
        outcomes.AddRange(mustContain.Select(s => new AssertionOutcome(s, Required: true, Present: code.Contains(s, StringComparison.Ordinal))));
        outcomes.AddRange(mustNotContain.Select(s => new AssertionOutcome(s, Required: false, Present: code.Contains(s, StringComparison.Ordinal))));
        return new AssertionCheckResult(outcomes);
    }
}
