using SimpleModelServer.Evals.Http;
using Xunit;

namespace SimpleModelServer.Evals.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IChatMessage"/> interface.
/// Validates chat message properties (role and content).
/// </summary>
public class ChatMessageTests
{
	private class MockChatMessage : IChatMessage
	{
		public string Role { get; init; } = "";
		public string Content { get; init; } = "";
	}

	/// <summary>
	/// Contract verification: IChatMessage has all required members.
	/// </summary>
	[Fact]
	public void IChatMessage_Interface_HasAllRequiredMembers()
	{
		// Arrange
		IChatMessage message = new MockChatMessage();

		// Act & Assert - All properties must be accessible
		_ = message.Role;
		_ = message.Content;
	}

	[Theory]
	[InlineData("user")]
	[InlineData("assistant")]
	[InlineData("system")]
	[InlineData("")]
	public void Role_CanBeSet(string role)
	{
		// Arrange & Act
		var message = new MockChatMessage { Role = role };

		// Assert
		Assert.Equal(role, message.Role);
	}

	[Theory]
	[InlineData("Hello")]
	[InlineData("What is 2+2?")]
	[InlineData("")]
	[InlineData("Multi-line\ncontent\nhere")]
	public void Content_CanBeSet(string content)
	{
		// Arrange & Act
		var message = new MockChatMessage { Content = content };

		// Assert
		Assert.Equal(content, message.Content);
	}

	[Fact]
	public void ChatMessage_UserMessage()
	{
		// Arrange & Act
		var message = new MockChatMessage
		{
			Role = "user",
			Content = "What is the weather?"
		};

		// Assert
		Assert.Equal("user", message.Role);
		Assert.Equal("What is the weather?", message.Content);
	}

	[Fact]
	public void ChatMessage_AssistantMessage()
	{
		// Arrange & Act
		var message = new MockChatMessage
		{
			Role = "assistant",
			Content = "The weather is sunny."
		};

		// Assert
		Assert.Equal("assistant", message.Role);
		Assert.Equal("The weather is sunny.", message.Content);
	}

	[Fact]
	public void ChatMessage_SystemMessage()
	{
		// Arrange & Act
		var message = new MockChatMessage
		{
			Role = "system",
			Content = "You are a helpful assistant."
		};

		// Assert
		Assert.Equal("system", message.Role);
		Assert.Equal("You are a helpful assistant.", message.Content);
	}

	[Fact]
	public void ChatMessage_EmptyMessage()
	{
		// Arrange & Act
		var message = new MockChatMessage
		{
			Role = "",
			Content = ""
		};

		// Assert
		Assert.Equal("", message.Role);
		Assert.Equal("", message.Content);
	}

	[Fact]
	public void ChatMessage_InterfaceContract()
	{
		// Arrange
		IChatMessage message = new MockChatMessage
		{
			Role = "user",
			Content = "Test message"
		};

		// Act & Assert
		Assert.Equal("user", message.Role);
		Assert.Equal("Test message", message.Content);
	}

	[Theory]
	[InlineData("user", "Question")]
	[InlineData("assistant", "Answer")]
	[InlineData("system", "System prompt")]
	public void ChatMessage_VariousRolesAndContent(string role, string content)
	{
		// Arrange & Act
		var message = new MockChatMessage { Role = role, Content = content };

		// Assert
		Assert.Equal(role, message.Role);
		Assert.Equal(content, message.Content);
	}

	[Fact]
	public void ChatMessage_LongContent()
	{
		// Arrange
		string longContent = new string('x', 10000);

		// Act
		var message = new MockChatMessage { Content = longContent };

		// Assert
		Assert.Equal(longContent, message.Content);
		Assert.Equal(10000, message.Content.Length);
	}

	[Fact]
	public void ChatMessage_SpecialCharactersInContent()
	{
		// Arrange
		string specialContent = "!@#$%^&*()_+-=[]{}|;':\",./<>?";

		// Act
		var message = new MockChatMessage { Content = specialContent };

		// Assert
		Assert.Equal(specialContent, message.Content);
	}

	[Fact]
	public void ChatMessage_UnicodeContent()
	{
		// Arrange
		string unicodeContent = "你好世界 🌍 مرحبا العالم";

		// Act
		var message = new MockChatMessage { Content = unicodeContent };

		// Assert
		Assert.Equal(unicodeContent, message.Content);
	}

	[Fact]
	public void ChatMessage_MultilineContent()
	{
		// Arrange
		string multilineContent = "Line 1\r\nLine 2\r\nLine 3";

		// Act
		var message = new MockChatMessage { Content = multilineContent };

		// Assert
		Assert.Equal(multilineContent, message.Content);
		Assert.Contains("\r\n", message.Content, StringComparison.Ordinal);
	}
}
