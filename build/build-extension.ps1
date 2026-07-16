#Requires -Version 7.0
<#
.SYNOPSIS
Build and package the SimpleModelServer VS Extension VSIX.

.DESCRIPTION
Automates the complete build pipeline for the VS Extension including:
- Cleaning previous builds
- Restoring NuGet packages
- Building the project in specified configuration
- Generating VSIX package
- Creating checksums for integrity verification
- Generating build reports

.PARAMETER Configuration
Build configuration: 'Release' (default) or 'Debug'

.PARAMETER Version
Override semantic version (e.g., 1.0.1). Uses version.props value if not specified.

.PARAMETER OutputDirectory
Output directory for built artifacts. Defaults to build-output/

.PARAMETER Platform
Target platform: 'AnyCPU' (default), 'x64', or 'ARM64'

.PARAMETER SignVsix
Enable VSIX signing (requires certificate)

.PARAMETER CreateChecksum
Create SHA256 checksums for artifacts (default: $true)

.PARAMETER Verbose
Enable verbose output

.EXAMPLE
.\build-extension.ps1 -Configuration Release
Build Release VSIX package with default settings

.EXAMPLE
.\build-extension.ps1 -Configuration Release -Version 1.2.3 -OutputDirectory "C:\output"
Build Release VSIX with custom version and output directory

.EXAMPLE
.\build-extension.ps1 -Configuration Release -SignVsix -CreateChecksum
Build Release VSIX with signing and checksum generation
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Version = '',

    [string]$OutputDirectory = 'build-output',

    [ValidateSet('AnyCPU', 'x64', 'ARM64')]
    [string]$Platform = 'AnyCPU',

    [switch]$SignVsix,

    [switch]$CreateChecksum = $true,

    [switch]$Verbose
)

# Set ErrorActionPreference for proper error handling
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

# Colors for output
$colors = @{
    Success = 'Green'
    Error   = 'Red'
    Warning = 'Yellow'
    Info    = 'Cyan'
    Section = 'Magenta'
}

function Write-Section {
    param([string]$Text)
    Write-Host "================================" -ForegroundColor $colors.Section
    Write-Host $Text -ForegroundColor $colors.Section
    Write-Host "================================" -ForegroundColor $colors.Section
}

function Write-Success {
    param([string]$Text)
    Write-Host "[SUCCESS] $Text" -ForegroundColor $colors.Success
}

function Write-Error-Custom {
    param([string]$Text)
    Write-Host "[ERROR] $Text" -ForegroundColor $colors.Error
}

function Write-Info {
    param([string]$Text)
    Write-Host "[INFO] $Text" -ForegroundColor $colors.Info
}

function Write-Warning-Custom {
    param([string]$Text)
    Write-Host "[WARNING] $Text" -ForegroundColor $colors.Warning
}

# Initialize paths
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$vsExtensionPath = Join-Path $projectRoot "src\Extensions\SimpleModelServer.Extensions.VS"
$vsExtensionCsproj = Join-Path $vsExtensionPath "SimpleModelServer.Extensions.VS.csproj"

# Validate project exists
if (-not (Test-Path $vsExtensionCsproj)) {
    Write-Error-Custom "VS Extension project not found: $vsExtensionCsproj"
    exit 1
}

# Create output directory
if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Write-Info "Created output directory: $OutputDirectory"
}

# Create configuration-specific output folder
$configOutputDir = Join-Path $OutputDirectory $Configuration
if (-not (Test-Path $configOutputDir)) {
    New-Item -ItemType Directory -Path $configOutputDir -Force | Out-Null
}

Write-Section "SimpleModelServer VS Extension Build"

# Display build parameters
Write-Info "Configuration: $Configuration"
Write-Info "Platform: $Platform"
Write-Info "Output Directory: $(Resolve-Path $OutputDirectory)"
if ($Version) { Write-Info "Version Override: $Version" }

# Step 1: Clean previous builds
Write-Section "Step 1: Cleaning Previous Builds"
try {
    $binPath = Join-Path $vsExtensionPath "bin\$Configuration"
    $objPath = Join-Path $vsExtensionPath "obj\$Configuration"

    if (Test-Path $binPath) {
        Remove-Item -Path $binPath -Recurse -Force
        Write-Success "Cleaned bin directory"
    }
    if (Test-Path $objPath) {
        Remove-Item -Path $objPath -Recurse -Force
        Write-Success "Cleaned obj directory"
    }
}
catch {
    Write-Error-Custom "Failed to clean directories: $_"
    exit 1
}

# Step 2: Restore NuGet packages
Write-Section "Step 2: Restoring NuGet Packages"
try {
    Write-Info "Restoring project packages"
    dotnet restore $vsExtensionCsproj
    Write-Success "Project packages restored"
}
catch {
    Write-Error-Custom "Failed to restore packages: $_"
    exit 1
}

# Step 3: Build the project
Write-Section "Step 3: Building VS Extension"
try {
    $buildArgs = @(
        "build",
        $vsExtensionCsproj,
        "--configuration", $Configuration,
        "--no-restore",
        "-p:Platform=$Platform"
    )

    if ($Version) {
        $buildArgs += "-p:Version=$Version"
    }

    if ($Verbose) {
        $buildArgs += "--verbosity", "detailed"
    }

    Write-Info "Building: dotnet $($buildArgs -join ' ')"
    dotnet @buildArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Error-Custom "Build failed with exit code $LASTEXITCODE"
        exit 1
    }

    Write-Success "Build completed successfully"
}
catch {
    Write-Error-Custom "Build failed: $_"
    exit 1
}

# Step 4: Locate and verify VSIX package
Write-Section "Step 4: Locating VSIX Package"
try {
    $vsixPath = Get-ChildItem -Path (Join-Path $vsExtensionPath "bin\$Configuration") -Filter "*.vsix" -ErrorAction SilentlyContinue | Select-Object -First 1

    if (-not $vsixPath) {
        Write-Error-Custom "No VSIX file found in bin output directory"
        exit 1
    }

    Write-Success "Found VSIX: $($vsixPath.Name)"
    Write-Info "Size: $([Math]::Round($vsixPath.Length / 1MB, 2)) MB"
}
catch {
    Write-Error-Custom "Failed to locate VSIX: $_"
    exit 1
}

# Step 5: Copy artifacts to output directory
Write-Section "Step 5: Copying Artifacts"
try {
    $destinationPath = Join-Path $configOutputDir $vsixPath.Name
    Copy-Item -Path $vsixPath.FullName -Destination $destinationPath -Force
    Write-Success "Copied VSIX to: $destinationPath"

    # Copy XML documentation if available
    $xmlPath = $vsixPath.FullName -replace '\.vsix$', '.xml'
    if (Test-Path $xmlPath) {
        Copy-Item -Path $xmlPath -Destination (Join-Path $configOutputDir ($vsixPath.BaseName + '.xml')) -Force
        Write-Success "Copied documentation XML"
    }
}
catch {
    Write-Error-Custom "Failed to copy artifacts: $_"
    exit 1
}

# Step 6: Create checksums
if ($CreateChecksum) {
    Write-Section "Step 6: Creating Checksums"
    try {
        $vsixForChecksum = Join-Path $configOutputDir $vsixPath.Name
        $checksumFile = $vsixForChecksum -replace '\.vsix$', '.vsix.sha256'

        # Create SHA256 checksum
        $hash = (Get-FileHash -Path $vsixForChecksum -Algorithm SHA256).Hash
        "$hash  $($vsixPath.Name)" | Out-File -FilePath $checksumFile -Encoding UTF8

        Write-Success "SHA256: $hash"
        Write-Info "Checksum file: $checksumFile"
    }
    catch {
        Write-Error-Custom "Failed to create checksums: $_"
        # Don't exit, as this is non-critical
    }
}

# Step 7: Generate build report
Write-Section "Step 7: Generating Build Report"
try {
    $reportPath = Join-Path $configOutputDir "BUILD_REPORT.txt"
    $report = @"
SimpleModelServer VS Extension Build Report
============================================
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

Build Configuration
-------------------
Configuration:     $Configuration
Platform:          $Platform
Version:           $(if ($Version) { $Version } else { "From version.props" })
Output Directory:  $(Resolve-Path $configOutputDir)

Artifacts
---------
VSIX File:         $($vsixPath.Name)
VSIX Path:         $destinationPath
VSIX Size:         $([Math]::Round($vsixPath.Length / 1MB, 2)) MB
Build Time:        $(Get-Date)

Build Command
-------------
dotnet build $vsExtensionCsproj --configuration $Configuration --platform $Platform

Verification
------------
To verify the VSIX package:
1. Install Visual Studio 2022 (17.14 or later)
2. Run: vsix-install.exe $destinationPath

Documentation
--------------
For more information, see: $projectRoot\src\Extensions\SimpleModelServer.Extensions.VS\README.md

Release Notes
-------------
See: https://github.com/rsperry79/SimpleModelServer/releases
"@

    $report | Out-File -FilePath $reportPath -Encoding UTF8
    Write-Success "Report generated: $reportPath"
}
catch {
    Write-Warning-Custom "Failed to generate report: $_"
}

# Step 8: Summary
Write-Section "Build Complete"
Write-Success "VSIX package successfully created!"
Write-Info "Location: $(Resolve-Path $configOutputDir)"
Write-Info ""
Write-Info "Quick verification: dotnet build $vsExtensionCsproj --configuration $Configuration"
Write-Info ""
Write-Info "To install the extension:"
Write-Info "  1. Open Visual Studio 2022 (17.14+)"
Write-Info "  2. Extensions > Manage Extensions > Install from File"
Write-Info "  3. Select: $destinationPath"

exit 0
