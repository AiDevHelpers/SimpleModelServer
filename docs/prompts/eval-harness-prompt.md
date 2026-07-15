# Prompt: Build the eval harness (SimpleModelServer.Evals)

Copy everything below the line into a Sonnet session running in the `F:\mcp\SimpleModelServer` repo.

---

Build the golden-set eval harness for this repo: a .NET console project that runs the test cases in `tests/golden/` against live llama-server endpoints and scores the results. Code only — do not edit the golden cases' `expected` blocks (they are frozen ground truth).

## Context (read these first)

- `tests/golden/README.md` + a few case files — the case schema you're consuming.
- `docs/staged-pipeline-test-plan.md` Part C — the harness spec. This prompt supersedes it where more specific.
- `docs/staged-pipeline-plan.md` — the pipeline; the harness tests the router stage and codegen quality independently.
- `build/scripts/phase0-benchmarks.ps1` — how llama-server is invoked/managed in this repo today (ports, flags, model paths). Reuse its conventions; the harness itself should NOT manage server processes — it takes base URLs and assumes servers are running (document the launch commands in the README you write).
- `src/` — follow the existing solution layout (net10.0, same style as `SimpleModelServer.Library.Tests`). Add the new project to `SimpleModelServer.slnx`.

## Project: `src/SimpleModelServer.Evals` (console app)

### CLI

```
SimpleModelServer.Evals --mode router  --router-url http://localhost:PORT [--cases tests/golden] [--filter prefix]
SimpleModelServer.Evals --mode codegen --gen-url http://localhost:PORT --label 3b-base-pair [--filter prefix] [--n-predict 1800] [--temperature 0.2]
SimpleModelServer.Evals --mode all     --router-url ... --gen-url ... --label ...
```

`--label` is a free-text tag recorded in the report (e.g. `3b-base-pair`, `7b-instruct-pair`) so two runs are comparable — this is how the 3B-vs-7B head-to-head works: same command, different `--gen-url`/`--label`.

### Mode: router (A5)

For every golden case: send the case `prompt` to the router model via the OpenAI-compatible `/v1/chat/completions` endpoint with JSON-schema-constrained output (same schema the plan defines: intent/quality/ambiguity/confidence/rewritten_prompt/plan/response/routing_reason — check `build/scripts/phase0-benchmarks.ps1` for the exact request shape used in the A4 test and reuse it). Compare parsed `intent`, `quality`, `ambiguity`, `routing_reason` against `expected`. Record per-case latency.

Scores: intent accuracy, routing_reason accuracy (the headline), per-field confusion counts (e.g. meta→question: 2), p50/p95 latency.

### Mode: codegen (quality)

Only cases whose `expected.routing_reason == "codegen"`. For each:

1. Send `prompt` to the generation endpoint (`/v1/chat/completions`, temperature 0.2, n_predict default 1800, stop on natural finish).
2. Extract C# code blocks from the response (fenced blocks first; if none, heuristic: largest brace-balanced region).
3. **Parse gate:** Roslyn `CSharpSyntaxTree.ParseText` — zero error-severity diagnostics required.
4. **Compile gate:** `CSharpCompilation` against standard refs (System.*, System.Linq, System.Text.Json, Microsoft.Extensions.* if needed). Per-case stub support: if `tests/golden/stubs/<case-id>.cs` exists, include it in the compilation (stubs define types the prompt says to assume exist). Create stub files for the cases that need them — the 5 prompts from `tests/prompts/csharp-tasks.json` document their assumed types; mine `docs/benchmarks/phase0-results.md` (quality-check section) for what was stubbed there.
5. **Assertion checks:** `must_contain` / `must_not_contain` as case-sensitive substring checks against the extracted code (not the prose).
6. Record: pass/fail per gate, diagnostics on failure, generation latency, output token count, `finish_reason` (flag repetition loops: `length` finish + repeated line detection).

Scores: parse-pass rate, compile-pass rate, assertion-pass rate, fully-clean rate (all three), per-case table.

**No repair loop in v1** — the harness measures raw model quality; the repair loop is a pipeline feature tested later. Structure the codegen runner so a repair step can be inserted behind a flag without rewriting (e.g. a `IGenerationPipeline` seam).

### Reports

Write to `tests/golden/reports/<timestamp>-<mode>-<label>.json` (machine) and `.md` (human summary table). Include: git SHA (`git rev-parse HEAD`), label, endpoint URLs, model info from the server's `/v1/models` if available, the two headline scalars (routing accuracy, compile-pass rate), and per-case rows. Never overwrite existing reports.

### Quality bar

- xUnit tests for the pure parts (code-block extraction, assertion checking, report serialization) in `src/SimpleModelServer.Evals.Tests`, following the existing test project conventions. No tests that require a live server.
- Graceful per-case failure: a timeout or malformed response marks the case failed with the error recorded and the run continues. Per-case timeout 180s default, configurable.
- `dotnet build` and `dotnet test` must pass. Print the final summary table to stdout.

### Finish by

1. Printing usage examples for the three concrete runs planned: A5 router accuracy, 3B-pair codegen quality, 7B-pair codegen quality.
2. Writing `src/SimpleModelServer.Evals/README.md` covering CLI, server launch prerequisites (with the actual llama-server commands from the phase0 script conventions), stub-file mechanism, and report format.
