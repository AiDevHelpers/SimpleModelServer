using SimpleModelServer.Contracts;


namespace SimpleModelServer.Contracts.Dtos;

using SimpleModelServer.Contracts.Models;

/// <summary>
///     Container for named model configurations that users can switch between
///     based on their needs (e.g., fast vs. accurate).
/// </summary>
public class ModelDefinitions
{
    /// <summary>
    ///     Named model configurations that users can switch between.
    ///     Each configuration represents a different use case (e.g., "fast", "accurate", "balanced").
    ///     The configuration with IsDefault=true will be used when none is explicitly selected.
    /// </summary>
    public List<ModelConfiguration> Configurations { get; set; } = new();
}



