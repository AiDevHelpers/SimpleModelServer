using SimpleModelServer.Evals.Codegen;

using Xunit;

namespace SimpleModelServer.Evals.Tests;

public class RepetitionDetectorTests
{
    [Fact]
    public void LooksLikeRepetitionLoop_RepeatedLineWithLengthFinish_ReturnsTrue()
    {
        string response = string.Join('\n', Enumerable.Repeat("using System.Xml.Linq;", 10));

        Assert.True(RepetitionDetector.LooksLikeRepetitionLoop(response, "length"));
    }

    [Fact]
    public void LooksLikeRepetitionLoop_RepeatedLineButStopFinish_ReturnsFalse()
    {
        string response = string.Join('\n', Enumerable.Repeat("using System.Xml.Linq;", 10));

        Assert.False(RepetitionDetector.LooksLikeRepetitionLoop(response, "stop"));
    }

    [Fact]
    public void LooksLikeRepetitionLoop_NormalCodeWithLengthFinish_ReturnsFalse()
    {
        string response = """
            public class Foo
            {
                public int Bar { get; set; }
                public string Baz { get; set; } = "";
            }
            """;

        Assert.False(RepetitionDetector.LooksLikeRepetitionLoop(response, "length"));
    }

    [Fact]
    public void LooksLikeRepetitionLoop_EmptyResponse_ReturnsFalse()
    {
        Assert.False(RepetitionDetector.LooksLikeRepetitionLoop("", "length"));
    }
}
