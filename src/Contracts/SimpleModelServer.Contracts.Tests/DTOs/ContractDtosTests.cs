using SimpleModelServer.Contracts.Dtos;
using Xunit;
using System.Reflection;

namespace SimpleModelServer.Contracts.Tests;

/// <summary>
/// Comprehensive tests for contract DTOs.
/// Validates that all required interface members exist and are accessible.
/// </summary>
public class ContractDtosTests
{
    [Fact]
    public void IGlobalConfigInfo_Interface_HasAllRequiredMembers()
    {
        var type = typeof(IGlobalConfigInfo);
        Assert.NotNull(type.GetProperty("RouterHost", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("RouterPort", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("RouterModelName", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("OllamaPort", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("DraftModel", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("MainModel", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("EmbedModel", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("ContextSize", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("DefaultAgentType", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("ForcedAgentType", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("MdnsEnabled", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("MdnsServiceName", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void IModelSourceInfo_Interface_HasAllRequiredMembers()
    {
        var type = typeof(IModelSourceInfo);
        Assert.NotNull(type.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("Url", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("Settings", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void ISystemInfoResponse_Interface_HasAllRequiredMembers()
    {
        var type = typeof(ISystemInfoResponse);
        Assert.NotNull(type.GetProperty("GlobalConfig", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("Configuration", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("Capabilities", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("Timestamp", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void ISystemCapabilities_Interface_HasAllRequiredMembers()
    {
        var type = typeof(ISystemCapabilities);
        Assert.NotNull(type.GetProperty("Hardware", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("Models", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("AgentTypes", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("Features", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void IHardwareCapabilities_Interface_HasAllRequiredMembers()
    {
        var type = typeof(IHardwareCapabilities);
        Assert.NotNull(type.GetProperty("GpuAvailable", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("TotalVramGb", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("GpuModels", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("GpuName", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void ISystemConfiguration_Interface_HasAllRequiredMembers()
    {
        var type = typeof(ISystemConfiguration);
        Assert.NotNull(type.GetProperty("Global", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("ModelRoles", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("ModelSources", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("Agents", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("Settings", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void ILoadedModelsInfo_Interface_HasAllRequiredMembers()
    {
        var type = typeof(ILoadedModelsInfo);
        Assert.NotNull(type.GetProperty("LoadedModels", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("ActiveRequests", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("DraftModelName", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("MainModelName", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("EmbedModelName", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("DraftOnGpu", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("MainOnGpu", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("EmbedOnGpu", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("EstimatedMemoryGb", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("ConfigurationId", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("LoadedAt", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetProperty("IsComplete", BindingFlags.Public | BindingFlags.Instance));
    }
}
