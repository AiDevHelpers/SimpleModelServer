using SimpleModelServer.Evals.Http;
using Xunit;

namespace SimpleModelServer.Evals.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IChatCompletionResult"/> interface.
/// Validates chat completion result properties and their values.
/// </summary>
public class ChatCompletionResultTests
{
	private class MockChatCompletionResult : IChatCompletionResult
	{
		public string Content { get; init; } = "";
		public string? FinishReason { get; init; }
		public int? PromptTokens { get; init; }
		public int? CompletionTokens { get; init; }
		public double LatencyMs { get; init; }
		public string? Error { get; init; }
		public bool Succeeded { get; init; }
	}

	/// <summary>
	/// Contract verification: IChatCompletionResult has all required members.
	/// </summary>
	[Fact]
	public void IChatCompletionResult_Interface_HasAllRequiredMembers()
	{
		// Arrange
		IChatCompletionResult result = new MockChatCompletionResult();

		// Act & Assert - All properties must be accessible
		_ = result.Content;
		_ = result.FinishReason;
		_ = result.PromptTokens;
		_ = result.CompletionTokens;
		_ = result.LatencyMs;
		_ = result.Error;
		_ = result.Succeeded;
	}

	[Theory]
	[InlineData("Hello, world!")]
	[InlineData("")]
	[InlineData("Multi-line\nresponse")]
	public void Content_CanBeSet(string content)
	{
		// Arrange & Act
		var result = new MockChatCompletionResult { Content = content };

		// Assert
		Assert.Equal(content, result.Content);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("stop")]
	[InlineData("length")]
	[InlineData("error")]
	public void FinishReason_CanBeSet(string? reason)
	{
		// Arrange & Act
		var result = new MockChatCompletionResult { FinishReason = reason };

		// Assert
		Assert.Equal(reason, result.FinishReason);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	[InlineData(10)]
	[InlineData(100)]
	public void PromptTokens_CanBeSet(int? tokens)
	{
		// Arrange & Act
		var result = new MockChatCompletionResult { PromptTokens = tokens };

		// Assert
		Assert.Equal(tokens, result.PromptTokens);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	[InlineData(10)]
	[InlineData(100)]
	public void CompletionTokens_CanBeSet(int? tokens)
	{
		// Arrange & Act
		var result = new MockChatCompletionResult { CompletionTokens = tokens };

		// Assert
		Assert.Equal(tokens, result.CompletionTokens);
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(10.5)]
	[InlineData(100.0)]
	[InlineData(1000.123)]
	public void LatencyMs_CanBeSet(double latency)
	{
		// Arrange & Act
		var result = new MockChatCompletionResult { LatencyMs = latency };

		// Assert
		Assert.Equal(latency, result.LatencyMs);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("Connection timeout")]
	[InlineData("Invalid response")]
	public void Error_CanBeSet(string? error)
	{
		// Arrange & Act
		var result = new MockChatCompletionResult { Error = error };

		// Assert
		Assert.Equal(error, result.Error);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Succeeded_CanBeSet(bool succeeded)
	{
		// Arrange & Act
		var result = new MockChatCompletionResult { Succeeded = succeeded };

		// Assert
		Assert.Equal(succeeded, result.Succeeded);
	}

	[Fact]
	public void ChatCompletionResult_SuccessfulCompletion()
	{
		// Arrange & Act
		var result = new MockChatCompletionResult
		{
			Content = "This is the response",
			FinishReason = "stop",
			PromptTokens = 10,
			CompletionTokens = 20,
			LatencyMs = 150.5,
			Error = null,
			Succeeded = true
		};

		// Assert
		Assert.Equal("This is the response", result.Content);
		Assert.Equal("stop", result.FinishReason);
		Assert.Equal(10, result.PromptTokens);
		Assert.Equal(20, result.CompletionTokens);
		Assert.Equal(150.5, result.LatencyMs);
		Assert.Null(result.Error);
		Assert.True(result.Succeeded);
	}

	[Fact]
	public void ChatCompletionResult_FailedCompletion()
	{
		// Arrange & Act
		var result = new MockChatCompletionResult
		{
			Content = "",
			FinishReason = null,
			PromptTokens = null,
			CompletionTokens = null,
			LatencyMs = 0.0,
			Error = "Request timeout",
			Succeeded = false
		};

		// Assert
		Assert.Equal("", result.Content);
		Assert.Null(result.FinishReason);
		Assert.Null(result.PromptTokens);
		Assert.Null(result.CompletionTokens);
		Assert.Equal(0.0, result.LatencyMs);
		Assert.Equal("Request timeout", result.Error);
		Assert.False(result.Succeeded);
	}

	[Fact]
	public void ChatCompletionResult_PartialSuccess()
	{
		// Arrange & Act
		var result = new MockChatCompletionResult
		{
			Content = "Partial response",
			FinishReason = "length",
			PromptTokens = 15,
			CompletionTokens = 50,
			LatencyMs = 200.0,
			Error = null,
			Succeeded = true
		};

		// Assert
		Assert.Equal("Partial response", result.Content);
		Assert.Equal("length", result.FinishReason);
		Assert.Equal(15, result.PromptTokens);
		Assert.Equal(50, result.CompletionTokens);
		Assert.Equal(200.0, result.LatencyMs);
		Assert.Null(result.Error);
		Assert.True(result.Succeeded);
	}

	[Fact]
	public void ChatCompletionResult_InterfaceContract()
	{
		// Arrange
		IChatCompletionResult result = new MockChatCompletionResult
		{
			Content = "Test response",
			FinishReason = "stop",
			PromptTokens = 5,
			CompletionTokens = 10,
			LatencyMs = 50.0,
			Error = null,
			Succeeded = true
		};

		// Act & Assert
		Assert.Equal("Test response", result.Content);
		Assert.Equal("stop", result.FinishReason);
		Assert.Equal(5, result.PromptTokens);
		Assert.Equal(10, result.CompletionTokens);
		Assert.Equal(50.0, result.LatencyMs);
		Assert.Null(result.Error);
		Assert.True(result.Succeeded);
	}

	[Theory]
	[InlineData(true, "stop", null)]
	[InlineData(true, "length", null)]
	[InlineData(false, null, "Network error")]
	[InlineData(false, null, "Model overloaded")]
	public void ChatCompletionResult_VariousStates(
		bool succeeded,
		string? finishReason,
		string? error)
	{
		// Arrange & Act
		var result = new MockChatCompletionResult
		{
			Succeeded = succeeded,
			FinishReason = finishReason,
			Error = error
		};

		// Assert
		Assert.Equal(succeeded, result.Succeeded);
		Assert.Equal(finishReason, result.FinishReason);
		Assert.Equal(error, result.Error);
	}

	[Fact]
	public void ChatCompletionResult_LargeTokenCounts()
	{
		// Arrange & Act
		var result = new MockChatCompletionResult
		{
			PromptTokens = 1000000,
			CompletionTokens = 2000000
		};

		// Assert
		Assert.Equal(1000000, result.PromptTokens);
		Assert.Equal(2000000, result.CompletionTokens);
	}

	[Fact]
	public void ChatCompletionResult_VerySmallLatency()
	{
		// Arrange & Act
		var result = new MockChatCompletionResult
		{
			LatencyMs = 0.001
		};

		// Assert
		Assert.Equal(0.001, result.LatencyMs);
	}

	[Fact]
	public void ChatCompletionResult_VeryLargeLatency()
	{
		// Arrange & Act
		var result = new MockChatCompletionResult
		{
			LatencyMs = 3600000.0 // 1 hour
		};

		// Assert
		Assert.Equal(3600000.0, result.LatencyMs);
	}
}
