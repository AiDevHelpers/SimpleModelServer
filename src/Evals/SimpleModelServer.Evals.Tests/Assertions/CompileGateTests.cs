using SimpleModelServer.Evals.Codegen;

using Xunit;

namespace SimpleModelServer.Evals.Tests;

public class CompileGateTests
{
    [Fact]
    public void Parse_ValidSyntax_Passes()
    {
        ParseGateResult result = CompileGate.Parse("public record Foo(int X);");

        Assert.True(result.Passed);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_InvalidSyntax_Fails()
    {
        ParseGateResult result = CompileGate.Parse("public record Foo(int X) {");

        Assert.False(result.Passed);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Compile_ValidSelfContainedCode_Passes()
    {
        string code = """
            public record OrderDto(System.Guid Id, decimal Total);
            """;

        CompileGateResult result = CompileGate.Compile(code, stubFilePath: null);

        Assert.True(result.Passed, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Compile_UsesImplicitUsingsPreamble_SoSystemLinqNeedsNoExplicitUsing()
    {
        string code = """
            public class Foo
            {
                public List<int> GetEvens(IEnumerable<int> xs) => xs.Where(x => x % 2 == 0).ToList();
            }
            """;

        CompileGateResult result = CompileGate.Compile(code, stubFilePath: null);

        Assert.True(result.Passed, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Compile_UndeclaredType_Fails()
    {
        string code = """
            public class Foo
            {
                public void Bar(ITotallyUndeclaredThing thing) { }
            }
            """;

        CompileGateResult result = CompileGate.Compile(code, stubFilePath: null);

        Assert.False(result.Passed);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("ITotallyUndeclaredThing", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_WithStubFile_ResolvesAssumedTypes()
    {
        string stubPath = Path.GetTempFileName() + ".cs";
        try
        {
            File.WriteAllText(stubPath, """
                namespace SimpleModelServer.Evals.Stubs;

                public interface IWidgetRepository
                {
                    int Count();
                }
                """);

            string code = """
                public class Foo
                {
                    public int Bar(IWidgetRepository repo) => repo.Count();
                }
                """;

            CompileGateResult result = CompileGate.Compile(code, stubPath);

            Assert.True(result.Passed, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }
        finally
        {
            File.Delete(stubPath);
        }
    }
}
