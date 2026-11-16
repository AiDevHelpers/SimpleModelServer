using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SimpleModelServer.Evals.Codegen;

public sealed record GateDiagnostic(string Id, string Message, string Severity, int Line) : IGateDiagnostic;

public sealed record ParseGateResult(bool Passed, IReadOnlyList<GateDiagnostic> Diagnostics) : IParseGateResult
{
	IReadOnlyList<IGateDiagnostic> IParseGateResult.Diagnostics => Diagnostics;
}

public sealed record CompileGateResult(bool Passed, IReadOnlyList<GateDiagnostic> Diagnostics) : ICompileGateResult
{
	IReadOnlyList<IGateDiagnostic> ICompileGateResult.Diagnostics => Diagnostics;
}

/// <summary>
/// Roslyn parse and compile gates. Parse-only failures (syntax errors) are reported separately from
/// compile failures (unresolved types/members, etc.) so a report row can distinguish "this isn't
/// even valid C#" from "this is valid C# but references something undeclared".
/// </summary>
public static class CompileGate
{
	// Standard ImplicitUsings global-using set (SDK-style projects enable this by default) — most
	// generated snippets are written as if they'll land in a project with ImplicitUsings enabled
	// and omit these, so prepending them mirrors the environment they were "written for" rather
	// than penalizing correct code for a missing `using System;`.
	private const string ImplicitUsingsPreamble = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using System.IO;
        global using System.Net.Http;

        """;

	private static readonly Lazy<IReadOnlyList<MetadataReference>> TrustedPlatformReferences = new(LoadTrustedPlatformReferences);

	public static ParseGateResult Parse(string code)
	{
		SyntaxTree tree = CSharpSyntaxTree.ParseText(ImplicitUsingsPreamble + code);
		List<GateDiagnostic> diagnostics = [.. tree.GetDiagnostics()
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.Select(ToGateDiagnostic)];

		return new ParseGateResult(diagnostics.Count == 0, diagnostics);
	}

	public static CompileGateResult Compile(string code, string? stubFilePath)
	{
		bool hasStub = stubFilePath is not null && File.Exists(stubFilePath);

		// The stub declares its members in SimpleModelServer.Evals.Stubs; generated code never
		// references that namespace explicitly (the prompt tells the model to assume the types
		// are ambient), so bring it into scope for the compilation as a global using rather than
		// requiring the snippet to know about it.
		string preamble = hasStub ? "global using SimpleModelServer.Evals.Stubs;\n" + ImplicitUsingsPreamble : ImplicitUsingsPreamble;
		SyntaxTree mainTree = CSharpSyntaxTree.ParseText(preamble + code, path: "Generated.cs");
		List<SyntaxTree> trees = [mainTree];

		if (hasStub)
		{
			trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(stubFilePath!), path: stubFilePath!));
		}

		CSharpCompilation compilation = CSharpCompilation.Create(
			assemblyName: "GoldenCaseCompile",
			syntaxTrees: trees,
			references: TrustedPlatformReferences.Value,
			options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

		List<GateDiagnostic> diagnostics = [.. compilation.GetDiagnostics()
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.Select(ToGateDiagnostic)];

		return new CompileGateResult(diagnostics.Count == 0, diagnostics);
	}

	private static GateDiagnostic ToGateDiagnostic(Diagnostic d) =>
		new(d.Id, d.GetMessage(), d.Severity.ToString(), d.Location.GetLineSpan().StartLinePosition.Line + 1);

	private static IReadOnlyList<MetadataReference> LoadTrustedPlatformReferences()
	{
		string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
		if (string.IsNullOrEmpty(tpa))
		{
			throw new InvalidOperationException(
				"TRUSTED_PLATFORM_ASSEMBLIES was not set — the compile gate needs to run on a " +
				"framework-dependent runtime to resolve System.*/Microsoft.Extensions.*/ASP.NET Core references.");
		}

		return [.. tpa.Split(Path.PathSeparator)
			.Where(File.Exists)
			.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];
	}
}
