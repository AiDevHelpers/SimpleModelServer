using SimpleModelServer.Contracts;

namespace SimpleModelServer.Contracts.Dtos;

using SimpleModelServer.Contracts.Models;

public class FamilyPair
{
    /// <summary>Model family (type-safe enum)</summary>
    public ModelFamily Family { get; set; }

    /// <summary>Model variant (e.g., "coder", "instruct", "chat")</summary>
    public string Variant { get; set; } = "";

    /// <summary>
    ///     Keep-alive duration in minutes for both draft and main models in this family pair.
    ///     0 = disabled (always hot, never unload). Positive values = minutes to keep alive.
    /// </summary>
    public int KeepAliveMinutes { get; set; }

    /// <summary>
    ///     Draft model configuration (typically smaller/faster).
    ///     Can be null if this family pair only uses a main model (e.g., best model has no draft variant).
    /// </summary>
    public ModelConfig? Draft { get; set; }

    /// <summary>
    ///     Main model configuration (typically larger/more capable).
    ///     Can be null if this family pair only uses a draft model.
    /// </summary>
    public ModelConfig? Main { get; set; }
}



