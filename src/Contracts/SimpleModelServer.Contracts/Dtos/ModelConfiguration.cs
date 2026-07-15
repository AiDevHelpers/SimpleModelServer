using SimpleModelServer.Contracts;


namespace SimpleModelServer.Contracts.Dtos;

using SimpleModelServer.Contracts.Models;

/// <summary>
///     A named model configuration that users can switch between based on their needs.
///     Configurations bundle a set of model families and an embedding model together
///     for specific use cases (e.g., "Fast & Responsive", "Accurate & Thorough").
/// </summary>
public class ModelConfiguration
{
    /// <summary>Unique identifier for this configuration (integer, stable across restarts)</summary>
    public int Id { get; set; }

    /// <summary>User-friendly display name (e.g., "Fast & Responsive", "Accurate & Thorough")</summary>
    public string Nickname { get; set; } = "";

    /// <summary>Whether this is the default configuration to use when none is specified</summary>
    public bool IsDefault { get; set; }

    /// <summary>Description of this configuration's purpose and characteristics</summary>
    public string? Description { get; set; }

    /// <summary>Model family pairs for this configuration (defaults only; system builds actual models)</summary>
    public List<FamilyPair> Families { get; set; } = new();

    /// <summary>Embedding model for this configuration (defaults only; system builds actual model if missing)</summary>
    public EmbedConfig? Embed { get; set; }
}



