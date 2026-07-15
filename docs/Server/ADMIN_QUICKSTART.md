# Admin Configuration - Quick Start

**⚡ TL;DR:** `curl http://localhost:11435/admin/config` to read config, `POST` the same path to change a setting. Localhost is always trusted, no authentication needed. The server is JSON-only — there is no web UI.

## Common Tasks

### Change Draft/Main Model

```
Requires: Server Restart
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.draftModel", "value": "deepseek-coder-1.3b"}'
```

### Enable Remote Access with API Key

```
1. Get an API key (e.g., from your password manager or generate one)
2. Add it to trusted keys:
   curl -X POST http://localhost:11435/admin/config \
     -H "Content-Type: application/json" \
     -d '{"setting": "global.trustedApiKeys", "value": ["your-key-here"]}'
3. Use from remote:
   curl -H "Authorization: Bearer your-key-here" \
     http://server:11435/admin/config
```

### Allow a Specific Network

```
Requires: Hot Reload (no restart needed!)
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.trustedNetworks", "value": ["192.168.1.0/24"]}'

Example trusted networks:
- 192.168.1.0/24 (entire 192.168.1.x subnet)
- 10.0.0.0/8 (entire 10.x.x.x network)
- 172.16.0.0/12 (common private range)
```

### Change Router Port

```
⚠️ Requires: Server Restart
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.routerPort", "value": 11436}'
# Server will report requiresRestart: true
# Then restart the service
```

### Adjust Context Size (No Restart)

```
Requires: No restart! Takes effect immediately.
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.contextSize", "value": 4096}'
```

### Switch Agent Type Globally

```
Requires: No restart!
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.agentType", "value": "debug"}'

Options: ask, code, debug, planning (or empty to disable forcing)
```

### Add/Remove an MCP Upstream Server

```
Requires: No restart! HTTP upstreams connect immediately.
curl -X POST http://localhost:11435/admin/mcp-servers \
  -H "Content-Type: application/json" \
  -d '{"name": "tokensaver", "transportType": "Http", "path": "http://localhost:11500/mcp"}'

curl -X POST http://localhost:11435/admin/mcp-servers/remove \
  -H "Content-Type: application/json" \
  -d '{"name": "tokensaver"}'
```

## What's Hot-Reloadable?

These settings take effect immediately without restarting:
- ✓ API keys and trusted networks
- ✓ Agent/model selection settings
- ✓ Draft mode and handoff timing
- ✓ Embedding model
- ✓ Hardware tuning (GPU layers, CPU threads)
- ✓ mDNS settings
- ✓ MCP upstream servers (HTTP transport)

## What Requires Restart? ⚠️

These require a server restart:
- ⚠️ Router port (where it listens)
- ⚠️ Ollama port (local model server)
- ⚠️ Router host binding (localhost vs all interfaces)
- ⚠️ Model names (draft/main model selection)
- ⚠️ Virtual model name

## API Cheat Sheet

```bash
# Get full config
curl http://localhost:11435/admin/config

# Update a setting
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.SETTING_NAME", "value": NEW_VALUE}'

# Reload hot-reloadable settings
curl -X POST http://localhost:11435/admin/reload

# List MCP upstream servers
curl http://localhost:11435/admin/mcp-servers

# Remote access with API key
curl -H "Authorization: Bearer YOUR_KEY" \
  http://server:11435/admin/config

# Set multiple values (one call per setting)
for key in key1 key2 key3; do
  curl -X POST http://localhost:11435/admin/config \
    -H "Content-Type: application/json" \
    -d "{\"setting\": \"global.trustedApiKeys\", \"value\": [\"$key\"]}"
done
```

## Troubleshooting

### "Connection Refused" from Remote
→ Check router host is `*` (not just `localhost`) or server firewall allows the port

### "Unauthorized" Error
→ Localhost? Verify your request actually originates from 127.0.0.1/::1. Remote? Include Bearer token in Authorization header

### Setting Not Taking Effect
→ Check if it requires restart (⚠️ indicator). If so, restart the server.

### Can't Add API Key
→ Make sure you're using the correct format: `{"setting": "global.trustedApiKeys", "value": ["key1", "key2"]}`

## Security Reminders

⚠️ **Always use strong API keys** for remote access
- At least 32 characters
- Use a password generator
- Treat like passwords

🔒 **Keep API keys private**
- Don't commit to git
- Don't share in logs
- Rotate regularly

🌐 **Be careful with network trust**
- Only add networks that need access
- Use specific CIDR ranges
- mDNS broadcasts availability on your LAN

## More Information

For detailed documentation, see:
- `docs/ADMIN_CONFIG.md` — Complete API reference and settings guide
- `ADMIN_FEATURES.md` — Implementation details and design decisions

## Still Need Help?

Check the server logs for error messages:
```bash
# Windows Service
eventvwr  # Look for "SimpleModelServer" source

# Console/Direct Run
# Error messages appear in the terminal
```

Common issues are logged with suggestions for fixing them.
