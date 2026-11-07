# Plan: Phase 3, warnings to zero

Status: in progress.
Base: `quality-phase2-eol` (PR #3 into `dev`). The tab reformat from Phase 2 is needed for these edits.

Decision (user): fix all suppressed warnings and remove their suppressions. Turn on `TreatWarningsAsErrors` for Release.

Baseline: a clean rebuild with the current `NoWarn` list reports 0 warnings. With the list removed (only `NU1603` kept), it reports 52 unique warnings. The MAUI `CS0618` warnings accepted earlier no longer appear: MAUI 10.0.120 wraps them in `#pragma warning disable 0618`.

Each unit removes its own codes from `NoWarn` in `src/Directory.Build.props`. That way the build shows whether the fix worked.

Units (each: remove the unit's codes from `NoWarn`, clean Release build with 0 warnings, tests pass, update the status below, commit, start the next):

- [x] U1. `xUnit1051` (31, test projects). Pass `TestContext.Current.CancellationToken` where a `CancellationToken` is accepted. Remove `xUnit1051` from `NoWarn`.
- [x] U2. `xUnit2002` (1). `InterfaceContractTests.cs:75`: replaced the `Assert.NotNull` on a `JsonElement` struct with an array-kind check. Removed `xUnit2002`.
- [x] U3. `CS8604` (6) and `CS8600` (1). Changed: the logger parameters in `AdminEndpointHandler.cs` (`LogAdminInvalidConfigUpdate`, `LogAdminFailedConfigUpdate`) and `SimpleModelPairGenerationService.cs` (`LogResearchJinaFailed`) to `string?`, since a null error is valid to log. This replaces the earlier `?? string.Empty` idea. `AgentConfigService.cs:155`: the local is now `string?`. `ConfigurationUpdaterTests.cs:329`: added `Assert.NotNull` before `Assert.NotEmpty`. Removed `CS8604` and `CS8600`.
- [ ] U4. `VSTHRD200` (1). Rename `ChatViewModelTests.cs:253` `Create<T>` to `CreateAsync<T>`. It has no callers outside that file. Remove `VSTHRD200`.
- [ ] U5. `CS1591` (12, `SimpleModelServer.UI`). Add XML doc comments to the public members listed in the build output. Remove `CS1591`.
- [ ] U6. Remove every remaining code from `NoWarn` except `NU1603`. Delete the stale comment block above the property. Add `TreatWarningsAsErrors` for Release as a conditional PropertyGroup in `src/Directory.Build.props`. Check `src/Extensions/SimpleModelServer.Extensions.VS/Properties/PublishProfiles/*.pubxml`, which set `TreatWarningsAsErrors` to false, and align them. Then a clean Release rebuild of the whole `SimpleModelServer.slnx`, with the MAUI and VS workloads installed, reports 0 warnings, and tests pass.

Done when: a clean Release build of the full solution has 0 warnings with `TreatWarningsAsErrors` on, and tests pass. The Release build fails on any new warning, which covers the quality plan's CI warning gate.

Risks: `NU1603` is kept by the override, so it may appear after U6. If it does, decide with the user before suppressing it. Release-only warnings can appear that a Debug build doesn't; U6 covers this with the full Release rebuild.
