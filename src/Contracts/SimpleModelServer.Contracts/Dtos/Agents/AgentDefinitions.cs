using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Dtos.Settings;

using SimpleModelServer.Contracts;


namespace SimpleModelServer.Contracts.Dtos.Agents;

using SimpleModelServer.Contracts.Models;

public class AgentDefinitions : IAgentDefinitions
{
    public IAgentConfig Code { get; set; } = new AgentConfig();
    public IAgentConfig Debug { get; set; } = new AgentConfig();
    public IAgentConfig Planning { get; set; } = new AgentConfig();
}






