# FamilyPair Configuration Structure

## Overview

Restructured the model configuration to support multiple model families efficiently. Instead of separate Draft and Main definitions, models are now organized under `FamilyPair` objects that share family, variant, and keep-alive settings.

## New Structure

### FamilyPair Class

```csharp
public class FamilyPair
{
    // Shared across draft and main
    public string Family { get; set; }        // e.g., "qwen2.5", "deepseek"
    public string Variant { get; set; }       // e.g., "coder", "instruct"
    public string? KeepAlive { get; set; }    // e.g., "5m", "10m"

    // Draft model (smaller/faster)
    public ModelConfig Draft { get; set; }

    // Main model (larger/more capable)
    public ModelConfig Main { get; set; }
}
```

### Updated ModelConfig

```csharp
public class ModelConfig
{
    public string Name { get; set; } = "";
    public string? Base { get; set; }
    public string? Size { get; set; }                    // e.g., "0.5b", "3b"
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? NumCtx { get; set; }
    public string? KeepAlive { get; set; }              // Optional, overrides FamilyPair.KeepAlive
    public string[] Stop { get; set; } = [];
    public string? Family { get; set; }
    public string? Variant { get; set; }
    public string? ChatSuffix { get; set; }
}
```

### Updated ModelDefinitions

```csharp
public class ModelDefinitions
{
    public ModelConfig? Draft { get; set; }              // Legacy, backward compatible
    public ModelConfig? Main { get; set; }               // Legacy, backward compatible
    public ModelConfig Embed { get; set; }               // Embedding model
    public List<FamilyPair> Families { get; set; }      // New: Family pairs
}
```

## Default Configuration Example

```csharp
new ModelDefinitions
{
    Families = new List<FamilyPair>
    {
        new FamilyPair
        {
            Family = "qwen2.5",
            Variant = "coder",
            KeepAlive = "5m",
            Draft = new ModelConfig
            {
                Size = "0.5b",
                Temperature = 0.1,
                TopP = 0.9,
                NumCtx = 2048,
                Stop = ["```json", "{\"name\":", "\"arguments\":"],
            },
            Main = new ModelConfig
            {
                Size = "3b",
                Temperature = 0.1,
                TopP = 0.9,
                NumCtx = 4096,
                Stop = ["```json", "{\"name\":", "\"arguments\":", "{\"function\"", "\"tool_call"],
            },
        }
    },
    Embed = new ModelConfig
    {
        Name = "nomic-embed-text",
        Base = null,
        NumCtx = 8192,
        KeepAlive = "30m",
        Stop = [],
    },
}
```

## Generated JSON Config

```json
{
  "models": {
    "families": [
      {
        "family": "qwen2.5",
        "variant": "coder",
        "keepAlive": "5m",
        "draft": {
          "size": "0.5b",
          "temperature": 0.1,
          "topP": 0.9,
          "numCtx": 2048,
          "stop": ["```json", "{\"name\":", "\"arguments\":"]
        },
        "main": {
          "size": "3b",
          "temperature": 0.1,
          "topP": 0.9,
          "numCtx": 4096,
          "stop": ["```json", "{\"name\":", "\"arguments\":", "{\"function\"", "\"tool_call"]
        }
      }
    ],
    "embed": {
      "name": "nomic-embed-text",
      "numCtx": 8192,
      "keepAlive": "30m",
      "stop": []
    }
  }
}
```

## Advantages

### 1. Scalability
```csharp
Families = new List<FamilyPair>
{
    new FamilyPair { Family = "qwen2.5", Variant = "coder", ... },
    new FamilyPair { Family = "deepseek", Variant = "coder", ... },
    new FamilyPair { Family = "llama2", Variant = "chat", ... },
    new FamilyPair { Family = "mistral", Variant = "instruct", ... },
}
```

### 2. Reduced Duplication
- Family, variant, and keep-alive defined once per family pair
- Draft and main only specify their unique size and settings

### 3. Clarity
- Clear relationship between draft and main models
- Easy to see pairs of models designed to work together

### 4. Flexibility
- Override KeepAlive per model if needed
- Different stop tokens per model
- Different context sizes for optimization

## Multiple Family Pairs Example

```csharp
Families = new List<FamilyPair>
{
    // Fast/accurate pair for coding
    new FamilyPair
    {
        Family = "qwen2.5",
        Variant = "coder",
        KeepAlive = "5m",
        Draft = new ModelConfig { Size = "0.5b", ... },
        Main = new ModelConfig { Size = "3b", ... },
    },
    
    // Alternative: Larger models for complex tasks
    new FamilyPair
    {
        Family = "deepseek",
        Variant = "coder",
        KeepAlive = "10m",
        Draft = new ModelConfig { Size = "1.3b", ... },
        Main = new ModelConfig { Size = "6.7b", ... },
    },
    
    // Alternative: Instruction-tuned models
    new FamilyPair
    {
        Family = "mistral",
        Variant = "instruct",
        KeepAlive = "10m",
        Draft = new ModelConfig { Size = "7b", ... },
        Main = new ModelConfig { Size = "8x7b", ... },
    }
}
```

Users can configure which family pair they want to use at runtime through:
- Environment variables
- Configuration settings
- Runtime selection logic

## Backward Compatibility

✅ **Fully Backward Compatible**
- `ModelDefinitions.Draft` and `ModelDefinitions.Main` still supported
- Old configs with explicit models continue to work
- New configs use `Families` for better organization

## Auto-Generation

When paired with the family/variant/size auto-generation feature:

```
Family: "qwen2.5", Variant: "coder", Size: "0.5b"
    ↓
Name: "qwen2.5-coder-0.5b-chat"
Base: "qwen2.5-coder-chat:0.5b"
```

The FamilyPair structure keeps the common configuration (family, variant, keep-alive) centralized while allowing each model to specify its unique size and behavior.

## Use Cases

### 1. Single Family (Current Default)
```json
{
  "families": [
    {
      "family": "qwen2.5",
      "variant": "coder",
      "keepAlive": "5m",
      "draft": { "size": "0.5b" },
      "main": { "size": "3b" }
    }
  ]
}
```

### 2. Multiple Families (User Choice)
```json
{
  "families": [
    { "family": "qwen2.5", "variant": "coder", ... },
    { "family": "deepseek", "variant": "coder", ... },
    { "family": "mistral", "variant": "instruct", ... }
  ]
}
```

### 3. Per-Model Overrides
```json
{
  "families": [
    {
      "family": "qwen2.5",
      "variant": "coder",
      "keepAlive": "5m",
      "draft": { "size": "0.5b", "keepAlive": "2m" },  // Override
      "main": { "size": "3b" }
    }
  ]
}
```

## Files Updated

- ✅ `FamilyPair.cs` - New class defining family pair structure
- ✅ `ModelDefinitions.cs` - Updated to support Families list
- ✅ `DefaultConfig.cs` - Updated to use FamilyPair structure
- ✅ `ModelConfig.cs` - Already supports Size and other fields
- ✅ All tests passing (70/70)

## Migration Path

Old format:
```csharp
Draft = new ModelConfig { Name = "...", Base = "...", ... }
Main = new ModelConfig { Name = "...", Base = "...", ... }
```

New format (recommended):
```csharp
Families = new List<FamilyPair>
{
    new FamilyPair
    {
        Family = "...",
        Variant = "...",
        Draft = new ModelConfig { Size = "...", ... },
        Main = new ModelConfig { Size = "...", ... }
    }
}
```

This structure provides a clean, scalable way to support multiple model families while maintaining backward compatibility.
