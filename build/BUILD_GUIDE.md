# SimpleModelServer VS Extension Build Guide

This guide explains how to build, package, and distribute the SimpleModelServer Visual Studio Extension.

## Quick Start

### Windows (PowerShell)

```powershell
# Build Release VSIX
.\build\build-extension.ps1 -Configuration Release

# Build Debug VSIX
.\build\build-extension.ps1 -Configuration Debug

# Build with custom version
.\build\build-extension.ps1 -Configuration Release -Version 1.2.3

# Build with custom output directory
.\build\build-extension.ps1 -Configuration Release -OutputDirectory "C:\out"

# Build with verbose output
.\build\build-extension.ps1 -Configuration Release -Verbose
```

### Linux/macOS (Bash)

```bash
# Build Release VSIX
./build/build-extension.sh --config Release

# Build Debug VSIX
./build/build-extension.sh --config Debug

# Build with custom version
./build/build-extension.sh --config Release --version 1.2.3

# Build with checksum
./build/build-extension.sh --config Release --checksum

# Build with verbose output
./build/build-extension.sh --config Release --verbose
```

## Build Infrastructure

### Files Created

#### 1. Version Management
- **`build/version.props`** - Centralized version definition
  - Defines semantic versioning (Major.Minor.Patch)
  - Manages assembly versioning
  - Supports pre-release labels (alpha, beta, rc)
  - Can be overridden via environment variables or CLI parameters

#### 2. Publish Profiles
Located in `src/Extensions/SimpleModelServer.Extensions.VS/Properties/PublishProfiles/`

- **`Release.pubxml`** - Production build configuration
  - Optimized binary (Optimize=true)
  - No debug symbols (DebugType=none)
  - Deterministic builds enabled
  - Release-labeled VSIX package

- **`Debug.pubxml`** - Development build configuration
  - Embedded debug symbols (DebugType=embedded)
  - No optimization (Optimize=false)
  - Development version suffix
  - Faster compilation for iteration

#### 3. Project Configuration
- **`src/Extensions/SimpleModelServer.Extensions.VS/SimpleModelServer.Extensions.VS.csproj`**
  - Updated to import `version.props`
  - VSIX generation enabled
  - Proper output paths configured
  - Assembly versioning linked to version.props

#### 4. Build Scripts
- **`build/build-extension.ps1`** - PowerShell build automation
  - Cross-platform compatible path handling
  - Comprehensive error handling
  - Colored output for clarity
  - Automatic checksum generation
  - Build report generation

- **`build/build-extension.sh`** - Bash build automation
  - POSIX-compliant shell script
  - Works on Linux, macOS, WSL
  - Color-coded output
  - SHA256 checksum support
  - Logging to file

#### 5. CI/CD Integration
- **`.github/workflows/build-vs-extension.yml`** - GitHub Actions workflow
  - Automatic builds on tag creation
  - Matrix build strategy (Debug/Release)
  - Artifact upload to GitHub
  - Automatic GitHub Release creation
  - SHA256 checksum generation
  - VSIX validation

### Directory Structure

After building, the output directory structure will be:

```
build-output/
├── Release/
│   ├── SimpleModelServer.Extensions.VS.vsix      # Release VSIX package
│   ├── SimpleModelServer.Extensions.VS.vsix.sha256  # SHA256 checksum
│   ├── SimpleModelServer.Extensions.VS.xml       # Documentation XML
│   ├── BUILD_REPORT.txt                          # Build report
│   └── ...
├── Debug/
│   ├── SimpleModelServer.Extensions.VS.vsix      # Debug VSIX package
│   ├── SimpleModelServer.Extensions.VS.vsix.sha256  # SHA256 checksum
│   └── ...
└── logs/
    └── build.log                                 # Build log
```

## Versioning

### Semantic Versioning

The project uses Semantic Versioning (SemVer) format: `MAJOR.MINOR.PATCH[-PRERELEASE]`

**Example versions:**
- `1.0.0` - Initial release
- `1.0.1` - Patch release (bug fix)
- `1.1.0` - Minor release (new feature, backward compatible)
- `2.0.0` - Major release (breaking change)
- `1.0.0-alpha` - Alpha pre-release
- `1.0.0-beta.1` - Beta pre-release with build number
- `1.0.0-rc.1` - Release candidate

### Version Configuration

#### Centralized Version (Default)

Edit `build/version.props`:

```xml
<PropertyGroup>
  <VersionMajor>1</VersionMajor>
  <VersionMinor>2</VersionMinor>
  <VersionPatch>3</VersionPatch>
  <VersionPrerelease></VersionPrerelease>  <!-- Leave empty for stable releases -->
</PropertyGroup>
```

#### Override via Command Line

**PowerShell:**
```powershell
.\build\build-extension.ps1 -Version 1.2.3
```

**Bash:**
```bash
./build/build-extension.sh --version 1.2.3
```

#### Override via Environment Variable

**PowerShell:**
```powershell
$env:BUILD_NUMBER = "42"
.\build\build-extension.ps1
```

**Bash:**
```bash
export BUILD_NUMBER=42
./build/build-extension.sh
```

## Building VSIX Packages

### Local Build

#### PowerShell (Windows)

```powershell
cd path/to/SimpleModelServer

# Build Release configuration
.\build\build-extension.ps1 -Configuration Release

# The VSIX will be in: build-output/Release/SimpleModelServer.Extensions.VS.vsix
```

#### Bash (Linux/macOS/WSL)

```bash
cd path/to/SimpleModelServer

# Build Release configuration
./build/build-extension.sh --config Release

# The VSIX will be in: build-output/Release/SimpleModelServer.Extensions.VS.vsix
```

### Manual Build with dotnet CLI

```bash
# Restore packages
dotnet restore

# Build Release configuration
dotnet build src/Extensions/SimpleModelServer.Extensions.VS/SimpleModelServer.Extensions.VS.csproj `
  --configuration Release

# VSIX is generated in: src/Extensions/SimpleModelServer.Extensions.VS/bin/Release/net472/
```

### CI/CD Build (GitHub Actions)

Push a tag to trigger the workflow:

```bash
git tag v1.2.3
git push origin v1.2.3
```

The workflow will:
1. Build both Debug and Release configurations
2. Generate VSIX packages
3. Create checksums
4. Validate VSIX integrity
5. Create GitHub Release with artifacts

## Installing the Extension

### From VSIX File

1. **Visual Studio 2022:**
   - Extensions → Manage Extensions
   - Click "Install from File"
   - Navigate to and select the VSIX file
   - VS will install the extension

2. **Command Line (Windows):**
   ```powershell
   # Locate Visual Studio installation
   $vsPath = "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE"
   
   # Install VSIX
   & "$vsPath\VSIXInstaller.exe" /install build-output/Release/SimpleModelServer.Extensions.VS.vsix
   ```

3. **Command Line (PowerShell):**
   ```powershell
   # Direct installation
   VSIXInstaller.exe build-output/Release/SimpleModelServer.Extensions.VS.vsix
   ```

### Uninstalling

1. **Visual Studio 2022:**
   - Extensions → Manage Extensions
   - Search for "SimpleModelServer"
   - Click "Uninstall"
   - Restart Visual Studio

2. **Command Line:**
   ```powershell
   VSIXInstaller.exe /uninstall "SimpleModelServer.Extensions.VS.56c8b593-11c3-48bb-ba20-21e0a7226282"
   ```

## Verifying Builds

### Check VSIX Integrity

**PowerShell:**
```powershell
$vsixPath = "build-output/Release/SimpleModelServer.Extensions.VS.vsix"
Add-Type -AssemblyName System.IO.Compression
$zip = [System.IO.Compression.ZipFile]::OpenRead($vsixPath)
Write-Host "VSIX Contents:"
$zip.Entries | Format-Table Name, Length
$zip.Dispose()
```

**Bash:**
```bash
unzip -l build-output/Release/SimpleModelServer.Extensions.VS.vsix
```

### Verify Checksums

**PowerShell:**
```powershell
$vsixPath = "build-output/Release/SimpleModelServer.Extensions.VS.vsix"
$hash = (Get-FileHash -Path $vsixPath -Algorithm SHA256).Hash
Write-Host "SHA256: $hash"
```

**Bash:**
```bash
sha256sum build-output/Release/SimpleModelServer.Extensions.VS.vsix
# Or verify against checksum file
sha256sum -c build-output/Release/SimpleModelServer.Extensions.VS.vsix.sha256
```

## Build Parameters

### PowerShell Script Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-Configuration` | `Release` | Build configuration: `Debug` or `Release` |
| `-Version` | (from props) | Override semantic version |
| `-OutputDirectory` | `build-output` | Output directory for artifacts |
| `-Platform` | `AnyCPU` | Target platform: `AnyCPU`, `x64`, or `ARM64` |
| `-SignVsix` | `false` | Enable VSIX signing |
| `-CreateChecksum` | `true` | Generate SHA256 checksums |
| `-Verbose` | `false` | Enable detailed output |

### Bash Script Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-c, --config` | `Release` | Build configuration: `Debug` or `Release` |
| `-v, --version` | (from props) | Override semantic version |
| `-o, --output` | `build-output` | Output directory for artifacts |
| `-p, --platform` | `AnyCPU` | Target platform: `AnyCPU`, `x64`, or `ARM64` |
| `-s, --sign` | `false` | Enable VSIX signing |
| `-k, --checksum` | `true` | Generate SHA256 checksums |
| `--verbose` | `false` | Enable detailed output |

## Advanced Scenarios

### Build with Code Signing

To sign the VSIX package, you need:
1. A code signing certificate (.pfx file)
2. The certificate password

**Update version.props:**
```xml
<PropertyGroup>
  <SignAssembly>true</SignAssembly>
  <AssemblyOriginatorKeyFile>path/to/certificate.pfx</AssemblyOriginatorKeyFile>
  <AssemblyOriginatorKeyFilePassword>password</AssemblyOriginatorKeyFilePassword>
</PropertyGroup>
```

**Or use PowerShell:**
```powershell
.\build\build-extension.ps1 -Configuration Release -SignVsix
```

### Build for Multiple Architectures

**PowerShell:**
```powershell
foreach ($platform in @('AnyCPU', 'x64', 'ARM64')) {
    .\build\build-extension.ps1 -Configuration Release -Platform $platform
}
```

**Bash:**
```bash
for platform in AnyCPU x64 ARM64; do
    ./build/build-extension.sh --config Release --platform $platform
done
```

### Continuous Integration (Local)

```powershell
# Full build pipeline
$config = 'Release'
$version = '1.2.3'

Write-Host "Building version $version..."
.\build\build-extension.ps1 `
    -Configuration $config `
    -Version $version `
    -CreateChecksum `
    -Verbose

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded!"
    Get-ChildItem "build-output\$config" -Recurse
}
else {
    Write-Error "Build failed!"
    exit 1
}
```

### Publish to Release Repository

After building and testing:

```bash
# Tag the release
git tag -a v1.2.3 -m "Release v1.2.3"
git push origin v1.2.3

# GitHub Actions will automatically:
# 1. Build VSIX packages
# 2. Generate checksums
# 3. Create GitHub Release
# 4. Upload artifacts
```

## Troubleshooting

### VSIX Not Generated

**Issue:** No `.vsix` file appears in output directory

**Solutions:**
1. Check build succeeded (no errors in console output)
2. Verify `GenerateVsixPackage` is set to `true` in .csproj
3. Ensure VSSDK packages are restored: `dotnet restore`
4. Check output path exists and is writable

### Build Fails with "Cannot find dotnet"

**Solution:** Ensure .NET SDK 8.0+ is installed:
```bash
dotnet --version
```

### Permission Denied on Bash Script

**Solution:** Make script executable:
```bash
chmod +x build/build-extension.sh
```

### Checksum Verification Fails

**Solutions:**
1. Verify VSIX file wasn't modified after checksum creation
2. Ensure correct checksum algorithm (SHA256)
3. Compare checksum file content:
   ```bash
   cat build-output/Release/SimpleModelServer.Extensions.VS.vsix.sha256
   ```

## See Also

- [VS Extension Architecture](../src/Extensions/SimpleModelServer.Extensions.VS/ARCHITECTURE.md)
- [VS Extension Implementation](../src/Extensions/SimpleModelServer.Extensions.VS/IMPLEMENTATION_GUIDE.md)
- [GitHub Release Workflow](.github/workflows/build-vs-extension.yml)
- [Version Management Guide](#versioning)

## Support

For issues with the build system:
1. Check the [BUILD_REPORT.txt](#directory-structure) in the output directory
2. Review the build log file
3. Re-run with `--verbose` flag for more details
4. Check GitHub Actions logs for CI/CD builds

---

**Last Updated:** 2024
**Maintainer:** SimpleModelServer Contributors
