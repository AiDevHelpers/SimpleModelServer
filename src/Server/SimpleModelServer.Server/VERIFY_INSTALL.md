# Verifying SimpleModelServer.Loader is fully working

Use this after SimpleModelServer.Loader is installed and its service is started (see
`TokenSaver/src/Tooling/TokenSaver.VsCodeExt/INSTALL_PROMPT.md` for the install/start/hot-check sequence).
That file already covers "is it hot" (`/health` then `/api/capacity`). This file goes further:
confirm the router's actual endpoints work end-to-end, not just that the process is alive.

Default port: `11435`. All commands below assume `http://localhost:11435`; adjust if
`global.routerPort`/`routerHost` were customized (check `SimpleModelServer.config.json`).

Run these in order. Stop and report at the first failure rather than continuing — a later test
passing doesn't mean an earlier failure was a fluke.

## 1. Health and readiness

```bash
curl -s http://localhost:11435/health
curl -s http://localhost:11435/health/ready
```

`/health` can return `200` (healthy) or `202` (still warming — retry, don't fail immediately) or
`503` (unhealthy — stop here, something is actually wrong). `/health/ready` is the strict
all-healthy check — expect `200` before proceeding. If either returns `503`, do not proceed to
the tests below; diagnose the service first (check `docs/pitfalls.md` in the repo root for known
SCM/port/localhost-vs-127.0.0.1 issues before assuming it's a new bug).

## 2. Capacity / hardware detection

```bash
curl -s http://localhost:11435/api/capacity
```

Expect a JSON body with `cpuThreads`, `gpuLayers`, a `gpu` object (`detected`, `name`,
`memoryMb`), and `maxConcurrentSummaryRequests`. If `gpu.detected` is `false` on a machine known
to have a GPU, that's worth flagging but is not necessarily a failed install — some configs
force CPU-only.

## 3. Model listing

```bash
curl -s http://localhost:11435/api/tags
curl -s http://localhost:11435/v1/models
```

Both should return non-empty model lists. `/api/tags` is the Ollama-style response; `/v1/models`
is the OpenAI-compatible equivalent. If these are empty, the draft/main/embed models never
finished loading even though `/health` passed — treat this as a hot-check failure, not a router
bug.

## 4. Ollama-native process check

```bash
curl -s http://localhost:11435/api/ps
```

Proxied straight through to Ollama's own `/api/ps` — confirms the router can actually reach the
underlying Ollama instance, not just that its own process is up.

## 5. Chat — non-streaming

```bash
curl -s http://localhost:11435/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"messages":[{"role":"user","content":"Reply with exactly the word: pong"}],"stream":false}'
```

Expect a `200` with a `choices[0].message.content` (or `.delta.content` if the server ignores
`stream:false`) containing a real completion — not an error body, not an empty `choices` array.
The exact wording of the reply doesn't matter; getting a coherent, non-empty response is the bar.

## 6. Chat — streaming

```bash
curl -s -N http://localhost:11435/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"messages":[{"role":"user","content":"Count from 1 to 5."}],"stream":true}'
```

Expect a stream of SSE (`data: ...`) chunks, ending in `data: [DONE]` or equivalent terminator,
not a single blocking response or a connection that hangs indefinitely. If it hangs, this is
likely the same class of `localhost` vs `127.0.0.1`/http.sys wedged-listener issue documented in
`docs/pitfalls.md` — check there before treating it as a new router bug.

## 7. Ollama-native generate endpoint

```bash
curl -s http://localhost:11435/api/generate \
  -H "Content-Type: application/json" \
  -d '{"prompt":"Reply with exactly the word: pong","stream":false}'
```

Expect NDJSON with a `response` field containing text and a final `done:true` line.

## 8. Embeddings

```bash
curl -s http://localhost:11435/v1/embeddings \
  -H "Content-Type: application/json" \
  -d '{"input":"test embedding"}'
```

Expect `data[0].embedding` to be a non-empty numeric array. This confirms the embed model
(separate from draft/main) is also loaded and reachable, not just the chat models.

## What "fully working" means

All of steps 1–8 return the expected shape with no `5xx`, no hangs, and non-empty model
output. If any step fails after `/health`/`/health/ready` both passed, that's a router-level or
model-level problem worth its own investigation — don't assume a passing health check means the
rest is fine, since `/health` deliberately doesn't exercise the chat/embed code paths.

If everything here passes, SimpleModelServer.Loader is confirmed working end-to-end and it's safe to
move on to installing/verifying TokenSaver.MCP — see
`TokenSaver/src/Host/TokenSaver.MCP/VERIFY_INSTALL.md`.
