namespace SimpleModelServer.Contracts.Models;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716",
    Justification = "Stop is domain vocabulary (LLM stop sequences) used consistently across this C#-only codebase; renaming for cross-language interop that doesn't apply here would ripple across many call sites for no functional benefit.")]
public interface IModelConfig
{
    string Name { get; set; }
    string? Base { get; set; }
    double? Temperature { get; set; }
    double? TopP { get; set; }
    int? NumCtx { get; set; }
    int? KeepAliveMinutes { get; set; }
    string[] Stop { get; set; }
    ModelFamily? Family { get; set; }
    string? Variant { get; set; }
    string? Size { get; set; }
    string? ChatSuffix { get; set; }
}


