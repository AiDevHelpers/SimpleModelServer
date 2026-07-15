# Staged Pipeline: Goal Statement (Proposed)

> Status: draft, 2026-07-06. Companion to `staged-pipeline-design.md` and `staged-pipeline-concerns.md`. Review notes: `staged-pipeline-review.md` §"Review of the goals doc".

## Goal

A multi-model, fault-tolerant, GPU-aware code-generation pipeline that:

1. **Runs locally on a GTX 1660** — one heavy model on GPU, all others on CPU, no VRAM crater, stable context window.
2. **Uses small CPU models to classify, rewrite, plan, and draft** — embedded model (classify/rewrite/ambiguity), planner model (deterministic plan), draft model (speculative fast path).
3. **Uses a 7B GPU model for final C# generation** — stable, predictable, Claude-friendly, fits in VRAM quantized.
4. **Uses speculative routing only for trivial tasks** — DTOs, simple classes, boilerplate, one-liners.
5. **Uses sequential routing for complex tasks** — multi-file, async logic, JSON parsing, service layers, anything correctness-critical.
6. **Falls back to Claude when local models are confused** — divergent/malformed speculative output, incomplete sequential output, unclear planner, flagged ambiguity, unclear user intent.
7. **Keeps Claude's job easy** — local models are stable, predictable, low-variance, easy to override and sanity-check.
8. **Produces reliable C# under hardware constraints** — no OOM crashes, KV-cache thrashing, speculative misfires, ambiguous routing, or hallucinated code.

## One-sentence version

A local multi-model code-generation pipeline that uses small CPU models for routing and planning, a single 7B GPU model for final code, speculative routing for trivial tasks, and Claude as the fallback when the local models are confused.
