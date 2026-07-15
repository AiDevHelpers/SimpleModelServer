namespace SimpleModelServer.Contracts.Configuration;

public interface IModelSourceConfig
{
    string Base { get; }
    int? NumCtx { get; }
    string? KeepAlive { get; }
    int? NumGpu { get; }
    IReadOnlyList<string> Stop { get; }
    string? Family { get; }
    string? Variant { get; }
    string? Size { get; }
    string? ChatSuffix { get; }
}
