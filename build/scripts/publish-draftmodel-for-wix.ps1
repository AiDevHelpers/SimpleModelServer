#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes SimpleModelServer.Loader self-contained into build/staging/SimpleModelServer.Loader/<Configuration>, ready for
    build/wix/SimpleModelServer.Setup/SimpleModelServer.Setup.wixproj to package.

.DESCRIPTION
    This is the "build" half of the SimpleModelServer installer story; the WiX project is the
    "package" half. Sibling of build/scripts/publish-tokensaver-for-wix.ps1 — kept separate
    from the wixproj itself for the same reason: a real PowerShell script gives better error
    output if `dotnet publish` fails than trying to invoke it as an MSBuild pre-step.

    This does NOT replace build/scripts/install-draftmodel-service.ps1, which remains a fully
    supported, directly-run install path (and is what SimpleModelServer.Loader.csproj's
    RunInstaller=true MSBuild hook invokes). This script instead produces
    build/dist/<configuration>/SimpleModelServer-Setup-<Version>.msi as an alternative, double-clickable installer
    packaging of the same service, built from the same publish output.

.PARAMETER Version
    Version string baked into the installer via -p:MyAppVersion. Defaults to the version
    Nerdbank.GitVersioning computes from version.json + git height (see docs/versioning.md);
    falls back to "1.0.0" if the `nbgv` tool isn't installed or git metadata isn't available
    (e.g. a shallow checkout). Pass -Version explicitly to override either default.

.PARAMETER Configuration
    dotnet build configuration. Defaults to "Release".

.PARAMETER Runtime
    .NET runtime identifier. Defaults to "win-x64".

.PARAMETER Pack
    After publishing, also invoke `dotnet build` on the WiX project to produce
    build/dist/SimpleModelServer-Setup-<Version>.msi. Requires the WiX .NET tool
    (`dotnet tool install --global wix`) — the wixproj itself only needs the WiX NuGet SDK,
    restored automatically on build.

.EXAMPLE
    .\publish-draftmodel-for-wix.ps1
    .\publish-draftmodel-for-wix.ps1 -Version 1.2.0 -Pack
#>
[CmdletBinding()]
param(
    [string] $Version       = "",
    [string] $Configuration = "Release",
    [string] $Runtime       = "win-x64",
    [switch] $Pack
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot   = $PSScriptRoot | Split-Path -Parent | Split-Path -Parent
$CsProj     = Join-Path $RepoRoot "src\Server\SimpleModelServer.Server\SimpleModelServer.Server.csproj"
$StageDir   = Join-Path $RepoRoot "build\staging\SimpleModelServer.Loader\$Configuration"
$WixProj    = Join-Path $RepoRoot "src\Installers\wix\SimpleModelServer.Setup\SimpleModelServer.Setup.wixproj"

function Write-Step([string]$msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "    WARN $msg" -ForegroundColor Yellow }

# ── 1. Prerequisites ───────────────────────────────────────────────────────────

Write-Step "Checking prerequisites"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install it from https://dot.net and re-run."
}
Write-Ok ".NET SDK $(dotnet --version)"

if ([string]::IsNullOrWhiteSpace($Version)) {
    if (Get-Command nbgv -ErrorAction SilentlyContinue) {
        $nbgvVersion = (nbgv get-version --project $RepoRoot --format json 2>$null | ConvertFrom-Json).SimpleVersion
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($nbgvVersion)) {
            $Version = $nbgvVersion
            Write-Ok "Version resolved via nbgv: $Version"
        } else {
            $Version = "1.0.0"
            Write-Warn "nbgv could not resolve a version (shallow clone / no git history?) — falling back to $Version"
        }
    } else {
        $Version = "1.0.0"
        Write-Warn "nbgv CLI not found (install with: dotnet tool install --global nbgv) — falling back to $Version"
    }
}

# ── 2. Publish ─────────────────────────────────────────────────────────────────

Write-Step "Publishing SimpleModelServer.Server"

if (Test-Path $StageDir) {
    Remove-Item $StageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

dotnet publish $CsProj `
    --configuration $Configuration `
    --runtime       $Runtime `
    --self-contained true `
    --output        $StageDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit $LASTEXITCODE)"
}

$exePath = Join-Path $StageDir "SimpleModelServer.Server.exe"
if (-not (Test-Path $exePath)) {
    throw "Publish succeeded but exe not found at: $exePath"
}

$size = (Get-Item $exePath).Length / 1MB
Write-Ok "SimpleModelServer.Server.exe  ($([math]::Round($size, 1)) MB)  -> $StageDir"

# ── 3. Optionally build the installer ──────────────────────────────────────────

if ($Pack) {
    Write-Step "Building installer with WiX"

    if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
        throw "wix CLI not found on PATH. Install it with: dotnet tool install --global wix"
    }

    $distDir = Join-Path $RepoRoot "build\dist\$($Configuration.ToLower())"
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null

    # WiX's light/validate step copies the built .msi to a "*_validate.msi" temp file inside
    # obj\<platform>\<config>\ for ICE validation (WIX7010 if that copy fails). A leftover
    # obj\ directory from a prior run - e.g. a build that was killed mid-validate, or an MSI
    # that's still open in Explorer's preview pane / an AV scan / a stray msiexec - can leave
    # a locked file there that collides with the next build. Clearing obj\ first means every
    # -Pack run starts from a clean slate instead of fighting a stale lock.
    $objDir = Join-Path $RepoRoot "build\wix\SimpleModelServer.Setup\obj"
    if (Test-Path $objDir) {
        Remove-Item $objDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # WIX7010 (file in use - antivirus scan, lingering msiexec, MSI open in another tool, etc.)
    # is almost always transient. Retry the build a few times with a short backoff before
    # giving up, rather than failing the whole -Pack run on what's usually a one-off lock.
    $maxAttempts = 3
    $attempt = 1
    $buildSucceeded = $false

    while ($attempt -le $maxAttempts -and -not $buildSucceeded) {
        if ($attempt -gt 1) {
            Write-Warn "WiX build attempt $($attempt - 1) failed - retrying ($attempt/$maxAttempts) after a short pause (file may be transiently locked, e.g. by antivirus or a lingering process)..."
            Start-Sleep -Seconds 5
        }

        dotnet build $WixProj -c $Configuration -p:MyAppVersion=$Version

        if ($LASTEXITCODE -eq 0) {
            $buildSucceeded = $true
        } else {
            $attempt++
        }
    }

    if (-not $buildSucceeded) {
        throw "WiX build failed after $maxAttempts attempts (exit $LASTEXITCODE). If this keeps happening, check Task Manager for a lingering msiexec.exe or dotnet.exe, make sure the .msi isn't open in Explorer's preview pane or an MSI editor (Orca, etc.), and check whether antivirus is scanning build\wix\SimpleModelServer.Setup\obj\ - you can exclude that folder from real-time scanning if it's the repeat cause."
    }

    $outFile = Join-Path $distDir "SimpleModelServer-Setup-$Version.msi"
    if (Test-Path $outFile) {
        $outSize = (Get-Item $outFile).Length / 1MB
        Write-Ok "$outFile  ($([math]::Round($outSize, 1)) MB)"
    } else {
        Write-Host "    WARN Expected output not found at $outFile — check build output above." -ForegroundColor Yellow
    }
}

# ── Done ──────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Staged build : $StageDir"
if ($Pack) {
    Write-Host "  Installer    : build\dist\$($Configuration.ToLower())\SimpleModelServer-Setup-$Version.msi"
} else {
    Write-Host "  Next step    : dotnet build build\wix\SimpleModelServer.Setup\SimpleModelServer.Setup.wixproj -p:MyAppVersion=$Version"
}
Write-Host ""
