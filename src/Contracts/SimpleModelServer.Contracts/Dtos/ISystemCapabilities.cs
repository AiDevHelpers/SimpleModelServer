namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Available system capabilities.
/// </summary>
public interface ISystemCapabilities
{
    /// <summary>Names of available models configured in the system.</summary>
    string[] Models { get; }

    /// <summary>Supported agent types (e.g., code, debug, planning, research).</summary>
    string[] AgentTypes { get; }

    /// <summary>Hardware capabilities and resource information.</summary>
    IHardwareCapabilities Hardware { get; }

    /// <summary>Feature flags and capabilities enabled in this system.</summary>
    string[] Features { get; }
}
