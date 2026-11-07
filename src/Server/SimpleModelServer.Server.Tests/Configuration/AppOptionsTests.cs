using SimpleModelServer.Server.Configuration;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers AppOptions.Parse's CLI argument handling — flag recognition (both "--x" and "-x"
///     forms), case-insensitivity, value-taking flags (--router-port, --config), and error
///     handling for a missing/invalid value. Zero coverage before this; a parsing regression here
///     silently changes how the service starts (e.g. --service being missed would mean the
///     Windows Service host never actually runs as a background service).
/// </summary>
public sealed class AppOptionsTests
{
	[Fact]
	public void Parse_NoArgs_AllFlagsDefaultFalse()
	{
		AppOptions options = AppOptions.Parse([]);

		Assert.False(options.Service);
		Assert.False(options.Router);
		Assert.False(options.Kill);
		Assert.False(options.Help);
		Assert.False(options.InstallService);
		Assert.False(options.UninstallService);
		Assert.Null(options.ConfigPath);
		Assert.Null(options.RouterPortOverride);
	}

	[Theory]
	[InlineData("--service")]
	[InlineData("-service")]
	[InlineData("--SERVICE")]
	[InlineData("-SERVICE")]
	public void Parse_ServiceFlag_AllCasingAndDashForms(string arg)
	{
		AppOptions options = AppOptions.Parse([arg]);
		Assert.True(options.Service);
	}

	[Theory]
	[InlineData("--install-service")]
	[InlineData("-install-service")]
	public void Parse_InstallServiceFlag(string arg)
	{
		Assert.True(AppOptions.Parse([arg]).InstallService);
	}

	[Theory]
	[InlineData("--uninstall-service")]
	[InlineData("-uninstall-service")]
	public void Parse_UninstallServiceFlag(string arg)
	{
		Assert.True(AppOptions.Parse([arg]).UninstallService);
	}

	[Theory]
	[InlineData("--router")]
	[InlineData("-router")]
	public void Parse_RouterFlag(string arg)
	{
		Assert.True(AppOptions.Parse([arg]).Router);
	}

	[Theory]
	[InlineData("--kill")]
	[InlineData("-kill")]
	public void Parse_KillFlag(string arg)
	{
		Assert.True(AppOptions.Parse([arg]).Kill);
	}

	[Theory]
	[InlineData("--help")]
	[InlineData("-help")]
	[InlineData("-h")]
	[InlineData("/?")]
	public void Parse_HelpFlag_AllForms(string arg)
	{
		Assert.True(AppOptions.Parse([arg]).Help);
	}

	[Fact]
	public void Parse_MultipleFlags_AllRecognized()
	{
		AppOptions options = AppOptions.Parse(["--service", "--router", "--kill"]);

		Assert.True(options.Service);
		Assert.True(options.Router);
		Assert.True(options.Kill);
		Assert.False(options.Help);
	}

	[Fact]
	public void Parse_UnknownArg_IsIgnoredRatherThanThrowing()
	{
		AppOptions options = AppOptions.Parse(["--totally-unknown-flag", "--service"]);
		Assert.True(options.Service);
	}

	[Theory]
	[InlineData("--router-port")]
	[InlineData("-router-port")]
	public void Parse_RouterPort_ParsesFollowingIntValue(string flag)
	{
		AppOptions options = AppOptions.Parse([flag, "12345"]);
		Assert.Equal(12345, options.RouterPortOverride);
	}

	[Fact]
	public void Parse_RouterPort_NonInteger_Throws()
	{
		_ = Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--router-port", "not-a-number"]));
	}

	[Fact]
	public void Parse_RouterPort_MissingValue_Throws()
	{
		_ = Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--router-port"]));
	}

	[Theory]
	[InlineData("--config")]
	[InlineData("--config-file")]
	[InlineData("-config")]
	public void Parse_Config_AllAliases_CaptureFollowingValue(string flag)
	{
		AppOptions options = AppOptions.Parse([flag, "C:\\some\\path.json"]);
		Assert.Equal("C:\\some\\path.json", options.ConfigPath);
	}

	[Fact]
	public void Parse_Config_MissingValue_Throws()
	{
		_ = Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--config"]));
	}

	[Fact]
	public void Parse_ConfigValueThatLooksLikeAnotherFlag_IsStillConsumedAsTheValue()
	{
		// NextValue unconditionally takes the following token regardless of its shape — this
		// documents that behavior (rather than, say, --service being swallowed as --config's
		// value and silently disabling service mode).
		AppOptions options = AppOptions.Parse(["--config", "--service"]);
		Assert.Equal("--service", options.ConfigPath);
		Assert.False(options.Service);
	}
}
