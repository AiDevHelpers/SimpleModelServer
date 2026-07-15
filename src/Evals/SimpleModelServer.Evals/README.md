# SimpleModelServer.Evals

Golden-set eval harness for the staged pipeline (see `docs/staged-pipeline-plan.md` and
`docs/staged-pipeline-test-plan.md` Part C). Runs the 50 cases in `tests/golden/` against live
llama-server endpoints and scores routing accuracy and codegen quality. This project never starts,
stops, or otherwise manages llama-server — it only talks to base URLs you give it and assumes the
server is already running.

## CLI

```
SimpleModelServer.Evals --mode router  --router-url http://localhost:PORT [--cases tests/golden] [--filter prefix]
SimpleModelServer.Evals --mode codegen --gen-url http://localhost:PORT --label 3b-base-pair [--filter prefix] [--n-predict 1800] [--temperature 0.2]
SimpleModelServer.Evals --mode all     --router-url http://localhost:PORT --gen-url http://localhost:PORT2 --label 3b-base-pair
```

| Flag | Applies to | Default | Meaning |
|---|---|---|---|
| `--mode` | required | — | `router`, `codegen`, or `all` |
| `--router-url` | router, all | — | Base URL of the router llama-server instance |
| `--gen-url` | codegen, all | — | Base URL of the generation llama-server instance |
| `--label` | codegen, all | `unlabeled` | Free-text tag recorded in the report so two runs are comparable (e.g. `3b-base-pair` vs `7b-instruct-pair` — same command, different `--gen-url`/`--label`, is how the 3B-vs-7B head-to-head works) |
| `--cases` | all | `tests/golden` | Directory of golden case JSON files |
| `--filter` | all | none | Only run cases whose `id` starts with this prefix |
| `--n-predict` | router, codegen | `1800` | Max generation tokens per request (router requests use a smaller effective budget internally) |
| `--temperature` | codegen | `0.2` | Sampling temperature for generation requests (router always samples at 0.0) |
| `--timeout` | all | `180` | Per-case timeout in seconds |

Run from the repo root so the default `tests/golden` (and the sibling `tests/prompts/router-schema.json`
the router mode needs) resolve correctly.

### Example runs

```bash
# A5: router classification accuracy
dotnet run --project src/SimpleModelServer.Evals -- --mode router --router-url http://127.0.0.1:8081 --label router-v1

# 3B-base + 0.5B-base draft pair, codegen quality
dotnet run --project src/SimpleModelServer.Evals -- --mode codegen --gen-url http://127.0.0.1:8080 --label 3b-base-pair --n-predict 1800 --temperature 0.2

# 7B-instruct + 0.5B draft pair, codegen quality (head-to-head vs the 3B run above — compare the
# two reports' compile-pass rate and fully-clean rate)
dotnet run --project src/SimpleModelServer.Evals -- --mode codegen --gen-url http://127.0.0.1:8080 --label 7b-instruct-pair --n-predict 1800 --temperature 0.2
```

## Server launch prerequisites

The harness assumes an OpenAI-compatible `/v1/chat/completions` endpoint (llama-server). Launch
conventions follow `build/scripts/phase0-benchmarks.ps1`:

```powershell
# Generation model (codegen mode) — main model with a speculative draft pair, Vulkan/CUDA build
llama-server --model D:\models\qwen2.5-coder-7b-q4_k_m.gguf --port 8080 --host 127.0.0.1 `
  --ctx-size 8192 --cache-type-k q8_0 --cache-type-v q8_0 `
  --model-draft D:\models\qwen2.5-coder-0.5b-q8_0.gguf --spec-draft-n-max 8

# Router model (router mode) — runs on CPU per the staged-pipeline plan
llama-server --model D:\models\qwen2.5-1.5b-instruct-q8_0.gguf --port 8081 --host 127.0.0.1 `
  --ctx-size 4096 -ngl 0
```

Point `--gen-url`/`--router-url` at whichever ports you started the servers on. For the 3B-vs-7B
head-to-head, run two `--mode codegen` invocations against the same port with the model swapped
between them (stop/restart llama-server with a different `--model`), one per `--label`.

## Stub-file mechanism

Several golden cases (`complex-*`, a few `weak-*`/`repair-*`) tell the model to "assume X already
exists" — an injected repository interface, a pre-existing domain type, etc. Since the compile gate
only has the model's extracted snippet to work with, `tests/golden/stubs/<case-id>.cs` supplies
minimal declarations for exactly those assumed types (never for anything the prompt asks the model
to declare itself). If a stub file exists for a case, `GoldenCaseLoader` attaches its path and the
compile gate:

1. Parses it as an extra syntax tree in the same `CSharpCompilation` as the extracted code.
2. Adds `global using SimpleModelServer.Evals.Stubs;` to the generated-code tree so the model's snippet
   doesn't need to reference the stub's namespace explicitly.

Because the stub types live in their own namespace (pulled in only via a `global using`), a model
that redeclares the same interface itself (some prompts paste the interface verbatim, e.g.
`complex-email-sender-05`) does not collide with the stub — the model's own declaration in the
global/ambient namespace simply shadows the `using`-imported one.

Framework types some prompts assume (`IApplicationBuilder`, `RequestDelegate`, `IMemoryCache`,
`BackgroundService`) are *not* stubbed — they're real types resolved via the project's
`FrameworkReference` to `Microsoft.AspNetCore.App`, which the compile gate picks up from
`TRUSTED_PLATFORM_ASSEMBLIES` at runtime.

Add a new stub by dropping `tests/golden/stubs/<case-id>.cs` — no code changes required.

## Report format

Each run writes a pair of files to `tests/golden/reports/`, named
`<timestamp>-<mode>-<label>.{json,md}` (e.g. `20260707-143000-codegen-3b-base-pair.json`). Existing
reports are never overwritten — a same-second collision gets a `-2`, `-3`, ... suffix instead.

- **`.json`** — machine-readable: git SHA, label, endpoint URL(s), model info (from `/v1/models`
  when available), headline scalars, and a full per-case row array (diagnostics, assertion detail,
  latency, token counts).
- **`.md`** — human summary: headline scalars, a per-case pass/fail table, and (codegen mode) a
  failure-detail section with the actual Roslyn diagnostics and which assertions failed.

Headline scalars:
- **Router mode:** routing-reason accuracy (the number that gates regressions) and intent accuracy.
- **Codegen mode:** compile-pass rate (the number that gates regressions) and fully-clean rate
  (parse + compile + assertions all green).

Any threshold, prompt-template, or model change requires a before/after run — either headline
scalar dropping is a regression, per `docs/staged-pipeline-test-plan.md` Part C.

## No repair loop in v1

`Codegen.IGenerationPipeline` is the seam: `DirectGenerationPipeline` (v1) makes one call per case
with no retry. A future repair-loop pipeline (re-prompt with Roslyn diagnostics on failure, up to N
attempts) can implement the same interface and be swapped in behind a `--repair` flag without
touching `CodegenRunner`.
