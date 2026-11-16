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

.PARAMETER SkipTokenSaver
    Leave the TokenSaver MCP component out. Use this when the TokenSaver submodule is not
    available (for example on fork PRs, which have no secrets to check it out).
#>
[CmdletBinding()]
param(
    [string] $Version = "",
    [string] $Configuration = "Release",
    [switch] $SkipChat,
    [switch] $SkipVsExtension,
    [switch] $SkipVsCodeExtension,
    [switch] $SkipTokenSaver
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
    # nbgv is a global tool, so it runs as `nbgv`. `dotnet nbgv` only works for local tools.
    if (-not (Get-Command nbgv -ErrorAction SilentlyContinue)) {
        throw "No -Version given and nbgv is not installed. Run: dotnet tool install --global nbgv, or pass -Version."
    }
    # NuGetPackageVersion matches the release tag CI creates (v<NuGetPackageVersion>).
    $nbgvOutput = & nbgv get-version -v NuGetPackageVersion 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "nbgv get-version failed (exit $LASTEXITCODE): $nbgvOutput"
    }
    return "$nbgvOutput".Trim()
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
    # Also published next to the installer, so the release carries the standalone extension.
    $vsOut = Join-Path $distDir "SimpleModelServer.Extensions.VS-$displayVersion.vsix"
    Copy-Item $vsix.FullName $vsOut -Force
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
    Copy-Item $vscodeVsix (Join-Path $distDir "SimpleModelServer.Extensions.VSCode-$displayVersion.vsix") -Force
    $defines += @("/DHasVsCodeExtension", "/DVsCodeExtensionVsix=$vscodeVsix")
}

if (-not $SkipTokenSaver) {
    # TokenSaver is checked out next to this repo (CI: ../TokenSaver), not inside it. Inside this
    # repo, our root Directory.Packages.props (central package management) would apply to its
    # projects and break its publish step (NU1008). TOKENSAVER_DIR overrides the location.
    $tsDir = if ($env:TOKENSAVER_DIR) { $env:TOKENSAVER_DIR } else { Join-Path (Split-Path -Parent $repoRoot) "TokenSaver" }
    $tsWix = Join-Path $tsDir "src\Installers\TokenSaver.Setup\TokenSaver.Setup.wixproj"
    if (-not (Test-Path $tsWix)) {
        throw "TokenSaver not found at $tsDir. Check out rsperry79/TokenSaver at the commit in installer/tokensaver-ref.txt, set TOKENSAVER_DIR, or pass -SkipTokenSaver."
    }
    # The MSI product version must be numeric (no pre-release suffix): 1.2.3.0 -> 1.2.3.
    $msiVersion = $numericVersion -replace '\.0$', ''
    Invoke-Native dotnet @("build", $tsWix, "-c", $Configuration, "-p:MyAppVersion=$msiVersion")
    $tsMsi = Join-Path $tsDir "build\dist\TokenSaver-Setup-$msiVersion.msi"
    if (-not (Test-Path $tsMsi)) { throw "TokenSaver MSI not found at $tsMsi." }
    $tsStage = Join-Path $staging "tokensaver"
    New-Item -ItemType Directory -Force $tsStage | Out-Null
    $tsCopy = Join-Path $tsStage "TokenSaver-Setup.msi"
    Copy-Item $tsMsi $tsCopy -Force
    $defines += @("/DHasTokenSaver", "/DTokenSaverMsi=$tsCopy")
}

# ── Compile ──────────────────────────────────────────────────────────────────────────
$iscc = Find-Iscc
Invoke-Native $iscc (@("/Q") + $defines + @($issScript))

# The script names its output SimpleModelServer-Setup-<MyAppVersion>.exe via OutputBaseFilename.
$installer = Join-Path $distDir "SimpleModelServer-Setup-$displayVersion.exe"
if (-not (Test-Path $installer)) { throw "Expected installer not found: $installer" }

# One SHA256SUMS.txt covers every release asset for this version (installer and any .vsix).
$assets = Get-ChildItem $distDir -File |
    Where-Object { $_.Name -like "*-$displayVersion.*" -and $_.Extension -in '.exe', '.vsix' } |
    Sort-Object Name
$sums = foreach ($asset in $assets) {
    "$((Get-FileHash -Path $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($asset.Name)"
}
$sumsPath = Join-Path $distDir "SHA256SUMS.txt"
$sums | Set-Content -Path $sumsPath -Encoding ascii

Write-Host ""
Write-Host "Assets:" -ForegroundColor Green
$sums | ForEach-Object { Write-Host "  $_" }
Write-Host "Checksums: $sumsPath" -ForegroundColor Green
