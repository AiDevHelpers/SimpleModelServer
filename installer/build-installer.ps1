#Requires -Version 7
<#
.SYNOPSIS
    Builds the SimpleModelServer Inno Setup installer: publishes every payload, compiles
    SimpleModelServer.iss with Inno Setup 6, and writes a SHA-256 checksum next to it.

.DESCRIPTION
    Output: build\dist\installer\SimpleModelServer-Setup-<version>.exe and a matching
    .sha256 file. CI calls this script; it also works locally.

    Version resolution: -Version when given (a leading "v" is stripped, so a tag like v1.2.3
    works), otherwise Nerdbank.GitVersioning's SimpleVersion (requires `dotnet nbgv`).

.PARAMETER Version
    Display version, e.g. 1.2.3 or 1.2.3-rc1. Numeric version for the EXE resource is derived
    from its first three numeric parts.

.PARAMETER Configuration
    Build configuration for the publishes. Default Release.

.PARAMETER SkipChat
    Leave the chat app out of the installer (it is an optional component).

.PARAMETER SkipVsExtension
    Leave the Visual Studio extension out. Needs the VS SDK on the machine when included.

.PARAMETER SkipVsCodeExtension
    Leave the VS Code extension out. Needs Node.js when included.
#>
[CmdletBinding()]
param(
    [string] $Version = "",
    [string] $Configuration = "Release",
    [switch] $SkipChat,
    [switch] $SkipVsExtension,
    [switch] $SkipVsCodeExtension
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot   = Split-Path -Parent $PSScriptRoot
$staging    = Join-Path $repoRoot "build\staging\installer"
$distDir    = Join-Path $repoRoot "build\dist\installer"
$serverProj = Join-Path $repoRoot "src\Server\SimpleModelServer.Server\SimpleModelServer.Server.csproj"
$chatProj   = Join-Path $repoRoot "src\Clients\SimpleModelServer.Chat.Maui\SimpleModelServer.Chat.Maui.csproj"
$vsProj     = Join-Path $repoRoot "src\Extensions\SimpleModelServer.Extensions.VS\SimpleModelServer.Extensions.VS.csproj"
$vsCodeDir  = Join-Path $repoRoot "src\Extensions\SimpleModelServer.Extensions.VSCode"
$issScript  = Join-Path $PSScriptRoot "SimpleModelServer.iss"

function Invoke-Native([string] $File, [string[]] $Arguments) {
    Write-Host ">> $File $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$File exited with code $LASTEXITCODE"
    }
}

function Resolve-DisplayVersion {
    if ($Version -ne "") {
        return $Version -replace '^v', ''
    }
    $nbgv = & dotnet nbgv get-version -v SimpleVersion 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($nbgv)) {
        throw "No -Version given and 'dotnet nbgv get-version' failed. Install nbgv (dotnet tool install -g nbgv) or pass -Version."
    }
    return $nbgv.Trim()
}

function Resolve-NumericVersion([string] $display) {
    # 1.2.3-rc1 -> 1.2.3.0 ; 1.2 -> 1.2.0.0. Inno's VersionInfoVersion needs four numeric parts.
    if ($display -notmatch '^(\d+)\.(\d+)(?:\.(\d+))?') {
        throw "Version '$display' does not start with Major.Minor[.Patch]."
    }
    $patch = if ($Matches[3]) { $Matches[3] } else { "0" }
    return "$($Matches[1]).$($Matches[2]).$patch.0"
}

function Find-Iscc {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw "ISCC.exe not found. Install Inno Setup 6 (choco install innosetup, or https://jrsoftware.org/isdl.php)."
}

$displayVersion = Resolve-DisplayVersion
$numericVersion = Resolve-NumericVersion $displayVersion
Write-Host "SimpleModelServer installer $displayVersion (numeric $numericVersion), $Configuration" -ForegroundColor Cyan

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging, $distDir | Out-Null

# ── Router / service payload (required) ──────────────────────────────────────────────
$serverDir = Join-Path $staging "server"
Invoke-Native dotnet @("publish", $serverProj, "-c", $Configuration, "-r", "win-x64",
    "--self-contained", "true", "-o", $serverDir)

$defines = @(
    "/DMyAppVersion=$displayVersion",
    "/DMyAppNumericVersion=$numericVersion",
    "/DServerDir=$serverDir",
    "/DRepoRoot=$repoRoot",
    "/DOutputDir=$distDir"
)

# ── Optional components ──────────────────────────────────────────────────────────────
if (-not $SkipChat) {
    $chatDir = Join-Path $staging "chat"
    Invoke-Native dotnet @("publish", $chatProj, "-f", "net10.0-windows10.0.19041.0", "-c", $Configuration,
        "-r", "win-x64", "--self-contained", "true", "-o", $chatDir)
    $defines += @("/DHasChat", "/DChatDir=$chatDir")
}

if (-not $SkipVsExtension) {
    Invoke-Native dotnet @("build", $vsProj, "-c", $Configuration)
    $vsix = Get-ChildItem (Join-Path $repoRoot "src\Extensions\SimpleModelServer.Extensions.VS\bin\$Configuration") `
        -Recurse -Filter *.vsix -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $vsix) { throw "VS extension build produced no .vsix under bin\$Configuration." }
    $defines += @("/DHasVsExtension", "/DVsExtensionVsix=$($vsix.FullName)")
}

if (-not $SkipVsCodeExtension) {
    Push-Location $vsCodeDir
    try {
        if (-not (Test-Path "node_modules")) { Invoke-Native npm @("ci") }
        Invoke-Native npm @("run", "package")
    } finally {
        Pop-Location
    }
    $vscodeVsix = Join-Path $repoRoot "build\staging\SimpleModelServer.Extensions.VSCode\simplemodelserver-vscode.vsix"
    if (-not (Test-Path $vscodeVsix)) { throw "VS Code packaging produced no .vsix at $vscodeVsix." }
    $defines += @("/DHasVsCodeExtension", "/DVsCodeExtensionVsix=$vscodeVsix")
}

# ── Compile ──────────────────────────────────────────────────────────────────────────
$iscc = Find-Iscc
Invoke-Native $iscc (@("/Q") + $defines + @($issScript))

# The script names its output SimpleModelServer-Setup-<MyAppVersion>.exe via OutputBaseFilename.
$installer = Join-Path $distDir "SimpleModelServer-Setup-$displayVersion.exe"
if (-not (Test-Path $installer)) { throw "Expected installer not found: $installer" }

$hash = (Get-FileHash -Path $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$installer.sha256"
"$hash  $(Split-Path -Leaf $installer)" | Set-Content -Path $checksumPath -Encoding ascii

Write-Host ""
Write-Host "Installer : $installer" -ForegroundColor Green
Write-Host "SHA-256   : $hash" -ForegroundColor Green
Write-Host "Checksum  : $checksumPath" -ForegroundColor Green
