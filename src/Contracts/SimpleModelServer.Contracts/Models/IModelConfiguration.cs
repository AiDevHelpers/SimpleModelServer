namespace SimpleModelServer.Contracts.Models;

public interface IModelConfiguration
{
    int Id { get; set; }
    string Nickname { get; set; }
    bool IsDefault { get; set; }
    string? Description { get; set; }
    IList<IFamilyPair> Families { get; set; }
    IEmbedConfig? Embed { get; set; }
}


