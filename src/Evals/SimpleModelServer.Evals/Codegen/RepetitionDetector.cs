namespace SimpleModelServer.Evals.Codegen;

/// <summary>
/// Flags the degenerate-repetition failure mode Phase 0 hit on the interface-impl task (the model
/// looped emitting the same using-directive dozens of times until the token budget ran out instead
/// of producing usable code). Only meaningful when combined with <c>finish_reason == "length"</c> —
/// legitimately repeated short lines (e.g. several one-line properties) are common and not a
/// failure on their own.
/// </summary>
public static class RepetitionDetector
{
    private const int RepeatThreshold = 6;
    private const int MinLineLength = 4;

    public static bool LooksLikeRepetitionLoop(string rawResponse, string? finishReason)
    {
        if (!string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return false;
        }

        Dictionary<string, int> counts = [];
        foreach (string rawLine in rawResponse.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length < MinLineLength)
            {
                continue;
            }

            counts[line] = counts.GetValueOrDefault(line) + 1;
            if (counts[line] >= RepeatThreshold)
            {
                return true;
            }
        }

        return false;
    }
}
