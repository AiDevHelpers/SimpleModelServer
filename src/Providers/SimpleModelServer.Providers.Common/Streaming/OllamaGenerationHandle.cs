namespace SimpleModelServer.Providers.Common.Streaming;

public sealed class OllamaGenerationHandle(HttpResponseMessage response, StreamReader reader) : IOllamaGenerationHandle
{
	public StreamReader Reader { get; } = reader;

	public ValueTask DisposeAsync()
	{
		Reader.Dispose();
		response.Dispose();
		return ValueTask.CompletedTask;
	}
}
