# Admin Configuration - Quick Start

**⚡ TL;DR:** Visit `http://localhost:11435/admin/` to manage your server configuration.

## One-Minute Setup

1. **Start the server** (if not already running)
2. **Open browser:** `http://localhost:11435/admin/`
3. **You're in!** Localhost is always trusted, no authentication needed.

## Common Tasks

### Change Draft/Main Model

```
Web UI: Admin Panel → Model Configuration → Update Draft/Main Model
Requires: Server Restart
API:
  curl -X POST http://localhost:11435/admin/config \
    -H "Content-Type: application/json" \
    -d '{"setting": "global.draftModel", "value": "deepseek-coder-1.3b"}'
```

### Enable Remote Access with API Key

```
1. Get an API key (e.g., from your password manager or generate one)
2. Add it to trusted keys:
   - Via Web UI: Go to Network & Security → Update API Keys
   - Or via API:
     curl -X POST http://localhost:11435/admin/config \
       -H "Content-Type: application/json" \
       -d '{"setting": "global.trustedApiKeys", "value": ["your-key-here"]}'
3. Use from remote:
   curl -H "Authorization: Bearer your-key-here" \
     http://server:11435/admin/config
```

### Allow a Specific Network

```
Web UI: Admin Panel → Network & Security → Update Trusted Networks
Requires: Hot Reload (no restart needed!)

Example trusted networks:
- 192.168.1.0/24 (entire 192.168.1.x subnet)
- 10.0.0.0/8 (entire 10.x.x.x network)
- 172.16.0.0/12 (common private range)
```

### Change Router Port

```
⚠️ Requires: Server Restart

Web UI:
  1. Admin Panel → Port Configuration → Router Port
  2. Change value
  3. Click "Update Router Port"
  4. Restart server (Windows Service, systemd, or process)

Or via API:
  curl -X POST http://localhost:11435/admin/config \
    -H "Content-Type: application/json" \
    -d '{"setting": "global.routerPort", "value": 11436}'
  # Server will report requiresRestart: true
  # Then restart the service
```

### Adjust Context Size (No Restart)

```
Web UI: Admin Panel → Model Configuration → Context Size
Requires: No restart! Takes effect immediately.
API:
  curl -X POST http://localhost:11435/admin/config \
    -H "Content-Type: application/json" \
    -d '{"setting": "global.contextSize", "value": 4096}'
```

### Switch Agent Type Globally

```
Web UI: Admin Panel → Agent & Generation → Force Agent Type
Requires: No restart!
API:
  curl -X POST http://localhost:11435/admin/config \
    -H "Content-Type: application/json" \
    -d '{"setting": "global.agentType", "value": "debug"}'

Options: ask, code, debug, planning (or empty to disable forcing)
```

## What's Hot-Reloadable?

These settings take effect immediately without restarting:
- ✓ API keys and trusted networks
- ✓ Agent/model selection settings
- ✓ Draft mode and handoff timing
- ✓ Embedding model
- ✓ Hardware tuning (GPU layers, CPU threads)
- ✓ mDNS settings

## What Requires Restart? ⚠️

These require a server restart:
- ⚠️ Router port (where it listens)
- ⚠️ Ollama port (local model server)
- ⚠️ Router host binding (localhost vs all interfaces)
- ⚠️ Model names (draft/main model selection)
- ⚠️ Virtual model name

## Web UI Overview

**Section Breakdown:**

| Section | Settings | Reload Type |
|---------|----------|------------|
| 🔐 Network & Security | Host, networks, API keys | Mixed |
| 🔌 Port Configuration | Router & Ollama ports | ⚠️ Restart |
| 🤖 Model Configuration | Draft, main, embed models, context | Mixed |
| 🎯 Agent & Generation | Agent type, draft mode, handoff | ✓ Hot |
| 📡 mDNS Discovery | mDNS enable, service name | ✓ Hot |
| 💻 Hardware Tuning | GPU layers, CPU threads | ✓ Hot |
| ⚡ Actions | Reload, refresh, logout | — |

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
→ Localhost? Check your browser. Remote? Include Bearer token in Authorization header

### Setting Not Taking Effect
→ Check if it requires restart (⚠️ indicator). If so, restart the server.

### Admin UI Won't Load
→ Try accessing `/admin/config` API first to verify server is running. Check network connectivity.

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
