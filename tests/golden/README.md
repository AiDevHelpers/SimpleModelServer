# Golden Evaluation Set

This directory contains the golden test set for the SimpleModelServer staged pipeline (see `docs/staged-pipeline-plan.md`). Each file is one test case asserting the correct behavior of the full request flow — from router classification through codegen, Roslyn validity gate, repair loop, and Claude fallback.

## Schema

```json
{
  "id": "<category>-<slug>-<nn>",
  "category": "<category prefix>",
  "prompt": "<exact user prompt>",
  "expected": {
    "intent": "code_generation | question | explanation | meta | other",
    "quality": "good | weak | invalid",
    "ambiguity": "none | low | medium | high",
    "routing_reason": "local_answer | codegen | ambiguous | context_overflow_split | repair_exhausted",
    "must_compile": true,
    "latency_class": "fast | normal | slow",
    "must_contain": ["substrings REQUIRED in final output"],
    "must_not_contain": ["substrings FORBIDDEN in final output"]
  },
  "notes": "why this case exists / what failure it guards against"
}
```

`must_compile`, `must_contain`, and `must_not_contain` are only present on `code_generation` cases. `must_contain` entries assert spec compliance that the compiler cannot check (e.g., `"record CustomerSpendSummary"` when the prompt demands a record — a class would compile but violates spec).

## Freeze rule

Expected labels are frozen after human review. Changing any `expected` field requires a commit whose message explains **why the label changed** — not just what changed. This ensures regressions are visible in git history and cannot be silently papered over by relabeling.

## Category counts

| Category | Count | routing_reason |
|---|---|---|
| `question-` | 4 | `local_answer` |
| `meta-` | 2 | `local_answer` |
| `trivial-` | 8 | `codegen` (`latency_class: fast`) |
| `complex-` | 8 | `codegen` (`latency_class: normal`; includes all 5 prompts from `tests/prompts/csharp-tasks.json` verbatim) |
| `weak-` | 6 | `codegen` (quality: weak; router must rewrite before sending) |
| `ambiguous-` | 6 | `ambiguous` |
| `overflow-` | 4 | `context_overflow_split` |
| `adversarial-` | 8 | mixed (see individual notes) |
| `repair-` | 4 | `codegen` (exercises Roslyn gate + repair loop; latency_class: slow) |
| **Total** | **50** | |

## Eval harness

The runner (planned for `SimpleModelServer.Evals` or the TokenSaver repo) executes every case against the live stack and produces:
- **routing accuracy** — percentage of cases where the logged `routing_reason` matches `expected.routing_reason`
- **compile-pass rate** — percentage of `must_compile: true` cases where final output passes Roslyn `CSharpSyntaxTree.ParseText` (and project compile where available)

Reports are stored in `tests/golden/reports/` with a timestamp, git SHA, and model hashes. Any threshold, prompt-template, or model change requires a before/after run; either scalar dropping is a regression.
