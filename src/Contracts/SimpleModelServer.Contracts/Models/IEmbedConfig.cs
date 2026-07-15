namespace SimpleModelServer.Contracts.Models;

public interface IEmbedConfig
{
    string Name { get; set; }
    int? NumCtx { get; set; }
    int? KeepAliveMinutes { get; set; }
    string[] Stop { get; set; }
}


