namespace SimpleModelServer.Contracts.Models;

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


