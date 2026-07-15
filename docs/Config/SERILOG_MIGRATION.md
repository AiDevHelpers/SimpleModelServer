# Serilog Migration - Summary of Changes

This document outlines the migration from Microsoft.Extensions.Logging to Serilog in SimpleModelServer.

## Overview

SimpleModelServer now uses **Serilog** for comprehensive structured logging with multiple sinks:

- **Console** with ANSI colorization (interactive mode)
- **Named Pipe** for real-time VS Code extension integration
- **Windows Event Log** for service lifecycle events (start, stop, crash, errors)
- **File Logging** (optional, configurable)

All logging is structured (JSON format internally), filterable by log level, and fails gracefully (never crashes the application).

## Files Changed

### Server-Side (.NET)

#### New Files
1. **`Infrastructure/NamedPipeSink.cs`**
   - Custom Serilog sink that writes to named pipe `\\.\pipe\SimpleModelServer.Logs`
   - Handles client connection/disconnection gracefully
   - Buffers events while waiting for VS Code extension to connect
   - Retries connection every 5 seconds

2. **`Infrastructure/SerilogSetup.cs`**
   - Centralized Serilog configuration
   - Configures all sinks (console, event log, named pipe, file)
   - Parses log level from config
   - Handles file path templates (`{Date:yyyyMMdd}`)
   - No-ops gracefully on permission errors (never crashes startup)

3. **`docs/LOGGING.md`**
   - Complete logging configuration guide
   - Sink descriptions and use cases
   - Configuration examples
   - Hot-reload capabilities
   - Troubleshooting guide

4. **`docs/SERILOG_MIGRATION.md`** (this file)
   - Migration summary and technical details

#### Modified Files
1. **`SimpleModelServer.Server.csproj`**
   - Added Serilog NuGet packages:
     - `Serilog` 4.2.0
     - `Serilog.Sinks.Console` 6.2.0
     - `Serilog.Sinks.File` 6.0.0
     - `Serilog.Sinks.EventLog` 3.2.0

2. **`Program.cs`**
   - Removed Microsoft.Extensions.Logging setup
   - Removed `EventLogRegistration` class (Serilog handles it now)
   - Added Serilog initialization via `SerilogSetup.ConfigureLogger()`
   - Both service and interactive modes now use Serilog
   - Simplified logging configuration (moved to `SerilogSetup.cs`)

3. **`Configuration/ConfigurationManager.cs`**
   - Added `logLevel` and `enable_file_logs` to `HotReloadableSettings`
   - Added validation for new logging settings

4. **`../Config/SimpleModelServer.Config/Configuration/AppConfig.cs`**
   - Added three new properties to `GlobalConfig`:
     - `LogLevel` (string): Minimum log level, default "Information"
     - `EnableFileLogs` (boolean): Enable/disable file logging, default false
     - `LogPath` (string): Custom file path, empty uses default
   - Updated `ParseGlobal()` to parse logging config from JSON

### Extension-Side (TypeScript/Node.js)

#### Modified Files
1. **`src/extension.ts`**
   - Added `channelSimpleModelServer` output channel for SimpleModelServer logs
   - Added named pipe connection variables and constants
   - Added `startSimpleModelServerPipePolling()` / `stopSimpleModelServerPipePolling()`
   - Added `connectSimpleModelServerPipe()` to establish named pipe connection
   - Added `processSimpleModelServerLogLine()` to parse and display logs
   - Added `levelToColorCode()` for emoji-based colorization (❌ ERROR, ⚠️  WARNING, etc.)
   - Logs display with timestamp, level indicator, and emoji for quick visual scanning
   - Pipe polling starts automatically on activation

## Configuration

### Default Behavior

No configuration changes needed! Logging works out of the box with:

```json
{
  "global": {
    "logLevel": "Information",
    "enableFileLogs": false
  }
}
```

### Custom Configuration

Add to `SimpleModelServer.config.json`:

```json
{
  "global": {
    "logLevel": "Debug",
    "enableFileLogs": true,
    "logPath": "%ProgramData%\\SimpleModelServer\\logs\\log-{Date:yyyyMMdd}.txt"
  }
}
```

### Log Level Options

| Level | Use Case |
|-------|----------|
| `Verbose` | Development: very detailed diagnostics |
| `Debug` | Development: per-request logs, internal state |
| `Information` | Normal operation: key milestones (default) |
| `Warning` | Unusual but recoverable conditions |
| `Error` | Failures requiring attention |
| `Fatal` | Unrecoverable errors |

### Hot-Reload

Change logging settings at runtime via `/admin/config` API:

```bash
curl -X POST http://localhost:11435/admin/config \
  -H "Content-Type: application/json" \
  -d '{"path": "global.logLevel", "value": "Debug"}'
```

## Sink Details

### Console (Always)
- **Format**: `[HH:mm:ss.fff ±00:00] [LVL] Message`
- **Colors**: ANSI codes for different levels
- **When**: Interactive runs only (not visible in Windows service)

### Named Pipe
- **Path**: `\\.\pipe\SimpleModelServer.Logs`
- **Format**: Compact JSON (one event per line)
- **Client**: VS Code extension reads from this pipe
- **Reliability**: Non-blocking, retries on client disconnect
- **Buffer**: Events queued while no client connected

### Event Log (Service Mode Only)
- **Source**: "SimpleModelServer" in Application log
- **Levels**: Warning and above (Error, Fatal)
- **Events**: Service lifecycle (start, stop), errors, fatals
- **Backup**: Extension polls Event Log every 5 seconds (Event Log is redundant, but kept as backup)

### File Logging (Optional)
- **Default Path**: `%ProgramData%\SimpleModelServer\logs\log-{Date:yyyyMMdd}.txt`
- **Template**: Supports `{Date:yyyyMMdd}` placeholder
- **Rolling**: Daily rotation, up to 7 retained files
- **Size Limit**: 10 MB per file before rotation
- **Format**: Compact JSON (structured logging for analysis)

## Breaking Changes

### For Consumers

**None!** The public HTTP/MCP APIs remain unchanged. All changes are internal logging infrastructure.

### For Developers

1. **ILogger Compatibility**: Serilog's `ILogger` is compatible with `Microsoft.Extensions.Logging.ILogger`, so existing code using `ILogger` works unchanged.

2. **Removed Code**:
   - `EventLogRegistration` class — moved to `SerilogSetup`
   - Microsoft.Extensions.Logging setup in `Program.cs` — replaced with Serilog

3. **New Imports** (for anyone modifying logging):
   - `using Serilog;`
   - `using SimpleModelServer.Server.Infrastructure;`

## Performance Impact

- **Named Pipe**: Non-blocking with 5-second retry backoff
- **Event Log**: Handled by Windows (minimal overhead in service mode)
- **File Logging**: Async writes, 10 MB size limits prevent unbounded growth
- **Console**: Only active in interactive mode (not service)
- **Safety**: Logging failures never crash the application

## Testing Checklist

- [ ] **Interactive Mode**: Run with `--service` flag, verify colorized console output
- [ ] **Service Mode**: Install service, check Windows Event Log for "SimpleModelServer" events
- [ ] **Named Pipe**: Open VS Code extension, check "Simple Model Server" channel
- [ ] **File Logging**: Enable, verify logs appear in `%ProgramData%\SimpleModelServer\logs\`
- [ ] **Hot-Reload**: Change log level via `/admin/config`, verify new level takes effect
- [ ] **Error Handling**: Stop service mid-startup, verify Trace() file has diagnostic info
- [ ] **Extension UI**: Verify emojis and timestamps display correctly in "Simple Model Server" channel

## Migration Path

### For Existing Deployments

1. **No migration needed** — existing config files work as-is
2. **Optional**: Add logging settings to config for finer control
3. **Optional**: Enable file logging for production debugging
4. **Recommended**: Update VS Code extension to see the new "Simple Model Server" channel

### For Development

1. Set `$env:SIMPLEMODELSERVER_VERBOSE_LOGGING = "1"` for Debug logs
2. Use the "Simple Model Server" channel in VS Code for real-time log viewing
3. Check Windows Event Log for service lifecycle events

## Troubleshooting

### No logs in "Simple Model Server" channel
- Ensure server is running: `sc query SimpleModelServerLoader`
- Check Windows Event Log for errors
- Verify pipe isn't blocked by Windows Defender or antivirus

### File logging not working
- Verify `enableFileLogs: true` in config
- Check directory permissions: `%ProgramData%\SimpleModelServer\logs\`
- Ensure valid log file path template

### Event Log shows nothing
- Event Log sink only captures Warning/Error/Fatal
- Check configured `logLevel` — if set to Warning, Info events won't appear
- Verify "SimpleModelServer" source exists: `wevtutil query-events Application | findstr SimpleModelServer`

## Future Enhancements

Potential improvements beyond this initial implementation:

1. **Structured Fields**: Add request ID, user context to all logs
2. **Remote Sink**: Send logs to a central log aggregator (ELK, Datadog, etc.)
3. **Performance Metrics**: Log timing and resource usage
4. **Audit Trail**: Dedicated sink for security events (API key usage, auth failures)
5. **Extensions**: Custom Serilog sinks for other consumers (Slack alerts, email, etc.)
