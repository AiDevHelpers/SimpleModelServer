# Model Family Selection Feature

## Overview

This feature adds support for model family, variant, and size configuration with automatic name generation. You can now define models by their characteristics instead of hardcoding full model names.

## What Was Implemented

### 1. Core Configuration Fields

Added three new optional fields to model configuration:
- **`family`**: Model family (e.g., "deepseek", "qwen2.5", "llama2")
- **`variant`**: Model variant (e.g., "coder", "instruct", "chat")
- **`size`**: Model size (e.g., "0.5b", "1.3b", "3b", "7b")

### 2. Automatic Name Generation

When all three components are provided, the system automatically generates:

**Model Name**: `{family}-{variant}-{size}`
```
Example: qwen2.5-coder-0.5b
```

**Ollama Base Reference**: `{family}-{variant}:{size}`
```
Example: qwen2.5-coder:0.5b
```

### 3. Per-Pair Size Configuration

Different sizes can now be used for draft and main models in the same pair:

```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b"
    },
    "main": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "3b"
    }
  }
}
```

### 4. Backward Compatibility

All existing configurations continue to work unchanged. The new feature is purely additive:

```json
{
  "models": {
    "legacy": {
      "name": "llama2:13b",
      "base": "llama2:13b"
    },
    "modern": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "7b"
    }
  }
}
```

## Usage Examples

### Basic Family Configuration

```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b",
      "num_ctx": 2048,
      "keep_alive": "5m"
    },
    "main": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "3b",
      "num_ctx": 4096,
      "num_gpu": 20
    }
  }
}
```

Generates:
- Draft: `name: qwen2.5-coder-0.5b`, `base: qwen2.5-coder:0.5b`
- Main: `name: qwen2.5-coder-3b`, `base: qwen2.5-coder:3b`

### Mixed Families

```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b"
    },
    "main": {
      "family": "deepseek",
      "variant": "coder",
      "size": "6.7b"
    }
  }
}
```

### Override Auto-Generated Values

```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b",
      "name": "custom-name",
      "base": "custom-base:0.5b"
    }
  }
}
```

Result:
- `name: custom-name` (explicit override)
- `base: custom-base:0.5b` (explicit override)
- `family`, `variant`, `size` still stored for reference

## Implementation Details

### Files Modified

1. **`src/Common/SimpleModelServer.Library/Configuration/AppConfig.cs`**
   - Extended `ModelSourceConfig` class with `Family`, `Variant`, and `Size` properties
   - Updated `ParseModelSources()` to support auto-generation
   - Added `GenerateModelName()` helper method
   - Added `GenerateModelBase()` helper method

2. **`src/Common/SimpleModelServer.Library.Tests/AppConfigTests.cs`**
   - Added 6 comprehensive test cases:
     - `Load_ModelFamily_AutoGeneratesNameAndBase`
     - `Load_ModelFamily_AllSizesSupported`
     - `Load_ModelFamily_ExplicitNameOverridesGeneration`
     - `Load_ModelFamily_ExplicitBaseOverridesGeneration`
     - `Load_ModelFamily_PartialComponents_SkipsModel`
     - `Load_ModelFamily_MixedExplicitAndGenerated`

### Test Coverage

All 61 tests in the config test suite pass, including:
- Auto-generation with all three components
- Multiple size variations (0.5b, 1.3b, 3b, 6.7b, 7b, 13b)
- Override of auto-generated values
- Partial component handling (correctly skips incomplete specs)
- Mixed explicit and auto-generated configurations

## Configuration Resolution

The parsing logic follows this priority:

1. **Explicit fields first**
   - If `name` is provided → use it (skip auto-generation)
   - If `base` is provided → use it (skip auto-generation)

2. **Auto-generate if all components present**
   - If `family`, `variant`, and `size` all provided → generate name and base

3. **Skip model if incomplete**
   - If neither approach yields a name → model is not loaded

Example:
```json
{
  "draft": {
    "family": "qwen2.5",
    "variant": "coder"
    // Missing "size" → model skipped
  },
  "main": {
    "family": "qwen2.5",
    "variant": "coder",
    "size": "3b"
    // Complete → auto-generates name and base
  }
}
```

## Benefits

1. **Clarity**: Model definitions are self-documenting
2. **Flexibility**: Easy to swap model families or sizes
3. **Consistency**: Enforced naming convention across configurations
4. **Maintainability**: Less duplication in config files
5. **Per-Pair Optimization**: Different sizes for draft/main without multiple families
6. **Future-Proof**: Easy to add new families/variants without code changes

## Migration Guide

### Before (Legacy)

```json
{
  "models": {
    "draft": {
      "name": "qwen2.5-coder-0.5b",
      "base": "qwen2.5-coder:0.5b"
    }
  }
}
```

### After (New)

```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b"
    }
  }
}
```

Both work identically — no breaking changes. Migration is optional and can be done incrementally.
