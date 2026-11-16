# Plan: Phase 5, tests and gates

Status: in progress.
Base: `quality-phase4-maintainability` (PR #5 into `dev`). Stacked on PRs #3, #4, #5.

Decisions (from the user):
- Coverage tool: Microsoft.Testing.Extensions.CodeCoverage 18.12.0 (depends on Testing Platform 2.5.0, resolves to 2.5.1). Replaces `coverlet.collector`, which is a VSTest data collector and has no effect on the Testing Platform.
- Coverage floor: 49% line coverage, measured merged across the 11 test projects. The floor only ratchets upward.
- Quality gate: a separate `quality-gate` CI job. Branch protection is not available on this private repo, so the gate can't be a required check. Review decision: `quality-gate` is also in `installer.needs`, so a gate failure blocks release assets on `dev` and `main`.
- Floor check reads `summary.linecoverage` from ReportGenerator's `Summary.json`, not the text summary, which is locale-dependent.
- `TestResults/` is in `.gitignore`. The merged coverage report goes to `build/dist/coverage/`, which is already ignored.

Facts checked:
- Step 1, pin the SDK: already done in Phase 1. `global.json` pins `10.0.100` with `rollForward: latestFeature`, which covers the 10.0.x line.
- Step 2, measure coverage: done. Per-project reports are written to `TestResults/<guid>/` (each run writes its own file). A fixed `--coverage-output` path fails, because the 11 test hosts write to the same file in parallel. Merged with ReportGenerator 5.5.12: line coverage 49.8%, 3,332 covered lines, 144 classes, 122 files.

Units (each: clean Release build with 0 warnings, tests pass, update status, commit, start the next):

- [x] U1. Coverage tool. Replace `coverlet.collector` with `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0 in all 11 test projects and in `Directory.Packages.props`. Measured 49.8% merged.
- [x] U2. Coverage floor file `build/coverage-floor.txt` (`49`). `TestResults/` added to `.gitignore`. The floor only moves up.
- [x] U3. CI `quality-gate` job in `.github/workflows/ci.yml`. It runs on every event. Steps: restore, format check, Release build (warnings are errors), vulnerability audit, test with `--coverage`, then the merged floor check (`Summary.json`, `linecoverage` against `build/coverage-floor.txt`). The check fails with no reports, or if coverage is below the floor. `installer.needs` now includes `quality-gate`. The floor check logic was run locally: 49.8% against 49, pass. The YAML is not validated locally; the first CI run is the check.
- [ ] U4. Verify end to end: clean Release build with 0 warnings, tests pass, format passes, vulnerability audit clean, and the merged coverage is at or above 49%.

Done when: `quality-gate` runs in CI on every push and pull request, the floor is enforced from `build/coverage-floor.txt`, and the plan's verification passes locally.

Risks: `coverlet.collector` is removed, so any local coverage workflow that depended on it changes. ReportGenerator is installed in CI as a global tool; its version is pinned to 5.5.12 to match the local measurement. The `quality-gate` job duplicates some work in `build-and-test` (build, format, vulnerability audit). This is accepted because the gate is a separate status.
