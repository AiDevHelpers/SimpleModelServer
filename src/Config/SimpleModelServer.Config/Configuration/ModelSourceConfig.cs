using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed class ModelSourceConfig : IModelSourceConfig
{
	public string Base { get; init; } = "";
	public int? NumCtx { get; init; }
	public string? KeepAlive { get; init; }

	/// <summary>
	///     Explicit GPU layer count pin. Null (the default) means "let GpuBudgetPlanner decide
	///     based on detected VRAM"; set this to bypass auto-planning for just this one model
	///     (e.g. to force a model fully onto CPU, or force a specific layer count you've already
	///     hand-tuned).
	/// </summary>
	public int? NumGpu { get; init; }

	public IReadOnlyList<string> Stop { get; init; } = [];

	/// <summary>
	///     Model family (e.g., "deepseek", "qwen2.5"). Used with <see cref="Variant" /> and
	///     <see cref="Size" /> to auto-generate the model name and base if not explicitly provided.
	/// </summary>
	public string? Family { get; init; }

	/// <summary>
	///     Model variant (e.g., "coder", "instruct"). Used with <see cref="Family" /> and
	///     <see cref="Size" /> to auto-generate the model name and base if not explicitly provided.
	/// </summary>
	public string? Variant { get; init; }

	/// <summary>
	///     Model size (e.g., "0.5b", "3b", "7b"). Used with <see cref="Family" /> and
	///     <see cref="Variant" /> to auto-generate the model name and base if not explicitly provided.
	/// </summary>
	public string? Size { get; init; }

	/// <summary>
	///     Chat suffix to append when auto-generating model name and base (e.g., "-chat", "-instruct").
	///     Defaults to "-chat" when not specified. Set to empty string to disable.
	///     Only used when auto-generating from <see cref="Family" />, <see cref="Variant" />, and <see cref="Size" />.
	/// </summary>
	public string? ChatSuffix { get; init; }

	/// <inheritdoc />
	public double? Temperature { get; init; }

	/// <inheritdoc />
	public double? TopP { get; init; }
}
