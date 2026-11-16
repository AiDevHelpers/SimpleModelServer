namespace SimpleModelServer.Contracts.Configuration;

public interface IDebugConfig
{
	bool LogUnattributedRequests { get; }
	bool LogRequestBodies { get; }
}
