using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using Xunit;

namespace SimpleModelServer.Config.Tests;

/// <summary>
///     Each model profile reports the local name of its own main model, so the chat client can
///     use the active profile's model instead of guessing from the server's model list.
/// </summary>
public sealed class ModelProfileMainModelTests
{
	[Fact]
	public void Profiles_EachCarryTheirOwnMainModel()
	{
		string path = Path.Combine(Path.GetTempPath(), $"smsconfig-profile-main-{Guid.NewGuid():N}.json");
		File.WriteAllText(path, """
			{
			  "models": { "configurations": [
			    { "id": 1, "nickname": "Small", "isDefault": true,
			      "families": [ { "family": "Qwen25", "variant": "coder", "main": { "size": "3b" } } ] },
			    { "id": 2, "nickname": "Large",
			      "families": [ { "family": "Qwen25", "variant": "coder", "main": { "size": "7b" } } ] }
			  ] }
			}
			""");
		try
		{
			AppConfig config = AppConfig.Load(path);

			Assert.Equal("qwen2.5-coder-3b-chat", config.ModelProfiles[0].MainModel);
			Assert.Equal("qwen2.5-coder-7b-chat", config.ModelProfiles[1].MainModel);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void StockConfig_DefaultProfileMainModel_MatchesMainRole()
	{
		string path = Path.Combine(Path.GetTempPath(), $"smsconfig-profile-stock-{Guid.NewGuid():N}.json");
		try
		{
			DefaultConfig.EnsureConfig(path);
			AppConfig config = AppConfig.Load(path);

			ModelProfile defaultProfile = config.ModelProfiles.Single(p => p.IsDefault);
			Assert.Equal(config.ModelRoles["main"], defaultProfile.MainModel);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
