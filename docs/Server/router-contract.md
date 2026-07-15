# SimpleModelServer.Loader Router — HTTP Contract

This document is the source of truth for the public HTTP surface exposed by `RouterHttpServer`
(the "ServiceRouter"), which normally listens on `http://localhost:11435`. It exists so the
endpoint tests in `SimpleModelServer.Loader.Tests` have a fixed spec to assert against, and so downstream
consumers (TokenSaver.MCP, the VS Code extension, curl/manual debugging) have one place to look
up exact request/response shapes instead of reading `RouterHttpServer.cs`.

Anything not listed here is not part of the contract and may change without notice.

## Cross-cutting behavior

- **Trusted-network gate**: every request (before routing) is checked against
  `global.trustedNetworks`. If the caller's remote address is not allowed, the router returns
  `403` with `{ "error": "forbidden: client network is not trusted" }` and does not process the
  request further. Loopback is always allowed regardless of configuration. An empty
  `trustedNetworks` list means unrestricted (every client allowed) — the historical default.
- **CORS**: every response (including 403s) includes
  `Access-Control-Allow-Origin: *`, `Access-Control-Allow-Headers: *`,
  `Access-Control-Allow-Methods: GET, POST, OPTIONS`.
- **OPTIONS**: any path, any method — the router replies `200 {}` unconditionally (CORS preflight).
- **Unknown method** (anything other than GET/POST/OPTIONS): `404 { "error": "not found" }`.
- **Unknown path**: `404 { "error": "not found" }` for both GET and POST.
- **Unhandled exception in a handler**: `500 { "error": "internal server error" }`. The real
  exception is logged server-side only — never echoed to the client (may contain paths/internals).
- **Fault tracking**: any unhandled exception from a request handler (the catch-all in
  `HandleRequestAsync`) is recorded via `ModelHealthMonitor.RecordFault` and stays "recent" for
  2 minutes. While a fault is recent, `/health` and `/health/ready` report degraded/unhealthy
  status — with a `lastError` field containing the exception's message — even if Ollama and both
  models otherwise probe as healthy. This does not affect the request that triggered the
  exception (it still gets its normal `500 { "error": "internal server error" }`); it only affects
  subsequent health checks. The periodic recovery loop does not try to "fix" a fault — there's
  nothing actionable for it to do — so it simply ages out after the 2-minute window.
- **Model warmup gate**: `POST /api/generate`, `/v1/chat/completions`, `/chat/completions` first
  check whether the draft or main model is warming up (or unresponsive and needs a warmup
  kicked off). If either is, the router replies `202` immediately instead of generating:
  ```json
  { "status": "warming_up", "message": "<human text>", "retry_after": 2|3 }
  ```
  `retry_after` is `3` if both models are warming, `2` if only the draft model is, `3` if only the
  main model is. `/v1/completions` and `/completions` do **not** check warmup before generating.
- **Path matching** is case-insensitive (paths are upper-cased before the switch).

## GET endpoints

### `GET /health`
Full health snapshot with self-healing: probes Ollama and both models, attempts recovery for any
unhealthy component, runs the endpoint self-test suite (see below), then reports the (possibly
recovered) state.

- `200` when Ollama + draft + main are all healthy and self-tests pass:
  ```json
  { "status": "ok", "ollama": "healthy", "models": { "draft": "healthy", "main": "healthy" }, "agent": "<current agent type>", "lastError": null, "selfTest": { "ok": true, "checks": [ { "name": "...", "ok": true } ] } }
  ```
- `202` when unhealthy but warming up (after the recovery attempt):
  ```json
  {
    "status": "warming_up",
    "message": "...",
    "retry_after": 2|3,
    "ollama": "healthy"|"unhealthy",
    "models": { "draft": "healthy"|"warming_up"|"unhealthy", "main": "healthy"|"warming_up"|"unhealthy" },
    "agent": "<current agent type>",
    "lastError": null|"<message>"
  }
  ```
- `503` when unhealthy and not warming up (including when Ollama/models probe healthy but either
  a recent fault was recorded or a self-test check failed):
  ```json
  {
    "status": "degraded"|"unhealthy",
    "ollama": "healthy"|"unhealthy",
    "models": { "draft": "...", "main": "..." },
    "agent": "...",
    "lastError": null|"<message>",
    "selfTest": { "ok": false, "checks": [ { "name": "...", "ok": true|false, "error": null|"<message>" } ] }
  }
  ```

### `GET /health/ready`
Like `/health` but **read-only** — does not attempt recovery and does not run self-tests (kept
cheap/fast for frequent polling), just reports current probe + fault state.

- `200`: `{ "status": "ok", "ollama": "healthy", "models": { "draft": "healthy", "main": "healthy" }, "agent": "...", "lastError": null }`
- `503`: `{ "status": "warming_up"|"unhealthy"|"degraded", "ollama": "...", "models": {...}, "agent": "...", "lastError": null|"<message>" }`

### Endpoint self-tests (part of `GET /health` only)
Alongside the Ollama/model probes, `/health` also exercises a handful of the router's own
lightweight GET endpoints in-process (not over the network) to catch a broken/throwing handler
that wouldn't otherwise show up until a real caller hits it — this is what makes an endpoint's own
bug (not just an Ollama/model outage) visible from `/health` before it's discovered by a real
client. Checks currently run: `/api/version` returns 200 with a `version` field, `/api/tags`
returns 200 with a `models` array, `/v1/models` returns 200 with an `object: "list"` shape. A
failing check flips overall status to `503`/`"degraded"` (via the same `selfTest.ok: false`
signal) even if Ollama and both models are otherwise healthy, and reports which check(s) failed
and why in `selfTest.checks[].error`.

### `GET /api/version`
Static. `200 { "version": "0.1.0" }`

### `GET /api/tags`
Ollama-native model listing (single virtual model). `200`:
```json
{ "models": [ { "name": "<routerModelName>", "modified_at": "<ISO-8601 UTC>", "size": 0 } ] }
```

### `GET /api/ps`
Proxied verbatim (status code, content-type, body) to the real Ollama's `GET /api/ps`.

### `GET /api/capacity`
Exposes resolved hardware settings for sizing background-inference concurrency elsewhere.
`200`:
```json
{
  "cpuThreads": <int|null>,
  "gpuLayers": <int|null>,
  "gpu": { "detected": <bool>, "name": "<string>", "memoryMb": <int|null> },
  "maxConcurrentSummaryRequests": <int>
}
```

### `GET /v1/models` and `GET /models`
OpenAI-compatible model listing (matched by path *prefix*, so `/v1/models/anything` also hits
this). `200`:
```json
{ "object": "list", "data": [ { "id": "<routerModelName>", "object": "model", "created": 0, "owned_by": "local" } ] }
```

## POST endpoints

### `POST /api/generate`
Ollama-native generate endpoint. Body: `{ "prompt": string, "stream"?: bool = true, ... }`
(agent type inferred per `AgentTypeResolver` from headers/query/body/prompt).

- If the resolved agent type is `research`, the prompt is classified web-vs-local and answered
  accordingly (see Research routing below); this can still stream or return a single JSON body
  depending on `stream`.
- `stream: false` → `200 { "model": "<routerModelName>", "response": "<full text>", "done": true }`
- `stream: true` (default) → `200`, `Content-Type: application/x-ndjson`, one JSON object per
  line: `{"response": "<token>", "done": false}` for each token, followed by a final
  `{"response": "", "done": true}` line.

### `POST /v1/chat/completions` and `POST /chat/completions`
OpenAI-compatible chat endpoint. Body: `{ "messages": [...], "stream"?: bool = false, ... }`.
Prompt is built from `messages` via `OllamaHelpers.BuildPrompt`.

- `stream: false` (default) → `200`:
  ```json
  {
    "id": "chatcmpl-<guid>",
    "object": "chat.completion",
    "choices": [ { "index": 0, "message": { "role": "assistant", "content": "<full text>" }, "finish_reason": "stop" } ]
  }
  ```
- `stream: true` → `200`, `Content-Type: text/event-stream`, SSE frames:
  ```
  data: {"choices":[{"delta":{"content":"<token or full text for research>"}}]}

  data: [DONE]

  ```
  For the `research` agent type, streaming still emits the full response as a single delta chunk
  rather than incremental tokens (there is no live web-search streaming path here, unlike
  `/api/generate`).

### `POST /v1/completions` and `POST /completions`
OpenAI-compatible legacy completion endpoint. Body: `{ "prompt": string, ... }`. Always
non-streaming. `200`:
```json
{ "id": "cmpl-<guid>", "object": "text_completion", "choices": [ { "index": 0, "text": "<full text>", "finish_reason": "stop" } ] }
```
Does not apply the model-warmup 202 gate.

### `POST /api/embed`
Passed straight through (status, content-type, body) to Ollama's own `POST /api/embed`.

### `POST /v1/embeddings` and `POST /embeddings`
OpenAI-compatible embeddings. Body: `{ "input": ..., "model"?: string }` (model defaults to the
configured embed model). Translated to Ollama's `/api/embed` and back.

- On success, `200`:
  ```json
  { "object": "list", "data": [ { "object": "embedding", "embedding": [...], "index": 0 }, ... ], "model": "<model>" }
  ```
- On failure (Ollama returns non-2xx), the same status code is passed through with
  `{ "error": "Embed model unavailable (<status>)" }`.

## Agent type resolution (applies to all POST generate/chat/completion routes)

`AgentTypeResolver.Resolve` picks the first match, in order:
1. `X-Agent-Type` header (special value `passthrough` short-circuits to the current agent,
   bypassing all other detection — used by internal pipeline calls).
2. `X-CP-Chat-Agent` header.
3. `X-CP-Chat-Mode` header.
4. `agent_type` query param.
5. `agent` query param.
6. `mode` query param.
7. Body field `agentType`/`agent_type`/`agent`/`mode` (top level).
8. Same field names inside body `metadata`.
9. `model` field suffix (`:code`, `-debug`, `/planning`, etc.).
10. A `system`/`developer` role message's content, keyword-matched.
11. Keyword match against the prompt text itself (`research`/`planning`/`debug`/`code` — see
    `AgentTypeResolver.AgentTypeFromPrompt` for exact trigger phrases).

If nothing matches a known agent type, falls back to `config.DefaultAgentType` (upper-cased).

## Not covered by this contract

- Exact token-by-token timing/order of streamed generation (implementation detail of the
  draft-pair merge in `DraftPairGenerationService`).
- Internal-only calls SimpleModelServer.Loader makes to Ollama directly (bypassing this router) for
  research classification — not part of the router's own HTTP surface.
