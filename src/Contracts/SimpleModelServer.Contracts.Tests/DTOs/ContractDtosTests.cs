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
	public void ILoadedModelsInfo_Interface_HasAllRequiredMembers()
	{
		var type = typeof(ILoadedModelsInfo);
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
