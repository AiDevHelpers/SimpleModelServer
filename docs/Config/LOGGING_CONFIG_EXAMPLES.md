# Logging Configuration Examples

## Default Configuration (No Changes Needed)

```json
{
  "global": {
    "routerPort": 11435,
    "draftModel": "qwen2.5-coder-0.5b-chat",
    "mainModel": "qwen2.5:3b"
  }
}
```

With defaults:
- Log level: `Information`
- File logging: Disabled
- Logs appear in: Console (interactive) + Named Pipe (real-time) + Event Log (service)

## Enable Debug Logging

Add to config:

```json
{
  "global": {
    "logLevel": "Debug",
    "routerPort": 11435,
    "draftModel": "qwen2.5-coder-0.5b-chat",
    "mainModel": "qwen2.5:3b"
  }
}
```

Or set via environment variable:

```powershell
$env:SIMPLEMODELSERVER_VERBOSE_LOGGING = "1"
.\SimpleModelServer.Server.exe --service
```

**Debug logs include**:
- Per-request router logs: `[GET] /api/generate`
- Model loading details
- Cache operations
- Internal state changes

## Enable File Logging with Default Path

```json
{
  "global": {
    "enableFileLogs": true,
    "logPath": "",
    "routerPort": 11435,
    "draftModel": "qwen2.5-coder-0.5b-chat",
    "mainModel": "qwen2.5:3b"
  }
}
```

Logs appear in: `%ProgramData%\SimpleModelServer\logs\log-20250714.txt`

Files rotate daily with up to 7 retained files.

## Custom Log File Path

```json
{
  "global": {
    "enableFileLogs": true,
    "logPath": "C:\\MyLogs\\simplemodel-{Date:yyyyMMdd}.txt",
    "routerPort": 11435,
    "draftModel": "qwen2.5-coder-0.5b-chat",
    "mainModel": "qwen2.5:3b"
  }
}
```

Logs appear in: `C:\MyLogs\simplemodel-20250714.txt`

**Template placeholders**:
- `{Date:yyyyMMdd}` — Current date (e.g., 20250714)

## Production Configuration (Verbose File Logging)

For debugging production issues:

```json
{
  "global": {
    "logLevel": "Information",
    "enableFileLogs": true,
    "logPath": "\\\\fileserver\\logs\\simplemodel\\log-{Date:yyyyMMdd}.txt",
    "routerPort": 11435,
    "draftModel": "qwen2.5-coder-0.5b-chat",
    "mainModel": "qwen2.5:3b"
  }
}
```

- Console: Information level (minimal noise in service mode)
- File: Information level (comprehensive audit trail)
- Event Log: Warning level (critical issues only)
- Named Pipe: Debug level (real-time extension monitoring)

## Development Configuration (Maximum Verbosity)

```json
{
  "global": {
    "logLevel": "Debug",
    "enableFileLogs": true,
    "logPath": "C:\\Dev\\logs\\simplemodel-{Date:yyyyMMdd}-{TickCount}.txt",
    "routerPort": 11435,
    "draftModel": "qwen2.5-coder-0.5b-chat",
    "mainModel": "qwen2.5:3b"
  }
}
```

Plus environment variable:

```powershell
$env:SIMPLEMODELSERVER_VERBOSE_LOGGING = "1"
```

**Result**:
- Console: Debug level
- File: Debug level
- Named Pipe: Debug level (real-time viewing in VS Code)
- Per-request logs visible everywhere

## Quiet Configuration (Errors Only)

```json
{
  "global": {
    "logLevel": "Error",
    "enableFileLogs": false,
    "routerPort": 11435,
    "draftModel": "qwen2.5-coder-0.5b-chat",
    "mainModel": "qwen2.5:3b"
  }
}
```

Only errors and fatal issues logged anywhere.

## Hot-Reload Configuration Changes

Change settings at runtime without restarting:

### Enable File Logging

```bash
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"path": "global.enableFileLogs", "value": true}'
```

### Change Log Level to Debug

```bash
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"path": "global.logLevel", "value": "Debug"}'
```

### Revert to Information

```bash
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"path": "global.logLevel", "value": "Information"}'
```

### Disable File Logging

```bash
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"path": "global.enableFileLogs", "value": false}'
```

**Note**: The `logPath` setting requires a service restart (not hot-reloadable).

## Log Levels Reference

| Level | Include | Use Case |
|-------|---------|----------|
| **Verbose** | Verbose, Debug, Info, Warn, Error, Fatal | Development deep-dive |
| **Debug** | Debug, Info, Warn, Error, Fatal | Development, per-request logs |
| **Information** | Info, Warn, Error, Fatal | Default, normal operation ✓ |
| **Warning** | Warn, Error, Fatal | Quiet mode, issues only |
| **Error** | Error, Fatal | Critical errors only |
| **Fatal** | Fatal | Crash-level only |

## Checking Configured Logging

### Via Admin API

```bash
curl http://localhost:11435/admin/config
```

Response includes current `logLevel`, `enableFileLogs`, `logPath`.

### Via Windows Event Log (Service)

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='SimpleModelServer'} -MaxEvents 1
```

### Via File

```powershell
Get-Content "$env:ProgramData\SimpleModelServer\logs\log-*.txt" -Tail 20
```

### Via VS Code Extension

Open the "Simple Model Server" output channel — logs stream in real-time.

## Network Path Examples

### UNC Path (Windows share)

```json
{
  "global": {
    "enableFileLogs": true,
    "logPath": "\\\\corp-fileserver\\logs\\simplemodel\\log-{Date:yyyyMMdd}.txt"
  }
}
```

### Azure File Share (premium SMB)

```json
{
  "global": {
    "enableFileLogs": true,
    "logPath": "\\\\azurefileshare.file.core.windows.net\\logs\\simplemodel\\log-{Date:yyyyMMdd}.txt"
  }
}
```

Make sure the service account has access to the network path.

## Troubleshooting Configuration

### Service won't start after config change

Check Windows Event Log or the trace file:

```powershell
# For recent service startup issues
Get-Content "$env:ProgramData\SimpleModelServer\service-startup-trace.log" -Tail 20
```

### File logging silently fails

1. Verify directory exists and service has write permission:
   ```powershell
   Test-Path "C:\MyLogs"
   (Get-Acl "C:\MyLogs").Access | Where-Object IdentityReference -Match "SYSTEM"
   ```

2. Check Windows Event Log for permission errors

3. Validate path doesn't contain invalid characters

### Named pipe not connected

1. Service must be running:
   ```powershell
   sc query SimpleModelServerLoader
   ```

2. Windows Defender or antivirus might be blocking:
   - Add `SimpleModelServer.Server.exe` to exclusions
   - Check firewall rules

3. VS Code extension must be running to connect

## Best Practices

1. **Production**: Use `Information` level with file logging for audit trail
2. **Debugging**: Use `Debug` level and real-time extension channel
3. **Performance**: Keep `logLevel: Warning` or `Information` to minimize overhead
4. **Archival**: Set `logPath` to network share for centralized log collection
5. **Rotation**: Don't disable file rotation; let Serilog manage retention
6. **Security**: Don't log sensitive data (API keys, passwords) — framework filters these
