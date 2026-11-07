# Plan: Phase 4, maintainability

Status: done. Moved to docs/plans/archived/ after U6.
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
- [x] U2. Chat client: use the active profile's model.
  - `ChatService.ResolveModelNameAsync`: read `/admin/config`. Use `global.configurationId` (or the default profile if null) to find the profile. Use its `mainModel` when non-empty.
  - Fallback: the existing `/api/tags` first-model logic, when `/admin/config` is unreachable or has no mainModel.
  - Remove the TODO comment.
  - Tests: the chat client (`Chat.Maui`) has no test project, and its selection helper `SelectActiveMainModel` is internal. Verification is a clean Release build and a review. Adding a client test project is a separate decision.
- [x] U3. Split `RouterHttpServer.cs` (1291 lines) into partial files by route family. Done: core, Health, Generate, Chat, Embeddings. No admin routes live in this class; those are in `AdminEndpointHandler`. Pure move, verified: every original line is present.
- [x] U4. Done: `AppConfig` is partial (core, Models, Mcp, Agents, Global, Helpers). Each top-level type has its own file (`GlobalConfig`, `DebugConfig`, `DraftMode`, `AgentConfig`, `McpUpstreamConfig`, `ModelSourceConfig`, `HardwareSetting`). Pure move, verified. Original file was 1006 lines, not 996.
  - Original spec: split into partial files: global, models, agents, MCP servers. `AppConfig` must become `partial`. Keep the model-parsing constants (`DefaultEmbedModelName`, default sizes, `DefaultStopTokens`) with the model-parsing code. `GlobalConfig`, `AgentConfig`, `McpUpstreamConfig`, and `ModelSourceConfig` are separate classes and move as whole files.
- [x] U5. Done. `ModelHealthMonitor` has Recovery and Warmup parts; state, timeouts, and logging stay in the main file. `AdminEndpointHandler` has Config and McpServers parts; reload and the logging stay in the main file. Pure move, verified.
  - Original spec: split into partial files by concern. `ModelHealthMonitor` shared state (`healLock`, `modelLastActive`, `warmingUpModels`, `modelWarmupFailures`, `recoveryInProgress`, `lastFault*`, `consecutiveUnhealthyCycles`) and the `static readonly` timeouts stay in the main file, so the lock protects state in one place. Only one part carries the primary-constructor parameters.
- [x] U6. Pragma review. Done. Dispositions:
  - `Discovery/ResolvedServerConnection.cs` CA1056: keep. The string is kept for serialization, as the comment says.
  - `UI/Controls/SettingsPanel.xaml.cs` CA1063 ×2: fixed. The class is now `sealed` (nothing subclasses it), and both pragmas are removed.
  - `Providers.Ollama/OllamaGenerationBackend.cs` CA2000: keep. False positive: the reader's ownership moves into the handle, as the comment says.
  - `Providers.LMStudio/LMStudioGenerationBackend.cs` CA2000: keep. Same reason.
  - `Server/SimpleModelPairApp.cs` CA1873 ×2: keep. Both calls are inside an `IsEnabled` guard, as the comments say.
  - Result: 5 disable/restore pairs remain (was 7 disable pairs). Each has a justification comment.

Done when: a clean Release build of the solution has 0 warnings, tests pass, the TODO is gone, no file in U3-U5 is over ~600 lines without a reason, and the pragma review is recorded.

Risks: U3-U5 move many lines. Each split must be a pure move, verified by `git diff -M --stat` showing no content change except the partial declarations. Profile model names depend on the chosen configuration; a profile with no main role gets null and falls back.
