namespace SimpleModelServer.Contracts.Dtos;

using SimpleModelServer.Contracts.Models;

public class ModelConfig
{
    public string Name { get; set; } = "";
    public string? Base { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? NumCtx { get; set; }
    /// <summary>
    ///     Keep-alive duration in minutes. 0 = disabled (always hot, never unload).
    ///     Positive values = minutes to keep alive. Null = use family pair default.
    /// </summary>
    public int? KeepAliveMinutes { get; set; }
    public string[] Stop { get; set; } = [];

    // Model family/variant/size for auto-generation
    public ModelFamily? Family { get; set; }
    public string? Variant { get; set; }
    public string? Size { get; set; }
    public string? ChatSuffix { get; set; }
}


