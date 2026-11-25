namespace SimpleModelServer.Contracts.Models;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716",
    Justification = "Stop/Error/Global are domain vocabulary (stop sequences, global config) used consistently across this C#-only codebase; renaming for cross-language interop that doesn't apply here would ripple across many call sites for no functional benefit.")]
public interface IEmbedConfig
{
    string Name { get; set; }
    int? NumCtx { get; set; }
    int? KeepAliveMinutes { get; set; }
    string[] Stop { get; set; }
}


