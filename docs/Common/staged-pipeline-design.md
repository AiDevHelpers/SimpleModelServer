# Staged Model Pipeline Design (Proposed)

> Status: draft proposal, 2026-07-06. Successor to the current draft-pair router (see `architecture.md`). Review: `staged-pipeline-review.md`.

## 1. Overview

We use a staged, fault-tolerant pipeline:

- **Embedded model (CPU)** → intent classification, rewriting, ambiguity detection
- **Planner model (CPU)** → deterministic plan generation
- **Main code model (GPU)** → C# code generation
- **Speculative pair (optional)** → fast path for trivial tasks
- **Claude fallback** → final arbiter when local models are confused

Claude is the **ultimate correctness layer**.

## 2. Embedded Model Responsibilities (CPU)

### 2.1 Intent Classification

Classifies user requests into: `code_generation`, `question`, `explanation`, `meta`, `other`.

### 2.2 Prompt Quality Assessment

Labels prompt as: `good`, `weak`, `invalid`.

### 2.3 Prompt Rewriting

If weak, rewrite into a structured, code-ready version.

### 2.4 Ambiguity Detection

Flags: unclear intent, missing details, contradictory instructions, multi-file complexity, unsafe speculative conditions.

### 2.5 Escalation Decision

Determines whether to: handle locally, escalate to planner, escalate to speculative pair, escalate to sequential pipeline, or escalate to Claude fallback.

### 2.6 Local Handling

Handles simple questions without invoking other models.

## 3. Embedded Model Output Schema

```json
{
  "intent": "code_generation | question | explanation | meta | other",
  "quality": "good | weak | invalid",
  "needs_escalation": true,
  "rewritten_prompt": "string or null",
  "ambiguity": "none | low | medium | high",
  "response": "string or null"
}
```

## 4. Pipeline Logic

### 4.1 If `needs_escalation == false`

Return `response`.

### 4.2 If `needs_escalation == true`

Select prompt: `good` → original, `weak` → rewritten. Then choose routing path: speculative, sequential, or Claude fallback.

## 5. Decision Tree: Speculative vs Sequential vs Claude

1. **Is the prompt weak?** → Sequential pipeline
2. **Is the task trivial?** (DTOs, simple classes, one-liners, boilerplate) → Speculative allowed
3. **Does correctness matter?** If correctness > speed → Sequential
4. **Can 0.5B produce valid output?** If no → Sequential
5. **Embedded ambiguity level** `medium` or `high` → Claude fallback
6. **Speculative pair disagreement** → Claude fallback
7. **Structural invalidity** (malformed output) → Claude fallback

## 6. Sequential Pipeline (0.5B → 7B)

### 6.1 Planner (CPU)

Produces deterministic plan:

```json
{
  "type": "plan",
  "steps": ["step 1", "step 2", "step 3"]
}
```

### 6.2 Main Model (GPU)

DeepSeek-Coder-7B-Lite generates final C# code.

## 7. Speculative Pipeline (0.5B + 7B)

### 7.1 When used

Only when: prompt is good, task is trivial, ambiguity is low, correctness is not critical, and 0.5B is likely to produce valid output.

### 7.2 How it works

- CPU 0.5B streams tokens; GPU 7B streams tokens
- Router compares early tokens
- If 0.5B output is valid → return; else wait for 7B
- If outputs diverge → **Claude fallback**

## 8. Claude Fallback System

Claude is invoked when:

1. Embedded ambiguity is medium/high — Claude clarifies intent
2. Speculative pair diverges — Claude resolves disagreement
3. Speculative output is malformed — Claude regenerates clean code
4. Sequential output is incomplete — Claude completes or rewrites
5. Planner output is unclear — Claude rewrites the plan
6. User intent is unclear — Claude asks clarifying questions
7. Local models hallucinate — Claude overrides with correct output

Claude is the **final arbiter** of correctness.

## 9. Claude Confusion-Detection Triggers

Local models producing: mismatched types, missing braces, broken JSON, hallucinated namespaces, partial output, repetition loops, contradictory structure, inconsistent indentation, divergent speculative outputs, planner ambiguity, embedded uncertainty.

## 10. Hardware Placement Rules (GTX 1660)

| Role | Model | Placement | Notes |
|---|---|---|---|
| Embedded | Qwen2.5-1.5B-Instruct | CPU | Strong rewriting and ambiguity detection, no VRAM |
| Planner | Qwen2.5-Coder-0.5B | CPU | Deterministic, low variance, no VRAM |
| Main | DeepSeek-Coder-7B-Lite Q4_K_M | GPU | Stable, predictable, ~8k context stable |
| Speculative draft | Qwen2.5-Coder-0.5B | CPU | Fast, lightweight, no VRAM |

## 11. Context Size Limits

- **Sequential (7B only):** stable 8k, usable 10k, borderline 12k tokens
- **Speculative (CPU + GPU):** stable ~6k tokens
- Claude fallback handles overflow cases.

## 12. Test Plan

- Non-code questions → embedded
- Weak prompts → sequential
- Trivial prompts → speculative
- Complex prompts → sequential
- Divergent outputs → Claude
- Ambiguous intent → Claude

## 13. Goals

Maximize correctness, stability, and clarity; minimize hallucinations and GPU load; fast trivial responses; reliable complex responses; Claude as the final correctness layer; predictable, safe routing.
