using SimpleModelServer.Evals.Cli;
using Xunit;

namespace SimpleModelServer.Evals.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IEvalOptions"/> interface.
/// Validates evaluation configuration properties and their values.
/// </summary>
public class EvalOptionsTests
{
    private class MockEvalOptions : IEvalOptions
    {
        public EvalMode Mode { get; init; }
        public string? RouterUrl { get; init; }
        public string? GenUrl { get; init; }
        public string Label { get; init; } = "";
        public string CasesDir { get; init; } = "";
        public string? Filter { get; init; }
        public int NPredict { get; init; }
        public double Temperature { get; init; }
        public int TimeoutSeconds { get; init; }
        public string ReportsDir { get; init; } = "";
    }

    /// <summary>
    /// Contract verification: IEvalOptions has all required members.
    /// </summary>
    [Fact]
    public void IEvalOptions_Interface_HasAllRequiredMembers()
    {
        // Arrange
        IEvalOptions options = new MockEvalOptions();

        // Act & Assert - All properties must be accessible
        _ = options.Mode;
        _ = options.RouterUrl;
        _ = options.GenUrl;
        _ = options.Label;
        _ = options.CasesDir;
        _ = options.Filter;
        _ = options.NPredict;
        _ = options.Temperature;
        _ = options.TimeoutSeconds;
        _ = options.ReportsDir;
    }

    [Fact]
    public void EvalOptions_Default_HasValidDefaults()
    {
        // Arrange & Act
        var options = new MockEvalOptions();

        // Assert
        Assert.Equal("", options.Label);
        Assert.Equal("", options.CasesDir);
        Assert.Null(options.Filter);
        Assert.Equal("", options.ReportsDir);
    }

    [Theory]
    [InlineData(EvalMode.Codegen)]
    [InlineData(EvalMode.Router)]
    public void Mode_CanBeSet(EvalMode mode)
    {
        // Arrange & Act
        var options = new MockEvalOptions { Mode = mode };

        // Assert
        Assert.Equal(mode, options.Mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:11435")]
    [InlineData("http://192.168.1.1:11435")]
    [InlineData("https://remote-server.com:11435")]
    public void RouterUrl_CanBeSet(string? url)
    {
        // Arrange & Act
        var options = new MockEvalOptions { RouterUrl = url };

        // Assert
        Assert.Equal(url, options.RouterUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:8000")]
    [InlineData("http://ollama-server:11434")]
    [InlineData("https://api.example.com")]
    public void GenUrl_CanBeSet(string? url)
    {
        // Arrange & Act
        var options = new MockEvalOptions { GenUrl = url };

        // Assert
        Assert.Equal(url, options.GenUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("test-label")]
    [InlineData("eval-run-1")]
    [InlineData("2026-07-14-codegen-v1")]
    public void Label_CanBeSet(string label)
    {
        // Arrange & Act
        var options = new MockEvalOptions { Label = label };

        // Assert
        Assert.Equal(label, options.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("./cases")]
    [InlineData("/var/eval/cases")]
    [InlineData("C:\\eval\\cases")]
    public void CasesDir_CanBeSet(string dir)
    {
        // Arrange & Act
        var options = new MockEvalOptions { CasesDir = dir };

        // Assert
        Assert.Equal(dir, options.CasesDir);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("router.*")]
    [InlineData("codegen-.*-3b")]
    public void Filter_CanBeSet(string? filter)
    {
        // Arrange & Act
        var options = new MockEvalOptions { Filter = filter };

        // Assert
        Assert.Equal(filter, options.Filter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void NPredict_CanBeSet(int value)
    {
        // Arrange & Act
        var options = new MockEvalOptions { NPredict = value };

        // Assert
        Assert.Equal(value, options.NPredict);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    public void Temperature_CanBeSet(double value)
    {
        // Arrange & Act
        var options = new MockEvalOptions { Temperature = value };

        // Assert
        Assert.Equal(value, options.Temperature);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(300)]
    public void TimeoutSeconds_CanBeSet(int value)
    {
        // Arrange & Act
        var options = new MockEvalOptions { TimeoutSeconds = value };

        // Assert
        Assert.Equal(value, options.TimeoutSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("./reports")]
    [InlineData("/var/eval/reports")]
    [InlineData("C:\\eval\\reports")]
    public void ReportsDir_CanBeSet(string dir)
    {
        // Arrange & Act
        var options = new MockEvalOptions { ReportsDir = dir };

        // Assert
        Assert.Equal(dir, options.ReportsDir);
    }

    [Fact]
    public void EvalOptions_CodegenMode_WithAllProperties()
    {
        // Arrange & Act
        var options = new MockEvalOptions
        {
            Mode = EvalMode.Codegen,
            RouterUrl = "http://localhost:11435",
            GenUrl = "http://localhost:11434",
            Label = "codegen-test-1",
            CasesDir = "./golden",
            Filter = ".*3b.*",
            NPredict = 256,
            Temperature = 0.7,
            TimeoutSeconds = 60,
            ReportsDir = "./reports"
        };

        // Assert
        Assert.Equal(EvalMode.Codegen, options.Mode);
        Assert.Equal("http://localhost:11435", options.RouterUrl);
        Assert.Equal("http://localhost:11434", options.GenUrl);
        Assert.Equal("codegen-test-1", options.Label);
        Assert.Equal("./golden", options.CasesDir);
        Assert.Equal(".*3b.*", options.Filter);
        Assert.Equal(256, options.NPredict);
        Assert.Equal(0.7, options.Temperature);
        Assert.Equal(60, options.TimeoutSeconds);
        Assert.Equal("./reports", options.ReportsDir);
    }

    [Fact]
    public void EvalOptions_RouterMode_WithAllProperties()
    {
        // Arrange & Act
        var options = new MockEvalOptions
        {
            Mode = EvalMode.Router,
            RouterUrl = "http://router-server:11435",
            GenUrl = null,
            Label = "router-eval",
            CasesDir = "./test-cases",
            Filter = null,
            NPredict = 100,
            Temperature = 0.5,
            TimeoutSeconds = 30,
            ReportsDir = "./eval-reports"
        };

        // Assert
        Assert.Equal(EvalMode.Router, options.Mode);
        Assert.Equal("http://router-server:11435", options.RouterUrl);
        Assert.Null(options.GenUrl);
        Assert.Equal("router-eval", options.Label);
    }

    [Fact]
    public void EvalOptions_InterfaceContract()
    {
        // Arrange
        IEvalOptions options = new MockEvalOptions
        {
            Mode = EvalMode.Codegen,
            RouterUrl = "http://localhost:11435",
            GenUrl = "http://localhost:11434",
            Label = "test-label",
            CasesDir = "./cases",
            Filter = "test-filter",
            NPredict = 128,
            Temperature = 0.7,
            TimeoutSeconds = 60,
            ReportsDir = "./reports"
        };

        // Act & Assert
        Assert.Equal(EvalMode.Codegen, options.Mode);
        Assert.Equal("http://localhost:11435", options.RouterUrl);
        Assert.Equal("http://localhost:11434", options.GenUrl);
        Assert.Equal("test-label", options.Label);
    }

    [Theory]
    [InlineData(EvalMode.Codegen, "http://localhost:11435", "http://localhost:11434")]
    [InlineData(EvalMode.Router, "http://localhost:11435", null)]
    public void EvalOptions_VariousModes(
        EvalMode mode,
        string? routerUrl,
        string? genUrl)
    {
        // Arrange & Act
        var options = new MockEvalOptions
        {
            Mode = mode,
            RouterUrl = routerUrl,
            GenUrl = genUrl
        };

        // Assert
        Assert.Equal(mode, options.Mode);
        Assert.Equal(routerUrl, options.RouterUrl);
        Assert.Equal(genUrl, options.GenUrl);
    }

    [Fact]
    public void EvalOptions_MinimalConfiguration()
    {
        // Arrange & Act
        var options = new MockEvalOptions
        {
            Mode = EvalMode.Codegen,
            Label = "minimal",
            CasesDir = "./cases",
            ReportsDir = "./reports"
        };

        // Assert
        Assert.Equal(EvalMode.Codegen, options.Mode);
        Assert.Equal("minimal", options.Label);
        Assert.Equal("./cases", options.CasesDir);
        Assert.Equal("./reports", options.ReportsDir);
        Assert.Null(options.RouterUrl);
        Assert.Null(options.GenUrl);
    }

    [Fact]
    public void EvalOptions_TemperatureExtremes()
    {
        // Arrange & Act
        var minTemp = new MockEvalOptions { Temperature = 0.0 };
        var maxTemp = new MockEvalOptions { Temperature = 2.0 };
        var normalTemp = new MockEvalOptions { Temperature = 0.7 };

        // Assert
        Assert.Equal(0.0, minTemp.Temperature);
        Assert.Equal(2.0, maxTemp.Temperature);
        Assert.Equal(0.7, normalTemp.Temperature);
    }

    [Fact]
    public void EvalOptions_LargeNPredictValue()
    {
        // Arrange & Act
        var options = new MockEvalOptions { NPredict = 10000 };

        // Assert
        Assert.Equal(10000, options.NPredict);
    }
}
