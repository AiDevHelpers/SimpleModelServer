#Requires -Version 7

<#
.SYNOPSIS
    Stops the SimpleModelServerLoader Windows service (if running) and builds/runs SimpleModelServer.Loader
    directly out of build\staging — the dev loop, as opposed to updating the installed service
    copy. Mirrors TokenSaver/scripts/run-dev-server.ps1.

.DESCRIPTION
    The "SimpleModelServerLoader" Windows service and a dev-loop `dotnet run` both bind the same router
    port (global.routerPort, default 11435) and would conflict if both tried to listen at once.
    This script stops the service first, then runs `dotnet run` against SimpleModelServer.Loader in
    --service mode (Ollama + router), in the foreground.

    Stopping a Windows service requires Administrator. If the service is actually running and
    this script wasn't started elevated, it relaunches itself elevated (one UAC prompt) in a new
    window and waits for that window to exit — the dev server itself then runs in that elevated
    window. If the service isn't running (or isn't installed), no elevation is needed and
    everything runs in the current, non-elevated window.

    This does NOT touch the installed copy at C:\Program Files\SimpleModelServer — for that, see
    build\scripts\install-draftmodel-service.ps1 instead. Use this script for iterating on code;
    use that one for pushing a build back to the always-on service.

    Press Ctrl+C to stop the dev server. The Windows service is left stopped afterward — restart
    it yourself (`Start-Service SimpleModelServerLoader`) or pass -RestartServiceOnExit to have this
    script do it automatically when the dev server exits.

    This never touches the real config at %ProgramData%\SimpleModelServer\SimpleModelServer.config.json — it
    points the dev run at its own copy under .draftmodel-dev\dev-config.json (seeded from the
    real config on first run if present, otherwise SimpleModelServer.Loader writes a fresh stock config
    there itself), created once and left alone on later runs so local edits persist.

    Console output (stdout+stderr) is teed to .draftmodel-dev\dev-server.log (overwritten each
    run) in addition to the live window, so the running server's output can be tailed/grepped by
    another process or tool without reading the terminal's screen buffer directly.

    If the dev server exits with a non-zero code (build failure, port conflict, unhandled startup
    exception), this window pauses on "Press Enter to close this window..." instead of closing
    immediately. A normal Ctrl+C stop never hits this.

.PARAMETER Configuration
    dotnet build configuration. Defaults to "Debug" (dev loop) — pass "Release" to reproduce
    what the installed service actually runs.

.PARAMETER RestartServiceOnExit
    Restart the SimpleModelServerLoader service automatically once the dev server process exits (Ctrl+C
    or crash). Off by default — most dev sessions end with the developer explicitly deciding
    whether they want the service back.

.PARAMETER Force
    Proceed even if another process is already listening on the configured router port. Off by
    default — a second dev server silently losing the bind is a confusing failure mode, so this
    script refuses to start over an already-occupied port unless told to.

.EXAMPLE
    .\scripts\run-dev-server.ps1
    .\scripts\run-dev-server.ps1 -Configuration Release
    .\scripts\run-dev-server.ps1 -RestartServiceOnExit
    .\scripts\run-dev-server.ps1 -Force
#>

[CmdletBinding()]
param(
    [string] $Configuration = "Debug",
    [switch] $RestartServiceOnExit,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot        = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ServiceName     = "SimpleModelServerLoader"
$CsProj          = Join-Path $RepoRoot "src\SimpleModelServer.Loader\SimpleModelServer.Loader.csproj"
$DevConfigDir    = Join-Path $RepoRoot ".draftmodel-dev"
$DevConfigPath   = Join-Path $DevConfigDir "dev-config.json"
$DevServerLogPath = Join-Path $DevConfigDir "dev-server.log"
$ProdConfigPath  = Join-Path $env:ProgramData "SimpleModelServer\SimpleModelServer.config.json"

function Write-Step([string]$msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "    OK  $msg" -ForegroundColor Green }

Write-Host ""
Write-Host "=== SimpleModelServer.Loader dev server ($Configuration) ===" -ForegroundColor Cyan

# ── 0. Refuse to start if another client is already bound to the router port ───────
# The Windows service is handled separately in step 1 below (it gets stopped, not treated as a
# conflict) — this catches everything else: a dev server already running from another terminal.

$ExpectedPort = 11435
if (Test-Path $DevConfigPath) {
    try {
        $existingDevConfig = Get-Content $DevConfigPath -Raw | ConvertFrom-Json
        if ($existingDevConfig.global.routerPort) {
            $ExpectedPort = [int]$existingDevConfig.global.routerPort
        }
    } catch {
        # Malformed dev-config.json — fall through with the default port; the dotnet run below
        # will surface the real parse error.
    }
}

Write-Step "Checking whether port $ExpectedPort is already in use"

$portOwner = Get-NetTCPConnection -LocalPort $ExpectedPort -State Listen -ErrorAction SilentlyContinue |
    Select-Object -First 1

if ($portOwner) {
    $ownerProcess = Get-Process -Id $portOwner.OwningProcess -ErrorAction SilentlyContinue
    $ownerIsThisService = (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue).Status -eq "Running"

    if ($ownerIsThisService) {
        Write-Ok "Port $ExpectedPort is held by the '$ServiceName' service — will be stopped in step 1"
    } elseif ($Force) {
        Write-Host "    WARN Port $ExpectedPort is already in use by PID $($portOwner.OwningProcess)$(if ($ownerProcess) { " ($($ownerProcess.ProcessName))" }) — continuing anyway (-Force)." -ForegroundColor Yellow
    } else {
        Write-Host ""
        Write-Host "    Port $ExpectedPort is already in use by PID $($portOwner.OwningProcess)$(if ($ownerProcess) { " ($($ownerProcess.ProcessName))" })." -ForegroundColor Red
        Write-Host "    This looks like another dev server or client already bound to this port." -ForegroundColor Red
        Write-Host "    Stop that process first, or re-run with -Force to proceed anyway." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Ok "Port $ExpectedPort is free"
}

# ── 1. Stop the service, if running — otherwise the dev server can't bind the port ──

Write-Step "Checking for '$ServiceName' service"

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$wasServiceRunning = $false

if ($svc -and $svc.Status -eq "Running") {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Host "    Service is running — relaunching elevated to stop it (UAC prompt incoming)..." -ForegroundColor Yellow

        $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"", "-Configuration", "`"$Configuration`"")
        if ($RestartServiceOnExit) {
            $arguments += "-RestartServiceOnExit"
        }
        if ($Force) {
            $arguments += "-Force"
        }

        $elevated = Start-Process -FilePath "pwsh" -ArgumentList $arguments -WorkingDirectory $RepoRoot -Verb RunAs -Wait -PassThru
        exit $elevated.ExitCode
    }

    Write-Host "    Stopping service '$ServiceName'..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    $wasServiceRunning = $true
    Start-Sleep 2
    Write-Ok "Service stopped"
} elseif ($svc) {
    Write-Ok "Service present but not running ($($svc.Status))"
} else {
    Write-Ok "Service not installed"
}

# ── 2. Seed dev config copy, if it doesn't already exist ───────────────────────────
# Keeps the dev loop off the real %ProgramData%\SimpleModelServer\SimpleModelServer.config.json entirely —
# created once, then left alone so local edits to it persist across runs.

Write-Step "Checking for dev config copy under .draftmodel-dev\"

New-Item -ItemType Directory -Force -Path $DevConfigDir | Out-Null

if (-not (Test-Path $DevConfigPath)) {
    if (Test-Path $ProdConfigPath) {
        Copy-Item $ProdConfigPath $DevConfigPath
        Write-Ok "Created .draftmodel-dev\dev-config.json (copied from $ProdConfigPath)"
    } else {
        Write-Ok "$ProdConfigPath doesn't exist yet — SimpleModelServer.Loader will write a fresh stock config to .draftmodel-dev\dev-config.json on first run"
    }
} else {
    Write-Ok ".draftmodel-dev\dev-config.json already exists — left as-is"
}

# ── 3. Build + run from build\staging (foreground; Ctrl+C to stop) ─────────────────

Write-Step "Building and running (dotnet run, $Configuration)"
Write-Host "    Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host "    Console output is also being teed to $DevServerLogPath" -ForegroundColor DarkGray

$devServerExitCode = 0
try {
    $env:DRAFTMODEL_VERBOSE_LOGGING = "1"
    # Tee to a log file (overwritten each run) in addition to the live console — lets a tool or
    # another process tail the file for errors/stack traces without needing to read this
    # window's screen buffer directly. 2>&1 folds stderr into the same stream so both land in
    # the log.
    dotnet run --project $CsProj --configuration $Configuration -- --service --config $DevConfigPath 2>&1 |
        Tee-Object -FilePath $DevServerLogPath
    $devServerExitCode = $LASTEXITCODE
} catch {
    Write-Host ""
    Write-Host "    Dev server threw: $($_.Exception.Message)" -ForegroundColor Red
    $devServerExitCode = 1
} finally {
    Remove-Item Env:\DRAFTMODEL_VERBOSE_LOGGING -ErrorAction SilentlyContinue
    # ── 4. Optionally hand the port back to the service ─────────────────────────────
    if ($RestartServiceOnExit -and $wasServiceRunning) {
        Write-Step "Restarting '$ServiceName' service"
        Start-Service -Name $ServiceName
        Start-Sleep 3

        $status = Get-Service -Name $ServiceName
        if ($status.Status -eq "Running") {
            Write-Ok "$ServiceName started successfully"
        } else {
            Write-Host "    WARN $ServiceName failed to start. Status: $($status.Status)" -ForegroundColor Yellow
        }
    } elseif ($wasServiceRunning) {
        Write-Host ""
        Write-Host "Note: '$ServiceName' was stopped for this dev session and was NOT restarted." -ForegroundColor Yellow
        Write-Host "Run 'Start-Service $ServiceName' (as Administrator) to bring it back." -ForegroundColor Yellow
    }
}

# ── 5. Wait on failure ───────────────────────────────────────────────────────────────
if ($devServerExitCode -ne 0) {
    Write-Host ""
    Write-Host "    Dev server exited with code $devServerExitCode." -ForegroundColor Red
    Write-Host "    Press Enter to close this window..." -ForegroundColor Yellow
    try { Read-Host | Out-Null } catch { }
    exit $devServerExitCode
}
