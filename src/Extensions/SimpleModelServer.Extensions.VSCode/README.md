# SimpleModelServer VS Code Extension

A VS Code extension that registers SimpleModelServer as a model provider for GitHub Copilot Chat.

Instead of a custom chat window, this extension integrates directly with Copilot Chat's model picker, allowing you to use SimpleModelServer's models alongside other Copilot providers. The router's models appear in Copilot's model picker and receive chat requests directly.

## Features

- **Copilot Chat Integration** — SimpleModelServer models appear in Copilot Chat's model picker
- **OpenAI-Compatible Streaming** — Automatic SSE parsing and streaming to Copilot Chat
- **Status Monitoring** — Real-time connection status indicator in the status bar
- **Settings Management** — Configure server host, port, API key, and agent type
- **Code Enhancement Commands** — Right-click commands for:
  - Explain Code
  - Generate Tests
  - Optimize Code
- **Connection Health Check** — Automatic periodic health checks with visual feedback

## Installation

### From VSIX

1. Download the `.vsix` file
2. Open VS Code
3. Go to **Extensions** (Ctrl+Shift+X / Cmd+Shift+X)
4. Click **Install from VSIX...**
5. Select the `.vsix` file

### From Source

```bash
cd src/Extensions/SimpleModelServer.Extensions.VSCode
npm install
npm run esbuild
# Then package or side-load
```

## Quick Start

### 1. Configure Connection

1. Open **Settings** (Ctrl+, / Cmd+,)
2. Search for "simplemodelserver"
3. Configure:
   - **Host**: `localhost` (or your server address)
   - **Port**: `11435` (default SimpleModelServer router port)
   - **API Key**: Leave empty unless your server requires auth
   - **Default Agent**: `auto` (or specify `claude`/`research`)
   - **Health Check Interval**: `30` seconds

### 2. Test Connection

1. Open the Command Palette (Ctrl+Shift+P / Cmd+Shift+P)
2. Search for "Test SimpleModelServer Connection"
3. Verify the connection succeeds

### 3. Use with Copilot Chat

1. Open Copilot Chat (Ctrl+Shift+L / Cmd+Shift+L by default)
2. Click the model picker at the top
3. Select "SimpleModelServer" vendor
4. Choose a model from the list
5. Start chatting!

### 4. Code Enhancement Commands

With code selected in the editor:

1. **Explain** — Right-click → "Explain with SimpleModelServer"
2. **Generate Tests** — Right-click → "Generate Tests with SimpleModelServer"
3. **Optimize** — Right-click → "Optimize Code with SimpleModelServer"

Results appear in the Output panel.

## Architecture

```
VS Code Extension
    ↓
Language Model Chat Provider (vscode.lm)
    ↓
RouterClient (HTTP client)
    ↓
SimpleModelServer Router (/v1/chat/completions, /v1/models)
    ↓
Ollama + Models
```

### Components

- **extension.ts** — Activation/deactivation, provider registration
- **chatModelProvider.ts** — Language Model Chat Provider implementation
- **routerClient.ts** — HTTP client for router endpoints (health, models, chat)
- **statusBar.ts** — Status bar indicator with health monitoring
- **settings.ts** — Configuration management
- **commands.ts** — Explain, Generate Tests, Optimize commands

## Configuration

All settings are under the `simplemodelserver` namespace:

| Setting | Type | Default | Description |
| --- | --- | --- | --- |
| `host` | string | `localhost` | SimpleModelServer host |
| `port` | number | `11435` | SimpleModelServer router port |
| `apiKey` | string | `` | API key (leave empty for localhost) |
| `defaultAgent` | string | `auto` | Default agent type (auto/claude/research) |
| `healthCheckInterval` | number | `30` | Health check interval in seconds |

## Router Endpoints Used

The extension uses these SimpleModelServer endpoints:

- `GET /health` — Connection health check
- `GET /v1/models` — List available models (OpenAI format)
- `POST /v1/chat/completions` — Stream chat completions (OpenAI-compatible)

## Troubleshooting

### Models not appearing in Copilot Chat

1. Verify SimpleModelServer is running: `curl http://localhost:11435/health`
2. Check settings are correct (host/port)
3. Open the Output panel and search for "SimpleModelServer" to see diagnostic logs
4. Run "Test SimpleModelServer Connection" command

### Chat requests failing

1. Verify router is responding to `/v1/models` and `/v1/chat/completions`
2. Check router logs for errors
3. Ensure Copilot Chat is installed and authenticated
4. Try a simpler prompt to isolate issues

### Status bar shows disconnected

1. Check SimpleModelServer router is running
2. Verify host/port in settings
3. Firewall or network connectivity issues
4. Check SimpleModelServer logs

### API key issues

1. Ensure the key is correctly configured in settings
2. For localhost, API keys are typically not needed
3. Remote servers may require `Authorization: Bearer <key>` header

## Development

### Build

```bash
npm install
npm run typecheck
npm run esbuild
```

### Run in development

1. Open the extension folder in VS Code
2. Press F5 to start debug session
3. A new VS Code window opens with the extension loaded

### Test

```bash
# Run against a live SimpleModelServer (requires SMS_TEST_HOST/SMS_TEST_PORT env vars)
SMS_TEST_HOST=localhost SMS_TEST_PORT=11435 npm run test:unit
```

## Limitations

- Token counting uses the cl100k_base tokenizer as an approximation; the router can serve arbitrary local models with different real vocabularies, so exact counts would require a router-side endpoint
- Commands (Explain, Generate Tests, Optimize) don't maintain multi-turn context by default
- Copilot Chat integration requires VS Code 1.104+ (when the Language Model Chat Provider API became stable)
- Loopback connections bypass API key auth (see RouterHttpServer.cs)

## Security

- API keys are stored in VS Code's workspace/user settings (`settings.json`), not `SecretStorage` — avoid committing workspace settings if you set a key there
- All requests go only to your configured SimpleModelServer instance
- No data is sent to external services
- Uses standard HTTP Bearer token auth when configured

## License

Part of SimpleModelServer. See LICENSE.txt for details.

## Support

- Issues: [GitHub Issues](https://github.com/rsperry79/SimpleModelServer/issues)
- Server Setup: See SimpleModelServer README.md
- VS Code Docs: [Language Model API](https://code.visualstudio.com/api/extension-guides/language-model)

## Roadmap

- [ ] Token counting endpoint support
- [ ] Multi-turn context persistence for commands
- [ ] Configuration validation and error messages
- [ ] Model parameter tuning (temperature, top_p, max_tokens)
- [ ] Chat history export
- [ ] Streaming progress improvements
