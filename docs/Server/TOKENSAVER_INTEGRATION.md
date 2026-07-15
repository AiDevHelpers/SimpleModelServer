# TokenSaver Integration with SimpleModelServer

This guide explains how to configure TokenSaver.MCP to use SimpleModelServer.Loader as its backend, enabling shared model inference across multiple machines and tools.

## Architecture Overview

```
┌─────────────────────────────────────────┐
│  Editor Integration Tools                │
│  (VS Code, Continue, IDE Extensions)    │
└────────────────┬────────────────────────┘
                 │ MCP (stdio)
┌────────────────▼────────────────────────┐
│  TokenSaver.MCP (local)                  │
│  (context window management,             │
│   token accounting)                      │
└────────────────┬────────────────────────┘
                 │ HTTP
┌────────────────▼────────────────────────┐
│  SimpleModelServer.Loader (shared)       │
│  (model routing, draft/refine,           │
│   multi-agent, hardware mgmt)            │
└────────────────┬────────────────────────┘
                 │
┌────────────────▼────────────────────────┐
│  Ollama or LM Studio (local or remote)   │
│  (actual model inference)                │
└─────────────────────────────────────────┘
```

**Benefits of this architecture:**
- **Shared inference** — Multiple machines can run TokenSaver locally and share a single SimpleModelServer instance
- **Advanced routing** — Benefit from SimpleModelServer's multi-agent support, draft-and-refine, and hardware optimization
- **Isolation** — Each tool gets its own TokenSaver instance; configuration is per-machine, not global
- **Fallback** — Local SimpleModelServer can continue running as fallback if remote becomes unreachable

## Quick Setup

### Prerequisites

1. **SimpleModelServer.Loader running** — On a machine accessible from this one (port 11435 by default, or custom port)
2. **TokenSaver.MCP installed** — On the local machine that will use it
3. **Network connectivity** — Can reach `<router_host>:<router_port>` from this machine

### Step 1: Verify Connectivity

```powershell
# Replace with your SimpleModelServer's actual host/port
$routerHost = "192.168.1.50"  # or "shared-server-name"
$routerPort = 11435

# Test connectivity
$response = Invoke-WebRequest -Uri "http://$routerHost:$routerPort/health" -ErrorAction SilentlyContinue
if ($response.StatusCode -eq 200) {
    Write-Host "✅ Router is reachable"
} else {
    Write-Host "❌ Router is not reachable. Check host/port and firewall rules."
    exit 1
}
```

### Step 2: Configure TokenSaver.MCP

Locate TokenSaver.MCP's configuration file. This follows `TokenSaverConfig` resolution:

**Default locations (in order):**
1. `appsettings.json` in the same directory as `TokenSaver.MCP.exe`
2. Environment variable `TOKENSAVER_CONFIG` (if set)
3. Auto-discovery via `SimpleModelServerServiceInstalled` registry key (Windows)

**Edit the config file** (create if doesn't exist):

```json
{
  "TokenSaver": {
    "OllamaUrl": "http://<ROUTER_HOST>:<ROUTER_PORT>",
    "OllamaModel": "HelperModel"
  }
}
```

**Example:**
```json
{
  "TokenSaver": {
    "OllamaUrl": "http://192.168.1.50:11435",
    "OllamaModel": "HelperModel"
  }
}
```

**Notes:**
- `OllamaUrl` should match SimpleModelServer's `global.routerHost` and `global.routerPort`
- `OllamaModel` should match SimpleModelServer's `global.routerModelName` (default: `"HelperModel"`)
- Ports: SimpleModelServer router is 11435 (not 11434, which is Ollama)

### Step 3: Restart TokenSaver.MCP

**In Continue (VS Code extension):**
```
Ctrl+Shift+P → "Continue: Reconnect MCP Servers"
or restart VS Code
```

**In Claude Code:**
- Restart the IDE or reload the window

**Via PowerShell (if running as a service):**
```powershell
Restart-Service TokenSaver.MCP -Force
```

### Step 4: Verify Configuration

Check that TokenSaver is actually using the new router:

**Option A: Using ContextDiag (if available)**
```powershell
& "C:\Program Files\TokenSaver\ContextDiag.exe"
# Look for "OllamaUrl: http://..." in the output
```

**Option B: Send a test request**
```powershell
# Use any TokenSaver-integrated tool (VS Code, Continue, etc.) 
# and send a message through the normal chat interface.
# If it works, TokenSaver is configured correctly.
```

**Option C: Check TokenSaver logs**
```powershell
# TokenSaver logs are usually in:
# %TEMP%\TokenSaver\logs\ or the same dir as TokenSaver.MCP.exe
Get-Content "C:\Program Files\TokenSaver\logs\latest.log" -Tail 50
```

## Network Configuration

### For Localhost (Same Machine)

```json
{
  "TokenSaver": {
    "OllamaUrl": "http://localhost:11435",
    "OllamaModel": "HelperModel"
  }
}
```

### For LAN (Different Machines on Same Network)

1. **On SimpleModelServer machine**, ensure router is listening on all interfaces:
   ```json
   {
     "global": {
       "routerHost": "*",
       "routerPort": 11435
     }
   }
   ```

2. **Allow TokenSaver's machine through firewall** (if `trustedNetworks` is restricted):
   ```json
   {
     "global": {
       "trustedNetworks": ["192.168.1.0/24"]
     }
   }
   ```

3. **On TokenSaver machine**, use SimpleModelServer's IP:
   ```json
   {
     "TokenSaver": {
       "OllamaUrl": "http://192.168.1.50:11435",
       "OllamaModel": "HelperModel"
     }
   }
   ```

### For Remote Access (VPN/SSH Tunnel)

If SimpleModelServer is on a remote network:

1. **Set up SSH tunnel** from TokenSaver machine:
   ```powershell
   ssh -L 11435:localhost:11435 user@remote-server
   ```

2. **Configure TokenSaver to use localhost tunnel**:
   ```json
   {
     "TokenSaver": {
       "OllamaUrl": "http://localhost:11435",
       "OllamaModel": "HelperModel"
     }
   }
   ```

## Troubleshooting

### "Connection refused" or "Timeout"

**Cause:** TokenSaver can't reach SimpleModelServer.

**Checklist:**
1. Is SimpleModelServer running? Check: `curl http://<host>:<port>/health`
2. Is the host/port correct in TokenSaver config?
3. Is there a firewall blocking port 11435?
4. Is SimpleModelServer's `routerHost` set to something other than `localhost`?

**Fix:**
```powershell
# Verify router is reachable
Invoke-WebRequest -Uri "http://192.168.1.50:11435/health" -Verbose

# If it fails, update SimpleModelServer config to listen on all interfaces
# and restart SimpleModelServer.Loader service
```

### "OllamaModel not found" or 404 errors

**Cause:** The model name doesn't match.

**Fix:**
1. Check SimpleModelServer's `global.routerModelName`:
   ```powershell
   curl http://192.168.1.50:11435/admin/config
   # Look for "routerModelName" field
   ```

2. Update TokenSaver config to match:
   ```json
   {
     "TokenSaver": {
       "OllamaUrl": "http://192.168.1.50:11435",
       "OllamaModel": "HelperModel"
     }
   }
   ```

3. Restart TokenSaver.MCP

### TokenSaver still using local Ollama

**Cause:** Config changes didn't take effect.

**Fix:**
1. Verify TokenSaver found the config file:
   ```powershell
   Write-Host "Looking for config at:"
   Get-ChildItem "C:\Program Files\TokenSaver\appsettings.json" -ErrorAction SilentlyContinue
   ```

2. Check JSON syntax:
   ```powershell
   $json = Get-Content "C:\Program Files\TokenSaver\appsettings.json" | ConvertFrom-Json
   Write-Host "OllamaUrl: $($json.TokenSaver.OllamaUrl)"
   ```

3. Ensure file is saved after editing
4. Restart TokenSaver.MCP completely (not just reconnect in the IDE)

## PowerShell Setup Helper

Save this as `Setup-TokenSaver-RemoteRouter.ps1`:

```powershell
param(
    [string]$RouterHost = "localhost",
    [int]$RouterPort = 11435,
    [string]$RouterModel = "HelperModel",
    [string]$TokenSaverPath = "C:\Program Files\TokenSaver"
)

Write-Host "🔧 Configuring TokenSaver.MCP to use SimpleModelServer router..."

# Test connectivity
Write-Host "`n1️⃣  Testing connectivity to $RouterHost:$RouterPort..."
try {
    $response = Invoke-WebRequest -Uri "http://$RouterHost:$RouterPort/health" `
        -ErrorAction Stop -TimeoutSec 5
    Write-Host "✅ Router is reachable"
} catch {
    Write-Host "❌ Cannot reach router: $_"
    Write-Host "   Fix: Ensure SimpleModelServer is running and reachable"
    exit 1
}

# Find config file
Write-Host "`n2️⃣  Locating TokenSaver config..."
$configPaths = @(
    (Join-Path $TokenSaverPath "appsettings.json"),
    (Join-Path $TokenSaverPath "..\TokenSaver.config.json"),
    $env:TOKENSAVER_CONFIG
)

$configPath = $null
foreach ($path in $configPaths) {
    if ($path -and (Test-Path $path)) {
        $configPath = $path
        Write-Host "✅ Found config at: $configPath"
        break
    }
}

if (-not $configPath) {
    $configPath = Join-Path $TokenSaverPath "appsettings.json"
    Write-Host "⚠️  Using default path: $configPath"
}

# Update or create config
Write-Host "`n3️⃣  Updating configuration..."
$config = @{
    TokenSaver = @{
        OllamaUrl = "http://$($RouterHost):$RouterPort"
        OllamaModel = $RouterModel
    }
}

if (Test-Path $configPath) {
    $existing = Get-Content $configPath | ConvertFrom-Json
    $existing.TokenSaver = $config.TokenSaver
    $config = $existing
}

$config | ConvertTo-Json | Set-Content -Path $configPath -Force
Write-Host "✅ Config updated"
Write-Host "   OllamaUrl: $($config.TokenSaver.OllamaUrl)"
Write-Host "   OllamaModel: $($config.TokenSaver.OllamaModel)"

Write-Host "`n✅ Setup complete!"
Write-Host "`nNext steps:"
Write-Host "1. Restart TokenSaver.MCP (or restart your IDE)"
Write-Host "2. Verify by sending a test message through the chat interface"
Write-Host "3. Check TokenSaver logs if there are issues"
```

**Usage:**
```powershell
# With defaults (localhost:11435)
.\Setup-TokenSaver-RemoteRouter.ps1

# With custom router
.\Setup-TokenSaver-RemoteRouter.ps1 -RouterHost "192.168.1.50" -RouterPort 11435
```

## Security Considerations

### Authentication

If SimpleModelServer requires API keys for remote access:

```json
{
  "TokenSaver": {
    "OllamaUrl": "http://192.168.1.50:11435",
    "OllamaModel": "HelperModel",
    "ApiKey": "your-bearer-token-here"
  }
}
```

**Note:** Store API keys securely — consider using environment variables or a secrets manager.

### Network Isolation

For multi-machine setups, restrict access using SimpleModelServer's security settings:

```json
{
  "global": {
    "routerHost": "*",
    "routerPort": 11435,
    "trustedNetworks": [
      "192.168.1.0/24",
      "10.0.0.0/8"
    ],
    "trustedApiKeys": ["prod-key-xyz", "dev-key-abc"]
  }
}
```

## Related Documentation

- **[AI Instructions for Remote Router](AI_INSTRUCTIONS_REMOTE_ROUTER.md)** — Instructions for AI agents to wire up the connection
- **[Admin Configuration](ADMIN_CONFIG.md)** — SimpleModelServer configuration reference
- **[Model Configuration](../Config/model-configuration.md)** — Configuring models in SimpleModelServer
