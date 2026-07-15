# Configuration Management & Admin API

SimpleModelServer provides a comprehensive configuration management system with a JSON REST API for managing server settings. The server has no web UI — every admin interaction goes through this API.

## Quick Start

### API Endpoints

All admin endpoints require authentication:
- **Localhost:** Automatically trusted
- **Remote:** Requires `Authorization: Bearer YOUR_API_KEY` header

#### GET /admin/config
Retrieves the complete configuration.

```bash
# Localhost (no auth needed)
curl http://localhost:11435/admin/config

# Remote (with Bearer token)
curl -H "Authorization: Bearer YOUR_API_KEY" http://server:11435/admin/config
```

Response:
```json
{
  "global": {
    "routerPort": 11435,
    "ollamaPort": 11434,
    "draftModel": "qwen2.5-coder-0.5b",
    "mainModel": "qwen2.5-coder-3b",
    "trustedNetworks": ["192.168.1.0"],
    "trustedApiKeys": [],
    "mdnsEnabled": true,
    ...
  },
  "agents": { ... },
  "modelCount": 5,
  "note": "Localhost is always trusted. Remote requests require 'Authorization: Bearer <key>' header."
}
```

#### POST /admin/config
Updates a single configuration setting.

```bash
# Update router port
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{
    "setting": "global.routerPort",
    "value": 11436
  }'
```

Response:
```json
{
  "success": true,
  "setting": "global.routerPort",
  "requiresRestart": true,
  "message": "Setting 'global.routerPort' updated. Server restart required for change to take effect."
}
```

#### POST /admin/reload
Reloads hot-reloadable configuration settings. Settings requiring restart must be applied via server restart.

```bash
curl -X POST http://localhost:11435/admin/reload
```

Response:
```json
{
  "success": true,
  "message": "Hot-reloadable configuration reloaded.",
  "note": "For settings marked requiresRestart, restart the server to apply changes."
}
```

#### GET /admin/mcp-servers
Lists every configured MCP upstream server and its live connection status.

```bash
curl http://localhost:11435/admin/mcp-servers
```

Response:
```json
{
  "mcpServers": {
    "tokensaver": { "transportType": "Http", "path": "http://localhost:11500/mcp", "connected": true, "toolCount": 4 }
  }
}
```

#### POST /admin/mcp-servers
Adds a new MCP upstream server, or replaces an existing one with the same name. `transportType` defaults to `"Http"` when omitted; `args` is optional and only meaningful for `"Stdio"`. HTTP upstreams are connected immediately — no restart required. `name` cannot contain `.` (the gateway namespaces tools as `<name>.<toolName>`).

```bash
curl -X POST http://localhost:11435/admin/mcp-servers \
  -H "Content-Type: application/json" \
  -d '{"name": "tokensaver", "transportType": "Http", "path": "http://localhost:11500/mcp"}'
```

Response:
```json
{
  "success": true,
  "name": "tokensaver",
  "message": "MCP server 'tokensaver' added."
}
```

#### POST /admin/mcp-servers/remove
Removes a configured MCP upstream server and disconnects it from the live gateway. Returns 404 if no server with that name is configured.

```bash
curl -X POST http://localhost:11435/admin/mcp-servers/remove \
  -H "Content-Type: application/json" \
  -d '{"name": "tokensaver"}'
```

Response:
```json
{
  "success": true,
  "name": "tokensaver",
  "message": "MCP server 'tokensaver' removed."
}
```

## Configuration Settings

### Network & Security

#### `global.routerPort` (number, default: 11435)
- **Type:** Port number (1-65535)
- **Reload:** ⚠️ Requires Restart
- Port the router listens on

#### `global.routerHost` (string, default: "localhost")
- **Type:** String
- **Reload:** ⚠️ Requires Restart
- Host the router binds to. Use:
  - `localhost` or `127.0.0.1` for local only
  - `*` or `+` for all interfaces (requires admin/elevated privileges)

#### `global.trustedNetworks` (array of strings, default: [])
- **Type:** Array of CIDR networks or dotted-quad addresses
- **Reload:** ✓ Hot Reload
- Networks allowed to connect without API key:
  - CIDR format: `192.168.1.0/24`, `10.0.0.0/8`
  - Dotted-quad: `192.168.1.0` (treated as /16), `192.168.1.0` (trailing zeros imply mask)
  - Localhost (127.0.0.1, ::1) is always trusted regardless

Example:
```json
{
  "trustedNetworks": [
    "192.168.1.0/24",
    "10.0.0.0/8"
  ]
}
```

#### `global.trustedApiKeys` (array of strings, default: [])
- **Type:** Array of API key strings
- **Reload:** ✓ Hot Reload
- API keys that grant remote access via `Authorization: Bearer <key>` header
- **Never exposed** in GET /admin/config responses for security

Example:
```json
{
  "trustedApiKeys": [
    "key-production-1",
    "key-developer-2"
  ]
}
```

### Port Configuration

#### `global.ollamaPort` (number, default: 11434)
- **Type:** Port number (1-65535)
- **Reload:** ⚠️ Requires Restart
- Port Ollama server listens on

### Model Configuration

#### `global.draftModel` (string, required)
- **Type:** Model name
- **Reload:** ⚠️ Requires Restart
- Draft (0.5b) model for preview generation

#### `global.mainModel` (string, required)
- **Type:** Model name
- **Reload:** ⚠️ Requires Restart
- Main (3b) model for final response

#### `global.embedModel` (string, default: "nomic-embed-text")
- **Type:** Model name
- **Reload:** ✓ Hot Reload
- Embedding model for semantic search
- Falls back to 'embed' role, then default if not specified

#### `global.routerModelName` (string, default: "HelperModel")
- **Type:** Model name
- **Reload:** ⚠️ Requires Restart
- Virtual model name advertised by the router (to clients, for mDNS, etc.)

#### `global.contextSize` (number, optional)
- **Type:** Non-negative integer
- **Reload:** ✓ Hot Reload
- Default token context window (num_ctx) for model inference
- If not specified, uses model defaults

### Agent & Generation

#### `global.defaultAgentType` (string, default: "code")
- **Type:** String (ask, code, debug, planning)
- **Reload:** ✓ Hot Reload
- Default agent type when not specified in request

#### `global.agentType` (string, default: "")
- **Type:** String (empty to disable, ask, code, debug, planning)
- **Reload:** ✓ Hot Reload
- When set, forces this agent type for all requests (overrides defaultAgentType)

#### `global.draftMode` (string, default: "Preview")
- **Type:** String (Preview, Cleanup, Both)
- **Reload:** ✓ Hot Reload
- How draft model output is used:
  - **Preview:** Draft streams answer preview, spliced into main's prompt
  - **Cleanup:** Draft rewrites prompt, main receives rewritten prompt
  - **Both:** Draft both cleans and previews (higher accuracy, more latency)

#### `global.draftHandoffTimeoutMs` (number, default: 900)
- **Type:** Non-negative integer (milliseconds)
- **Reload:** ✓ Hot Reload
- How long to let draft model stream before handing off to main
- Handoff happens immediately if draft finishes sooner

### mDNS Discovery

#### `global.mdnsEnabled` (boolean, default: true)
- **Type:** Boolean
- **Reload:** ✓ Hot Reload
- Advertise router over mDNS for LAN discovery (_draftmodel._tcp.local)

#### `global.mdnsServiceName` (string, default: "")
- **Type:** String (empty for auto-generated from hostname)
- **Reload:** ✓ Hot Reload
- mDNS instance name (e.g., "SimpleModelServer laptop")
- Defaults to "SimpleModelServer {hostname}" if empty

### Hardware Tuning

#### `global.gpuLayers` (number or "auto", default: unspecified)
- **Type:** Integer or "auto"
- **Reload:** ✓ Hot Reload
- GPU layer count:
  - `"auto"`: GpuBudgetPlanner automatically determines
  - Number: Force explicit layer count
  - Unspecified: Let model defaults decide

#### `global.cpuThreads` (number or "auto", default: unspecified)
- **Type:** Integer or "auto"
- **Reload:** ✓ Hot Reload
- CPU thread count for inference
- Same options as gpuLayers

### Agent Configuration

Agent prompts and settings can be edited at runtime via the admin API. Each agent (code, debug, planning) has:
- `systemPrompt`: The system instruction given to the model
- `temperature`: Model sampling temperature (0–2 range)

#### Editing Agent Settings

Use `agents.<agentName>.<field>` as the setting path:

```bash
# Update the code agent's system prompt
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{
    "setting": "agents.code.systemPrompt",
    "value": "You are a C# coding expert. Provide clear, idiomatic solutions."
  }'
```

Response:
```json
{
  "success": true,
  "setting": "agents.code.systemPrompt",
  "requiresRestart": false,
  "suggestGoldenSet": true,
  "message": "Agent 'code' prompt updated and reloaded. Consider running the golden set (SimpleModelServer.Evals --mode router) to validate the change."
}
```

When a prompt is changed, the response includes `suggestGoldenSet: true`, indicating you should validate the change against the golden set test suite before trusting it in production.

#### Available Agents

- **code** – Specialized in code generation, explanation, and optimization
- **debug** – Focused on troubleshooting and error analysis
- **planning** – Designed for task decomposition and planning

#### Running the Golden Set

After changing an agent prompt, validate it with the golden set:

```bash
# Terminal: run the golden set eval against the modified prompt
SimpleModelServer.Evals --mode router --router-url http://localhost:11435
```

Or via the **VS Extension Settings Panel**:
1. Open Settings → Agent Prompts
2. Select an agent and edit its system prompt
3. Click "Save Prompt"
4. If prompted, click "Yes" to run the golden set
5. Review the results — all tests should pass

#### Example: Optimizing Code Agent

Original prompt (generic):
```
You are a code assistant.
```

Improved prompt (domain-specific):
```
You are an expert C# developer. When suggesting code:
1. Prioritize idiomatic patterns over generic solutions
2. Assume .NET 8+ features are available
3. Reference best practices from Microsoft's .NET docs
4. Provide concrete examples with error handling
```

Update via API and run the golden set to ensure the new prompt maintains routing accuracy without regressing on other agents.

## Authentication

### Localhost (Always Trusted)

Localhost connections (127.0.0.1, ::1) require **no authentication**:

```bash
curl http://localhost:11435/admin/config
```

### Remote Access (Requires API Key)

Remote requests must provide a Bearer token:

```bash
curl -H "Authorization: Bearer YOUR_API_KEY" http://server:11435/admin/config
```

### Adding API Keys

1. **Via Config File (before startup):**
```json
{
  "global": {
    "trustedApiKeys": [
      "prod-key-12345",
      "dev-key-67890"
    ]
  }
}
```

2. **Via Admin API (after startup):**
```bash
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{
    "setting": "global.trustedApiKeys",
    "value": ["prod-key-12345", "dev-key-67890"]
  }'
```

## Restart vs. Hot Reload

### Settings Requiring Restart ⚠️

Some settings require a full server restart to take effect:
- Port changes (routerPort, ollamaPort)
- Host binding (routerHost)
- Model names (draftModel, mainModel)
- Provider configuration

When you update these settings, the API returns `requiresRestart: true`:
```json
{
  "success": true,
  "requiresRestart": true,
  "message": "Setting updated. Server restart required."
}
```

**To apply:**
```bash
# Windows Service
net stop SimpleModelServerLoader
net start SimpleModelServerLoader

# Or restart the process directly
```

### Hot-Reloadable Settings ✓

Other settings can be applied immediately without restart:
- API keys and trusted networks
- Agent/generation settings
- Hardware tuning
- mDNS settings

After updating hot-reloadable settings, you can trigger a reload:
```bash
curl -X POST http://localhost:11435/admin/reload
```

Or just send the next request — hot-reloadable settings take effect immediately.

## Configuration File Format

Settings can also be specified in `SimpleModelServer.config.json`:

```json
{
  "global": {
    "routerPort": 11435,
    "ollamaPort": 11434,
    "routerHost": "localhost",
    "draftModel": "qwen2.5-coder-0.5b",
    "mainModel": "qwen2.5-coder-3b",
    "embedModel": "nomic-embed-text",
    "contextSize": 8192,
    "defaultAgentType": "code",
    "draftMode": "Preview",
    "draftHandoffTimeoutMs": 900,
    "trustedNetworks": [
      "192.168.1.0/24"
    ],
    "trustedApiKeys": [
      "key-production-1"
    ],
    "mdnsEnabled": true,
    "mdnsServiceName": ""
  },
  "agents": {
    "code": {
      "systemPrompt": "You are a coding assistant...",
      "temperature": 0.7
    }
  }
}
```

## Security Best Practices

1. **Use Strong API Keys**
   - Generate cryptographically random keys
   - At least 32 characters recommended
   - Treat like passwords

2. **Limit Trusted Networks**
   - Only add networks that need access
   - Use specific CIDR ranges, not 0.0.0.0/0
   - Localhost is already trusted by default

3. **Rotate API Keys**
   - Update keys via the admin API
   - Remove old keys when no longer needed

4. **Enable mDNS Carefully**
   - mDNS broadcasts your service on the LAN
   - Set to false on security-sensitive networks
   - Consider disabling on open networks

5. **Monitor Admin Access**
   - Check server logs for admin endpoint access
   - Look for unauthorized authentication attempts
   - Review configuration changes in logs

## Troubleshooting

### Remote Connection Refused
- Check that the server's firewall allows the router port
- Verify the host binding: use `*` to bind all interfaces
- Check if port 11435 is already in use

### API Key Not Working
- Ensure you're using the exact key (case-sensitive)
- Verify the key is in trustedApiKeys array
- Check that the Authorization header format is correct: `Bearer KEY`

### Changes Not Taking Effect
- Check if the setting requires restart (`requiresRestart: true`)
- For hot-reloadable settings, call `/admin/reload` or wait for next request
- Check server logs for error messages

## Examples

### Update Router Port (requires restart)
```bash
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.routerPort", "value": 11436}'

# Result: requiresRestart: true
# Action: Restart the server
```

### Add Trusted Network (hot-reload)
```bash
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{
    "setting": "global.trustedNetworks",
    "value": ["192.168.1.0/24", "10.0.0.0/8"]
  }'

# Result: requiresRestart: false
# The network is immediately trusted
```

### Change Agent Type (hot-reload)
```bash
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.agentType", "value": "debug"}'

# Result: requiresRestart: false
# Next request uses debug agent
```

### Remote Access with API Key
```bash
# 1. Add API key first (from localhost)
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{
    "setting": "global.trustedApiKeys",
    "value": ["remote-key-xyz"]
  }'

# 2. Use from remote machine
curl -H "Authorization: Bearer remote-key-xyz" \
  http://server.local:11435/admin/config
```
