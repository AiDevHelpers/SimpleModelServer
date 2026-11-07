using SimpleModelServer.Evals.Codegen;

using Xunit;

namespace SimpleModelServer.Evals.Tests;

public class CodeBlockExtractorTests
{
	[Fact]
	public void Extract_SingleFencedBlock_ReturnsBody()
	{
		string response = """
            Sure, here's the enum:

            ```csharp
            public enum OrderStatus
            {
                Pending,
                Shipped
            }
            ```

            Let me know if you need anything else.
            """;

		string result = CodeBlockExtractor.Extract(response);

		Assert.Contains("public enum OrderStatus", result, StringComparison.Ordinal);
		Assert.DoesNotContain("Let me know", result, StringComparison.Ordinal);
		Assert.DoesNotContain("```", result, StringComparison.Ordinal);
	}

	[Fact]
	public void Extract_MultipleFencedBlocks_ConcatenatesInOrder()
	{
		string response = """
            ```csharp
            public record A(int X);
            ```

            some prose in between

            ```csharp
            public record B(int Y);
            ```
            """;

		string result = CodeBlockExtractor.Extract(response);

		Assert.Contains("record A", result, StringComparison.Ordinal);
		Assert.Contains("record B", result, StringComparison.Ordinal);
		Assert.True(result.IndexOf("record A", StringComparison.Ordinal) < result.IndexOf("record B", StringComparison.Ordinal));
	}

	[Fact]
	public void Extract_UnfencedResponse_FallsBackToBraceBalancedRegion()
	{
		string response = """
            Here you go, no fences this time:
            public class Foo
            {
                public int Bar { get; set; }
            }
            Hope that helps.
            """;

		string result = CodeBlockExtractor.Extract(response);

		Assert.Contains("public class Foo", result, StringComparison.Ordinal);
		Assert.Contains("public int Bar", result, StringComparison.Ordinal);
		Assert.DoesNotContain("Hope that helps", result, StringComparison.Ordinal);
	}

	[Fact]
	public void Extract_NoBraces_ReturnsEmpty()
	{
		string result = CodeBlockExtractor.Extract("I cannot help with that request.");

		Assert.Equal("", result);
	}

	[Fact]
	public void Extract_EmptyInput_ReturnsEmpty()
	{
		Assert.Equal("", CodeBlockExtractor.Extract(""));
		Assert.Equal("", CodeBlockExtractor.Extract("   "));
	}

	[Fact]
	public void LargestBraceBalancedRegion_PicksLongestRegion_NotFirst()
	{
		string text = "{short}" + new string('x', 50) + "{ this region is much longer than the first one }";

		string result = CodeBlockExtractor.LargestBraceBalancedRegion(text);

		Assert.Contains("much longer", result, StringComparison.Ordinal);
	}
}
