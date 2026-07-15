<#
.SYNOPSIS
Configures TokenSaver.MCP to connect to a remote SimpleModelServer router.

.DESCRIPTION
This script automates the setup of TokenSaver.MCP to use a SimpleModelServer.Loader instance
as its backend. It:
- Tests connectivity to the remote router
- Finds or creates TokenSaver's configuration file
- Updates the OllamaUrl to point to the SimpleModelServer router
- Verifies the configuration

.PARAMETER RouterHost
The hostname or IP address of the machine running SimpleModelServer.Loader.
Default: localhost

.PARAMETER RouterPort
The port SimpleModelServer.Loader is listening on.
Default: 11435

.PARAMETER RouterModel
The model name advertised by SimpleModelServer (routerModelName).
Default: HelperModel

.PARAMETER TokenSaverPath
The installation path of TokenSaver.MCP.
Default: C:\Program Files\TokenSaver

.PARAMETER AutoRestart
If specified, attempts to restart TokenSaver.MCP after configuration.

.EXAMPLE
# Configure TokenSaver to use local SimpleModelServer
.\Setup-TokenSaver-RemoteRouter.ps1

# Configure to use a remote router on a specific machine
.\Setup-TokenSaver-RemoteRouter.ps1 -RouterHost "192.168.1.50" -RouterPort 11435

# Auto-restart TokenSaver after setup
.\Setup-TokenSaver-RemoteRouter.ps1 -RouterHost "server.local" -AutoRestart

.LINK
https://github.com/rsperry79/SimpleModelServer/docs/Server/TOKENSAVER_INTEGRATION.md
#>

param(
    [string]$RouterHost = "localhost",
    [int]$RouterPort = 11435,
    [string]$RouterModel = "HelperModel",
    [string]$TokenSaverPath = "C:\Program Files\TokenSaver",
    [switch]$AutoRestart
)

$ErrorActionPreference = "Stop"

Write-Host "🔧 Configuring TokenSaver.MCP to use SimpleModelServer router" -ForegroundColor Cyan
Write-Host "   Router: http://$RouterHost:$RouterPort`n" -ForegroundColor Gray

# Step 1: Test connectivity
Write-Host "1️⃣  Testing connectivity to router..." -ForegroundColor Cyan
try {
    $response = Invoke-WebRequest -Uri "http://$RouterHost:$RouterPort/health" `
        -ErrorAction Stop `
        -TimeoutSec 5 `
        -UseBasicParsing

    if ($response.StatusCode -eq 200) {
        Write-Host "✅ Router is reachable" -ForegroundColor Green
    } else {
        throw "Unexpected status code: $($response.StatusCode)"
    }
} catch {
    Write-Host "❌ Cannot reach router at http://$RouterHost:$RouterPort" -ForegroundColor Red
    Write-Host "   Error: $_" -ForegroundColor Red
    Write-Host "`n📋 Troubleshooting:" -ForegroundColor Yellow
    Write-Host "   • Is SimpleModelServer.Loader running?" -ForegroundColor Yellow
    Write-Host "   • Is the host/port correct?" -ForegroundColor Yellow
    Write-Host "   • Is there a firewall blocking port $RouterPort?" -ForegroundColor Yellow
    Write-Host "   • Is SimpleModelServer listening on all interfaces (routerHost: '*')?" -ForegroundColor Yellow
    exit 1
}

# Step 2: Locate config file
Write-Host "`n2️⃣  Locating TokenSaver configuration..." -ForegroundColor Cyan
$configPaths = @(
    (Join-Path $TokenSaverPath "appsettings.json"),
    (Join-Path $TokenSaverPath "tokensaver.config.json"),
    (Join-Path $env:APPDATA "TokenSaver\appsettings.json"),
    $env:TOKENSAVER_CONFIG
) | Where-Object { $_ -and -not [string]::IsNullOrWhiteSpace($_) }

$configPath = $null
foreach ($path in $configPaths) {
    if (Test-Path $path) {
        $configPath = $path
        Write-Host "✅ Found existing config at:" -ForegroundColor Green
        Write-Host "   $configPath" -ForegroundColor Gray
        break
    }
}

if (-not $configPath) {
    $configPath = Join-Path $TokenSaverPath "appsettings.json"
    Write-Host "ℹ️  No existing config found, will create:" -ForegroundColor Cyan
    Write-Host "   $configPath" -ForegroundColor Gray

    # Ensure directory exists
    $configDir = Split-Path $configPath
    if (-not (Test-Path $configDir)) {
        Write-Host "📁 Creating directory..." -ForegroundColor Gray
        New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    }
}

# Step 3: Update or create configuration
Write-Host "`n3️⃣  Updating configuration..." -ForegroundColor Cyan

# Read existing config or start fresh
$config = @{}
if (Test-Path $configPath) {
    try {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json -AsHashtable
    } catch {
        Write-Host "⚠️  Could not parse existing config, starting fresh" -ForegroundColor Yellow
        $config = @{}
    }
}

# Ensure TokenSaver section exists and update it
if (-not $config.ContainsKey("TokenSaver")) {
    $config["TokenSaver"] = @{}
}

$oldUrl = $config["TokenSaver"]["OllamaUrl"]
$oldModel = $config["TokenSaver"]["OllamaModel"]

$config["TokenSaver"]["OllamaUrl"] = "http://$($RouterHost):$RouterPort"
$config["TokenSaver"]["OllamaModel"] = $RouterModel

# Write config back
$configJson = ConvertTo-Json -InputObject $config -Depth 10
Set-Content -Path $configPath -Value $configJson -Force

Write-Host "✅ Configuration updated" -ForegroundColor Green
Write-Host "   File: $configPath" -ForegroundColor Gray
Write-Host "   OllamaUrl:   $($config.TokenSaver.OllamaUrl)" -ForegroundColor Gray
Write-Host "   OllamaModel: $($config.TokenSaver.OllamaModel)" -ForegroundColor Gray

if ($oldUrl -and $oldUrl -ne $config["TokenSaver"]["OllamaUrl"]) {
    Write-Host "`nℹ️  Changed from: $oldUrl" -ForegroundColor Gray
    if ($oldModel) {
        Write-Host "   (was using model: $oldModel)" -ForegroundColor Gray
    }
}

# Step 4: Restart TokenSaver if requested
if ($AutoRestart) {
    Write-Host "`n4️⃣  Restarting TokenSaver.MCP..." -ForegroundColor Cyan
    try {
        # Try to stop the service
        $service = Get-Service -Name "TokenSaver.MCP" -ErrorAction SilentlyContinue
        if ($service) {
            Write-Host "   Stopping service..." -ForegroundColor Gray
            Stop-Service -Name "TokenSaver.MCP" -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2

            Write-Host "   Starting service..." -ForegroundColor Gray
            Start-Service -Name "TokenSaver.MCP" -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2

            Write-Host "✅ Service restarted" -ForegroundColor Green
        } else {
            Write-Host "ℹ️  TokenSaver service not found (may be running as app, not service)" -ForegroundColor Cyan
            Write-Host "   Please restart TokenSaver or your IDE manually" -ForegroundColor Cyan
        }
    } catch {
        Write-Host "⚠️  Could not restart service automatically: $_" -ForegroundColor Yellow
        Write-Host "   Please restart TokenSaver or your IDE manually" -ForegroundColor Yellow
    }
}

# Summary
Write-Host "`n✅ Configuration complete!" -ForegroundColor Green
Write-Host "`n📋 Next steps:" -ForegroundColor Cyan
if (-not $AutoRestart) {
    Write-Host "1. Restart TokenSaver.MCP:" -ForegroundColor Cyan
    Write-Host "   • In VS Code: Ctrl+Shift+P → 'Continue: Reconnect MCP Servers'" -ForegroundColor Gray
    Write-Host "   • Or restart your IDE" -ForegroundColor Gray
    Write-Host "2. Send a test message through the chat interface" -ForegroundColor Cyan
} else {
    Write-Host "1. Send a test message through the chat interface" -ForegroundColor Cyan
}
Write-Host "2. If there are issues, check TokenSaver logs in:" -ForegroundColor Cyan
Write-Host "   $TokenSaverPath\logs\" -ForegroundColor Gray

Write-Host "`n📖 For more info, see:" -ForegroundColor Cyan
Write-Host "   docs/Server/TOKENSAVER_INTEGRATION.md" -ForegroundColor Gray
