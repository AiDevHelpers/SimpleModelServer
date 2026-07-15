namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Current system configuration.
/// </summary>
public interface ISystemConfiguration
{
    /// <summary>Global router and system settings.</summary>
    IGlobalConfigInfo Global { get; }

    /// <summary>Model role assignments (e.g., draft, main, embed).</summary>
    IReadOnlyDictionary<string, string> ModelRoles { get; }

    /// <summary>Model source configurations.</summary>
    IReadOnlyDictionary<string, IModelSourceInfo> ModelSources { get; }

    /// <summary>Agent configurations.</summary>
    IReadOnlyDictionary<string, IAgentInfo> Agents { get; }
}
