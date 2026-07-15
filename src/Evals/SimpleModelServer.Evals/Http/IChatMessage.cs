namespace SimpleModelServer.Evals.Http;

public interface IChatMessage
{
    string Role { get; }
    string Content { get; }
}
