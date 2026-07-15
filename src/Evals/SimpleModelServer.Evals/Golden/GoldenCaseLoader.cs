using System.Text.Json;

using SimpleModelServer.Evals.Models;

namespace SimpleModelServer.Evals.Golden;

/// <summary>
/// Loads golden test cases from a directory of <c>*.json</c> files following the schema documented
/// in <c>tests/golden/README.md</c>. Non-case files (README.md, the stubs/ and reports/
/// subdirectories) are skipped.
/// </summary>
public static class GoldenCaseLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<GoldenCase> Load(string casesDir, string? filterPrefix = null)
    {
        if (!Directory.Exists(casesDir))
        {
            throw new DirectoryNotFoundException($"Golden case directory not found: {casesDir}");
        }

        string stubsDir = Path.Combine(casesDir, "stubs");

        List<GoldenCase> cases = [];
        foreach (string file in Directory.EnumerateFiles(casesDir, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            string json = File.ReadAllText(file);
            GoldenCase? parsed = JsonSerializer.Deserialize<GoldenCase>(json, SerializerOptions);
            if (parsed is null)
            {
                throw new InvalidDataException($"Failed to parse golden case: {file}");
            }

            if (!string.IsNullOrEmpty(filterPrefix) &&
                !parsed.Id.StartsWith(filterPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string stubPath = Path.Combine(stubsDir, $"{parsed.Id}.cs");
            GoldenCase withStub = File.Exists(stubPath)
                ? parsed with { StubFilePath = stubPath }
                : parsed;

            cases.Add(withStub);
        }

        return cases;
    }
}
