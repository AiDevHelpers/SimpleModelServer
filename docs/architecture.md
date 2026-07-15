# Architecture Overview

This repo manages a **local Ollama instance** and exposes it as an OpenAI/Ollama-API-compatible
router, plus a standalone chat client and shared libraries.

## Project Dependency Graph

```text
SimpleModelServer.Chat   → SimpleModelServer.Library
SimpleModelServer.Loader → SimpleModelServer.Library
```

## SimpleModelServer Stack (net10.0)

**`SimpleModelServer.Loader`** is the primary service binary. It:
1. Manages the Ollama process lifecycle (start, kill, health recovery via background loop every 15s)
2. Ensures both `draftModel` and `mainModel` are pulled and responsive at startup
3. Runs a **ServiceRouter** (`HttpListener`) on port 11435 that accepts requests and fans them out to both models simultaneously
4. Advertises the router over mDNS (`_draftmodel._tcp.local.`) so LAN clients can discover host/port without hardcoding config — see `docs/mdns-discovery.md`
5. Translates between Ollama API (`/api/generate`) and OpenAI API (`/v1/chat/completions`, `/v1/completions`, `/v1/models`, `/v1/embeddings`)
6. Exposes `GET /api/capacity` returning `ResolvedHardwareSettings` so external callers (e.g. TokenSaver's background indexer, in the separate rsperry79/TokenSaver repo) can size their own concurrency from the real machine capacity

**Draft-pair generation**: Both draft (small/fast) and main (larger) models receive every prompt in parallel. Draft output is shown on console until the main model starts producing tokens, then main output takes over. The final response returned to callers is always the main model's output.

**Agent type routing**: Requests are inspected for an agent type via headers (`X-Agent-Type`), query params, JSON body fields, model name suffixes (e.g. `HelperModel:debug`), system message content, and prompt keyword detection. Each agent type maps to a `systemPrompt`, `temperature`, and `sampling` block in `SimpleModelServer.config.json`.

**`SimpleModelServer.config.json`** schema:
```json
{
  "global": { "defaultAgentType": "code", "gpuLayers": "auto", "cpuThreads": "auto" },
  "agents": {
    "code": { "systemPrompt": "...", "temperature": 0.1, "sampling": { "top_p": 0.9 } },
    "debug": { ... },
    "planning": { ... }
  }
}
```

**`SimpleModelServer.Chat`** is a standalone interactive/one-shot CLI that connects to an already-running Ollama instance (or SimpleModelServer.Loader) and performs the same draft-pair generation locally without the router layer.

**`SimpleModelServer.Library`** provides shared types used by both Chat and Loader:
- `AppConfig` / `GlobalConfig` / `AgentConfig` — config loading and parsing
- `HardwareDetection` — nvidia-smi GPU detection, resolves `num_gpu`, `num_thread`, and `MaxConcurrentSummaryRequests` for Ollama options
- `OllamaGenerationHandle` / `OllamaHelpers` — streaming response wrapper and token reader
- `ChatMessage`, `TokenResult` — core data types

## Ports and Boundaries

| Component | Port | Role |
| --- | --- | --- |
| Ollama native | 11434 | Actual model inference |
| SimpleModelServer.Loader router | 11435 | Draft-pair proxy, OpenAI/Ollama API compatible, `/api/capacity` endpoint |

**Consumers**: TokenSaver.MCP (in the separate rsperry79/TokenSaver repo) points its `OllamaUrl`
at this router (11435) rather than talking to Ollama directly, so model creation, GPU detection,
and lifecycle management live entirely here.

## Router API Contract

The router (`RouterHttpServer`) must implement both `IRouter` and `IOllama` interfaces:

- **`IRouter`**: Router-specific endpoints (`/health`, `/api/capacity`, etc.)
- **`IOllama`**: All official Ollama API endpoints (see below)

This ensures the router supports the complete Ollama API surface so any client expecting an Ollama-compatible
instance (like TokenSaver.MCP) can point at the router with no configuration changes.

### Official Ollama Endpoints (Must Support)

**Generation & Chat:**
- `POST /api/generate` — Generate text with a model
- `POST /api/chat` — Chat completions

**Embeddings:**
- `POST /api/embed` — Generate embeddings (recommended)
- `POST /api/embeddings` — Generate embeddings (legacy, should proxy to `/api/embed`)

**Model Management:**
- `GET /api/tags` — List available models
- `GET /api/ps` — List models loaded in memory
- `GET /api/version` — Get Ollama version
- `POST /api/show` — Display model info
- `POST /api/create` — Create a model
- `POST /api/copy` — Copy/rename a model
- `POST /api/delete` — Delete a model
- `POST /api/pull` — Download a model
- `POST /api/push` — Upload a model

**Blob Management:**
- `HEAD /api/blobs/:digest` — Check if blob exists
- `POST /api/blobs/:digest` — Push blob to server

**OpenAI Compatibility (Aliases):**
- `POST /v1/chat/completions` → `/api/chat`
- `POST /v1/completions` → `/api/generate`
- `GET /v1/models` → `/api/tags`
- `POST /v1/embeddings` → `/api/embed`
