namespace SimpleModelServer.Contracts.Models;

public interface IModelDefinitions
{
	IList<IModelConfiguration> Configurations { get; set; }
}


