#Requires -Version 7.0
<#
.SYNOPSIS
    Support script invoked by SimpleModelServer.Setup's MSI custom actions (Service.wxs).

.DESCRIPTION
    Packaged into the MSI as a file resource (see Product.wxs InstallSupportComponent) and run
    via `powershell.exe -NoProfile -ExecutionPolicy Bypass -File install-support.ps1 -Action <name>`
    from deferred CustomActions in Service.wxs. Kept as a real .ps1 file rather than inline
    WiX/PowerShell embedded in a WXS attribute value specifically to avoid nested-quote
    escaping bugs that are very hard to catch without an actual WiX build+install round trip —
    this file can be tested standalone with `pwsh -File install-support.ps1 -Action ConfigWrite`
    before ever being wrapped in a CustomAction.

    Mirrors build/scripts/install-draftmodel-service.ps1 steps 5/5a/5b/5c. See
    SimpleModelServer/SimpleModelServer.Loader/INSTALLER_REQUIREMENTS.md for the full spec these actions
    implement.

.PARAMETER Action
    ConfigWrite  - create %ProgramData%\SimpleModelServer and write a stock SimpleModelServer.config.json
                   only if one does not already exist. Never overwrites an existing config
                   (matches DefaultConfig.EnsureConfig's own contract).
    UrlAcl       - register (if not already registered) the netsh http URL ACL for the
                   router's HttpListener prefix, derived from whatever routerHost/routerPort
                   is in SimpleModelServer.config.json (falls back to localhost:11435 if the config
                   or those fields are missing).
    OllamaCheck  - warn (via the Windows Application event log, source 'SimpleModelServer') if the
                   `ollama` CLI/service isn't found on PATH. SimpleModelServer.Loader hard-depends on
                   it — see DraftPairApp.RunAsync's CommandExists("ollama") check — but an MSI
                   custom action has no console, so Write-Host/Write-Warning here are invisible
                   to whoever is running the installer. The event log is the one place both
                   this installer and install-draftmodel-service.ps1 already point operators at
                   for post-install diagnosis (see INSTALLER_REQUIREMENTS.md). Never blocks
                   install: Ollama can legitimately be installed after this MSI finishes, and
                   the service will pick it up on its next health-recovery cycle.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("ConfigWrite", "UrlAcl", "OllamaCheck")]
    [string] $Action
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ConfigDir  = Join-Path $env:ProgramData "SimpleModelServer"
$ConfigPath = Join-Path $ConfigDir "SimpleModelServer.config.json"

function Invoke-ConfigWrite {
    New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null

    if (Test-Path $ConfigPath) {
        Write-Host "SimpleModelServer.config.json already exists at $ConfigPath — leaving it untouched."
        return
    }

    # Minimal stock config sufficient for SimpleModelServer.Loader's own DefaultConfig.EnsureConfig
    # to leave alone on first run (matches the shape install-draftmodel-service.ps1's -Local
    # branch writes, minus trustedNetworks since this MSI doesn't currently offer a -Local
    # equivalent switch — see INSTALLER_REQUIREMENTS.md).
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
            trustedNetworks  = @()
        }
    }
    $stockConfig | ConvertTo-Json -Depth 10 | Set-Content $ConfigPath
    Write-Host "Wrote stock SimpleModelServer.config.json to $ConfigPath"
}

function Invoke-UrlAcl {
    $routerHost = "localhost"
    $routerPort = 11435

    if (Test-Path $ConfigPath) {
        try {
            $existingConfig = Get-Content $ConfigPath -Raw | ConvertFrom-Json
            if ($existingConfig.global.routerHost) { $routerHost = $existingConfig.global.routerHost }
            if ($existingConfig.global.routerPort) { $routerPort = $existingConfig.global.routerPort }
        } catch {
            Write-Warning "Could not read $ConfigPath — defaulting URL ACL to localhost:11435"
        }
    }

    $urlAclUrl = "http://${routerHost}:${routerPort}/"

    try {
        $aclOutput = netsh http show urlacl url=$urlAclUrl 2>&1
        if ($aclOutput -match "Reserved URL") {
            Write-Host "URL ACL already registered for $urlAclUrl"
        } else {
            & netsh http add urlacl url=$urlAclUrl user=Everyone
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Registered URL ACL for $urlAclUrl"
            } else {
                Write-Warning "Failed to register URL ACL (exit $LASTEXITCODE) — router may need admin on each run"
            }
        }
    } catch {
        Write-Warning "netsh failed: $_ — router may need admin on each run"
    }
}

function Invoke-OllamaCheck {
    if (Get-Command ollama -ErrorAction SilentlyContinue) {
        Write-Host "ollama found on PATH."
        return
    }

    $message = "SimpleModelServer.Loader was installed, but 'ollama' was not found on PATH. " +
        "The router will not become healthy until Ollama is installed (https://ollama.com/download) " +
        "and the SimpleModelServerLoader service is restarted."

    Write-Warning $message

    # Best-effort: also write to the Application event log under the 'SimpleModelServer' source, since
    # a silent MSI custom action has no console the person running the installer will see.
    # install-draftmodel-service.ps1 registers this same source (Ensure-EventSource); the MSI
    # install order here isn't guaranteed to have created it yet on a from-scratch install, so
    # this creates it too if needed rather than assuming it already exists.
    try {
        if (-not [System.Diagnostics.EventLog]::SourceExists('SimpleModelServer')) {
            New-EventLog -Source 'SimpleModelServer' -LogName 'Application'
        }
        Write-EventLog -LogName 'Application' -Source 'SimpleModelServer' -EventId 1 -EntryType Warning -Message $message
    } catch {
        # Event log write is a nice-to-have here, not load-bearing — the Write-Warning above
        # (visible in msiexec /l*v verbose logs) is the fallback if this fails for any reason
        # (e.g. non-admin context, though deferred custom actions in this project always run
        # elevated — see Service.wxs's Impersonate="no").
    }
}

switch ($Action) {
    "ConfigWrite"  { Invoke-ConfigWrite }
    "UrlAcl"       { Invoke-UrlAcl }
    "OllamaCheck"  { Invoke-OllamaCheck }
}
