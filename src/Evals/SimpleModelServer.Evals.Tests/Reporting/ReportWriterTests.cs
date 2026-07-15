using SimpleModelServer.Evals.Reporting;

using Xunit;

namespace SimpleModelServer.Evals.Tests;

public class ReportWriterTests
{
    [Fact]
    public void BuildBaseFileName_FollowsTimestampModeLabelConvention()
    {
        ReportMetadata meta = new(
            "codegen", "3b-base-pair", "abc1234",
            new DateTimeOffset(2026, 7, 7, 12, 34, 56, TimeSpan.Zero),
            null, "http://localhost:8080", null);

        string name = ReportWriter.BuildBaseFileName(meta);

        Assert.Equal("20260707-123456-codegen-3b-base-pair", name);
    }

    [Fact]
    public void Sanitize_ReplacesInvalidFileNameCharsAndSpaces()
    {
        string sanitized = ReportWriter.Sanitize("weird label/with:chars");

        Assert.DoesNotContain('/', sanitized);
        Assert.DoesNotContain(':', sanitized);
        Assert.DoesNotContain(' ', sanitized);
    }

    [Fact]
    public void ResolveAvailableBaseName_NoCollision_ReturnsPreferredName()
    {
        string dir = CreateTempDir();
        try
        {
            string result = ReportWriter.ResolveAvailableBaseName(dir, "20260707-000000-router-v1");
            Assert.Equal("20260707-000000-router-v1", result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveAvailableBaseName_JsonCollision_AppendsSuffix()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "20260707-000000-router-v1.json"), "{}");

            string result = ReportWriter.ResolveAvailableBaseName(dir, "20260707-000000-router-v1");

            Assert.Equal("20260707-000000-router-v1-2", result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_NeverOverwritesExistingReport()
    {
        string dir = CreateTempDir();
        try
        {
            ReportMetadata meta = new("router", "v1", "sha", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "http://x", null, null);

            ReportPaths first = ReportWriter.Write(dir, meta, "{\"run\":1}", "# run 1");
            ReportPaths second = ReportWriter.Write(dir, meta, "{\"run\":2}", "# run 2");

            Assert.NotEqual(first.JsonPath, second.JsonPath);
            Assert.Equal("{\"run\":1}", File.ReadAllText(first.JsonPath));
            Assert.Equal("{\"run\":2}", File.ReadAllText(second.JsonPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "eval-report-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
