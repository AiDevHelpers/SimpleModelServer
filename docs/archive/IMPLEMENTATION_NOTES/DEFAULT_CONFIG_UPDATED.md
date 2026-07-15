# Updated Default Configuration

## Summary

The default `SimpleModelServer.config.json` has been updated to use the new model family/variant/size format with automatic chat suffix generation instead of explicit model names.

## Default Configuration (Generated)

When SimpleModelServer starts for the first time, it will generate this default configuration:

```json
{
  "global": {
    "defaultAgentType": "code",
    "gpuLayers": "auto",
    "cpuThreads": "auto",
    "draftModel": "qwen2.5-coder-0.5b-chat",
    "mainModel": "qwen2.5-coder-3b-chat",
    "ollamaPort": 11434,
    "routerPort": 11435,
    "routerHost": "localhost",
    "routerModelName": "HelperModel",
    "trustedNetworks": [],
    "mdnsEnabled": true
  },
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b",
      "temperature": 0.1,
      "topP": 0.9,
      "numCtx": 2048,
      "keepAlive": "5m",
      "stop": [
        "```json",
        "{\"name\":",
        "\"arguments\":"
      ]
    },
    "main": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "3b",
      "temperature": 0.1,
      "topP": 0.9,
      "numCtx": 4096,
      "keepAlive": "5m",
      "stop": [
        "```json",
        "{\"name\":",
        "\"arguments\":",
        "{\"function\"",
        "\"tool_call"
      ]
    },
    "embed": {
      "name": "nomic-embed-text",
      "numCtx": 8192,
      "keepAlive": "30m",
      "stop": []
    }
  },
  "agents": {
    "code": {
      "systemPrompt": "You are a code assistant. Complete every task with the information given. Never ask clarifying questions about parameters, values, or inputs — make a reasonable assumption and proceed. When reviewing or fixing code, mentally trace through each function with a concrete example input. For every conditional check (callable(), isinstance(), ==, etc.), verify the operand's actual runtime type is what the check expects. However, if you identify a structural or architectural flaw you cannot fix without redesigning the overall program flow, output QUESTION: followed by one specific question for the reviewer.",
      "temperature": 0.1,
      "sampling": {
        "topP": 0.9
      }
    },
    "debug": {
      "systemPrompt": "You are a debugging assistant. Trace through code with concrete examples, verify types at each conditional, and identify the root cause before proposing a fix.",
      "temperature": 0.15,
      "sampling": {
        "topP": 0.85
      }
    },
    "planning": {
      "systemPrompt": "You are a planning assistant. Think step by step before proposing solutions. Identify dependencies and risks, then produce a clear, ordered action plan.",
      "temperature": 0.2,
      "sampling": {
        "topP": 0.9
      }
    }
  }
}
```

## What Changed

### Before (Explicit Names)
```json
{
  "models": {
    "draft": {
      "name": "qwen2.5-coder-0.5b-chat",
      "base": "qwen2.5-coder:0.5b",
      "temperature": 0.1,
      ...
    },
    "main": {
      "name": "qwen2.5-coder-3b-chat",
      "base": "qwen2.5-coder:3b",
      "temperature": 0.1,
      ...
    }
  }
}
```

### After (Family/Variant/Size)
```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b",
      "temperature": 0.1,
      ...
    },
    "main": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "3b",
      "temperature": 0.1,
      ...
    }
  }
}
```

## Auto-Generated References

With the new format, SimpleModelServer automatically generates:

### Draft Model
- **Family**: `qwen2.5`
- **Variant**: `coder`
- **Size**: `0.5b`
- **Auto-Generated Name**: `qwen2.5-coder-0.5b-chat`
- **Auto-Generated Base**: `qwen2.5-coder-chat:0.5b`

### Main Model
- **Family**: `qwen2.5`
- **Variant**: `coder`
- **Size**: `3b`
- **Auto-Generated Name**: `qwen2.5-coder-3b-chat`
- **Auto-Generated Base**: `qwen2.5-coder-chat:3b`

## Benefits of Updated Config

1. **Clearer Intent**: Model definition clearly shows family, variant, and size
2. **Easier Customization**: Change size or variant without duplicating model names
3. **Automatic Suffix**: `-chat` is automatically added (configurable per model)
4. **Maintainability**: Less duplication in configuration
5. **Discovery**: Self-documenting model configurations

## Global Settings

The `global.draftModel` and `global.mainModel` still reference the auto-generated model names:

```json
{
  "global": {
    "draftModel": "qwen2.5-coder-0.5b-chat",
    "mainModel": "qwen2.5-coder-3b-chat"
  }
}
```

These values point to the names that will be auto-generated from the family/variant/size components in the models section.

## Backward Compatibility

✅ **Fully Backward Compatible**
- Existing configurations with explicit `name` and `base` continue to work unchanged
- The new format is purely additive
- New installations use the recommended format
- Existing installations are unaffected

## Testing

✅ **All tests pass (70/70)**
- 9 model family tests verify auto-generation works correctly
- Default configuration generation tested during build

## Usage

When SimpleModelServer starts for the first time on a new system:

1. Creates the configuration directory (if needed)
2. Generates `SimpleModelServer.config.json` with the new default format
3. Uses the family/variant/size components to auto-generate model names and bases
4. Automatically adds the `-chat` suffix to match Ollama model naming
5. Applies all other settings (context size, keep-alive, temperature, etc.)

## Customization Example

To use different models, simply update the family/variant/size:

```json
{
  "models": {
    "draft": {
      "family": "deepseek",
      "variant": "coder",
      "size": "1.3b",
      "numCtx": 2048
    },
    "main": {
      "family": "deepseek",
      "variant": "coder",
      "size": "6.7b",
      "numCtx": 4096
    }
  }
}
```

This will auto-generate:
- Draft: `deepseek-coder-1.3b-chat` → `deepseek-coder-chat:1.3b`
- Main: `deepseek-coder-6.7b-chat` → `deepseek-coder-chat:6.7b`

## Files Updated

- ✅ `src/Config/SimpleModelServer.Config/ModelConfig.cs` - Added Family, Variant, Size, ChatSuffix properties
- ✅ `src/Config/SimpleModelServer.Config/DefaultConfig.cs` - Updated CreateStockConfig() to use new format
- ✅ All tests passing (70/70)
- ✅ Backward compatible with existing configurations
