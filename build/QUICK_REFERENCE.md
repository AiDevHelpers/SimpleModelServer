# VS Extension Build - Quick Reference

## Fast Commands

### Build Release VSIX (Windows)
```powershell
.\build\build-extension.ps1
```

### Build Release VSIX (Linux/macOS)
```bash
./build/build-extension.sh
```

### Build Debug VSIX
```powershell
.\build\build-extension.ps1 -Configuration Debug
```

### Build with Custom Version
```powershell
.\build\build-extension.ps1 -Version 1.2.3
```

### Build with Verbose Output
```powershell
.\build\build-extension.ps1 -Verbose
```

## File Locations

| What | Where |
|------|-------|
| Version definition | `build/version.props` |
| Release profile | `src/Extensions/SimpleModelServer.Extensions.VS/Properties/PublishProfiles/Release.pubxml` |
| Debug profile | `src/Extensions/SimpleModelServer.Extensions.VS/Properties/PublishProfiles/Debug.pubxml` |
| Built VSIX (Release) | `build-output/Release/SimpleModelServer.Extensions.VS.vsix` |
| Built VSIX (Debug) | `build-output/Debug/SimpleModelServer.Extensions.VS.vsix` |
| SHA256 checksums | `build-output/{Release,Debug}/SimpleModelServer.Extensions.VS.vsix.sha256` |
| Build reports | `build-output/{Release,Debug}/BUILD_REPORT.txt` |

## Common Scenarios

### I want to build a Release VSIX

```powershell
cd SimpleModelServer
.\build\build-extension.ps1 -Configuration Release
# Output: build-output/Release/SimpleModelServer.Extensions.VS.vsix
```

### I want to build with a specific version

```powershell
.\build\build-extension.ps1 -Configuration Release -Version 1.0.1
```

### I want to change the version permanently

Edit `build/version.props`:
```xml
<VersionMajor>1</VersionMajor>
<VersionMinor>0</VersionMinor>
<VersionPatch>1</VersionPatch>
```

### I want to create a GitHub Release

```bash
git tag v1.0.1
git push origin v1.0.1
# GitHub Actions automatically builds and creates Release
```

### I want to verify a VSIX file

**Windows:**
```powershell
$vsix = "build-output/Release/SimpleModelServer.Extensions.VS.vsix"
Add-Type -AssemblyName System.IO.Compression
$zip = [System.IO.Compression.ZipFile]::OpenRead($vsix)
Write-Host "VSIX is valid"
$zip.Dispose()
```

**Linux/macOS:**
```bash
unzip -t build-output/Release/SimpleModelServer.Extensions.VS.vsix
```

### I want to install the VSIX

1. Open Visual Studio 2022
2. Extensions > Manage Extensions
3. Install from File
4. Select `build-output/Release/SimpleModelServer.Extensions.VS.vsix`

### I want to check the build log

```powershell
Get-Content build-output/Release/BUILD_REPORT.txt
```

### I want to verify checksums

```powershell
# Display checksum
Get-Content build-output/Release/SimpleModelServer.Extensions.VS.vsix.sha256

# Verify
(Get-FileHash build-output/Release/SimpleModelServer.Extensions.VS.vsix -Algorithm SHA256).Hash
```

### I want to build both Release and Debug

```powershell
.\build\build-extension.ps1 -Configuration Release
.\build\build-extension.ps1 -Configuration Debug
```

### I want to clean build files

```powershell
Remove-Item -Path build-output -Recurse -Force
Remove-Item -Path src/Extensions/SimpleModelServer.Extensions.VS/bin -Recurse -Force
Remove-Item -Path src/Extensions/SimpleModelServer.Extensions.VS/obj -Recurse -Force
```

## Troubleshooting

### "dotnet: command not found"
Install .NET 8.0 SDK from https://dotnet.microsoft.com/

### Build fails with MSB error
```powershell
# Clean and retry
dotnet clean
.\build\build-extension.ps1 -Verbose
```

### VSIX not generated
Check the output:
```powershell
Get-ChildItem src/Extensions/SimpleModelServer.Extensions.VS/bin/Release
```

### Script won't run (Windows)
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Script won't run (Linux/macOS)
```bash
chmod +x build/build-extension.sh
```

## Version Updates

### For patch release (1.0.0 → 1.0.1)
Edit `build/version.props`:
```xml
<VersionPatch>1</VersionPatch>
```

### For minor release (1.0.0 → 1.1.0)
Edit `build/version.props`:
```xml
<VersionMinor>1</VersionMinor>
<VersionPatch>0</VersionPatch>
```

### For major release (1.0.0 → 2.0.0)
Edit `build/version.props`:
```xml
<VersionMajor>2</VersionMajor>
<VersionMinor>0</VersionMinor>
<VersionPatch>0</VersionPatch>
```

### For pre-release (1.0.0-beta.1)
Edit `build/version.props`:
```xml
<VersionPrerelease>beta.1</VersionPrerelease>
```

## Parameters Cheat Sheet

### PowerShell: `.\build\build-extension.ps1`

```
-Configuration  Release|Debug (default: Release)
-Version        Override version (e.g., 1.2.3)
-OutputDirectory Output folder (default: build-output)
-Platform       AnyCPU|x64|ARM64 (default: AnyCPU)
-SignVsix       Enable VSIX signing
-CreateChecksum Generate SHA256 (default: true)
-Verbose        Detailed output
```

### Bash: `./build/build-extension.sh`

```
--config        Release|Debug (default: Release)
--version       Override version (e.g., 1.2.3)
--output        Output folder (default: build-output)
--platform      AnyCPU|x64|ARM64 (default: AnyCPU)
--sign          Enable VSIX signing
--checksum      Generate SHA256 (default: true)
--no-checksum   Don't generate checksums
--verbose       Detailed output
```

## GitHub Actions

### Manual Trigger
Push to GitHub, then:
1. Go to repository > Actions
2. Select "Build VS Extension"
3. Click "Run workflow"

### Automatic Triggers
- Push tag: `git push origin v1.0.0`
- Commit to extension folder
- Manual workflow dispatch

## Size Reference

| Build | Typical Size | With Symbols |
|-------|--------------|--------------|
| Release | ~1.6 MB | - |
| Debug | ~3.7 MB | Embedded |

## Documentation

For detailed information, see:
- `build/BUILD_GUIDE.md` - Complete build guide
- `build/VERSIONING.md` - Versioning strategy
- `build/INFRASTRUCTURE_SUMMARY.md` - What was created

---

**Last Updated:** July 14, 2026
