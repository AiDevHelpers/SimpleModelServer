namespace SimpleModelServer.Contracts.Dtos;

using SimpleModelServer.Contracts.Models;

/// <summary>
///     GPU preference mode for model selection and placement.
/// </summary>
public enum PreferenceMode
{
	/// <summary>Automatically prefer largest practical model that fits hardware constraints</summary>
	Auto,

	/// <summary>Prefer GPU utilization when possible</summary>
	GPU,

	/// <summary>Prefer largest model size regardless of GPU placement</summary>
	Size,
}


