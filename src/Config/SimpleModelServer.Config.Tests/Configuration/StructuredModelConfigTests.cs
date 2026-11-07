using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using Xunit;

namespace SimpleModelServer.Config.Tests;

/// <summary>
///     Covers the structured <c>models.configurations</c> schema (what <see cref="DefaultConfig" />
///     writes on first run) and the Ollama base naming it must produce. Before this, the runtime
///     parsed only the flat schema, so a fresh install had no model sources and every custom model
///     name was pulled literally, which fails with "pull model manifest: file does not exist".
/// </summary>
public sealed class StructuredModelConfigTests
{
    private static string WriteTemp(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"smsconfig-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StockConfig_FreshInstall_ResolvesDraftMainAndEmbedRoles()
    {
        string path = Path.Combine(Path.GetTempPath(), $"smsconfig-stock-{Guid.NewGuid():N}.json");
        try
        {
            DefaultConfig.EnsureConfig(path);
            AppConfig config = AppConfig.Load(path);

            Assert.Equal("qwen2.5-coder-0.5b-chat", config.ModelRoles["draft"]);
            Assert.Equal("qwen2.5-coder-3b-chat", config.ModelRoles["main"]);
            Assert.Equal("nomic-embed-text", config.ModelRoles["embed"]);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StockConfig_FreshInstall_BasesAreRealRegistryTags()
    {
        string path = Path.Combine(Path.GetTempPath(), $"smsconfig-stock-{Guid.NewGuid():N}.json");
        try
        {
            DefaultConfig.EnsureConfig(path);
            AppConfig config = AppConfig.Load(path);

            // Regression: the "-chat" suffix belongs on the local name only, never the registry base.
            Assert.Equal("qwen2.5-coder:0.5b", config.ModelSources["qwen2.5-coder-0.5b-chat"].Base);
            Assert.Equal("qwen2.5-coder:3b", config.ModelSources["qwen2.5-coder-3b-chat"].Base);
            Assert.Equal("nomic-embed-text", config.ModelSources["nomic-embed-text"].Base);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StockConfig_FreshInstall_CarriesSizingAndKeepAlive()
    {
        string path = Path.Combine(Path.GetTempPath(), $"smsconfig-stock-{Guid.NewGuid():N}.json");
        try
        {
            DefaultConfig.EnsureConfig(path);
            AppConfig config = AppConfig.Load(path);

            IModelSourceConfig draft = config.ModelSources["qwen2.5-coder-0.5b-chat"];
            IModelSourceConfig main = config.ModelSources["qwen2.5-coder-3b-chat"];
            Assert.Equal(2048, draft.NumCtx);
            Assert.Equal(4096, main.NumCtx);
            Assert.Equal("5m", draft.KeepAlive); // family pair keepAliveMinutes = 5
            Assert.Contains("```json", draft.Stop);
            Assert.Equal("qwen2.5", draft.Family);
            Assert.Equal("coder", draft.Variant);
            Assert.Equal("0.5b", draft.Size);

            Assert.Equal("30m", config.ModelSources["nomic-embed-text"].KeepAlive); // embed keepAliveMinutes = 30
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StructuredConfig_SelectsConfigurationById()
    {
        string path = WriteTemp("""
        {
          "global": { "configurationId": 2 },
          "models": { "configurations": [
            { "id": 1, "isDefault": true, "families": [
                { "family": 0, "variant": "coder", "draft": { "size": "0.5b" }, "main": { "size": "3b" } } ] },
            { "id": 2, "isDefault": false, "families": [
                { "family": 1, "variant": "coder", "draft": { "size": "1.3b" }, "main": { "size": "6.7b" } } ] }
          ] }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.Equal("deepseek-coder-1.3b-chat", config.ModelRoles["draft"]);
            Assert.Equal("deepseek-coder:6.7b", config.ModelSources[config.ModelRoles["main"]].Base);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StructuredConfig_NoIdMatch_FallsBackToIsDefault()
    {
        string path = WriteTemp("""
        {
          "global": { "configurationId": 99 },
          "models": { "configurations": [
            { "id": 1, "isDefault": false, "families": [
                { "family": 1, "variant": "coder", "draft": { "size": "1.3b" } } ] },
            { "id": 2, "isDefault": true, "families": [
                { "family": 0, "variant": "coder", "draft": { "size": "0.5b" } } ] }
          ] }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.Equal("qwen2.5-coder-0.5b-chat", config.ModelRoles["draft"]);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StructuredConfig_ExplicitNameAndBase_AreRespected()
    {
        string path = WriteTemp("""
        {
          "models": { "configurations": [
            { "id": 1, "isDefault": true, "families": [
                { "family": 6, "variant": "", "draft": {
                    "name": "my-draft", "base": "hf.co/someone/model:Q4" } } ] }
          ] }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.Equal("my-draft", config.ModelRoles["draft"]);
            Assert.Equal("hf.co/someone/model:Q4", config.ModelSources["my-draft"].Base);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StructuredConfig_CustomFamilyWithoutBase_IsSkipped()
    {
        // Custom has no registry family name, so the base can't be derived. Skipping (rather than
        // guessing) means no bogus `ollama pull` is attempted.
        string path = WriteTemp("""
        {
          "models": { "configurations": [
            { "id": 1, "isDefault": true, "families": [
                { "family": 6, "variant": "coder", "draft": { "size": "0.5b" } } ] }
          ] }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.False(config.ModelRoles.ContainsKey("draft"));
            Assert.DoesNotContain(config.ModelSources.Keys, k => k.Contains("coder"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StructuredConfig_OnlyMainGiven_DoesNotInventDraft()
    {
        string path = WriteTemp("""
        {
          "models": { "configurations": [
            { "id": 1, "isDefault": true, "families": [
                { "family": 0, "variant": "coder", "main": { "size": "3b" } } ] }
          ] }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.False(config.ModelRoles.ContainsKey("draft"));
            Assert.Equal("qwen2.5-coder-3b-chat", config.ModelRoles["main"]);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StructuredConfig_NoDraftOrMain_UsesDefaultSizes()
    {
        string path = WriteTemp("""
        {
          "models": { "configurations": [
            { "id": 1, "isDefault": true, "families": [ { "family": 0, "variant": "coder" } ] }
          ] }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.Equal("qwen2.5-coder-0.5b-chat", config.ModelRoles["draft"]);
            Assert.Equal("qwen2.5-coder-3b-chat", config.ModelRoles["main"]);
        }
        finally
        {
            Delete(path);
        }
    }

    [Theory]
    [InlineData(0, "-1")]   // 0 = always hot (schema docs) -> Ollama's forever
    [InlineData(5, "5m")]
    public void StructuredConfig_KeepAliveMinutes_MapToOllamaDuration(int minutes, string expected)
    {
        string path = WriteTemp($$"""
        {
          "models": { "configurations": [
            { "id": 1, "isDefault": true, "families": [
                { "family": 0, "variant": "coder", "keepAliveMinutes": {{minutes}}, "draft": { "size": "0.5b" } } ] }
          ] }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.Equal(expected, config.ModelSources["qwen2.5-coder-0.5b-chat"].KeepAlive);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StructuredConfig_FamilyAcceptsEnumName_AsWellAsOrdinal()
    {
        string path = WriteTemp("""
        {
          "models": { "configurations": [
            { "id": 1, "isDefault": true, "families": [
                { "family": "Qwen25", "variant": "coder", "draft": { "size": "0.5b" } } ] }
          ] }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.Equal("qwen2.5-coder-0.5b-chat", config.ModelRoles["draft"]);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StockConfig_ListsBothProfiles_WithDefaultFlagged()
    {
        string path = Path.Combine(Path.GetTempPath(), $"smsconfig-profiles-{Guid.NewGuid():N}.json");
        try
        {
            DefaultConfig.EnsureConfig(path);
            AppConfig config = AppConfig.Load(path);

            Assert.Equal(2, config.ModelProfiles.Count);
            Assert.Equal(1, config.ModelProfiles[0].Id);
            Assert.True(config.ModelProfiles[0].IsDefault);
            Assert.Equal("Fast & Responsive", config.ModelProfiles[0].Nickname);
            Assert.Equal(2, config.ModelProfiles[1].Id);
            Assert.False(config.ModelProfiles[1].IsDefault);
            Assert.Equal(1, config.Global.ConfigurationId);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StructuredConfig_ConfigurationIdFromGlobal_SelectsProfile()
    {
        string path = WriteTemp("""
        {
          "global": { "configurationId": 2 },
          "models": { "configurations": [
            { "id": 1, "nickname": "Fast", "isDefault": true, "families": [
                { "family": 0, "variant": "coder", "draft": { "size": "0.5b" } } ] },
            { "id": 2, "nickname": "Accurate", "isDefault": false, "families": [
                { "family": 1, "variant": "coder", "draft": { "size": "1.3b" } } ] }
          ] }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.Equal(2, config.Global.ConfigurationId);
            Assert.Equal("deepseek-coder-1.3b-chat", config.ModelRoles["draft"]);
            Assert.Equal(new[] { "Fast", "Accurate" }, config.ModelProfiles.Select(p => p.Nickname).ToArray());
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StructuredConfig_SizingTemperatureAndTopP_AreCarried()
    {
        string path = Path.Combine(Path.GetTempPath(), $"smsconfig-sampling-{Guid.NewGuid():N}.json");
        try
        {
            DefaultConfig.EnsureConfig(path);
            AppConfig config = AppConfig.Load(path);

            IModelSourceConfig draft = config.ModelSources["qwen2.5-coder-0.5b-chat"];
            Assert.Equal(0.1, draft.Temperature);
            Assert.Equal(0.9, draft.TopP);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void FlatSchema_ReadsTemperatureAndTopP_AndHasNoProfiles()
    {
        string path = WriteTemp("""
        {
          "models": {
            "draft": { "name": "tiny", "base": "qwen2.5-coder:0.5b", "temperature": 0.2, "top_p": 0.8 }
          }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.Equal(0.2, config.ModelSources["tiny"].Temperature);
            Assert.Equal(0.8, config.ModelSources["tiny"].TopP);
            Assert.Empty(config.ModelProfiles);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void FlatSchema_StillLoads_AndBaseOmitsChatSuffix()
    {
        // Backward compatibility: the flat role-keyed shape already on users' machines keeps working,
        // and auto-generated bases get the same suffix-free registry form as the structured path.
        string path = WriteTemp("""
        {
          "models": {
            "draft": { "family": "qwen2.5", "variant": "coder", "size": "0.5b" },
            "main":  { "family": "qwen2.5", "variant": "coder", "size": "3b", "chatSuffix": "-instruct" }
          }
        }
        """);
        try
        {
            AppConfig config = AppConfig.Load(path);

            Assert.Equal("qwen2.5-coder-0.5b-chat", config.ModelRoles["draft"]);
            Assert.Equal("qwen2.5-coder:0.5b", config.ModelSources["qwen2.5-coder-0.5b-chat"].Base);
            Assert.Equal("qwen2.5-coder-3b-instruct", config.ModelRoles["main"]);
            Assert.Equal("qwen2.5-coder:3b", config.ModelSources["qwen2.5-coder-3b-instruct"].Base);
        }
        finally
        {
            Delete(path);
        }
    }
}
