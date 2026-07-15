# Serilog Logging Configuration

SimpleModelServer now uses [Serilog](https://serilog.net/) for centralized structured logging across console, Windows Event Log, named pipes, and optional file outputs.

## Log Sinks

### 1. **Console** (Interactive Mode)
- **When**: Always, for interactive console runs
- **Format**: Colorized with timestamps and log levels
- **Colors**: ANSI color codes for syntax highlighting
- **Filtering**: Respects configured `logLevel` setting

### 2. **Named Pipe** (Real-time Streaming)
- **When**: Always active
- **Pipe Name**: `SimpleModelServer.Logs`
- **Format**: Compact JSON (one event per line)
- **Consumers**: VS Code extension reads from this pipe
- **Behavior**: Buffers events while waiting for clients to connect; retries every 5 seconds

### 3. **Windows Event Log** (Service Mode Only)
- **When**: Only when running as a Windows service
- **Source**: "SimpleModelServer" in Application log
- **Level**: Warning and above (Error, Fatal)
- **Events**: Service starts, stops, crashes, and error conditions
- **Auto-registration**: Event source is created automatically on first run

### 4. **File Logging** (Optional)
- **When**: Only if `enableFileLogs` is true in configuration
- **Default Path**: `%ProgramData%\SimpleModelServer\logs\log-{Date:yyyyMMdd}.txt`
- **Custom Path**: Set via `logPath` config (supports `{Date:yyyyMMdd}` template)
- **Rolling**: Daily rotation with up to 7 retained files
- **Size Limit**: 10 MB per file
- **Format**: Compact JSON for structured analysis

## Configuration

Add logging settings to `SimpleModelServer.config.json` in the `global` section:

```json
{
  "global": {
    "logLevel": "Information",
    "enableFileLogs": false,
    "logPath": "",
    
    "routerPort": 11435,
    "draftModel": "qwen2.5-coder-0.5b-chat",
    "mainModel": "qwen2.5:3b"
  }
}
```

### Configuration Options

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `logLevel` | string | "Information" | Minimum log level: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal` |
| `enableFileLogs` | boolean | false | Enable file logging to disk |
| `logPath` | string | "" | Custom log file path; empty uses default |

## Environment Variables

### Development Mode

Set `SIMPLEMODELSERVER_VERBOSE_LOGGING=1` or `true` to enable Debug-level logging:

```powershell
$env:SIMPLEMODELSERVER_VERBOSE_LOGGING = "1"
.\SimpleModelServer.Server.exe --service
```

This overrides the configured `logLevel` and surfaces Debug events like per-request router logs.

## Extension Integration

The VS Code extension (`TokenSaver.VsCodeExt`) listens to the named pipe and displays logs in a "Simple Model Server" output channel with:

- Colorized output (ERROR/FATAL in red, WARNING in yellow, INFO/DEBUG in default)
- Timestamp on first line of multi-line events
- Indented continuation lines (stack traces, multi-line messages)
- Real-time streaming as logs are emitted

## Log Levels Explained

| Level | When to Use | Example |
|-------|------------|---------|
| **Verbose** | Development only; very detailed diagnostics | Every method entry/exit, cache hits |
| **Debug** | Development; request-level details | Per-request router logs, internal state changes |
| **Information** | Normal operation; key milestones | Service started, model loaded, request received |
| **Warning** | Unusual but recoverable conditions | Retry attempt, deprecated config option |
| **Error** | Failures requiring attention but recoverable | Model load failed, request timeout |
| **Fatal** | Unrecoverable errors; process should stop | Out of memory, critical system resource unavailable |

## Hot-Reload Configuration

The following logging settings can be changed via the `/admin/config` API without restarting:

- `logLevel` — Changes the minimum log level immediately
- `enableFileLogs` — Enables/disables file logging (without restart)

The `logPath` setting requires a full restart to take effect (appears in `RestartRequiredSettings`).

## File Logging Example

Enable file logging by updating config:

```json
{
  "global": {
    "enableFileLogs": true,
    "logPath": "%ProgramData%\\SimpleModelServer\\logs\\log-{Date:yyyyMMdd}.txt"
  }
}
```

Then POST to `/admin/config`:

```bash
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"path": "global.enableFileLogs", "value": true}'

curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"path": "global.logPath", "value": "%ProgramData%\\SimpleModelServer\\logs\\log-custom.txt"}'
```

## Structured Logging

All logs are structured with context:

```json
{
  "@t": "2025-07-14T10:30:45.1234567Z",
  "@m": "Service started",
  "@l": "Information",
  "ProcessId": 12345,
  "MachineName": "WORKSTATION-01"
}
```

This structure enables powerful filtering and analysis in log aggregation systems.

## Troubleshooting

### No logs appearing in VS Code extension

1. Verify the service is running: `sc query SimpleModelServerLoader`
2. Check Windows Event Log under Application → SimpleModelServer
3. If file logging is enabled, check `%ProgramData%\SimpleModelServer\logs\`
4. Ensure the named pipe isn't blocked by a firewall or security tool

### Event Log showing no events

- Event Log sink only captures Warning and above (Error, Fatal)
- Check the configured `logLevel` — if set to "Warning" or higher, Information events won't appear
- Verify the "SimpleModelServer" event source exists: `wevtutil el | findstr SimpleModelServer`

### File logging not working

- Check that `%ProgramData%\SimpleModelServer\logs\` directory is writable
- Verify `enableFileLogs: true` in config
- Ensure the path template is valid (use `{Date:yyyyMMdd}` for date placeholders)
- Check Windows Event Log for permission errors

### Performance concerns

- Named pipe operations are non-blocking with retry backoff
- File logging has size limits (10 MB) and rolling retention (7 days)
- Console output only impacts interactive mode (not service)
- Logging failures never crash the application
