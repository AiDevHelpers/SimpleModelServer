# Staged Pipeline: Test Plan

> Status: draft, 2026-07-06. Companion to `staged-pipeline-plan.md`. Covers Phase 0 benchmarks (decision-blocking), the golden set, and the ongoing eval harness.

## Part A — Phase 0 benchmarks (run on the 1660 box, blocks decisions)

Prep for every run: browser + IDE open (realistic desktop load), note free VRAM before/after via `nvidia-smi --query-gpu=memory.used,memory.total --format=csv -l 1`.

### A1. VRAM headroom → decides context cap & KV quant

1. Record minimum free VRAM over 10 min of normal desktop use.
2. Load Qwen2.5-Coder-7B Q4_K_M in llama-server with `--ctx-size 8192 --cache-type-k q8_0 --cache-type-v q8_0`; fill context to 8k with a long prompt; record peak VRAM.
3. Repeat at `--ctx-size 10240`. Expect spill; confirm.

**Pass:** 8k fits with ≥300MB margin. **Fail:** drop to 6k cap or q4 KV, re-test.

### A2. Throughput baseline

Same server, 5 prompts (500 / 1k / 2k / 4k / 8k tokens), measure prompt-processing tok/s and generation tok/s separately (llama-server timings output). This is the no-draft baseline.

### A3. Speculative decode payoff → decides whether 0.5B draft ships

Add `--model-draft qwen2.5-coder-0.5b --draft-max 8`. Re-run A2 prompts plus 5 real C# generation tasks (DTO, service class, async method, LINQ-heavy, interface impl). Record: acceptance rate, net generation tok/s vs A2.

**Pass:** ≥1.3× generation speedup on the C# set. **Fail:** ship 7B alone; delete draft from lineup.

### A4. Router latency → decides router-on-every-request vs heuristic pre-filter

Qwen2.5-1.5B-Instruct on CPU, schema-constrained JSON output, temp 0. 20 prompts (mix of the intent classes). Record p50/p95 latency and schema-validity rate (should be 100% with grammar enforcement).

**Pass:** p95 < 1.5s → router runs on every request. **Fail:** add cheap heuristics (length, code fences, keywords) in front; router only on inconclusive.

### A5. Classification accuracy (pre-threshold data)

Run the golden set (Part B) through the 1.5B router only. Record intent/quality/ambiguity vs expected labels. Target ≥90% intent accuracy. Below 80% → try Qwen2.5-3B-Instruct on CPU (latency permitting) before redesigning.

Record all results in `docs/benchmarks/phase0-results.md` — the plan's model lineup and caps get confirmed or amended from this file.

## Part B — Golden set

`tests/golden/` — one JSON file per case:

```json
{
  "id": "trivial-dto-01",
  "prompt": "…",
  "expected": {
    "routing_reason": "codegen",
    "intent": "code_generation",
    "must_compile": true,
    "latency_class": "fast",
    "must_contain": ["class OrderDto"],
    "must_not_contain": ["TODO", "..."]
  }
}
```

~50 cases, coverage matrix:

| Branch | Cases |
|---|---|
| Non-code question / meta | 6 |
| Trivial codegen (DTO, one-liner, boilerplate) | 8 |
| Complex single-file (async, LINQ, service layer) | 8 |
| Weak-but-clear prompt (needs rewrite) | 6 |
| Ambiguous (should route to clarify) | 6 |
| Over-8k context (should split) | 4 |
| Adversarial: wrong language requested, malformed JSON in prompt, empty prompt, contradictory instructions, prompt-injection attempt in a code comment | 8 |
| Repair-path: prompts historically producing broken output | 4 |

Author trivial/complex/weak cases from real SimpleModelServer.Chat history where possible; synthetic otherwise. Expected labels reviewed by hand once, then frozen — label changes require a commit with rationale.

## Part C — Eval harness

New project: `SimpleModelServer.Evals` (or in TokenSaver repo once the split lands — it tests the orchestration layer).

- Runner executes every golden case against the live stack, captures: `routing_reason`, validation result, repair count, latency, Claude invocations + cost.
- Assertions per case from the `expected` block; compile checks via Roslyn against a fixtures project.
- Output: single JSON report + two headline scalars — **routing accuracy** (% expected reason) and **compile-pass rate**. Store reports in `tests/golden/reports/` with timestamp + git SHA + model hashes.
- **Regression rule:** any threshold, prompt-template, or model change requires a before/after run; either scalar dropping is a regression, no exceptions for anecdotes.
- Claude-path cases run against a mock by default (assert the trigger fired + prompt shape); a `--live-claude` flag runs them for real, capped, before releases.
- Cadence: on demand during development, nightly once Phase 5 lands.

## Part D — Per-phase acceptance tests

- **Phase 1 (router, log-only):** A5 accuracy holds on live traffic sample; zero schema-invalid outputs in a week of logs; p95 added latency < A4 result + 20%.
- **Phase 2 (llama-server swap):** A2/A3 numbers reproduced through the Loader; existing OpenAI-compatible clients (Chat, TokenSaver indexer) work unchanged; racing code deleted or dead.
- **Phase 3 (validity gate):** golden repair-path cases pass; injected-fault test (corrupt a valid output, assert repair loop fixes it); deadline test returns `validated: false` instead of hanging.
- **Phase 4 (Claude fallback):** each of the 4 triggers fires on its golden cases and nothing else; budget cap enforced (mock bill); Claude output failing the gate takes the Claude-repair path.
- **Phase 4b (splitting):** over-8k cases produce parts that individually fit, contracts carry forward, assembled output compiles; cross-part mismatch injection triggers the assembly repair pass.
- **Phase 5 (routing live):** full golden set green; headline scalars recorded as the baseline for all future regression runs.
