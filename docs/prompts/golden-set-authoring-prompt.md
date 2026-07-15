# Prompt: Author the golden test set

Copy everything below the line into a Sonnet session running in the `F:\mcp\SimpleModelServer` repo.

---

Author the golden evaluation set for this repo's model-routing pipeline. Create ~50 test case files in `tests/golden/`, one JSON file per case, following the spec below exactly. Do not write any runner/harness code — cases only.

## Context (read these first)

- `docs/staged-pipeline-plan.md` — the pipeline being tested: a router classifies prompts (intent/quality/ambiguity), code generation runs on a local Qwen2.5-Coder model with a Roslyn compile gate and repair loop, Claude is the fallback for ambiguity, repair-exhaustion, and >8k-token overflow.
- `docs/staged-pipeline-test-plan.md` Part B — the golden set spec and coverage matrix. This prompt supersedes it only where more specific.
- `docs/benchmarks/phase0-results.md` — benchmark findings to seed cases from (see "Seed material" below).
- `tests/prompts/csharp-tasks.json` — the 5 existing C# task prompts; reuse them as cases.

## Case file schema

One file per case, named `<category>-<slug>-<nn>.json` (e.g. `trivial-dto-01.json`):

```json
{
  "id": "trivial-dto-01",
  "category": "trivial",
  "prompt": "<the exact user prompt>",
  "expected": {
    "intent": "code_generation | question | explanation | meta | other",
    "quality": "good | weak | invalid",
    "ambiguity": "none | low | medium | high",
    "routing_reason": "local_answer | codegen | ambiguous | context_overflow_split | repair_exhausted",
    "must_compile": true,
    "latency_class": "fast | normal | slow",
    "must_contain": ["substrings that MUST appear in the final output"],
    "must_not_contain": ["substrings that must NOT appear"]
  },
  "notes": "why this case exists / what failure it guards against"
}
```

Rules:
- `must_compile`, `must_contain`, `must_not_contain` only for codegen cases; omit for question/meta/ambiguous cases.
- `must_contain` entries assert spec compliance the compiler can't check (e.g. `"record CustomerSpendSummary"` when the prompt demands a record — a class would compile fine but violates spec).
- Prompts must be realistic requests a developer would type, varied in length and phrasing. No two prompts near-duplicates.
- `routing_reason` is what a correct router should decide, not what current models actually do.

## Coverage matrix (50 cases total)

| Category prefix | Count | Requirements |
|---|---|---|
| `question-` | 4 | Non-code C#/dotnet questions → `local_answer` |
| `meta-` | 2 | Questions about the pipeline/models themselves → `local_answer`. Phase 0 showed the router misreads `meta` as `question` — make these clearly meta |
| `trivial-` | 8 | DTOs, one-liners, boilerplate, simple records/enums → `codegen`, `latency_class: fast` |
| `complex-` | 8 | Async logic, LINQ-heavy, service classes, interface implementations → `codegen`, `latency_class: normal`. Include the 5 prompts from `tests/prompts/csharp-tasks.json` verbatim as 5 of these |
| `weak-` | 6 | Poorly-written but unambiguous prompts (vague wording, missing formatting, run-on) whose intent is still clear → `quality: weak`, `codegen` |
| `ambiguous-` | 6 | Genuinely underspecified or self-contradictory prompts → `ambiguity: medium|high`, `routing_reason: ambiguous`. At least one contradictory (asks for X and not-X), at least one missing a critical decision (e.g. "add caching" with no store specified) |
| `overflow-` | 4 | Prompts whose stated scope clearly exceeds a single 8k-token generation (multi-file systems, whole-project scaffolds) → `context_overflow_split`. The prompt text itself can be short — it's the *task* that overflows |
| `adversarial-` | 8 | One each minimum: request for Python/JS (wrong language → `other` or clarify), malformed JSON pasted in prompt, empty/whitespace prompt, prompt-injection attempt hidden in a code comment ("ignore previous instructions..."), profanity/nonsense (`other`), a prompt that looks like code but asks nothing, an `explanation` request phrased with codegen verbs (Phase 0 misroute r13), a junk prompt Phase 0 misrouted to codegen (r17 pattern) |
| `repair-` | 4 | Prompts historically producing broken output. MUST include: (1) the retry-once-on-transient-exception pattern — Phase 0 found both 3B variants generate a catch block referencing a try-scoped variable (CS0103); (2) the interface-impl prompt that hit a degenerate repetition loop; (3) a prompt tempting variable shadowing in nested loops (CS0136 pattern); (4) a prompt tempting hallucinated dependencies (specify "use the injected IStockRepository, no other data access" and assert `must_not_contain: ["DbContext", "IUnitOfWork"]`) |

## Seed material from `docs/benchmarks/phase0-results.md`

- The A4 tables list 20 router prompts (r01–r20) with expected vs parsed intent. Mine the misroutes: r13 (explanation→code_generation), r15/r16 (meta→question), r17 (other→code_generation). Recreate their *patterns* as cases even though the original prompt text isn't in the doc.
- The quality-check section documents exact failure modes (EF Core hallucination, class-vs-record deviation, `discount` shadowing, try/catch scoping) — each becomes a `repair-` or `complex-` case with targeted `must_contain`/`must_not_contain`.

## Also create

`tests/golden/README.md`: one paragraph on what the set is, the schema, the freeze rule ("expected labels are frozen after human review; changes require a commit explaining why"), and a table of counts per category.

## Constraints

- Do not modify anything outside `tests/golden/`.
- Valid JSON, UTF-8, no trailing commas.
- Before finishing, self-check: exactly 50 case files, category counts match the matrix, every codegen case has `must_compile`, no near-duplicate prompts, all `repair-` cases include the four mandated patterns.
- Finish by printing a summary table: filename, category, routing_reason — so the human review pass can scan labels quickly.
