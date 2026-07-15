# Multi-Model Pipeline: Key Concerns & Constraints (Proposed)

> Status: draft, 2026-07-06. Companion to `staged-pipeline-design.md`. Review notes: `staged-pipeline-review.md` §"Review of the concerns doc".

## 1. Hardware Constraints (GTX 1660)

### 1.1 VRAM Limitations

- Only ~4.5GB VRAM is realistically available for models.
- A single 7B model fits only when quantized (Q4_K_M).
- Multiple GPU models cause OOM crashes, KV-cache thrashing, severe slowdowns.

### 1.2 CPU/GPU Placement

| Model | Placement | Reason |
|-------|-----------|--------|
| Embedded (0.5B–1.5B) | CPU | Fast enough, frees VRAM |
| Planner (0.5B) | CPU | Deterministic, low-latency |
| Main Model (7B-Lite) | GPU | Needs acceleration |
| Speculative Draft (0.5B) | CPU | Avoids GPU overload |

## 2. Context Size

- Sequential (7B only): stable ~8k, usable ~10k, borderline ~12k tokens
- Speculative (CPU + GPU): stable ~6k (KV cache grows on both sides)
- MoE models: higher variance, unpredictable KV cache, harder confusion detection — not recommended

## 3. Speculative Routing

Works only for trivial tasks (DTOs, simple classes, one-liners, boilerplate). Fails on weak prompts, complex logic, multi-file generation, JSON parsing, async workflows, large context, planner/embedded ambiguity, divergent outputs. Failure modes (draft hallucination, mismatched structure, incomplete output, model disagreement, malformed code) trigger Claude fallback.

## 4. Sequential Routing

Required when prompt is weak, correctness matters, task is complex, speculative is unreliable, or ambiguity is medium/high. Produces deterministic plans, predictable code, stable formatting, easier Claude override.

## 5. Embedded Model

Weak embedded models cause bad rewrites, misclassification, incorrect routing, speculative misfires, Claude confusion. Required: strong rewriting, classification, ambiguity detection, structured JSON output, no hallucinated code. Recommended: **Qwen2.5-1.5B-Instruct (CPU)**.

## 6. Planner Model

Must be deterministic, low-variance, short-output, Claude-interpretable, CPU-friendly. Recommended: **Qwen2.5-Coder-0.5B (CPU)**.

## 7. Main Model

Requirements: stable, predictable, consistent formatting, Claude-friendly, fits in VRAM, supports sliding window. Recommended: **DeepSeek-Coder-7B-Lite (GPU, Q4_K_M)**. MoE rejected: higher variance, harder confusion detection, unpredictable expert activation, worse fallback behavior.

## 8. Claude Fallback Triggers

1. Embedded ambiguity (medium/high, unclear intent, contradictions)
2. Speculative pair divergence (structure, code paths, types, indentation)
3. Structural invalidity (missing braces, broken JSON, partial output, repetition loops, hallucinated namespaces)
4. Planner ambiguity (unclear/contradictory/missing steps)
5. User intent unclear → Claude asks clarifying questions
6. Local model hallucination → Claude overrides

## 9. Routing Summary

- **Speculative:** good prompt + trivial task + low ambiguity + correctness not critical + 0.5B viable
- **Sequential:** weak prompt, complex task, correctness matters, ambiguity medium/high
- **Claude:** speculative diverges/malformed, sequential incomplete, planner unclear, embedded uncertain

## 10. Stability Rules

Only one model on GPU. No MoE. Sliding window for long tasks. Strong embedded model. Speculative only for trivial tasks.

## 11. Summary

Hardware: 1660 VRAM limits, one GPU model, CPU hosts small models. Models: strong embedded, deterministic planner, stable main, no MoE. Routing: speculative→trivial, sequential→complex, Claude→confusion. Context: sequential 8k–10k, speculative ~6k. Claude: final arbiter, clarifies intent, overrides malformed output.
