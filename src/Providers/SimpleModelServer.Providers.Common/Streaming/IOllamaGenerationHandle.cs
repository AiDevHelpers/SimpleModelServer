namespace SimpleModelServer.Providers.Common.Streaming;

public interface IOllamaGenerationHandle : IAsyncDisposable
{
    StreamReader Reader { get; }
}
