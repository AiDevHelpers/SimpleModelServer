namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Agent configuration information.
/// </summary>
public interface IAgentInfo
{
    /// <summary>Agent display name.</summary>
    string? Name { get; }

    /// <summary>Agent description.</summary>
    string? Description { get; }

    /// <summary>Draft model override for this agent, if configured.</summary>
    string? DraftModel { get; }

    /// <summary>Main model override for this agent, if configured.</summary>
    string? MainModel { get; }

    /// <summary>Agent-specific settings.</summary>
    IReadOnlyDictionary<string, object?> Settings { get; }
}
