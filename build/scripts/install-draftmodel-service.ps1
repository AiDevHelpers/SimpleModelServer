#Requires -Version 7.0
<#
.SYNOPSIS
    Builds and installs SimpleModelServer.Loader as a Windows service.

.DESCRIPTION
    1. Publishes SimpleModelServer.Loader as a self-contained win-x64 executable.
    2. Copies the output to C:\Program Files\SimpleModelServer\ (or -InstallDir).
    3. Registers and starts the SimpleModelServerLoader Windows service.

.PARAMETER InstallDir
    Installation directory. Defaults to C:\Program Files\SimpleModelServer.

.PARAMETER Runtime
    .NET runtime identifier. Defaults to win-x64.

.PARAMETER SkipBuild
    Skip dotnet publish and use whatever is already in -InstallDir.

.PARAMETER Clean
    Delete the existing SimpleModelServer.config.json under %ProgramData%\SimpleModelServer before
    installing, so EnsureConfig regenerates a fresh stock config on next start. Does not
    touch the models\ subfolder (pulled Ollama models are left in place).

.PARAMETER Local
    Restrict router access to local-machine traffic only: loopback (always allowed
    regardless of this switch), plus the private-address blocks commonly used by Hyper-V
    virtual switches, WSL2, and Docker Desktop's default networks (172.16.0.0/12 and
    192.168.0.0/16). Writes these into global.trustedNetworks in the generated config.
    Without -Local, trustedNetworks is left empty (unrestricted — matches prior behavior).
    Combine with -Clean to force these defaults onto an existing installation.

.PARAMETER SkipGpuSetup
    Skip GPU detection and the NVIDIA driver/CUDA Toolkit install step entirely. Without
    this switch, the installer detects the GPU vendor and, for NVIDIA hardware, attempts to
    bring the driver up to date (via Windows Update's Drivers category) and installs the
    CUDA Toolkit (via winget) if not already present. Note: Ollama itself bundles its own
    CUDA/ROCm runtime and only strictly needs a current NVIDIA driver to use the GPU — the
    CUDA Toolkit install here is not required by SimpleModelServer.Loader/Ollama, it's installed
    because it was explicitly requested for this environment (e.g. other tooling on the
    machine wants nvcc/the toolkit directly). Use -SkipGpuSetup to opt out.

.EXAMPLE
    .\install-draftmodel-service.ps1
    .\install-draftmodel-service.ps1 -InstallDir "D:\SimpleModelServer"
    .\install-draftmodel-service.ps1 -SkipBuild
    .\install-draftmodel-service.ps1 -Clean
    .\install-draftmodel-service.ps1 -Clean -Local
    .\install-draftmodel-service.ps1 -SkipGpuSetup
#>
[CmdletBinding()]
param(
    [string] $InstallDir = "$env:ProgramFiles\SimpleModelServer",
    [string] $Runtime    = "win-x64",
    [switch] $SkipBuild,
    [switch] $Clean,
    [switch] $Local,
    [switch] $SkipGpuSetup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Auto-elevate ───────────────────────────────────────────────────────────────
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    foreach ($key in $PSBoundParameters.Keys) {
        $val = $PSBoundParameters[$key]
        if ($val -is [switch]) {
            if ($val.IsPresent) { $argList += "-$key" }
        } else {
            $argList += "-$key"
            $argList += "`"$val`""
        }
    }
    Start-Process pwsh -Verb RunAs -ArgumentList $argList
    exit
}

$RepoRoot   = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$ProjectFile = Join-Path $RepoRoot "src\SimpleModelServer.Loader\SimpleModelServer.Loader.csproj"
$ExePath     = Join-Path $InstallDir "SimpleModelServer.Loader.exe"
$ServiceName = "SimpleModelServerLoader"
$ConfigDir   = Join-Path $env:ProgramData "SimpleModelServer"

function Write-Step([string]$msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "    WARN $msg" -ForegroundColor Yellow }

function Ensure-EventSource([string]$source, [string]$logName = 'Application') {
    if (-not [System.Diagnostics.EventLog]::SourceExists($source)) {
        Write-Step "Registering Windows event source '$source'"
        New-EventLog -Source $source -LogName $logName
        Write-Ok "Event source '$source' created"
    } else {
        Write-Ok "Event source '$source' already exists"
    }
}

# ── 1. Prerequisites ───────────────────────────────────────────────────────────

Write-Step "Checking prerequisites"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install it from https://dot.net and re-run."
}
Write-Ok ".NET SDK $(dotnet --version)"

# SimpleModelServer.Loader is self-contained (it doesn't need .NET on the target machine), but it
# still hard-depends on the separate `ollama` CLI/service being installed and on PATH —
# DraftPairApp.RunAsync checks for it at startup and, if missing, logs an error and exits
# without ever binding the router. Without this check here, that failure is invisible until
# someone goes looking in the Application event log (source 'SimpleModelServer') after wondering why
# /health never comes back OK. This is a warning, not a hard stop: Ollama can legitimately be
# installed after this script runs, and the service will pick it up on its next start/health
# recovery cycle (see BackgroundHealthLoopAsync in DraftPairApp.cs).
if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
    Write-Warn "'ollama' was not found on PATH. SimpleModelServer.Loader depends on it and will not become healthy until it's installed."
    Write-Warn "Install it from https://ollama.com/download, then either restart this machine or run: Restart-Service SimpleModelServerLoader"
} else {
    Write-Ok "ollama found: $(Get-Command ollama | Select-Object -ExpandProperty Source)"
}

# ── 1b. GPU detection + NVIDIA driver / CUDA Toolkit setup ─────────────────────
# gpuLayers in SimpleModelServer.config.json defaults to "auto" and is resolved by the app itself
# at runtime — this step does not touch that setting. What it does handle: making sure the
# hardware underneath is actually usable before the app ever gets a chance to detect it.
# Note that Ollama bundles its own CUDA/ROCm runtime libraries and only strictly requires a
# current GPU driver (not a separate CUDA Toolkit) to use the GPU. The CUDA Toolkit install
# below is done anyway because it was explicitly requested for this environment; skip it
# with -SkipGpuSetup if you don't want it.
if ($SkipGpuSetup) {
    Write-Step "Skipping GPU detection/setup (-SkipGpuSetup)"
} else {
    Write-Step "Detecting GPU"

    $gpus = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch 'Basic Display|Remote Display|Virtual' }
    $nvidiaGpus = $gpus | Where-Object { $_.Name -match 'NVIDIA' }
    $amdGpus    = $gpus | Where-Object { $_.Name -match 'AMD|Radeon' }

    if (-not $gpus) {
        Write-Warn "No dedicated GPU detected — SimpleModelServer.Loader will run on CPU."
    } elseif ($nvidiaGpus) {
        foreach ($g in $nvidiaGpus) { Write-Ok "NVIDIA GPU detected: $($g.Name)" }

        # ── Driver check ────────────────────────────────────────────────────────
        $driverOk = $false
        $nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
        if ($nvidiaSmi) {
            try {
                $smiOut = & nvidia-smi --query-gpu=driver_version --format=csv,noheader 2>$null
                if ($LASTEXITCODE -eq 0 -and $smiOut) {
                    $driverVersion = ($smiOut | Select-Object -First 1).ToString().Trim()
                    $majorVersion = 0
                    [void][int]::TryParse(($driverVersion -split '\.')[0], [ref]$majorVersion)
                    if ($majorVersion -ge 550) {
                        Write-Ok "NVIDIA driver $driverVersion detected (>= 550, OK)"
                        $driverOk = $true
                    } else {
                        Write-Warn "NVIDIA driver $driverVersion detected but is older than the recommended 550+."
                    }
                }
            } catch {
                Write-Warn "nvidia-smi found but failed to report a driver version: $_"
            }
        } else {
            Write-Warn "No NVIDIA driver detected (nvidia-smi not found)."
        }

        if (-not $driverOk) {
            Write-Step "Attempting automatic NVIDIA driver install/update via Windows Update"
            try {
                if (-not (Get-Module -ListAvailable -Name PSWindowsUpdate)) {
                    Write-Step "Installing PSWindowsUpdate module"
                    Install-PackageProvider -Name NuGet -Force -Scope AllUsers | Out-Null
                    Install-Module -Name PSWindowsUpdate -Force -Scope AllUsers -Repository PSGallery | Out-Null
                }
                Import-Module PSWindowsUpdate -ErrorAction Stop

                $driverUpdates = Get-WindowsUpdate -MicrosoftUpdate -Category "Drivers" -ErrorAction SilentlyContinue |
                    Where-Object { $_.Title -match 'NVIDIA' }

                if ($driverUpdates) {
                    Write-Step "Installing NVIDIA driver update(s) via Windows Update (this can take a while)"
                    Install-WindowsUpdate -MicrosoftUpdate -Category "Drivers" -Title '*NVIDIA*' -AcceptAll -IgnoreReboot -Confirm:$false -ErrorAction Stop | Out-Null
                    Write-Warn "Driver update installed — a reboot may be required before the GPU is fully usable. Re-run this script after rebooting to confirm."
                } else {
                    Write-Warn "No NVIDIA driver update found via Windows Update."
                    Write-Warn "Install the latest driver manually from https://www.nvidia.com/drivers, then re-run this script."
                }
            } catch {
                Write-Warn "Automatic driver install failed: $_"
                Write-Warn "Install the NVIDIA driver manually from https://www.nvidia.com/drivers, then re-run this script."
            }
        }

        # ── CUDA Toolkit ────────────────────────────────────────────────────────
        $cudaInstalled = (Get-Command nvcc -ErrorAction SilentlyContinue) -or
            (Test-Path "$env:ProgramFiles\NVIDIA GPU Computing Toolkit\CUDA")
        if ($cudaInstalled) {
            Write-Ok "CUDA Toolkit already present"
        } else {
            Write-Step "Installing NVIDIA CUDA Toolkit"
            if (Get-Command winget -ErrorAction SilentlyContinue) {
                & winget install -e --id Nvidia.CUDA --silent --accept-package-agreements --accept-source-agreements
                if ($LASTEXITCODE -eq 0) {
                    Write-Ok "CUDA Toolkit installed"
                } else {
                    Write-Warn "winget install of Nvidia.CUDA exited with code $LASTEXITCODE — verify manually."
                }
            } else {
                Write-Warn "winget not found — install the CUDA Toolkit manually from https://developer.nvidia.com/cuda-downloads"
            }
        }
    } elseif ($amdGpus) {
        foreach ($g in $amdGpus) { Write-Ok "AMD GPU detected: $($g.Name)" }
        Write-Warn "AMD ROCm support on Windows is limited and there is no reliable unattended install path for it today. Ollama's bundled ROCm runtime will be used if the driver stack supports it, but automatic driver/ROCm install is not attempted here — verify GPU offload manually (ollama run <model> --verbose) after installing the latest AMD driver from https://www.amd.com/en/support if needed."
    } else {
        Write-Warn "Unrecognized GPU vendor(s): $($gpus.Name -join ', ') — automatic driver/CUDA setup skipped."
    }
}

# ── 2. Stop existing service before overwriting files ──────────────────────────

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
# Track whether this is an upgrade (exe already present AND service registered).
# On upgrade we replace binaries only and leave the SCM registration unchanged.
$isUpgrade = (Test-Path $ExePath) -and ($null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue))

if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Write-Step "Stopping existing $ServiceName service"
        Stop-Service -Name $ServiceName -Force
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        Write-Ok "Service stopped"
    } else {
        Write-Warn "Service already stopped"
    }
}

# ── 3. Publish ─────────────────────────────────────────────────────────────────

if ($SkipBuild) {
    Write-Step "Skipping build (--SkipBuild)"
    if (-not (Test-Path $ExePath)) {
        throw "SkipBuild specified but exe not found at $ExePath"
    }
} else {
    Write-Step "Publishing SimpleModelServer.Loader"

    $stagingDir = Join-Path $env:TEMP "SimpleModelServer.Loader.publish"
    if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

    dotnet publish $ProjectFile `
        --configuration Release `
        --runtime       $Runtime `
        --self-contained true `
        --output        $stagingDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit $LASTEXITCODE)"
    }
    Write-Ok "Published to staging: $stagingDir"

    # ── 4. Copy to install dir ─────────────────────────────────────────────────

    Write-Step "Installing to $InstallDir"

    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

    # Robocopy: /MIR mirrors (adds new, removes deleted), /NFL /NDL suppress file listings
    # Exit codes 0-7 are success (bit flags: 0=no change, 1=copied, 2=extra, 4=mismatch)
    robocopy $stagingDir $InstallDir /MIR /NFL /NDL /NJH /NJS
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy failed (exit $LASTEXITCODE)"
    }
    Write-Ok "Copied to $InstallDir"

    Remove-Item $stagingDir -Recurse -Force
}

# ── 5. Create config directory ────────────────────────────────────────────────

Write-Step "Creating config directory"
New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null
Write-Ok $ConfigDir

$ConfigJsonPath = Join-Path $ConfigDir "SimpleModelServer.config.json"

# Copy the AI-agent instructions for connecting to a shared/remote router (see
# SimpleModelServer.Loader/AI_INSTRUCTIONS_REMOTE_ROUTER.md for why this exists as a plain file
# instead of a programmatic config-writer). Always refreshed on install/upgrade so it stays
# in sync with the version of SimpleModelServer.Loader being installed.
$AiInstructionsSource = Join-Path $RepoRoot "src\SimpleModelServer.Loader\AI_INSTRUCTIONS_REMOTE_ROUTER.md"
if (Test-Path $AiInstructionsSource) {
    Copy-Item $AiInstructionsSource (Join-Path $ConfigDir "AI_INSTRUCTIONS_REMOTE_ROUTER.md") -Force
    Write-Ok "Copied AI_INSTRUCTIONS_REMOTE_ROUTER.md to $ConfigDir"
}

# ── 5a. Clean existing config (-Clean) ─────────────────────────────────────────
# Removes only SimpleModelServer.config.json so EnsureConfig writes a fresh stock config on next
# start. Deliberately leaves ConfigDir\models untouched — that's the Ollama model store and
# wiping it would force a full re-pull of every model.
if ($Clean) {
    Write-Step "Cleaning existing config (-Clean)"
    $modelSourcesPath = Join-Path $ConfigDir "config.json"

    if (Test-Path $ConfigJsonPath) {
        Remove-Item $ConfigJsonPath -Force
        Write-Ok "Removed $ConfigJsonPath"
    } else {
        Write-Ok "No existing SimpleModelServer.config.json to remove"
    }

    if (Test-Path $modelSourcesPath) {
        Remove-Item $modelSourcesPath -Force
        Write-Ok "Removed legacy $modelSourcesPath"
    }
}

# ── 5b. Restrict to local networks only (-Local) ───────────────────────────────
# SimpleModelServer.Loader's own DefaultConfig.EnsureConfig only writes SimpleModelServer.config.json if
# it doesn't already exist — it never overwrites one that's present. So -Local must not rely
# on racing the service's first start to populate the file; it writes/patches
# global.trustedNetworks directly, here, before the service is ever started. If no config
# exists yet, a minimal stock config is written outright (the exe will leave it alone on
# first run since EnsureConfig only acts when the file is missing).
$LocalTrustedNetworks = @("172.16.0.0", "192.168.0.0")

function Set-TrustedNetworks([string]$path, [string[]]$networks) {
    $configJson = Get-Content $path -Raw | ConvertFrom-Json
    if (-not $configJson.global) {
        $configJson | Add-Member -MemberType NoteProperty -Name global -Value ([PSCustomObject]@{})
    }

    if ($configJson.global.PSObject.Properties.Name -contains "trustedNetworks") {
        $configJson.global.trustedNetworks = $networks
    } else {
        $configJson.global | Add-Member -MemberType NoteProperty -Name trustedNetworks -Value $networks
    }

    $configJson | ConvertTo-Json -Depth 10 | Set-Content $path
}

if ($Local) {
    Write-Step "Restricting router to local networks only (-Local)"

    if (-not (Test-Path $ConfigJsonPath)) {
        # Minimal stock config sufficient for SimpleModelServer.Loader to start; matches the shape
        # DefaultConfig.StockJson would produce, so the exe's own EnsureConfig leaves it alone.
        $stockConfig = @{
            global = @{
                defaultAgentType = "code"
                gpuLayers        = "auto"
                cpuThreads       = "auto"
                draftModel       = "qwen2.5-coder-0.5b-chat"
                mainModel        = "qwen2.5-coder-3b-chat"
                ollamaPort       = 11434
                routerPort       = 11435
                routerHost       = "localhost"
                routerModelName  = "HelperModel"
                trustedNetworks  = $LocalTrustedNetworks
            }
        }
        $stockConfig | ConvertTo-Json -Depth 10 | Set-Content $ConfigJsonPath
        Write-Ok "Wrote new config with global.trustedNetworks = $($LocalTrustedNetworks -join ', ') to $ConfigJsonPath"
    } else {
        Set-TrustedNetworks -path $ConfigJsonPath -networks $LocalTrustedNetworks
        Write-Ok "Set global.trustedNetworks = $($LocalTrustedNetworks -join ', ') in $ConfigJsonPath"
    }
    Write-Ok "(loopback is always allowed in addition to these ranges)"
}

# ── 5c. Register URL ACL for HttpListener (router port) ───────────────────────
# The router uses HttpListener which requires a URL ACL reservation for the listen prefix.
# Without this, non-admin users get "Access is denied" when the router tries to start.
# The ACL is derived from the actual config on disk (routerHost/routerPort) rather than
# hardcoded, so it reflects whatever -Local / manual edits have set — it must never grant
# broader access than the config's own trustedNetworks intends. netsh urlacl itself only
# controls which Windows *users* may bind/register the prefix, not which remote *network*
# clients may connect — that network-level restriction is enforced in-process by
# DraftPairApp against global.trustedNetworks (see TrustedNetworkList). So "user=Everyone"
# here is about allowing any local Windows account to run the service, not about opening the
# router to the network — network exposure is still gated entirely by RouterHost (does it
# bind loopback-only or all interfaces) and trustedNetworks.
Write-Step "Registering URL ACL for router"

$routerHost = "localhost"
$routerPort = 11435
if (Test-Path $ConfigJsonPath) {
    try {
        $existingConfig = Get-Content $ConfigJsonPath -Raw | ConvertFrom-Json
        if ($existingConfig.global.routerHost) { $routerHost = $existingConfig.global.routerHost }
        if ($existingConfig.global.routerPort) { $routerPort = $existingConfig.global.routerPort }
    } catch {
        Write-Warn "Could not read $ConfigJsonPath to determine routerHost/routerPort — defaulting to localhost:11435"
    }
}

# HttpListener prefixes use "+" or "*" for all-interfaces; netsh urlacl uses the same syntax.
$urlAclHost = $routerHost
$urlAclUrl = "http://${urlAclHost}:${routerPort}/"

if ($routerHost -notin @("localhost", "127.0.0.1", "+", "*")) {
    Write-Warn "routerHost '$routerHost' is not localhost/+/* — registering URL ACL for it as configured, but double-check global.trustedNetworks restricts who can actually reach it."
}

try {
    $aclOutput = netsh http show urlacl url=$urlAclUrl 2>&1
    if ($aclOutput -match "Reserved URL") {
        Write-Ok "URL ACL already registered for $urlAclUrl"
    } else {
        & netsh http add urlacl url=$urlAclUrl user=Everyone
        if ($LASTEXITCODE -eq 0) {
            Write-Ok "Registered URL ACL for $urlAclUrl (user=Everyone controls which Windows accounts may bind this prefix, not network access — see global.trustedNetworks for that)"
        } else {
            Write-Warn "Failed to register URL ACL (exit $LASTEXITCODE) — router may need admin on each run"
        }
    }
} catch {
    Write-Warn "netsh failed: $_ — router may need admin on each run"
}

# ── 6. Register / update the Windows service ──────────────────────────────────

if ($isUpgrade) {
    Write-Step "Upgrade detected — binaries replaced, service registration unchanged"
    Write-Ok "Skipped sc.exe config (existing registration preserved)"
} elseif ($svc) {
    # Service exists but exe wasn't there before — update binpath to current location
    Write-Step "Registering Windows service"
    & sc.exe config $ServiceName binpath= "`"$ExePath`" --service" start= auto | Out-Null
    Write-Ok "Updated existing service binpath"
} else {
    Write-Step "Registering Windows service"
    & sc.exe create $ServiceName binpath= "`"$ExePath`" --service" start= auto DisplayName= "SimpleModelServer Server"
    if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed (exit $LASTEXITCODE)" }
    & sc.exe description $ServiceName "Manages the local Ollama instance and exposes a draft-pair router for AI coding tools."
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000
    Write-Ok "Service registered"
}

# Ensure EventLog source exists so polling tools can read SimpleModelServer logs
Ensure-EventSource 'SimpleModelServer' 'Application'

# ── 7. Ensure service is running ───────────────────────────────────────────────
# Note: the SCM's own "Running" status now flips almost immediately after Start-Service,
# because SimpleModelServerWorker reports started before doing any Ollama/model/router bring-up
# (that work happens on a detached background task — see SimpleModelServerWorker.cs). So SCM
# "Running" no longer means the router is actually up yet; poll /health separately below
# to give real signal instead of a false "OK" the moment SCM flips its own status.

Write-Step "Starting service"

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc.Status -ne 'Running') {
    Start-Service -Name $ServiceName
    $svc.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
}
Write-Ok "Service is running (SCM status)"

Write-Step "Waiting for router to become healthy"
# $routerPort was already resolved from the config file in the URL ACL step above.
$healthy = $false
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:$routerPort/health" -UseBasicParsing -TimeoutSec 3
        if ($resp.StatusCode -eq 200) { $healthy = $true; break }
    } catch {
        # Not up yet — keep polling until $deadline.
    }
    Start-Sleep -Seconds 2
}

if ($healthy) {
    Write-Ok "Router responded healthy on port $routerPort"
} else {
    Write-Warn "Router did not respond healthy within 120s. The service process is running, but Ollama/model bring-up may still be in progress or may have failed — check the Application event log (source 'SimpleModelServer') for details."
}

# ── Done ───────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "SimpleModelServer.Loader installed and running." -ForegroundColor Green
Write-Host ""
Write-Host "  Install dir  : $InstallDir"
Write-Host "  Config dir   : $ConfigDir"
Write-Host "  Service name : $ServiceName"
Write-Host "  Exe          : $ExePath"
Write-Host ""
Write-Host "Manage with:" -ForegroundColor DarkGray
Write-Host "  Get-Service $ServiceName"
Write-Host "  Stop-Service $ServiceName"
Write-Host "  Start-Service $ServiceName"
Write-Host "  .\uninstall-draftmodel-service.ps1"
Write-Host ""
Read-Host "Press Enter to close"
