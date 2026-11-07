namespace SimpleModelServer.Contracts.Models;

/// <summary>
///     Supported model families. Using an enum provides type safety and prevents
///     typos or invalid family names.
/// </summary>
public enum ModelFamily
{
	/// <summary>Qwen2.5 family (Alibaba)</summary>
	Qwen25,

	/// <summary>DeepSeek family</summary>
	DeepSeek,

	/// <summary>Llama 2 family (Meta)</summary>
	Llama2,

	/// <summary>Mistral family</summary>
	Mistral,

	/// <summary>Phi family (Microsoft)</summary>
	Phi,

	/// <summary>Neural Chat family</summary>
	NeuralChat,

	/// <summary>Custom/user-defined model family</summary>
	Custom,
}


