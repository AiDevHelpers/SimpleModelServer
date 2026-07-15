namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Model source configuration information.
/// </summary>
public interface IModelSourceInfo
{
    /// <summary>Source type (e.g., ollama, huggingface, local).</summary>
    string? Type { get; }

    /// <summary>Provider URL or path.</summary>
    string? Url { get; }

    /// <summary>Source-specific settings.</summary>
    IReadOnlyDictionary<string, object?> Settings { get; }
}
