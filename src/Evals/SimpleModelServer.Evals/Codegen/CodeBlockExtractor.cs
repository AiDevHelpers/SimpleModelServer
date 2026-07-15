using System.Text;
using System.Text.RegularExpressions;

namespace SimpleModelServer.Evals.Codegen;

/// <summary>
/// Pulls C# source out of a model's raw chat response. Fenced code blocks are preferred (models
/// are asked for code and normally wrap it in ```csharp fences even when they add surrounding
/// prose); if none are present, falls back to a brace-balanced-region heuristic over the whole
/// response so a compile attempt can still be made against un-fenced output.
/// </summary>
public static partial class CodeBlockExtractor
{
    [GeneratedRegex(@"```(?:[a-zA-Z0-9_+-]*)\r?\n(?<body>.*?)```", RegexOptions.Singleline)]
    private static partial Regex FencedBlockRegex();

    public static string Extract(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return "";
        }

        MatchCollection matches = FencedBlockRegex().Matches(rawResponse);
        if (matches.Count > 0)
        {
            IEnumerable<string> bodies = matches.Select(m => m.Groups["body"].Value.TrimEnd());
            return string.Join("\n\n", bodies).Trim();
        }

        return LargestBraceBalancedRegion(rawResponse);
    }

    /// <summary>
    /// Scans for the longest substring that is brace-balanced (every <c>{</c> closed by a matching
    /// <c>}</c> within the substring, with no point where depth goes negative). Returns the empty
    /// string if the text never opens a brace.
    /// </summary>
    internal static string LargestBraceBalancedRegion(string text)
    {
        int depth = 0;
        int regionStart = -1;
        int bestStart = -1;
        int bestLength = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '{')
            {
                if (depth == 0)
                {
                    regionStart = i;
                }

                depth++;
            }
            else if (ch == '}')
            {
                if (depth > 0)
                {
                    depth--;
                    if (depth == 0 && regionStart >= 0)
                    {
                        int length = i - regionStart + 1;
                        if (length > bestLength)
                        {
                            bestStart = regionStart;
                            bestLength = length;
                        }
                    }
                }
            }
        }

        if (bestStart < 0)
        {
            return "";
        }

        // Include the declaration line(s) above the opening brace (e.g. "public class Foo") by
        // walking back line-by-line while each line above is blank up to the brace — Allman-style
        // formatting puts the brace alone on its own line, so a single line-start lookup would stop
        // there and miss the actual "public class Foo" declaration one line further up.
        int cursor = bestStart;
        while (true)
        {
            int newlineIndex = text.LastIndexOf('\n', Math.Max(0, cursor - 1));
            int lineStart = newlineIndex + 1;
            if (newlineIndex > 0 && string.IsNullOrWhiteSpace(text[lineStart..cursor]))
            {
                cursor = newlineIndex;
                continue;
            }

            cursor = lineStart;
            break;
        }

        return text[cursor..(bestStart + bestLength)].Trim();
    }
}
