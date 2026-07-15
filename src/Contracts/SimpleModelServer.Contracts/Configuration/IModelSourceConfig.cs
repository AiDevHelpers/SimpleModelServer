namespace SimpleModelServer.Contracts.Configuration;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716",
    Justification = "Stop is domain vocabulary (LLM stop sequences) used consistently across this C#-only codebase; renaming for cross-language interop that doesn't apply here would ripple across many call sites for no functional benefit.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056",
    Justification = "Url is a plain string bound from JSON config and used verbatim in string interpolation elsewhere; changing to System.Uri would complicate config binding for no functional benefit.")]
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
