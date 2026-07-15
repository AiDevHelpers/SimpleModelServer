# Multi-Configuration System

## Overview

Implemented a flexible configuration system where users can define multiple named model configurations and switch between them based on their needs. Each configuration has a unique ID, user-friendly nickname, and complete model setup.

## Structure

### ModelConfiguration Class

```csharp
public class ModelConfiguration
{
    public string Id { get; set; }              // Unique identifier (e.g., "fast", "accurate")
    public string Nickname { get; set; }        // Display name (e.g., "Fast & Responsive")
    public bool IsDefault { get; set; }         // Is this the default configuration?
    public string? Description { get; set; }    // Purpose and characteristics
    
    public List<FamilyPair> Families { get; set; }      // Model families for this config
    public ModelConfig? Embed { get; set; }             // Embedding model
}
```

### Updated ModelDefinitions

```csharp
public class ModelDefinitions
{
    // Legacy support (backward compatible)
    public ModelConfig? Draft { get; set; }
    public ModelConfig? Main { get; set; }
    public ModelConfig? Embed { get; set; }
    public List<FamilyPair>? Families { get; set; }
    
    // New: Multiple named configurations
    public List<ModelConfiguration> Configurations { get; set; }
}
```

## Default Configurations

The default setup includes three pre-configured options:

### 1. **Fast & Responsive** (Default)
```
Id: "fast"
Nickname: "Fast & Responsive"
IsDefault: true
Description: "Small, fast models optimized for quick responses and low latency"

Model Pair:
- Family: qwen2.5-coder
- Draft: 0.5b (2048 context)
- Main: 3b (4096 context)
- Keep-Alive: 5m
```

**Use Case:** Quick interactions, rapid iteration, development workflows

### 2. **Accurate & Thorough**
```
Id: "accurate"
Nickname: "Accurate & Thorough"
IsDefault: false
Description: "Larger models optimized for accuracy and quality, accepts higher latency"

Model Pair:
- Family: deepseek-coder
- Draft: 1.3b (4096 context)
- Main: 6.7b (8192 context)
- Keep-Alive: 10m
```

**Use Case:** Complex problems, production code, thorough analysis

### 3. **Balanced**
```
Id: "balanced"
Nickname: "Balanced"
IsDefault: false
Description: "Medium-sized models balancing speed and quality for general use"

Model Pair:
- Family: qwen2.5-instruct
- Draft: 1.5b (3072 context)
- Main: 7b (6144 context)
- Keep-Alive: 7m
```

**Use Case:** General-purpose work, good speed/quality tradeoff

## JSON Configuration Format

```json
{
  "models": {
    "configurations": [
      {
        "id": "fast",
        "nickname": "Fast & Responsive",
        "isDefault": true,
        "description": "Small, fast models optimized for quick responses...",
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
          "keepAlive": "30m"
        }
      },
      {
        "id": "accurate",
        "nickname": "Accurate & Thorough",
        "isDefault": false,
        "description": "Larger models optimized for accuracy and quality...",
        ...
      },
      {
        "id": "balanced",
        "nickname": "Balanced",
        "isDefault": false,
        "description": "Medium-sized models balancing speed and quality...",
        ...
      }
    ]
  }
}
```

## Usage Patterns

### Runtime Configuration Selection

Applications can select a configuration in several ways:

```csharp
// 1. Use default configuration
var defaultConfig = configurations
    .FirstOrDefault(c => c.IsDefault);

// 2. Select by ID
var fastConfig = configurations
    .FirstOrDefault(c => c.Id == "fast");

// 3. Select by environment variable
string configId = Environment.GetEnvironmentVariable("MODEL_CONFIG") ?? "fast";
var selectedConfig = configurations
    .FirstOrDefault(c => c.Id == configId);

// 4. User selection from UI
var userSelectedConfig = configurations
    .FirstOrDefault(c => c.Nickname == userChoice);
```

### Configuration Switching

```csharp
// List available configurations
foreach (var config in configurations)
{
    Console.WriteLine($"{config.Id}: {config.Nickname}");
    Console.WriteLine($"  {config.Description}");
}

// Switch to a configuration
var selectedConfig = configurations.FirstOrDefault(c => c.Id == "accurate");
if (selectedConfig != null)
{
    // Load families and embed model from selectedConfig
    var families = selectedConfig.Families;
    var embedModel = selectedConfig.Embed;
}
```

## Custom Configuration Example

Users can add their own configurations to the list:

```json
{
  "models": {
    "configurations": [
      { ... existing configs ... },
      {
        "id": "experimental",
        "nickname": "Experimental",
        "isDefault": false,
        "description": "Testing new model combinations",
        "families": [
          {
            "family": "llama2",
            "variant": "chat",
            "keepAlive": "15m",
            "draft": { "size": "7b", ... },
            "main": { "size": "70b", ... }
          }
        ],
        "embed": { ... }
      }
    ]
  }
}
```

## Benefits

### 1. **User Choice**
- Switch between configurations without restarting
- Optimize for different use cases
- Easy to add new configurations

### 2. **Clear Semantics**
- ID for programmatic use
- Nickname for UI display
- Description for user guidance

### 3. **Default Handling**
- IsDefault flag identifies the default configuration
- Automatic fallback when no selection is made
- Predictable behavior

### 4. **Flexibility**
- Each configuration can have different families
- Different context sizes per configuration
- Independent embedding models

### 5. **Scalability**
- Add configurations without code changes
- Support unlimited number of configurations
- Easy to manage through configuration files

## Implementation Details

### Files Created/Modified

| File | Change | Purpose |
|------|--------|---------|
| `ModelConfiguration.cs` | NEW | Define individual configuration structure |
| `ModelDefinitions.cs` | MODIFIED | Support Configurations list |
| `DefaultConfig.cs` | MODIFIED | Provide three default configurations |

### Backward Compatibility

✅ **100% Backward Compatible**
- Old `Draft`, `Main`, `Families` properties still work
- New `Configurations` is optional
- Legacy configs continue to function

### Test Status

✅ **All 70 Library Tests Pass**
✅ **Config Project Builds Successfully**

## Configuration Selection Flow

```
Application Start
    ↓
Check Global Settings for Model Override
    ↓
If specified: Use that model name directly
    ↓
If not: Look for default configuration (IsDefault=true)
    ↓
Load families and embed model from selected configuration
    ↓
Auto-generate model names from family/variant/size
    ↓
Apply temperature, context, stop tokens, etc.
```

## Example: Switching Configurations at Runtime

```csharp
// Load all configurations
var configurations = config.Models.Configurations;

// Provide user options
Console.WriteLine("Available configurations:");
foreach (var cfg in configurations)
{
    var marker = cfg.IsDefault ? " (default)" : "";
    Console.WriteLine($"  {cfg.Id}: {cfg.Nickname}{marker}");
}

// Get user selection
Console.Write("Select configuration: ");
string userInput = Console.ReadLine() ?? "fast";

// Switch configuration
var selected = configurations.FirstOrDefault(c => c.Id == userInput)
    ?? configurations.FirstOrDefault(c => c.IsDefault);

if (selected != null)
{
    Console.WriteLine($"\nUsing: {selected.Nickname}");
    Console.WriteLine($"{selected.Description}");
    
    // Load models from selected configuration
    foreach (var family in selected.Families)
    {
        Console.WriteLine($"  - {family.Family}/{family.Variant}");
        Console.WriteLine($"    Draft: {family.Draft.Size}");
        Console.WriteLine($"    Main: {family.Main.Size}");
    }
}
```

## Adding New Configurations

Users can add configurations by:

1. **Editing the config file:**
   ```json
   {
     "models": {
       "configurations": [
         { ... existing ... },
         {
           "id": "my-config",
           "nickname": "My Config",
           "isDefault": false,
           "description": "My custom configuration",
           "families": [ ... ],
           "embed": { ... }
         }
       ]
     }
   }
   ```

2. **Programmatically:**
   ```csharp
   var newConfig = new ModelConfiguration
   {
       Id = "my-config",
       Nickname = "My Config",
       IsDefault = false,
       Description = "My custom configuration",
       Families = new List<FamilyPair> { ... },
       Embed = new ModelConfig { ... }
   };
   
   configurations.Add(newConfig);
   ```

## Future Enhancements

Possible future features:
- Configuration management UI
- Import/export configurations
- Configuration templates
- Performance metrics per configuration
- Automatic configuration recommendations

## Summary

The multi-configuration system provides:
- ✅ Multiple named configurations
- ✅ Easy runtime switching
- ✅ Clear user-friendly nicknames
- ✅ Unique IDs for programmatic use
- ✅ Default configuration support
- ✅ Full backward compatibility
- ✅ Easy to extend and customize

Users can now optimize for different scenarios (speed vs. accuracy) without restarting or modifying core application code!
