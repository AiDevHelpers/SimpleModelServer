namespace SimpleModelServer.Contracts.Dtos.Settings;

using SimpleModelServer.Contracts.Dtos.Agents;

using SimpleModelServer.Contracts.Models;

public class ServerConfig
{
    public GlobalSettings Global { get; set; } = new();
    public ModelDefinitions Models { get; set; } = new();
    public AgentDefinitions Agents { get; set; } = new();
}





