# Review: Staged Model Pipeline Design

Reviewed 2026-07-06 against `staged-pipeline-design.md` and the current implementation in `architecture.md`.

## What's solid

Staged escalation with a cheap CPU classifier in front is the right shape for a 6GB card. Keeping the 7B as the only GPU tenant, structured JSON between stages, and an explicit external fallback are all sound. The test plan maps cleanly to routing paths.

## Major issues

### 1. §7 is not speculative decoding — it's racing

What §7.2 describes (both models stream, router compares tokens) is the draft-pair racing the Loader already does. Real speculative decoding has the draft model propose tokens that the *target model verifies in one forward pass* — output quality is exactly the 7B's, with a speedup. The racing design instead *ships 0.5B-quality code* whenever its output "looks valid." Valid ≠ correct: a 0.5B DTO can compile and still have wrong property types. Either call it racing and accept 0.5B output quality on trivial tasks, or do true speculative decoding.

### 2. The chosen pair can't do true speculative decoding

Qwen2.5-Coder-0.5B and DeepSeek-Coder-7B use different tokenizers/vocabularies; llama.cpp/Ollama speculative decoding requires a compatible draft/target pair. If you want real spec-decode, use **Qwen2.5-Coder-7B (main) + Qwen2.5-Coder-0.5B (draft)** — same family, shared vocab, and Qwen2.5-Coder-7B generally benchmarks at or above DeepSeek-Coder-7B-Lite on C#. This also cuts the model zoo from 3 checkpoints to 2 families.

### 3. "Token comparison" between different models is meaningless

Even semantically identical outputs from two different models diverge at the token level within a few tokens (whitespace, identifier choice). §5 step 6 / §8.2 "divergence → Claude" would fire on nearly every non-trivial request, turning the escape hatch into the main path. Compare *validity of the finished artifact* (parse/compile), not token streams.

### 4. Confusion triggers (§9) will flood Claude

"Inconsistent indentation," "mismatched types," etc. are everyday small-model output. Sending each to Claude defeats the local-first goal. Insert a tier between: **local repair retry** (re-prompt the 7B with the compiler error, 1–2 attempts) → then Claude. And make the validity gate objective: run the output through Roslyn (`CSharpSyntaxTree.ParseText` + optionally a compile against the target project). A compiler is a better confusion detector than any heuristic list.

### 5. Decision tree ordering and conflicts (§5)

Ambiguity is checked at step 5, *after* speculative routing is allowed at step 2 — an ambiguous-but-trivial-looking prompt reaches the speculative path. Check ambiguity first. Also §5 step 1 (weak → sequential) conflicts with §8.1 (medium/high ambiguity → Claude) when a prompt is both weak and ambiguous; define precedence explicitly (suggest: ambiguity ≥ medium wins → Claude/clarify).

### 6. The 0.5B planner is the weakest link

"Deterministic" is a sampling setting (temperature 0), not a model property — the 0.5B is no more deterministic than the 1.5B. A 0.5B-generated plan is likely to be shallow or wrong, and a wrong plan actively misleads the 7B. Either have the 1.5B embedded model produce the plan (it's already loaded and has seen the prompt) or drop the planner stage for single-file tasks. Three CPU models is complexity without payoff.

### 7. Who watches the watcher?

The embedded model's JSON (§3) will itself be malformed some fraction of the time, and nothing in the design handles that. Use grammar/schema-constrained generation (Ollama `format: json` / JSON schema, or GBNF) so the classifier physically can't emit broken JSON, plus a default route (sequential) if classification fails or times out.

## Hardware reality check (GTX 1660, 6GB)

7B Q4_K_M weights ≈ 4.1–4.4GB. At 8k context the KV cache pushes total near the 6GB ceiling; 12k will spill layers to CPU and throughput falls off a cliff — it's not "borderline," it's a different performance regime. Recommend: hard cap at 8k with quantized KV cache (q8_0), and route overflow to Claude rather than degrading silently. Also note the 1660 has no tensor cores, so prompt processing is slow — long-context speculative racing on CPU (§11's 6k) will often lose to just running the 7B.

## Schema additions

Add to the §3 output: `confidence` (0–1) and `routing_reason` (string). Without them you can't tune thresholds or debug why a request went where it did. Log every routing decision; the §12 test plan should assert on `routing_reason`, not just final output.

## Review of the concerns doc (`staged-pipeline-concerns.md`)

The concerns doc restates the design's decisions as if they were validated conclusions — it's a summary, not a stress test. It inherits every major issue above (racing mislabeled as speculative decoding, incompatible draft/target tokenizers, token-divergence triggers, no compile gate, no handling of malformed classifier output). Additional problems specific to it:

**Internal contradiction on VRAM.** §1.1 says ~4.5GB is realistically available, while §7.2 recommends 7B Q4_K_M — whose weights alone are ~4.1–4.4GB. Both can't hold: at a 4.5GB budget there is near-zero room for an 8k KV cache, which contradicts §2's "8k stable." Measure actual headroom (`nvidia-smi` with desktop load) and derive the context cap from it.

**Sliding window is asserted, not available.** §7.1 lists "supports sliding window" as a main-model requirement and §10.3 prescribes it, but DeepSeek-Coder-7B has no sliding-window attention; llama.cpp context-shifting is a different mechanism that silently evicts the oldest tokens — dangerous for codegen, where the evicted tokens are often the class definitions the model must reference. Drop this or replace with explicit chunking/summarization.

**MoE sections are a red herring.** Three sections argue against MoE, but no MoE fits usefully in 6GB anyway — it was never a real option, and claims like "harder confusion detection" are unsupported. Cut it.

**Inverted responsibility in §11.** "Claude detects confusion reliably" is backwards: the *router* must detect confusion locally to know when to call Claude. If local detection is weak, Claude is never invoked. All detection (parse/compile checks, timeouts, schema validation) is the orchestrator's job; Claude only remediates.

**Triple-stated routing rules.** Sections 3, 8, and 9 each restate the routing conditions with slightly different wording ("different indentation" appears as a divergence trigger only in §8.2). Duplicated rule lists drift; keep one canonical rule table and reference it.

## Review of the goals doc (`staged-pipeline-goals.md`)

As a goal statement it's accurate, but it commits a spec-writing error: it bakes *mechanisms* into the *goals*. Items 2, 4, and 6 (planner stage, speculative routing, divergence-triggered fallback) are solution choices — the same ones flagged as flawed above — not outcomes. If they're stated as goals, fixing them later looks like scope change instead of correction. Rewrite goals at the outcome level: "trivial requests return in under N seconds," "delivered code compiles against the target project," "external-API spend below X per day." Two things it gets right and the other docs don't state: point 7 (keep local output low-variance so remediation is cheap) is the best design principle in any of the three documents, and point 8 gives testable failure conditions. Keep those; make point 8 the acceptance-test list.

## Priority of changes

1. Swap main model to Qwen2.5-Coder-7B and use true speculative decoding with the 0.5B draft (kills issues 1–3).
2. Add Roslyn parse/compile as the validity gate + local repair retry before Claude (kills 4).
3. Reorder decision tree: ambiguity first; define weak+ambiguous precedence (5).
4. Drop or reassign the planner to the 1.5B (6).
5. Constrain classifier output with a JSON schema; add default route (7).
6. Add `confidence`/`routing_reason` and routing telemetry.
