# Model Configuration Quick Reference

## Three Ways to Define Models

### Option 1: Explicit Name + Base (Traditional)
```json
{
  "draft": {
    "name": "llama2:7b",
    "base": "llama2:7b",
    "num_ctx": 2048
  }
}
```

### Option 2: Family + Variant + Size (Recommended)
```json
{
  "draft": {
    "family": "qwen2.5",
    "variant": "coder",
    "size": "0.5b",
    "num_ctx": 2048
  }
}
```
**Auto-generates:**
- Name: `qwen2.5-coder-0.5b`
- Base: `qwen2.5-coder:0.5b`

### Option 3: Mixed (Override Auto-Generation)
```json
{
  "draft": {
    "family": "qwen2.5",
    "variant": "coder",
    "size": "0.5b",
    "base": "custom-base:0.5b"
  }
}
```

## Common Model Families

| Family | Variant | Sizes | Example |
|--------|---------|-------|---------|
| `qwen2.5` | `coder`, `instruct` | 0.5b, 1.5b, 3b, 7b, 32b | `qwen2.5-coder-0.5b` |
| `deepseek` | `coder`, `instruct` | 1.3b, 6.7b, 33b | `deepseek-coder-1.3b` |
| `llama2` | `chat` | 7b, 13b, 70b | `llama2-chat-7b` |
| `mistral` | `instruct` | 7b, 8x7b, 8x22b | `mistral-instruct-7b` |

## Per-Pair Configuration

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
      "keep_alive": "10m",
      "num_gpu": 20
    }
  }
}
```

## All Model Fields

```json
{
  "role_key": {
    // Name/Base generation
    "family": "string",              // e.g., "qwen2.5" (optional)
    "variant": "string",             // e.g., "coder" (optional)
    "size": "string",                // e.g., "0.5b" (optional)
    "chatSuffix": "string",          // e.g., "-chat", "-instruct" (optional, default: "-chat")
    
    // Explicit overrides (optional)
    "name": "string",                // Overrides auto-generation
    "base": "string",                // Overrides auto-generation
    
    // Model settings
    "num_ctx": number,               // Context window size (optional)
    "keep_alive": "string",          // Keep-alive duration (optional)
    "num_gpu": number | "auto",      // GPU layers (optional)
    "stop": ["string", ...]          // Stop tokens (optional)
  }
}
```

## Requirements for Auto-Generation

✅ **All THREE required:**
- `family` (non-empty)
- `variant` (non-empty)  
- `size` (non-empty)

❌ **If any missing:**
- Must provide explicit `name`
- Base will auto-generate if possible, or must provide explicit `base`

## Example: Complete Configuration

```json
{
  "global": {
    "defaultAgentType": "code",
    "gpuLayers": "auto"
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
      "num_gpu": 20
    },
    "embed": {
      "name": "nomic-embed-text",
      "base": "nomic-embed-text"
    }
  }
}
```

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Model not loading | Incomplete family/variant/size | Provide all three components or use explicit `name` |
| Wrong model name | Auto-generation conflict | Use explicit `name` to override |
| Wrong Ollama base | Custom model | Use explicit `base` to override |
| Context size mismatch | Inherited default | Set `num_ctx` on the specific model |

## Tips

- Use `family/variant/size` for readability and maintainability
- Use explicit `name`/`base` for special cases or custom models
- Set different `num_ctx` per pair for optimization
- Use `keep_alive` to manage memory usage patterns
- Combine with `num_gpu` for hardware-specific tuning
