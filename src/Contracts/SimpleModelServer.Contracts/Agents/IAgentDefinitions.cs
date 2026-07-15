namespace SimpleModelServer.Contracts.Agents;

using SimpleModelServer.Contracts.Models;

public interface IAgentDefinitions
{
    IAgentConfig Code { get; set; }
    IAgentConfig Debug { get; set; }
    IAgentConfig Planning { get; set; }
}




