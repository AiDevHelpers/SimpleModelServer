# Quality plan

Baseline measured on committed `HEAD` at `39deb22`, .NET 10.0.403 SDK. Re-measure after each phase.

## Baseline

| Check | Result |
| --- | --- |
| Build (Release) | Succeeds, 0 errors |
| Warnings | 94 lines: CS0618 ×52, CS8622 ×32, NU1902 ×6 (1 advisory), CA2024 ×2, xUnit2029 ×1, plus 2 unlabelled |
| Tests | 863 passing across 11 projects (all listed in the solution) |
| `dotnet format --verify-no-changes` | Fails: 87 whitespace errors in 10 files (line endings) |
| `TreatWarningsAsErrors` | Not set anywhere |
| Nullable | Enabled in all 26 projects |
| Test projects | 11, all in `SimpleModelServer.slnx` |
| `#pragma warning disable` | 7, each with a justification comment |
| Empty or comment-only `catch` | 20 in non-test code (2 fully empty) |
| TODO markers | 1 real (`ChatService.cs:17`) |
| Largest files | `RouterHttpServer.cs` 1291 lines, `AppConfig.cs` 996, `ModelHealthMonitor.cs` 632, `AdminEndpointHandler.cs` 602 |
| Code coverage | Not measured |

## Phase 1: Security and error handling (now)

1. **Vulnerable package.** `MessagePack` 3.1.8 has a moderate advisory (GHSA-qhrr-8q5h-9q3h). It is transitive. Find the referencing project with `dotnet list package --include-transitive --vulnerable`, then move to a patched version.
2. **Swallowed exceptions.** `SimpleModelPairApp.cs:196` has an empty catch-all around `Console.Clear()`. Review all 20 empty or comment-only catches. Each one either logs the exception, or gets a comment saying why ignoring it is correct.
3. **Dependency audit in CI.** Add a `dotnet list package --vulnerable --include-transitive` step that fails the build on any finding.

Done when: no NU1902 warnings, and every catch is either logged or justified.

## Phase 2: Line endings and formatting

1. Add `.gitattributes` and `.editorconfig`. The 87 WHITESPACE errors come from mixed line endings. Pick one policy for the whole repo; `eol=lf` is the usual choice for source in git.
2. Re-normalize the 10 affected files in one commit, so the diff is only line endings.
3. Add `dotnet format --verify-no-changes` to CI.

Done when: `dotnet format --verify-no-changes` passes on the committed tree.

## Phase 3: Warnings to zero

1. **CS0618 (52).** MAUI obsolete APIs in the chat client: `Page.DisplayAlert` (28) and `Frame` (24). Move to `DisplayAlertAsync` and `Border`. Keep this out of the server work.
2. **CS8622 (32).** Nullability mismatches. Most are likely event-handler signatures. Fix the signatures rather than suppressing.
3. **CA2024, xUnit2029, unlabelled (5).** Small, fix directly.
4. Turn on `TreatWarningsAsErrors` for the Release configuration, starting with the non-UI projects.
5. Add a CI step that fails on any warning.

Done when: the baseline build has 0 warnings, and CI enforces it.

## Phase 4: Maintainability

1. **Split the four large files.** `RouterHttpServer.cs` (1291 lines) by route family: generate, chat, embeddings, admin, health. `AppConfig.cs` (996) into parsing for global, models, agents, and MCP servers. `ModelHealthMonitor.cs` and `AdminEndpointHandler.cs` by concern.
2. **TODO at `ChatService.cs:17`.** Dual-model handoff is now wireable: the structured profiles (`configurationId`) have landed. Implement it, or move it to an issue and remove the comment.
3. **Pragma suppressions (7).** Review the two CA1063 in `SettingsPanel.xaml.cs` (dispose pattern) and the CA1056 in `ResolvedServerConnection.cs` (string URL, which the comment justifies). Fix the dispose pattern if it's wrong.

Done when: no file over ~600 lines without a reason, and the TODO is resolved or tracked.

## Phase 5: Tests and gates

1. **Pin the SDK.** Add `global.json` pinning 10.0.x. The .NET 11 preview SDK already breaks `dotnet test` on `Server.Tests` on this machine, so every developer hits it.
2. **Measure coverage.** Add coverlet to the test run, record a baseline, and set a floor that only ratchets upward.
3. **Quality gate job in CI.** Mirror the root repo's gate: format check, zero-warning build, vulnerability audit. Run it on every pull request.

Done when: `global.json` is committed, coverage is reported in CI with a floor, and the quality gate job is required.

## Decisions

- **Line-ending policy:** LF in the repo. Not yet applied (Phase 2).
- **Warnings-as-errors:** all projects at once. Phase 3 must clear the 52 obsolete MAUI API and 32 nullability warnings before the switch.
- **Coverage floor:** set after the first measurement, not before.

## Status

- Phase 1, in progress: the CI vulnerability gate is added. The `MessagePack` fix exists in the working tree (3.1.12) but is not committed; the committed tree still pins 3.1.8, so the gate fails until it's committed. The empty catch in `SimpleModelPairApp.RunAsync` is narrowed to `IOException`. The other 18 comment-only catches are not reviewed yet.
