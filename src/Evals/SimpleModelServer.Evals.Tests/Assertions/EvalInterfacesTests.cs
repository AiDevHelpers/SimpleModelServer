using SimpleModelServer.Evals.Codegen;
using SimpleModelServer.Evals.Models;
using SimpleModelServer.Evals.Router;
using SimpleModelServer.Evals.Reporting;
using SimpleModelServer.Evals.Interfaces;
using Xunit;
using System.Reflection;

namespace SimpleModelServer.Evals.Tests;

/// <summary>
/// Comprehensive tests for Evals interfaces.
/// Validates that all required interface members exist and are accessible.
/// </summary>
public class EvalInterfacesContractTests
{
	[Fact]
	public void IGateDiagnostic_HasAllRequiredMembers()
	{
		var type = typeof(IGateDiagnostic);
		Assert.NotNull(type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Message", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Severity", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Line", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IParseGateResult_HasAllRequiredMembers()
	{
		var type = typeof(IParseGateResult);
		Assert.NotNull(type.GetProperty("Passed", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Diagnostics", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void ICompileGateResult_HasAllRequiredMembers()
	{
		var type = typeof(ICompileGateResult);
		Assert.NotNull(type.GetProperty("Passed", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Diagnostics", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void ICodegenCaseResult_HasAllRequiredMembers()
	{
		var type = typeof(ICodegenCaseResult);
		Assert.NotNull(type.GetProperty("CaseId", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("GenerationSucceeded", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("ExtractedCode", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("ParsePassed", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("ParseDiagnostics", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("CompilePassed", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("CompileDiagnostics", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("AssertionsPassed", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Assertions", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IAssertionOutcome_HasAllRequiredMembers()
	{
		var type = typeof(IAssertionOutcome);
		Assert.NotNull(type.GetProperty("Text", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Required", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Present", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Passed", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IAssertionCheckResult_HasAllRequiredMembers()
	{
		var type = typeof(IAssertionCheckResult);
		Assert.NotNull(type.GetProperty("Outcomes", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("AllPassed", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void ICodegenSummary_HasAllRequiredMembers()
	{
		var type = typeof(ICodegenSummary);
		Assert.NotNull(type.GetProperty("TotalCases", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("ParsePassRatePct", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("CompilePassRatePct", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("AssertionPassRatePct", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IExpectedResult_HasAllRequiredMembers()
	{
		var type = typeof(IExpectedResult);
		Assert.NotNull(type.GetProperty("Intent", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Quality", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Ambiguity", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("RoutingReason", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("MustCompile", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("LatencyClass", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IGoldenCase_HasAllRequiredMembers()
	{
		var type = typeof(IGoldenCase);
		Assert.NotNull(type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Prompt", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Expected", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IRouterCaseResult_HasAllRequiredMembers()
	{
		var type = typeof(IRouterCaseResult);
		Assert.NotNull(type.GetProperty("CaseId", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("SchemaValid", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("IntentMatch", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("QualityMatch", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("AmbiguityMatch", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("RoutingReasonMatch", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IRouterSummary_HasAllRequiredMembers()
	{
		var type = typeof(IRouterSummary);
		Assert.NotNull(type.GetProperty("TotalCases", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("SchemaValidCount", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("IntentAccuracyPct", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("RoutingReasonAccuracyPct", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IRouterResponse_HasAllRequiredMembers()
	{
		var type = typeof(IRouterResponse);
		Assert.NotNull(type.GetProperty("Intent", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Quality", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Response", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("RoutingReason", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IReportMetadata_HasAllRequiredMembers()
	{
		var type = typeof(IReportMetadata);
		Assert.NotNull(type.GetProperty("Mode", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("TimestampUtc", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IReportPaths_HasAllRequiredMembers()
	{
		var type = typeof(IReportPaths);
		Assert.NotNull(type.GetProperty("JsonPath", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("MdPath", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IDirectGenerationPipeline_HasAllRequiredMembers()
	{
		// IDirectGenerationPipeline is a marker interface extending IGenerationPipeline — no
		// members of its own, so verify the inherited contract instead of a nonexistent property.
		// Type.GetMethod on a derived interface doesn't surface members inherited from an
		// extended interface (that only works reliably for classes), so check IGenerationPipeline
		// itself rather than IDirectGenerationPipeline.
		var type = typeof(IDirectGenerationPipeline);
		Assert.True(typeof(IGenerationPipeline).IsAssignableFrom(type));
		Assert.NotNull(typeof(IGenerationPipeline).GetMethod("GenerateAsync", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void IGenerationOutcome_HasAllRequiredMembers()
	{
		var type = typeof(IGenerationOutcome);
		Assert.NotNull(type.GetProperty("Succeeded", BindingFlags.Public | BindingFlags.Instance));
		Assert.NotNull(type.GetProperty("RawResponse", BindingFlags.Public | BindingFlags.Instance));
	}
}
