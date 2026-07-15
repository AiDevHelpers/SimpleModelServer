# Staged Pipeline: Consolidated Plan

> Status: recommended plan, 2026-07-06. Supersedes `staged-pipeline-design.md`, `staged-pipeline-concerns.md`, `staged-pipeline-goals.md` by applying `staged-pipeline-review.md`. Targets the existing SimpleModelServer.Loader / ServiceRouter codebase.

## Goals (outcome-level)

1. All delivered C# parses (Roslyn); compile-checked where a target project exists.
2. Trivial requests (DTO, boilerplate) complete in < 10s; complex single-file in < 60s.
3. No OOM or CPU-spill at the configured context cap.
4. Claude invoked on < 15% of requests, with a daily budget cap.
5. Every request logs `routing_reason` — routing is auditable and testable.

## Model lineup (2 families, 4 checkpoints)

| Role | Model | Placement | Why |
|---|---|---|---|
| Router (classify + rewrite + plan) | Qwen2.5-1.5B-Instruct | CPU | One stage does all pre-processing; already loaded, sees full prompt |
| Main codegen | **Qwen2.5-Coder-3B (base) Q4_K_M** | GPU, Vulkan or CUDA | Swapped down from 7B after Phase 0: same VRAM headroom problem the 7B had on the GTX 1660 (566–615MB margin) becomes ~2GB margin on the 3B, and no-draft throughput is 6–8x faster (35–65 vs 6.1–7.8 tok/s) |
| Speculative draft | Qwen2.5-Coder-0.5B (**base, not -Instruct**) | GPU, Vulkan or CUDA (see Phase 0 note below) | Draft for spec decode, verified by main model; shares vocab with the 3B |
| Standalone fast-path | Qwen2.5-Coder-0.5B-**Instruct** | GPU or CPU, separate endpoint | Not paired with the main model — serves trivial/boilerplate requests directly at 8-9x the 7B's generation speed |

Changes vs the draft docs: DeepSeek-Coder-7B dropped (incompatible tokenizer with the Qwen draft; older model). Separate 0.5B planner stage dropped (a 0.5B plan can mislead the main model; the 1.5B plans in the same call as classification). The 0.5B checkpoint is split into two independent deployments (draft partner vs. standalone fast-path) rather than one shared instance. Main codegen model changed from 7B-Instruct to 3B-base — see `docs/benchmarks/phase0-results.md` for the measurements behind both decisions.

**Open question carried by the 7B→3B swap — investigated further, conclusion: this is an
architectural problem, not a model-selection problem (2026-07-07).** Follow-up testing (see
`docs/benchmarks/phase0-results.md`, "Follow-up: interface-impl retest + Qwen2.5-Coder-3B-Instruct
comparison") closed out both open next-steps from the first quality check: (1) the interface-impl
repetition failure was retested with a larger `n_predict` and did NOT recur — confirming it was
decoding variance, not truncation, which is worse news since it means the failure is
non-deterministic; (2) Qwen2.5-Coder-3B-**Instruct** was downloaded and run against the same 5
tasks with the same 0.5B-base draft (pairing works fine across base/Instruct). Instruct generated
more robustly (no repetition loops, cleaner output) but did **not** reduce the defect rate — still
2 of 5 tasks compiling clean, same two tasks as the base model, different bugs on the other three.
Most notably, both the base-model retry and the Instruct run independently produced the *identical*
bug on the interface-impl task: a `retry once on transient exception` pattern that references a
try-scoped variable from inside the catch block — a specific blind spot in this model family for
that idiom, present regardless of instruction-tuning. **Conclusion: swapping base→Instruct is not
the fix.** The 3B class of model (either variant) at this size produces code with a real, roughly
1-in-3-task defect rate that instruction-tuning doesn't close. This makes the planned Phase 4
Roslyn compile-gate load-bearing rather than a nice-to-have — the pipeline must not trust raw 3B
output without a compile check in front of it. Still needs the full Part B golden set for a real
pass/fail number across a larger sample, but the direction is now clear enough to act on.

**Phase 0 finding (CUDA draft-loading bug — 7B-specific, not backend-wide):** on the CUDA backend (both 12.4 and 13.3 builds of llama.cpp b9878), loading a 0.5B model as `--model-draft` alongside the **7B** crashed with `invalid vector subscript` — reproduced with both the base and -Instruct 0.5B GGUFs. This was initially assumed to be a general CUDA-backend bug, but directly testing the 3B+0.5B pairing on CUDA disproved that: it loads and runs correctly on CUDA, with throughput (58–65 tok/s) comparable to or slightly better than Vulkan for the same pair. **The crash is specific to the 7B GGUF used in that test, not a backend-wide limitation** — since the current plan uses the 3B, both Vulkan and CUDA are viable backends for the full pipeline (main + draft). The 7B-specific crash is unresolved and would need re-investigation if the 7B is ever reconsidered.

## Speculative decoding, not racing

The 0.5B *drafts* tokens and the 7B *verifies* them in one forward pass (llama.cpp `--model-draft`). Output quality is always exactly the 7B's, so:

- No trivial/non-trivial quality split — spec decode is just a speedup on everything.
- No "divergence" concept — token disagreement is handled internally by verification, never a fallback trigger.
- Note: Ollama does not currently expose speculative decoding; this requires running `llama-server` directly for the main model (the Loader already manages a process lifecycle — point it at llama-server instead). If staying on Ollama short-term, run the 7B alone and skip the draft; do NOT keep racing.

## Request flow

```
request
  → [1] Router (1.5B, CPU, schema-constrained JSON, temp 0, 5s timeout)
        fail/malformed/timeout → default: route straight to codegen (reason: classifier_failed)
  → [2] ambiguity ≥ medium → Claude clarify (reason: ambiguous)   ← checked FIRST
  → [3] intent = question/meta and confidence high → answer locally (reason: local_answer)
  → [4] prompt weak → use rewritten prompt; plan included for multi-step tasks
  → [5] context > 8k tokens → split path: Claude decomposes once, 7B generates parts (reason: context_overflow_split)
  → [6] Codegen (7B + 0.5B spec decode, GPU)
  → [7] Validity gate: Roslyn ParseText (+ compile if project context available)
        pass → return
        fail → repair loop: re-prompt 7B with compiler diagnostics, max 2 attempts
        still failing → Claude with original prompt + last attempt + diagnostics (reason: repair_exhausted)
```

Precedence rule: ambiguity beats everything (weak+ambiguous → Claude, not sequential).

## Router output schema

```json
{
  "intent": "code_generation | question | explanation | meta | other",
  "quality": "good | weak | invalid",
  "ambiguity": "none | low | medium | high",
  "confidence": 0.0,
  "rewritten_prompt": null,
  "plan": null,
  "response": null,
  "routing_reason": "string"
}
```

Enforced with grammar/JSON-schema-constrained generation (llama.cpp GBNF / Ollama `format`), so malformed router JSON is structurally impossible; timeout is the remaining failure mode and defaults to codegen.

## Claude fallback (only 4 triggers)

1. `ambiguous` — router flags medium/high → Claude clarifies with the user.
2. `repair_exhausted` — output still fails Roslyn after 2 local repair attempts.
3. `context_overflow_split` — request exceeds the 8k cap; Claude makes one decomposition call (see Task splitting), local 7B generates the parts.
4. `classifier_failed` twice in a row for the same request (router down/broken).

Everything in the old §9 trigger list ("inconsistent indentation", "mismatched types", divergence) is either handled by the Roslyn gate + repair loop or deleted. Daily budget cap; when exceeded, return the best local attempt with a warning.

## Hardware budget (GTX 1660, 6GB)

- Measure free VRAM under desktop load first (`nvidia-smi`); assume ~5.2–5.5GB usable.
- 7B Q4_K_M weights ≈ 4.4GB + KV cache q8_0 at 8k ≈ 0.7GB → fits; f16 KV does not.
- **Hard cap 8k context.** No 10k/12k "usable/borderline" tiers — beyond 8k spills to CPU and throughput collapses. Overflow → Claude.
- No sliding window / context shifting: silently evicting early tokens (usually the type definitions) is worse than refusing.
- One GPU tenant only; 1.5B router stays on CPU permanently.

## Task splitting (context overflow handling)

When a request won't fit the 8k cap, split it into parts instead of sending the whole job to Claude.

**Why naive splitting fails:** part 2 needs to know what part 1 built. Passing part 1's full code forward just re-fills the context. **Fix: pass contracts, not code.** After each part, use Roslyn to strip method bodies and keep only the public surface (namespaces, type declarations, signatures, DTO shapes). Accumulated contract context grows slowly, so total context stays bounded regardless of part count.

**Who splits:** decomposition is the hard step — too hard for the 1.5B. On overflow, Claude makes *one cheap planning call* producing:

```json
{
  "type": "split_plan",
  "shared_contracts": "C# interface/DTO stubs all parts must conform to",
  "parts": [
    { "id": 1, "subtask": "…", "depends_on": [] },
    { "id": 2, "subtask": "…", "depends_on": [1] }
  ]
}
```

The local 7B then generates every part: prompt = shared contracts + contracts extracted from completed dependencies + subtask. Claude spend drops from "whole job" to one small planning call (reason: `context_overflow_split`).

**Two delivery modes:**

1. *Server-side loop (default):* caller sends one request; the Loader iterates parts internally and streams the concatenated result. Callers remain plain OpenAI-compatible clients.
2. *Explicit continuation (opt-in, `X-Continuation: client` header):* response returns part 1 + `continuation_id` + remaining-parts summary; caller reviews and requests the next part (`POST /v1/continuations/{id}/next`). State (split plan + extracted contracts, not full outputs) is held server-side with a TTL.

Each part passes the same validity gate (parse → repair loop → Claude) independently. A final assembly check parses all parts together; cross-part mismatches (part 2 calls a method part 1 never made) go through one repair pass with the mismatch diagnostics before escalating.

## Component split: SimpleModelServer vs TokenSaver

Principle: **SimpleModelServer is a transparent model server** — any response must be what a plain client would get from the underlying model. Anything that mutates prompts or post-processes output is orchestration and lives in the TokenSaver MCP, which calls SimpleModelServer as an ordinary OpenAI-compatible client (it already consumes `/api/capacity`).

| Concern | Lives in | Notes |
|---|---|---|
| Model/process lifecycle (llama-server), pulls, health loop | SimpleModelServer | |
| Speculative decoding (7B + 0.5B draft) | SimpleModelServer | Output bit-identical to 7B alone → transparency-safe |
| Serving 1.5B / 7B / 0.5B as plain endpoints | SimpleModelServer | Router model is just another served model |
| 8k context cap | SimpleModelServer | Enforced as an honest 413-style error with token counts — never silent truncation |
| `/api/capacity`, `/v1/models`, mDNS | SimpleModelServer | |
| Sampling defaults (temp, top_p) | SimpleModelServer | Config-level, no prompt mutation |
| Router stage (classify / rewrite / ambiguity via 1.5B) | TokenSaver | Prompt mutation |
| `X-Agent-Type` → system-prompt injection | TokenSaver | Moves out of Loader; prompt mutation |
| Roslyn validity gate + repair loop | TokenSaver | Output post-processing |
| Claude fallback, triggers, budget cap | TokenSaver | |
| Task splitting: decomposition, contract extraction, part loop, continuation state | TokenSaver | Reacts to SimpleModelServer's 413 |
| `routing_reason` telemetry, threshold tuning, caching | TokenSaver | |

TokenSaver exposes the pipeline upward as MCP tools (e.g. `generate_code`, `continue_task`, `explain_routing`). Flow: MCP client → TokenSaver (route → rewrite → call SimpleModelServer → validate → repair/split/escalate) → SimpleModelServer (serve tokens). Phases 1, 3, 4, 4b, 5 therefore land in the TokenSaver repo; Phases 0 and 2 in SimpleModelServer.

## Implementation phases

**Phase 0 — Measure (no code).** Pull Qwen2.5-Coder-7B Q4_K_M + 0.5B + 1.5B-Instruct. Benchmark: 7B tok/s at 2k/4k/8k with q8_0 KV; spec-decode speedup via llama-server; 1.5B classification latency on CPU. Confirms the context cap and whether spec decode pays off on this card.

**Phase 1 — Router stage.** Add classification call to ServiceRouter pipeline: schema-constrained 1.5B call, temp 0, 5s timeout, `routing_reason` on every response + structured log line. Ship with routing in "log-only" mode first (classify but don't act) to tune thresholds on real traffic.

**Phase 2 — Swap codegen path.** Replace draft-pair racing with llama-server + `--model-draft` (or plain 7B if staying on Ollama). Delete divergence-comparison code.

**Phase 3 — Validity gate.** Roslyn `CSharpSyntaxTree.ParseText` on extracted code blocks; repair loop (max 2) feeding diagnostics back to the 7B. New project: `SimpleModelServer.Validation` + tests.

**Phase 4 — Claude fallback.** Anthropic API client with the 4 triggers, daily budget cap, and per-call logging of trigger + cost.

**Phase 4b — Task splitting.** Claude decomposition call, Roslyn contract extraction (signature stripping), server-side part loop, then the opt-in continuation API. Depends on Phases 3–4.

**Phase 5 — Turn routing on + test suite.** Flip Phase 1 from log-only to active. Tests assert on `routing_reason` per the matrix: non-code → `local_answer`; ambiguous → `ambiguous`; trivial/complex code → codegen path; syntactically broken output (injected) → repair then `repair_exhausted`; >8k prompt → `context_overflow`.

## Open questions & risks

### 1. Streaming vs the validity gate

Roslyn can only validate complete output; streamed tokens can't be retracted. Resolve per surface, never both on one endpoint:

- **SimpleModelServer `/v1/*` (transparent):** streams raw tokens, no gate, no buffering. Consistent with the transparency principle — callers get exactly what the model produced.
- **TokenSaver MCP tools:** buffer the full generation, run the gate + repair loop, return validated code. MCP tool calls are request/response anyway, so buffering costs nothing UX-wise. If perceived latency matters later, add a `progress` notification (tokens generated so far as a status count, not as code the caller might use).
- Repair-loop consequence: total worst-case latency = 3 generations (initial + 2 repairs). Set a per-tool deadline (e.g. 120s); on deadline, return the best attempt flagged `validated: false` rather than hanging the MCP client.

### 2. Single-GPU concurrency and queueing

One 7B serves everyone; TokenSaver's background indexer must never starve interactive use.

- Priority queue in TokenSaver, in front of SimpleModelServer: `interactive` > `background`. SimpleModelServer itself stays dumb (llama-server with `--parallel 1`, or small slot count if KV budget allows).
- Every request carries a priority tag (MCP tool param, default interactive; indexer tags itself background).
- **Cancellation:** propagate client disconnects / MCP cancellations to llama-server (abort the slot). An abandoned 8k-context generation otherwise blocks the GPU for a minute+.
- **Preemption:** don't preempt mid-generation (KV state loss); instead cap background requests at 4k context so the wait behind one is bounded.
- `/api/capacity` gains `queue_depth` and `est_wait_ms` so the indexer self-throttles (it already sizes concurrency from this endpoint).

### 3. Security (noted, not yet designed)

mDNS advertises an unauthenticated endpoint that can spend GPU time and, once fallback exists, Anthropic budget. Minimum: API key on TokenSaver tools; likely also on SimpleModelServer. To be designed separately.

### 4. Clarification round-trip

"Claude asks a clarifying question" requires plumbing: Claude → TokenSaver → MCP client → human → back, with state held across the gap. Build it as the **same mechanism as the continuation API** — one suspended-job store:

- Any pipeline stage can suspend a job with `{ "status": "needs_clarification", "job_id": "…", "question": "…", "options": ["…"] }` returned as the MCP tool result.
- Caller resumes via `continue_task(job_id, answer)`. Stored state: original prompt, router output, split plan/contracts if any — not model KV state.
- TTL (e.g. 30 min) with explicit `expired` error on late resume; cap stored jobs to bound memory.
- Same shape serves three cases: ambiguity clarification, explicit part-by-part continuation, and deadline-exceeded partial results. One store, one resume tool.

### 5. Claude output passes the same gate

No path returns unvalidated code — Claude is the arbiter, not infallible. Claude responses containing code go through the identical Roslyn parse (+ project compile) check. If Claude's output fails: one repair round-trip *to Claude* with the diagnostics (not to the 7B — it already failed there), then return flagged `validated: false`. Also applies to Claude's decomposition output: validate `shared_contracts` stubs parse before fanning out parts, or every part inherits the error.

### 6. Eval harness before tuning

Phase 1's "tune thresholds on real traffic" needs a fixed yardstick or it's guesswork:

- Golden set: ~50 prompts covering every routing branch — non-code questions, trivial DTOs, complex single-file, ambiguous, weak-but-clear, >8k, adversarial (prompt asks for Python; prompt contains malformed JSON; empty prompt).
- Each case asserts: expected `routing_reason`, compile-pass of final output, and max latency class.
- Runner: a TokenSaver test project executing the set against the live stack; CI-runnable nightly, mandatory before/after any threshold or model change.
- Track two scalar metrics over time: routing accuracy (% matching expected reason) and end-to-end compile-pass rate. A model or threshold change that moves either down is a regression regardless of anecdotes.

### 7. Operational details

- **Prefix/KV caching:** enable llama-server prompt caching. System prompts, shared contracts, and repair-loop prefixes repeat heavily — this is the cheapest large win on a 1660, where prompt processing (no tensor cores) is the slow part.
- **Warm-up:** Loader's health loop should send a 1-token generation to each model at startup so the first real request doesn't eat 7B load + graph-compile time; report `warming` vs `ready` in `/api/capacity`.
- **Reproducibility:** pin model files by SHA256 in config (not just tag names — `latest` tags drift); log per request: model hash, quant, sampling params, `routing_reason`, repair count, final validation status. This is the audit trail for "why did it produce this."
- **VRAM measurement on Windows:** the 1660 also drives the display; free VRAM varies with what's on screen. Benchmark (Phase 0) with a browser + IDE open, take the *minimum* observed headroom, and have the Loader read actual free VRAM at startup to choose KV size rather than assuming a constant.

## Retrieval & router follow-ups (from benchmark data, 2026-07-07)

Phase 0 results (`docs/benchmarks/phase0-results.md`) and the TokenSaver retrieval benchmark
(`TokenSaver/docs/retrieval-benchmark/RESULTS.md`, design in `TokenSaver/docs/retrieval-design.md`) changed several plan assumptions:

- **Model lineup is now 3B(base) + 0.5B(base) draft** (was 7B) — pending a quality head-to-head: run the 5-task compile check against the 7B+0.5B pair before locking in. The 3B scored 2/5 clean; some failures (hallucinated dependencies, class-vs-record) *compile fine* and are invisible to the Roslyn gate. The compile gate is necessary but not sufficient — the golden set needs spec-compliance assertions (`must_contain`).
- **Router pre-filter → embedding-kNN experiment.** The 1.5B router failed the every-request latency bar (p95 ~4.6s). The Qwen3 embedder classifies at ~53ms; try kNN over labeled example prompts as the pre-filter, 1.5B only on low-confidence margins. Bar: ≥80% intent accuracy at <200ms on the A4 prompt set.
- **0.5B-Instruct standalone fast-path**: must sit behind the same validity gate + spec checks; measure its defect rate on the golden set before any traffic routes to it. (This is the racing-quality concern in new clothing.)
- **Reranker: CPU only, conditional, with an abstain threshold** — see `retrieval-design.md`. Retrieval feeding codegen context treats no-match as "omit," never best-guess.
- **Backend**: 3B pair runs on Vulkan or CUDA; the CUDA draft-loading crash was 7B-specific. Benchmark script fixes needed: guard `predicted_ms ≤ 0` (bogus 1000000 tok/s rows), populate the A2-vs-A3 speedup table, always fill A1 peak-VRAM cells.

## Chained-pipeline benchmark: router stage validated end-to-end (2026-07-07)

Everything above benchmarked the router (A4) and the 3B+0.5B pair (quality check) **in isolation** —
no test had ever actually sent a prompt through the router and piped its output into the codegen
pair. Two new benchmarks close that gap (`build/scripts/chained-pipeline-benchmarks.ps1`, driven by
`build/scripts/run-chained-benchmarks.ps1`; results and per-task raw outputs in
`docs/benchmarks/phase0-results.md` and `tests/quality-check/`), run twice independently against
5 tasks x {Base, Instruct} = 10 generations per benchmark per run:

- **B1: router (1.5B classify+rewrite) → 0.5B+3B speculative pair.** This is exactly the Phase 1 +
  Phase 2 chain as planned. Result: **10/10 and 9/10 usable across the two runs** — same quality
  ceiling as the already-documented standalone 3B+0.5B pair (the `class`-vs-`record` and occasional
  compile-bug defect rate noted above is unchanged whether the router sits in front or not), same
  ~60 tok/s generation throughput once each combo runs in its own process, and the router only adds
  ~4-6s up front. **This is the first empirical evidence the planned Phase 1→2 chain actually works
  end-to-end**, not just as two isolated benchmarks — de-risks starting Phase 1.
- **B2: router → 0.5B(-instruct) rewrites/expands the prompt into a spec → 3B alone (no speculative
  decode) generates from that spec.** This tests a rewrite-stage shape close to the already-dropped
  "0.5B planner stage" idea (see the dropped-ideas table below), just as a prompt-rewriter instead of
  a plan-producer. Result: **0/10 and 1/10 usable across the two runs** — the rewrite step's
  code-free spec output, fed to the 3B as a raw completion prompt with no "now write the code"
  primer, overwhelmingly makes the 3B continue the spec as a bullet-list repetition loop instead of
  switching to code. **Confirms the original "0.5B planner misleads the main model" call empirically**,
  in a different shape (rewriter, not planner) — reinforces that decision rather than reopening it.
  Root cause looks fixable (a code primer appended after the rewritten spec) but wasn't tried in this
  pass; not worth another benchmark run before that fix lands.
- Also reproduced the anticipated `predicted_ms ≤ 0` benchmark-script bug flagged just above: one B2
  run produced a genuinely empty generation, and the raw `predicted_per_second` came back as
  `1000000` in the table instead of `0`/`n/a`. Same fix applies here.

Net effect on this plan: no changes to the architecture or phase order. B1 raises confidence in
Phase 1; B2 is a negative result against a shape that was never part of the plan (the 0.5B planner
was already cut) and doesn't reopen that decision — it just adds a second, independent data point
for why it was cut.

## What was dropped and why

| Dropped | Reason |
|---|---|
| DeepSeek-Coder-7B-Lite | Tokenizer-incompatible with Qwen draft; superseded by Qwen2.5-Coder-7B |
| 0.5B planner/rewrite stage feeding the 3B alone | Bad plans mislead the main model; 1.5B plans in the router call. Confirmed twice more empirically by the B2 chained benchmark (2026-07-07, see above) using a rewriter shape instead of a planner — 0/10 and 1/10 usable outputs, same underlying failure (main model gets misled by an intermediate 0.5B pass) |
| Racing / token comparison | Ships draft-quality code; token divergence between models is noise |
| Divergence → Claude trigger | Would fire on nearly every request |
| §9 heuristic confusion list | Replaced by objective Roslyn gate |
| 10k/12k context tiers, sliding window | Performance cliff / silent context loss |
| MoE discussion | Never viable in 6GB |
