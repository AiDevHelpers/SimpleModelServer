#Requires -Version 7.0
<#
.SYNOPSIS
    Stops and removes the SimpleModelServerLoader Windows service.

.DESCRIPTION
    Stops the SimpleModelServerLoader service, deletes the SCM registration, and
    optionally removes the installation directory.

.PARAMETER InstallDir
    Installation directory to remove. Defaults to C:\Program Files\SimpleModelServer.
    Pass -RemoveFiles to actually delete files (not done by default).

.PARAMETER RemoveFiles
    Also delete the installation directory after unregistering the service.

.EXAMPLE
    .\uninstall-draftmodel-service.ps1
    .\uninstall-draftmodel-service.ps1 -RemoveFiles
    .\uninstall-draftmodel-service.ps1 -RemoveFiles -InstallDir "D:\SimpleModelServer"
#>
[CmdletBinding()]
param(
    [string] $InstallDir  = "$env:ProgramFiles\SimpleModelServer",
    [switch] $RemoveFiles
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

$ServiceName = "SimpleModelServerLoader"

function Write-Step([string]$msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "    WARN $msg" -ForegroundColor Yellow }

# ── 1. Stop service ────────────────────────────────────────────────────────────

Write-Step "Stopping $ServiceName"

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Warn "Service '$ServiceName' not found — nothing to stop"
} elseif ($svc.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName -Force
    $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    Write-Ok "Service stopped"
} else {
    Write-Ok "Service already stopped"
}

# ── 2. Delete service registration ────────────────────────────────────────────

Write-Step "Removing service registration"

if ($svc) {
    & sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe delete failed (exit $LASTEXITCODE)"
    }
    Write-Ok "Service '$ServiceName' deleted"
} else {
    Write-Warn "No service to delete"
}

# ── 3. Optionally remove files ─────────────────────────────────────────────────

if ($RemoveFiles) {
    Write-Step "Removing installation directory"
    if (Test-Path $InstallDir) {
        Remove-Item $InstallDir -Recurse -Force
        Write-Ok "Removed $InstallDir"
    } else {
        Write-Warn "$InstallDir does not exist — nothing to remove"
    }
} else {
    Write-Host ""
    Write-Warn "Files left in place at $InstallDir"
    Write-Host "    Run with -RemoveFiles to delete them." -ForegroundColor DarkGray
}

# ── Done ───────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "SimpleModelServerLoader uninstalled." -ForegroundColor Green
Write-Host ""
Read-Host "Press Enter to close"
