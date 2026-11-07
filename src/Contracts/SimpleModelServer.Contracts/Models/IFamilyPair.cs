namespace SimpleModelServer.Contracts.Models;

public interface IFamilyPair
{
	ModelFamily Family { get; set; }
	string Variant { get; set; }
	int KeepAliveMinutes { get; set; }
	IModelConfig? Draft { get; set; }
	IModelConfig? Main { get; set; }
}


