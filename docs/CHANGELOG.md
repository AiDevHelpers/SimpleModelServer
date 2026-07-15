# Changelog

All notable changes to this project are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow the scheme resolved
by Nerdbank.GitVersioning from `version.json`.

## [Unreleased]

### Added

- NuGet packaging for `SimpleModelServer.Config` and `SimpleModelServer.Library` (`dotnet pack`, output to
  `build/dist/<config>/nuget/`).
- MSI installer packaging for `SimpleModelServer.Loader` via WiX (`build/wix/SimpleModelServer.Setup`),
  output to `build/dist/<config>/SimpleModelServer-Setup-<version>.msi`.
- `version.json` for Nerdbank.GitVersioning.
- `.gitignore`, `README.md`.
- **Agent Prompt Editing**: New `AgentConfigService` and `GoldenSetRunner` classes in the shared UI library enable
  editing agent prompts (code/debug/planning) via the admin API (`POST /admin/config` with `agents.<name>.systemPrompt`).
  When a prompt is changed, the response flags `suggestGoldenSet: true`, allowing the XAML settings panel to offer
  running the golden set eval (`SimpleModelServer.Evals --mode router`) to validate the new prompt before trusting it.
- **LM Studio Smoke Test**: New `RealLMStudioSmokeTests` integration test that connects to a real LM Studio instance
  on the default port (1234), fetches available models, sends a real chat request, and verifies a non-empty response.
  Parallels the existing `RealOllamaSmokeTests` for smoke testing against live local model servers.
- **Settings Panel Agent Prompts Section**: The shared `SettingsPanel` control now includes an "Agent Prompts" tab
  where developers can pick an agent, edit its system prompt, save changes via the admin API, and optionally run
  the golden set to validate the change.
- **TokenSaver Integration Guide**: New documentation and PowerShell setup script for configuring TokenSaver.MCP
  to use SimpleModelServer as its backend. Enables multi-machine shared inference with TokenSaver handling token
  accounting and context management, SimpleModelServer providing routing and optimization. Includes troubleshooting
  guide, network configuration examples, and automated setup helper (`Setup-TokenSaver-RemoteRouter.ps1`).
- **TokenSaver SignalR Integration Guide**: Complete implementation guide for adding real-time model reload
  coordination to TokenSaver.MCP via SimpleModelServer's `ModelStatusHub`. Includes production-ready C# code
  for `TokenSaverSignalRClient`, integration patterns for pause/resume handling, graceful degradation when
  SignalR is unavailable, and unit test examples.

### Fixed

- **WiX Installer Path**: Corrected `PublishDir` path in `SimpleModelServer.Setup.wixproj` to match the actual
  staging directory used by the `PublishSimpleModelServerLoader` target. Was looking 4 levels up from the .wixproj
  (to build root), now correctly looks 2 levels up to the local `staging/` folder.
- **Command Injection in GoldenSetRunner**: Quoted the router URL in process arguments to defend against
  shell interpretation (Windows `CreateProcessW` with `UseShellExecute=false` is already safe, but quoting adds
  defense in depth).
- **Process Cleanup on Cancellation**: `GoldenSetRunner.RunRouterEvalAsync()` now kills the Evals process if
  `WaitForExitAsync()` is cancelled, preventing zombie processes.
- **HttpClient Disposal**: `AgentConfigService` and `ChatService` now implement `IDisposable`, only disposing the
  `HttpClient` if they created it (allows test injection while preventing connection leaks in production).
- **JSON Parsing Efficiency**: `AgentConfigService` consolidated response parsing to single-pass with
  `ExtractUpdateResponse()`, eliminating redundant re-parsing of the JSON response.
- **IDisposable Pattern**: `SettingsPanel` now properly implements `IDisposable` and disposes child services
  on unload via the `Unloaded` event.
