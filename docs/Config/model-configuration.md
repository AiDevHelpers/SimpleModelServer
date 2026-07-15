# Model Configuration Guide

## Overview

SimpleModelServer supports flexible model configuration through JSON. Models can be specified either explicitly or through automatic name generation using family, variant, and size components.

## Configuration Format

Models are defined in the `"models"` section of your configuration file:

```json
{
  "models": {
    "draft": {
      // model definition
    },
    "main": {
      // model definition
    }
  }
}
```

The key (`"draft"`, `"main"`, etc.) is the role identifier that maps to the actual model name.

## Model Specification Methods

### Method 1: Explicit Name and Base (Legacy)

Specify the model name and Ollama base reference directly:

```json
{
  "models": {
    "draft": {
      "name": "llama2:7b",
      "base": "llama2:7b",
      "num_ctx": 2048,
      "keep_alive": "10m"
    }
  }
}
```

### Method 2: Auto-Generated Name and Base (Recommended)

Specify the model family, variant, and size. The name and base will be automatically generated:

```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b",
      "num_ctx": 2048,
      "keep_alive": "10m"
    },
    "main": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "3b",
      "num_gpu": 20
    }
  }
}
```

**Auto-generation rules:**
- **Model name**: `{family}-{variant}-{size}{chatSuffix}`
  - Example: `qwen2.5-coder-0.5b-chat`
  - Default chatSuffix: `-chat`
- **Ollama base**: `{family}-{variant}{chatSuffix}:{size}`
  - Example: `qwen2.5-coder-chat:0.5b`
  - Default chatSuffix: `-chat`

### Method 3: Mixed Approach

Use auto-generation but override specific fields:

```json
{
  "models": {
    "draft": {
      "family": "deepseek",
      "variant": "coder",
      "size": "1.3b",
      "base": "deepseek-coder-custom:1.3b"
    }
  }
}
```

This will:
- Auto-generate the name: `deepseek-coder-1.3b`
- Use the explicit base: `deepseek-coder-custom:1.3b`

## Supported Model Families and Variants

### Common Examples

**Qwen2.5 Family:**
```json
{
  "draft": {
    "family": "qwen2.5",
    "variant": "coder",
    "size": "0.5b"
  },
  "main": {
    "family": "qwen2.5",
    "variant": "instruct",
    "size": "7b"
  }
}
```

**DeepSeek Family:**
```json
{
  "draft": {
    "family": "deepseek",
    "variant": "coder",
    "size": "1.3b"
  },
  "main": {
    "family": "deepseek",
    "variant": "instruct",
    "size": "6.7b"
  }
}
```

**Mixed Family (Draft + Main):**
```json
{
  "draft": {
    "family": "qwen2.5",
    "variant": "coder",
    "size": "0.5b"
  },
  "main": {
    "family": "llama2",
    "variant": "chat",
    "size": "13b"
  }
}
```

## Chat Suffix Configuration

When auto-generating model names and bases, a chat suffix (default: `-chat`) is automatically appended. This allows proper handling of chat variants in Ollama.

**Customizing the chat suffix:**

```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b",
      "chatSuffix": "-instruct"
    }
  }
}
```

Result: `name: qwen2.5-coder-0.5b-instruct`, `base: qwen2.5-coder-instruct:0.5b`

**Disabling the chat suffix:**

```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b",
      "chatSuffix": ""
    }
  }
}
```

Result: `name: qwen2.5-coder-0.5b`, `base: qwen2.5-coder:0.5b`

## Per-Pair Size Configuration

You can now configure different sizes for draft and main models in the same pair:

```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b",
      "num_ctx": 2048,
      "keep_alive": "5m",
      "num_gpu": "auto"
    },
    "main": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "3b",
      "num_ctx": 4096,
      "keep_alive": "10m",
      "num_gpu": 20
    }
  }
}
```

This configuration:
- Runs a small 0.5b model for draft/preview
- Runs a larger 3b model for main generation
- Each can have independent hardware settings and context sizes

## Complete Configuration Example

```json
{
  "global": {
    "defaultAgentType": "code",
    "gpuLayers": "auto",
    "ollamaPort": 11434,
    "routerPort": 11435,
    "contextSize": 4096,
    "draftHandoffTimeoutMs": 900,
    "draftMode": "Preview"
  },
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "0.5b",
      "num_ctx": 2048,
      "keep_alive": "5m",
      "stop": ["<|im_end|>"]
    },
    "main": {
      "family": "qwen2.5",
      "variant": "coder",
      "size": "3b",
      "num_ctx": 4096,
      "keep_alive": "10m",
      "num_gpu": 20,
      "stop": ["<|im_end|>"]
    },
    "embed": {
      "name": "nomic-embed-text",
      "base": "nomic-embed-text"
    }
  },
  "agents": {
    "code": {
      "system_prompt": "You are a helpful coding assistant.",
      "temperature": 0.2
    }
  }
}
```

## Inference Provider (Ollama / LM Studio)

`global.provider` selects which backend actually generates tokens for the draft and main models. It's optional — every existing config without a `"provider"` section keeps working unchanged, defaulting to Ollama on `127.0.0.1:11434`.

```json
{
  "global": {
    "provider": {
      "type": "ollama",
      "host": "127.0.0.1",
      "port": 11434
    }
  }
}
```

To use LM Studio instead:

```json
{
  "global": {
    "provider": {
      "type": "lmstudio",
      "host": "127.0.0.1",
      "port": 1234,
      "apiKey": "optional-if-your-lmstudio-server-requires-one"
    }
  }
}
```

Fields:
- `type` — `"ollama"` (default) or `"lmstudio"`.
- `host` — defaults to `127.0.0.1`.
- `port` — defaults to `11434` for Ollama, `1234` for LM Studio (LM Studio's conventional default) if omitted.
- `apiKey` — optional; sent as `Authorization: Bearer <apiKey>` if your LM Studio server has API-key auth enabled.

**Ollama vs. LM Studio, what's different:**
- Both draft and main models run through whichever provider is selected — there's no per-model mixing of backends.
- **LM Studio is health-check-only.** SimpleModelServer never starts, kills, or pulls models for LM Studio the way it does for Ollama — LM Studio is expected to already be running with its local server enabled (Developer tab → "Start Server" in the LM Studio app). SimpleModelServer only probes reachability (`GET /v1/models`).
- Ollama's per-request `num_gpu`/`num_thread`/`keep_alive` controls (from your model definitions above) have no equivalent in LM Studio's OpenAI-compatible API and are silently ignored when `type` is `"lmstudio"` — configure GPU offload and idle-unload behavior in LM Studio's own model settings instead.
- Because LM Studio has no keep-alive/warmup control, there's no "warming up, retry in a few seconds" response for it — a cold LM Studio model load is just a slower first request.
- Ollama's model-management endpoints (`/api/pull`, `/api/create`, `/api/tags`, `/api/ps`, `/api/show`, `/api/embed`, etc.) are Ollama-native passthrough and are not currently wired to work against LM Studio — only chat/completion generation through the draft-pair pipeline is supported for `type: "lmstudio"`.
- **Provider selection is config-file-only.** It's read once at startup (`global.provider`) and isn't exposed through the `/admin/config` hot-reload API — changing it requires a server restart, since it determines the whole generation/health-monitoring wiring constructed at startup.

## Configuration Resolution Order

When generating model information, SimpleModelServer follows this priority:

1. **Explicit fields** (if provided)
   - `name` (overrides auto-generation)
   - `base` (overrides auto-generation)

2. **Family/Variant/Size components** (if all three are provided)
   - Auto-generates `name`: `{family}-{variant}-{size}`
   - Auto-generates `base`: `{family}-{variant}:{size}`

3. **No model** (if neither approach provides a complete model)
   - Model is skipped

## Validation Requirements

For auto-generation to work, all three components must be provided:
- `family` (non-empty)
- `variant` (non-empty)
- `size` (non-empty)

If any component is missing or empty, auto-generation fails and you must provide an explicit `name`.

**Example of invalid auto-generation** (missing size):
```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder"
      // Missing "size" - model will be skipped
    }
  }
}
```

**Fix by providing explicit name:**
```json
{
  "models": {
    "draft": {
      "family": "qwen2.5",
      "variant": "coder",
      "name": "custom-model-name"
    }
  }
}
```

## Benefits of Family/Variant/Size Configuration

1. **Clarity**: Models are defined by their characteristics rather than opaque names
2. **Consistency**: Easy to switch model families while keeping variant/size structure
3. **Flexibility**: Per-pair size configuration for draft/main optimization
4. **Maintainability**: Clear naming convention across your configuration
5. **Discovery**: Self-documenting model configurations

## Backward Compatibility

All existing configurations using explicit `name` and `base` fields continue to work unchanged. The auto-generation feature is purely additive and optional.
