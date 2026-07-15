# SimpleModelServer Admin Configuration System

Complete guide to the admin configuration API and configuration management. The server is JSON-only — there is no admin web UI.

## 📚 Documentation Files

Start here based on your needs:

### For Users
- **[ADMIN_QUICKSTART.md](ADMIN_QUICKSTART.md)** — Fast practical guide to common tasks
- **[ADMIN_CONFIG.md](ADMIN_CONFIG.md)** — Complete API reference and settings documentation

### For Developers
- **../ADMIN_FEATURES.md** — Implementation details and architecture
- **../ADMIN_CONFIG.md** — Original user guide

## 🚀 Quick Start

### API
```bash
# Get config
curl http://localhost:11435/admin/config

# Update a setting
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.agentType", "value": "debug"}'

# Reload hot-reloadable settings
curl -X POST http://localhost:11435/admin/reload

# List configured MCP upstream servers
curl http://localhost:11435/admin/mcp-servers

# Add/update an MCP upstream server
curl -X POST http://localhost:11435/admin/mcp-servers \
  -H "Content-Type: application/json" \
  -d '{"name": "tokensaver", "transportType": "Http", "path": "http://localhost:11500/mcp"}'

# Remove an MCP upstream server
curl -X POST http://localhost:11435/admin/mcp-servers/remove \
  -H "Content-Type: application/json" \
  -d '{"name": "tokensaver"}'
```

## 🎯 Key Features

✅ **Localhost Always Trusted** — No authentication needed from 127.0.0.1  
✅ **Bearer Token Auth** — Secure remote access with API keys  
✅ **Smart Reload** — Only restart when necessary  
✅ **Type Validation** — All inputs validated before applying  
✅ **20+ Settings** — Configure every aspect of the server  
✅ **REST API** — Programmatic configuration management, JSON only (no HTML UI)  

## 📋 Settings Overview

### Network & Security
- Router host and port
- Trusted networks (CIDR notation)
- API keys for authentication

### Models
- Draft and main models
- Embedding model
- Context size
- Virtual router model name

### Agent & Generation
- Default and forced agent type
- Draft mode (Preview, Cleanup, Both)
- Handoff timeout

### mDNS & Discovery
- Enable/disable mDNS advertising
- Custom service name

### Hardware
- GPU layer allocation
- CPU thread count

## 🔄 Hot Reload vs. Restart

**Hot-Reloadable** (take effect immediately):
- Trusted networks and API keys
- Agent settings
- Draft mode and timing
- Embedding model
- Hardware tuning
- mDNS settings

**Requires Restart**:
- Port numbers
- Router host binding
- Model names
- Infrastructure changes

When you update a restart-required setting, the API returns `requiresRestart: true`. Restart the server to apply.

## 🛡️ Security

### Authentication
- **Localhost**: Always trusted (127.0.0.1, ::1)
- **Remote**: Must provide Bearer token in Authorization header

### Best Practices
1. Use strong, randomly-generated API keys
2. Keep keys private (like passwords)
3. Rotate keys regularly
4. Use CIDR notation to limit trusted networks
5. Monitor logs for unauthorized access attempts

### Secret Handling
- API keys are never returned in GET responses
- Keys are only stored in-memory (not logged)
- Configuration dumps never include actual key values

## 💻 Integration

The admin system integrates seamlessly with:
- Existing configuration files (SimpleModelServer.config.json)
- Router HTTP server (RouterHttpServer)
- Model and agent management
- Event logging

Settings can be configured via:
1. JSON config file (before startup)
2. REST API (after startup, in-memory only)

Note: API changes are not persisted to disk. To make them permanent, update the config file.

## 🧪 Testing

### Manual Testing Checklist
- [ ] Localhost needs no authentication
- [ ] Can retrieve config via GET /admin/config
- [ ] Can update hot-reloadable setting
- [ ] Hot-reloadable setting takes effect immediately
- [ ] Can update restart-required setting
- [ ] API returns requiresRestart: true for restart-required settings
- [ ] Remote access requires API key
- [ ] API key validation works
- [ ] Invalid settings are rejected with validation errors

### Example Tests
```bash
# Test 1: Get config
curl http://localhost:11435/admin/config | jq .

# Test 2: Update hot-reloadable setting
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.mdnsEnabled", "value": false}'
# Should return requiresRestart: false

# Test 3: Try invalid value
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"setting": "global.routerPort", "value": 999999}'
# Should return validation error

# Test 4: Remote access without key
curl http://different-machine:11435/admin/config
# Should return 401 Unauthorized

# Test 5: Remote access with key
curl -H "Authorization: Bearer your-key" \
  http://different-machine:11435/admin/config
# Should return config if key is valid
```

## 📡 Endpoints

### GET /admin/config
Returns current configuration as JSON

### POST /admin/config
Updates a single configuration setting
- Input: `{"setting": "path", "value": newValue}`
- Output: `{"success": bool, "requiresRestart": bool, "message": string}`

### POST /admin/reload
Reloads hot-reloadable settings

### GET /admin/mcp-servers
Lists configured MCP upstream servers and their live connection status
- Output: `{"mcpServers": {"<name>": {"transportType": string, "path": string, "connected": bool, "toolCount": number}}}`

### POST /admin/mcp-servers
Adds a new MCP upstream server, or replaces an existing one with the same name
- Input: `{"name": string, "transportType": "Http"|"Stdio", "path": string, "args": string[]}` (`transportType` defaults to "Http", `args` is optional)
- Output: `{"success": bool, "name": string, "message": string}`
- HTTP upstreams are connected immediately — no restart required.

### POST /admin/mcp-servers/remove
Removes a configured MCP upstream server and disconnects it from the live gateway
- Input: `{"name": string}`
- Output: `{"success": bool, "name": string, "message": string}` (404 if the name isn't configured)

### The same three operations as MCP tools, at /mcp

The gateway itself (mapped at `/mcp`) also exposes `list_mcp_servers`, `add_mcp_server`, and
`remove_mcp_server` as MCP tools — the `SimpleModelServer.Mcp` project's `McpAdminTools`, always
registered alongside the proxied upstream tools (no config needed, no restart to pick them up).
Same behavior and JSON response shape as the HTTP endpoints above, just reachable by any MCP
client connected to `/mcp` instead of a plain HTTP call to `/admin/mcp-servers`.

## 🔧 Configuration Examples

### Enable Remote Access
```json
{
  "global": {
    "trustedApiKeys": ["prod-key-xyz", "dev-key-abc"]
  }
}
```

### Restrict to Local Network Only
```json
{
  "global": {
    "trustedNetworks": ["192.168.1.0/24"],
    "routerHost": "0.0.0.0"
  }
}
```

### Override Agent Type Globally
```json
{
  "global": {
    "agentType": "debug"
  }
}
```

### Tune for Low-Memory System
```json
{
  "global": {
    "draftHandoffTimeoutMs": 500,
    "gpuLayers": 20,
    "contextSize": 2048
  }
}
```

## 📊 Configuration Structure

```json
{
  "global": {
    "routerPort": 11435,
    "routerHost": "localhost",
    "draftModel": "qwen2.5-coder-0.5b",
    "mainModel": "qwen2.5-coder-3b",
    "embedModel": "nomic-embed-text",
    "contextSize": 8192,
    "defaultAgentType": "code",
    "agentType": "",
    "draftMode": "Preview",
    "draftHandoffTimeoutMs": 900,
    "trustedNetworks": [],
    "trustedApiKeys": [],
    "mdnsEnabled": true,
    "mdnsServiceName": "",
    "gpuLayers": "auto",
    "cpuThreads": "auto"
  },
  "agents": {
    "code": {
      "systemPrompt": "You are a code assistant...",
      "temperature": 0.7
    }
  },
  "models": {
    "draft": { "name": "...", "base": "..." },
    "main": { "name": "...", "base": "..." }
  }
}
```

## 🎯 Use Cases

### Use Case 1: Development Setup
- Set `agentType: "debug"` for detailed logging
- Lower `draftHandoffTimeoutMs` for faster iteration
- Use high context size for long files

### Use Case 2: Production Server
- Set `trustedApiKeys` for remote access
- Restrict to `trustedNetworks`
- Enable mDNS for automatic discovery
- Monitor admin access logs

### Use Case 3: Multi-GPU Setup
- Configure `gpuLayers` and `cpuThreads` for optimal performance
- Adjust `contextSize` based on available VRAM
- Use `draftMode: "Both"` for better accuracy

### Use Case 4: Testing
- Switch `agentType` to "test" (if configured)
- Temporarily lower context size
- Enable debug logging via config

## ❓ FAQ

**Q: Do I need to restart the server when updating a setting?**  
A: Only for restart-required settings. The API tells you via `requiresRestart: true/false`.

**Q: How do I make configuration changes permanent?**  
A: Edit the JSON config file. API changes are in-memory only.

**Q: Can I configure multiple API keys?**  
A: Yes, pass an array: `"value": ["key1", "key2", "key3"]`

**Q: What happens if I provide an invalid setting value?**  
A: The API returns a validation error and doesn't apply the change.

**Q: Is localhost always trusted?**  
A: Yes, 127.0.0.1 and ::1 bypass authentication entirely.

**Q: Can I access the admin API from a mobile phone?**  
A: Yes if you add its network to `trustedNetworks` or provide an API key — it's a plain JSON API, so any HTTP client works.

**Q: How do I rotate API keys?**  
A: Add new keys to `trustedApiKeys`, then remove old ones via the same API.

## 📝 Logging

Admin operations are logged for auditing:
```
[ADMIN] [GET] /admin/config from 192.168.1.100
[ADMIN] Configuration updated: global.agentType = debug (requiresRestart=false)
[ADMIN] [AUTH FAILED] 203.0.113.45 - invalid or missing API key
```

Check server logs to monitor configuration changes and access attempts.

## 🚀 Next Steps

1. Read [ADMIN_QUICKSTART.md](ADMIN_QUICKSTART.md) for common tasks
2. Explore [ADMIN_CONFIG.md](ADMIN_CONFIG.md) for complete reference
3. Set up API keys for remote access if needed
4. Monitor logs for security and debugging

## 📞 Support

For detailed configuration options, see the full documentation:
- Settings reference: [ADMIN_CONFIG.md](ADMIN_CONFIG.md)
- Quick reference: [ADMIN_QUICKSTART.md](ADMIN_QUICKSTART.md)
- Implementation notes: ../ADMIN_FEATURES.md
