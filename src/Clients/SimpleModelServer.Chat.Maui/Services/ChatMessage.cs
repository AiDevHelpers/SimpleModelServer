namespace SimpleModelServer.Chat.Maui.Services;

/// <summary>
/// Represents a chat message in the conversation.
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// Gets the role of the message sender.
    /// </summary>
    public ChatMessageRole Role { get; set; } = ChatMessageRole.Assistant;

    /// <summary>
    /// Gets or sets the message text. This property is settable to allow
    /// incremental updates during streaming.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets the timestamp when the message was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents the role of a chat message sender.
/// </summary>
public enum ChatMessageRole
{
    /// <summary>The user sending the message.</summary>
    User,

    /// <summary>The assistant responding to the user.</summary>
    Assistant
}
