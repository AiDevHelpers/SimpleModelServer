using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public readonly record struct HardwareSetting(bool IsSpecified, bool Auto, int? Value)
{
	public static HardwareSetting Unspecified { get; } = new(false, false, null);
	public static HardwareSetting Automatic { get; } = new(true, true, null);

	public static HardwareSetting Override(int value)
	{
		return new HardwareSetting(true, false, value);
	}
}
