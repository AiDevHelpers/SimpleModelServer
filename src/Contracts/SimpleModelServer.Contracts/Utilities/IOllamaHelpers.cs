using SimpleModelServer.Contracts.Models;

using System.Text.Json.Nodes;

namespace SimpleModelServer.Contracts.Utilities;

public interface IOllamaHelpers
{
	string BuildPrompt(IReadOnlyList<ChatMessage> history);
	string BuildPrompt(JsonArray messages);
	Task<TokenResult> ReadTokenAsync(StreamReader reader, TimeSpan timeout);
}
