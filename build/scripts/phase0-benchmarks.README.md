# Running the Phase 0 benchmarks

`phase0-benchmarks.ps1` automates Part A (A1–A4) of `docs/staged-pipeline-test-plan.md` against a
real `llama-server` on the 1660 box. It does **not** run in this sandbox — it needs an actual GPU,
`nvidia-smi`, `llama-server.exe`, and downloaded GGUF weights. Run it locally in PowerShell 7+.

## Prerequisites

- PowerShell 7+ (`pwsh`)
- `llama-server` (llama.cpp) on PATH, or pass `-LlamaServerPath`
- `nvidia-smi` on PATH
- GGUF weights for:
  - Qwen2.5-Coder-7B Q4_K_M (main codegen)
  - Qwen2.5-Coder-0.5B (speculative draft, needed for A3)
  - Qwen2.5-1.5B-Instruct (router, needed for A4)
- Before running A1: open a browser and IDE for a realistic idle VRAM baseline.

## Usage

```powershell
cd build/scripts

# Full Part A run
./phase0-benchmarks.ps1 -Test All `
    -ModelPath D:\models\qwen2.5-coder-7b-q4_k_m.gguf `
    -SimpleModelServerPath D:\models\qwen2.5-coder-0.5b-q8_0.gguf `
    -RouterModelPath D:\models\qwen2.5-1.5b-instruct-q8_0.gguf

# Just one test
./phase0-benchmarks.ps1 -Test A2 -ModelPath D:\models\qwen2.5-coder-7b-q4_k_m.gguf

# Smoke-test the harness itself without the real 10-minute idle window
./phase0-benchmarks.ps1 -Test A1 -ModelPath D:\models\qwen2.5-coder-7b-q4_k_m.gguf -QuickIdleSeconds 15
```

Results are appended to `docs/benchmarks/phase0-results.md` with a timestamp and git SHA per run.

## What's covered vs. not

| Test | Covered | Notes |
|---|---|---|
| A1 VRAM headroom | Yes | Idle sampling window configurable via `-QuickIdleSeconds` for dry runs |
| A2 Throughput baseline | Yes | 500/1k/2k/4k/8k synthetic prompts, filler text sized by word count |
| A3 Speculative decode | Yes | Same prompts + `tests/prompts/csharp-tasks.json` (5 real C# tasks); acceptance rate read from `timings.draft_n`/`draft_n_accepted` if your llama-server build reports them |
| A4 Router latency | Yes | `tests/prompts/router-prompts.json` (20 prompts) against `tests/prompts/router-schema.json` via llama-server's `json_schema` constrained decoding |
| A5 Classification accuracy | **No** | Needs the Part B golden set (`tests/golden/`), which doesn't exist yet. Build that first, then extend this script or write a companion runner. |

## Caveats to check on your build of llama-server

- `draft_n` / `draft_n_accepted` in the `/completion` response's `timings` block are only present
  on newer llama.cpp builds. If A3's acceptance-rate column comes back blank, check your
  `llama-server --version` and the `/completion` response shape directly (`curl` it once) before
  trusting the numbers.
- `json_schema` constrained decoding in the request body requires a llama-server build with that
  feature; older builds only support GBNF via `grammar`. If A4 fails or schema validity is
  suspiciously low, swap `json_schema` for a hand-written `.gbnf` grammar file and pass it as
  `grammar` in `Invoke-Completion`'s `-ExtraBody` instead.
- Token-count estimates in `New-FillerPrompt` are approximate (~1.3 tokens/word); actual prompt
  length will drift a bit from the target. Fine for a throughput sweep, not exact.
