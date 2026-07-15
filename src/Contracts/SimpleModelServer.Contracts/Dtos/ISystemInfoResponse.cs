namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Complete system information response including capabilities and configuration.
/// </summary>
public interface ISystemInfoResponse
{
    /// <summary>System capabilities including models, agents, hardware, and features.</summary>
    ISystemCapabilities Capabilities { get; }

    /// <summary>Current global and model configuration.</summary>
    ISystemConfiguration Configuration { get; }

    /// <summary>Timestamp when this information was gathered (UTC).</summary>
    DateTime Timestamp { get; }
}
