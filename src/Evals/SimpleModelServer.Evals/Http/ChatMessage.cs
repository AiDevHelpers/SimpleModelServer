using System.Text.Json.Serialization;

namespace SimpleModelServer.Evals.Http;

public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content) : IChatMessage;
