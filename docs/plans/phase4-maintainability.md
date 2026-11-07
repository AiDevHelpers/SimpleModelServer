# Plan: Phase 4, maintainability

Status: in progress.
Base: `quality-phase3-warnings` (PR #4 into `dev`). Phase 3 is required: warnings are errors in Release.

Decisions (from the user):
- Large files are split as partial classes: same types, same APIs, one file per route family or concern. No new classes, no DI changes.
- ChatService TODO (`src/Clients/SimpleModelServer.Chat.Maui/Services/ChatService.cs:17`): the chat client uses the active profile's main model. The TODO is removed.
- Server change: each profile in `/admin/config` gets its main model name (`mainModel`).

Facts checked:
- The profile picker already exists in `SettingsPage.xaml.cs` (lines 173-225). It sets `global.configurationId`, which applies after a server restart.
- `ModelProfile` is `src/Contracts/SimpleModelServer.Contracts/Configuration/ModelProfile.cs`: `record ModelProfile(int Id, string Nickname, bool IsDefault)`. Its only constructor call is in `AppConfig.cs:72` (`ParseModelProfiles`).
- `ParseStructuredModels(configurations, configurationId)` in `AppConfig.cs` resolves one configuration's roles. The main role's name is `roles["main"]`.

Units (each: build clean in Release with 0 warnings, tests pass, update status, commit, start the next):

- [x] U1. Server: profile main model name. Done. Also restored `NU1701` in `NoWarn` with a reason: the VS SDK test packages target .NET Framework. Phase 3 had removed it and missed the warnings on a fresh restore.
  - Add `string? MainModel` to `ModelProfile` (default null).
  - `ParseModelProfiles`: for each configuration, set `MainModel` with `roles.TryGetValue("main", ...)`, null when absent. Duplicate configuration ids resolve to the first match (existing `SelectConfiguration` behavior); noted, not changed.
  - Declare as `string? MainModel = null` and document the parameter, because XML-doc warnings are errors.
  - `AdminEndpointHandler`: add `mainModel = p.MainModel` to each profile in `/admin/config`.
  - Tests: parse with two configurations, check each profile's MainModel. Check `/admin/config` output includes it.
- [ ] U2. Chat client: use the active profile's model.
  - `ChatService.ResolveModelNameAsync`: read `/admin/config`. Use `global.configurationId` (or the default profile if null) to find the profile. Use its `mainModel` when non-empty.
  - Fallback: the existing `/api/tags` first-model logic, when `/admin/config` is unreachable or has no mainModel.
  - Remove the TODO comment.
  - Tests: profile selection and fallback.
- [ ] U3. Split `RouterHttpServer.cs` (1291 lines) into partial files by route family: generate, chat, embeddings, admin, health.
- [ ] U4. Split `AppConfig.cs` (996 lines) into partial files: global, models, agents, MCP servers. `AppConfig` must become `partial`. Keep the model-parsing constants (`DefaultEmbedModelName`, default sizes, `DefaultStopTokens`) with the model-parsing code. `GlobalConfig`, `AgentConfig`, `McpUpstreamConfig`, and `ModelSourceConfig` are separate classes and move as whole files.
- [ ] U5. Split `ModelHealthMonitor.cs` (632) and `AdminEndpointHandler.cs` (602) into partial files by concern. `ModelHealthMonitor` shared state (`healLock`, `modelLastActive`, `warmingUpModels`, `modelWarmupFailures`, `recoveryInProgress`, `lastFault*`, `consecutiveUnhealthyCycles`) and the `static readonly` timeouts stay in the main file, so the lock protects state in one place. Only one part carries the primary-constructor parameters.
- [ ] U6. Pragma suppressions: review all 7 `#pragma warning disable` lines, each with a recorded disposition (fix or keep with justification):
  - `Discovery/ResolvedServerConnection.cs:12` CA1056
  - `UI/Controls/SettingsPanel.xaml.cs:13` and `:30` CA1063 (fix the dispose pattern if wrong)
  - `Providers.Ollama/OllamaGenerationBackend.cs:37` CA2000
  - `Providers.LMStudio/LMStudioGenerationBackend.cs:28` CA2000
  - `Server/SimpleModelPairApp.cs:211` and `:453` CA1873

Done when: a clean Release build of the solution has 0 warnings, tests pass, the TODO is gone, no file in U3-U5 is over ~600 lines without a reason, and the pragma review is recorded.

Risks: U3-U5 move many lines. Each split must be a pure move, verified by `git diff -M --stat` showing no content change except the partial declarations. Profile model names depend on the chosen configuration; a profile with no main role gets null and falls back.
