namespace SimpleModelServer.Evals.Reporting;

public sealed record ReportPaths(string JsonPath, string MdPath) : IReportPaths;

/// <summary>
/// Writes the machine (JSON) and human (Markdown) report pair for a run. Filenames are
/// <c>&lt;timestamp&gt;-&lt;mode&gt;-&lt;label&gt;.{json,md}</c> under <c>tests/golden/reports/</c>.
/// Existing reports are never overwritten — a run whose exact filename already exists (e.g. two
/// runs in the same second) gets a numeric suffix instead of clobbering history.
/// </summary>
public static class ReportWriter
{
	public static string BuildBaseFileName(ReportMetadata meta) =>
		$"{meta.TimestampUtc:yyyyMMdd-HHmmss}-{meta.Mode}-{Sanitize(meta.Label)}";

	public static string Sanitize(string label)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		char[] cleaned = [.. label.Select(c => invalid.Contains(c) || c == ' ' ? '-' : c)];
		return new string(cleaned);
	}

	/// <summary>Finds a base filename (no extension) under <paramref name="reportsDir"/> for which
	/// neither the .json nor the .md sibling already exists, appending "-2", "-3", ... as needed.</summary>
	public static string ResolveAvailableBaseName(string reportsDir, string preferredBaseName)
	{
		string candidate = preferredBaseName;
		int suffix = 2;
		while (File.Exists(Path.Combine(reportsDir, candidate + ".json")) ||
			   File.Exists(Path.Combine(reportsDir, candidate + ".md")))
		{
			candidate = $"{preferredBaseName}-{suffix}";
			suffix++;
		}

		return candidate;
	}

	public static ReportPaths Write(string reportsDir, ReportMetadata meta, string jsonContent, string markdownContent)
	{
		Directory.CreateDirectory(reportsDir);
		string baseName = ResolveAvailableBaseName(reportsDir, BuildBaseFileName(meta));

		string jsonPath = Path.Combine(reportsDir, baseName + ".json");
		string mdPath = Path.Combine(reportsDir, baseName + ".md");

		File.WriteAllText(jsonPath, jsonContent);
		File.WriteAllText(mdPath, markdownContent);

		return new ReportPaths(jsonPath, mdPath);
	}
}
