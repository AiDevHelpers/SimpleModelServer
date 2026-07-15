# VS Code Extension Plan — SimpleModelServer Router as a Copilot Model Provider

## Goal

Port the existing Visual Studio extension (`src/Extensions/SimpleModelServer.Extensions.VS/`) to VS Code, with one
deliberate scope change: **no standalone chat window**. Instead of a custom chat UI, the VS Code extension registers
the router (`RouterHttpServer`, default port `11435`) as a **model provider inside GitHub Copilot Chat**, so the
router's models show up in Copilot's model picker and Copilot Chat talks to them directly.

Everything else the VS extension does (connection status, settings/options, commands like Explain/Generate
Tests/Optimize, connection health) should be mirrored.

## What NOT to port (chat-window-specific, VS-only)

From the VS extension, exclude:
- `UI/Chat/ChatToolWindow.cs` / `.xaml` / `.xaml.cs` — WPF chat tool window
- `ViewModels/ChatViewModel.cs` — chat message send/stream/cancel logic (superseded by Copilot Chat itself)
- `UI/Options/OptionsPage.xaml*`, `OptionsPageProvider.cs` — WPF settings page (replace with VS Code `settings.json`
  contributions instead)
- `Commands/CommandHandlers.cs`'s `ShowChatWindowCommand` specifically (other commands like Explain/Generate
  Tests/Optimize can be kept as VS Code commands)

Port/mirror (concept, not code — these are C#, VS Code extensions are TypeScript):
- `Features/StatusBarProvider.cs` → VS Code `StatusBarItem` showing router connection state
- Settings equivalents from `OptionsPage` / `SettingsManager.cs` → VS Code `contributes.configuration` in
  `package.json` (server host/port, API key, default agent type)
- `Commands/CommandHandlers.cs` (minus chat) → VS Code commands in `contributes.commands` + `registerCommand`
- Connection test / health check logic from `ChatService.TestConnectionAsync()` → reused for status bar polling

## Router API contract (source of truth: `src/Server/SimpleModelServer.Server/RouterHttpServer.cs`)

Default base URL: `http://localhost:11435` (note: **not** Ollama's default `11434` — the existing
`SettingsManager.cs` default of `11434` looks like a bug worth flagging/fixing during the port, not copying).

Relevant endpoints for the VS Code provider:
- `GET /health`, `GET /health/ready` — connection/health check for the status bar
- `GET /v1/models` (or `/models`) — OpenAI-style `{data:[{id}]}` model list → feed into the Copilot model picker
- `POST /v1/chat/completions` (or `/chat/completions`) — OpenAI-compatible SSE streaming chat completions.
  **Prefer this endpoint for the Copilot provider integration**, since VS Code's Language Model API expects an
  OpenAI-shaped chat/completions request-response cycle, and the router already speaks it natively (auto-detected
  via `ClientDetector`, no Ollama-NDJSON parsing needed as the old `ChatService.cs` did).
- Auth: `Authorization: Bearer {ApiKey}` header when configured; loopback requests bypass auth.

## Registering as a Copilot model provider

VS Code exposes a Language Model API (`vscode.lm`) that lets an extension register a **chat model provider**
so its models appear inside Copilot Chat's model picker and can serve Copilot Chat requests directly, without a
custom chat UI. The general shape (verify exact API names/shape against the VS Code version being targeted —
this API has moved from proposed to stable across recent releases, so **the implementer must check the installed
`@types/vscode` / `vscode.d.ts` and current `contributes` schema before coding**):

1. `package.json` contributions:
   - `contributes.languageModelChatProviders` (or the current equivalent contribution point) declaring a vendor id,
     e.g. `simplemodelserver`, with a display name.
   - `enabledApiProposals` if the target VS Code version still requires the proposed API.
2. Extension activation (`extension.ts`):
   - Call `vscode.lm.registerLanguageModelChatProvider(vendor, provider)` (or current equivalent) with a provider
     object implementing something like:
     - `provideLanguageModelChatInformation()` → calls router `GET /v1/models`, maps each model id into a
       `LanguageModelChatInformation` entry (id, name, family, context window size if known/configurable).
     - `provideLanguageModelChatResponse(model, messages, options, progress, token)` → translates VS Code's
       `LanguageModelChatMessage[]` into an OpenAI `messages` array, POSTs to
       `{serverUrl}/v1/chat/completions` with `stream: true`, parses the SSE stream, and reports token deltas via
       `progress.report(new LanguageModelTextPart(...))`. Must respect the cancellation `token`.
     - `provideTokenCount(model, text)` → simple heuristic or call a router endpoint if one exists; otherwise
       approximate (chars/4) as a placeholder.
3. Respect the `token` cancellation and map router HTTP/stream errors to thrown `Error`s so Copilot Chat surfaces
   them properly instead of hanging.

## Proposed project layout

```
src/Extensions/SimpleModelServer.Extensions.VSCode/
├── package.json                 # manifest: contributes.commands, configuration, languageModelChatProviders
├── src/
│   ├── extension.ts              # activate()/deactivate(), wires everything below
│   ├── chatModelProvider.ts       # the LanguageModelChatProvider implementation (core of this port)
│   ├── routerClient.ts            # thin HTTP client: health, /v1/models, /v1/chat/completions (SSE parsing)
│   ├── statusBar.ts                # status bar item, polls /health on an interval
│   ├── settings.ts                 # reads/writes contributes.configuration values (host, port, apiKey, agentType)
│   └── commands.ts                 # Explain/Generate Tests/Optimize commands (minus chat-window command)
├── test/
│   └── routerClient.test.ts        # mirrors ChatViewModelTests.cs coverage for the HTTP client logic
├── tsconfig.json
├── .vscodeignore
└── README.md
```

## Implementation steps (in order)

1. Scaffold the extension with `yo code` conventions (or hand-write `package.json` + `tsconfig.json`) targeting the
   engines.vscode version already in use elsewhere in the org, if any; otherwise a recent stable VS Code version.
2. Implement `routerClient.ts`: `checkHealth()`, `listModels()`, `streamChatCompletion(messages, model, signal)`
   using `fetch` + SSE parsing (no external deps needed for basic SSE line parsing).
3. Implement `chatModelProvider.ts` against the current `vscode.lm` provider interface, wired to `routerClient.ts`.
4. Register the provider in `extension.ts activate()`, guarded by a config check (don't register if no server URL
   configured / server unreachable — surface a friendly error via `vscode.window.showErrorMessage` instead).
5. Add `contributes.configuration` mirroring `SettingsManager.cs` fields: server host, port (default **11435**,
   fixing the VS extension's stale 11434 default), API key, default agent type.
6. Add status bar item (`statusBar.ts`) polling `/health` every N seconds, click-to-open-settings.
7. Port non-chat commands (Explain Code, Generate Tests, Optimize Code) as VS Code commands that build a prompt and
   route it through the same `routerClient.ts`, opening results in a new untitled editor or output channel (no
   custom chat UI).
8. Write unit tests for `routerClient.ts` (health, model list parsing, SSE parsing, auth header, cancellation).
9. Manually verify: install the `.vsix` locally, confirm the router's models appear in the Copilot Chat model
   picker, and a chat message round-trips through `/v1/chat/completions`.
10. Write `README.md` documenting setup, matching the style of the VS extension's `README.md`/`QUICK_START.md`.

## Open items for the implementer to verify against current VS Code docs

- Exact current name/shape of the Language Model Chat Provider API (proposed vs. stable) in the target VS Code
  version — this is the single riskiest unknown in this plan.
- Whether the router needs a new endpoint for token counting, or an approximation is acceptable for v1.
- Confirm whether Copilot Chat requires the extension to be published/allow-listed for third-party model providers,
  or whether local/dev registration works unrestricted.
