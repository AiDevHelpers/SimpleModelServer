# Versioning Strategy for SimpleModelServer VS Extension

This document describes the versioning strategy for the SimpleModelServer VS Extension, ensuring consistent versioning across build systems, CI/CD, and releases.

## Version Scheme

The project uses **Semantic Versioning 2.0.0** format: `MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]`

### Components

| Component | Purpose | Format | Examples |
|-----------|---------|--------|----------|
| MAJOR | Breaking changes | Integer | `1`, `2`, `3` |
| MINOR | New features (backward compatible) | Integer | `0`, `1`, `5` |
| PATCH | Bug fixes (backward compatible) | Integer | `0`, `1`, `10` |
| PRERELEASE | Development/testing stage | Alphanumeric | `alpha`, `beta`, `rc` |
| BUILD | Build number/commit info | Alphanumeric | CI build number |

### Version Examples

```
1.0.0              ← Initial stable release
1.0.1              ← Patch release (bug fix)
1.1.0              ← Minor release (new feature)
2.0.0              ← Major release (breaking change)

1.0.0-alpha        ← Alpha pre-release
1.0.0-beta.1       ← Beta with build number
1.0.0-rc.1         ← Release candidate

1.0.0+20240715.1   ← Stable release with build metadata
1.0.0-dev+42       ← Development build from CI
```

## Version Definition

### Primary Source: version.props

The canonical version is defined in `build/version.props`:

```xml
<PropertyGroup>
  <VersionMajor>1</VersionMajor>
  <VersionMinor>0</VersionMinor>
  <VersionPatch>0</VersionPatch>
  <VersionPrerelease></VersionPrerelease>  <!-- Leave empty for stable -->
</PropertyGroup>
```

This file is imported by the extension's `.csproj` and used by all build systems.

### Version Propagation

```
version.props
    ↓
SimpleModelServer.Extensions.VS.csproj (imports)
    ↓
AssemblyVersion (e.g., 1.0.0.0)
FileVersion (e.g., 1.0.0.0)
InformationalVersion (e.g., 1.0.0)
    ↓
VSIX Package Metadata
    ↓
GitHub Release Tags
```

## Release Workflow

### Development Version Scheme

During development, versions follow this pattern:

```
1.0.0-dev.{BUILD_NUMBER}
```

Example:
- `1.0.0-dev.42` ← Build #42 in CI
- `1.0.0-dev.1` ← Local development build

### Pre-release Version Scheme

Before major releases, use pre-release labels:

```
{MAJOR}.{MINOR}.{PATCH}-{LABEL}[.{NUMBER}]
```

Examples:
- `1.0.0-alpha` ← First alpha release
- `1.0.0-beta` ← First beta release
- `1.0.0-beta.2` ← Second beta release
- `1.0.0-rc.1` ← First release candidate

### Stable Release Version Scheme

Stable releases use pure semantic versioning:

```
{MAJOR}.{MINOR}.{PATCH}
```

Examples:
- `1.0.0` ← Initial release
- `1.0.1` ← Patch update
- `1.1.0` ← Minor update (new features)
- `2.0.0` ← Major update (breaking changes)

## Versioning in Different Contexts

### Local Development Build

When building locally without CI variables:

```powershell
# Uses version from version.props
.\build\build-extension.ps1 -Configuration Debug
# Output: SimpleModelServer.Extensions.VS.1.0.0.vsix (if VersionPatch=0)
```

### CI Development Build (Pull Request)

When building during CI for PR validation:

```
Version: 1.0.0-dev.{GITHUB_RUN_NUMBER}
Example: 1.0.0-dev.123
```

### Release Build (Tag Push)

When pushing a version tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The workflow extracts the version from the tag:
```
Tag: v1.0.0
Version: 1.0.0
```

## Version Increment Strategy

### When to Increment MAJOR

- Breaking changes to extension API
- Dropping support for older VS versions
- Incompatible configuration format changes
- Changes requiring users to reconfigure

### When to Increment MINOR

- New features added (backward compatible)
- New commands added to extension
- New configuration options
- UI improvements

### When to Increment PATCH

- Bug fixes
- Performance improvements
- Documentation updates
- Minor UI tweaks

## Incrementing Versions

### For Next Release

Edit `build/version.props`:

```xml
<!-- Current: 1.0.0 -->
<VersionMajor>1</VersionMajor>
<VersionMinor>0</VersionMinor>
<VersionPatch>0</VersionPatch>

<!-- Next patch release: 1.0.1 -->
<VersionMajor>1</VersionMajor>
<VersionMinor>0</VersionMinor>
<VersionPatch>1</VersionPatch>
```

Then commit and tag:

```bash
git add build/version.props
git commit -m "Bump version to 1.0.1"
git tag v1.0.1
git push origin main --follow-tags
```

### For Beta Release

```xml
<VersionMajor>1</VersionMajor>
<VersionMinor>1</VersionMinor>
<VersionPatch>0</VersionPatch>
<VersionPrerelease>beta.1</VersionPrerelease>
```

Then:
```bash
git commit -m "Release v1.1.0-beta.1"
git tag v1.1.0-beta.1
git push origin main --follow-tags
```

## Assembly Versioning

The `.csproj` derives assembly versions from `version.props`:

```xml
<AssemblyVersion>$(VersionMajor).$(VersionMinor).0.0</AssemblyVersion>
<FileVersion>$(VersionMajor).$(VersionMinor).$(VersionPatch).0</FileVersion>
<InformationalVersion>$(SemanticVersion)</InformationalVersion>
```

Examples:
- **AssemblyVersion:** `1.0.0.0` (never changes during patch releases per .NET conventions)
- **FileVersion:** `1.0.1.0` (changes for each patch)
- **InformationalVersion:** `1.0.1` or `1.0.1-beta.1` (full semantic version)

## CI/CD Version Handling

### GitHub Actions Workflow

The build workflow (`build-vs-extension.yml`) automatically:

1. **Detects the version source:**
   ```powershell
   if (${{ github.ref }} -match "^refs/tags/v(.*)$") {
       $version = $matches[1]  # Extract from tag: v1.0.1 → 1.0.1
   } else {
       $version = "1.0.0-dev.${{ github.run_number }}"  # Development
   }
   ```

2. **Passes to build script:**
   ```bash
   dotnet build -p:Version=$version
   ```

3. **Embeds in VSIX package:**
   - VSIX manifest updated with version
   - VSIX filename includes version

## Version Verification

### Check Assembly Version

**PowerShell:**
```powershell
$dllPath = "src/Extensions/SimpleModelServer.Extensions.VS/bin/Release/net472/SimpleModelServer.Extensions.VS.dll"
[System.Reflection.Assembly]::LoadFile($dllPath) | 
    ForEach-Object {
        Write-Host "Version: $($_.GetName().Version)"
        Write-Host "Product: $($_.GetCustomAttributes([System.Reflection.AssemblyProductAttribute], $false).Product)"
    }
```

### Check VSIX Version

**PowerShell:**
```powershell
$vsixPath = "build-output/Release/SimpleModelServer.Extensions.VS.vsix"
Add-Type -AssemblyName System.IO.Compression
$zip = [System.IO.Compression.ZipFile]::OpenRead($vsixPath)
$manifest = $zip.Entries | Where-Object { $_.Name -eq 'extension.vsixmanifest' }
if ($manifest) {
    $reader = [System.IO.StreamReader]::new($manifest.Open())
    [xml]$xml = $reader.ReadToEnd()
    Write-Host "VSIX Version: $($xml.PackageManifest.Metadata.Identity.Version)"
}
```

### View Version in Visual Studio

After installing the extension:
1. Tools → Extensions and Updates
2. Search "SimpleModelServer"
3. Click on extension → Version shown in details

## Version Compatibility

### Supported Visual Studio Versions

The VSIX manifest specifies compatible VS versions:
```xml
<InstallationTarget Id="Microsoft.VisualStudio.Pro" Version="[17.14,)">
  <ProductArchitecture>amd64</ProductArchitecture>
</InstallationTarget>
```

- Minimum: VS 2022 v17.14
- Maximum: Any future VS 2022 version

### Backward Compatibility

- **1.x.x** versions maintain backward compatibility within MAJOR version
- **2.0.0** may break compatibility; users should test before upgrading
- Pre-release versions are not recommended for production use

## Release Checklist

When preparing a release:

- [ ] Update `build/version.props` with new version
- [ ] Update `CHANGELOG.md` with changes
- [ ] Run full test suite locally
- [ ] Build Release configuration: `.\build\build-extension.ps1 -Configuration Release`
- [ ] Test VSIX installation in Visual Studio
- [ ] Commit changes: `git commit -m "Release v1.0.1"`
- [ ] Tag release: `git tag v1.0.1`
- [ ] Push: `git push origin main --follow-tags`
- [ ] GitHub Actions automatically creates Release with artifacts
- [ ] Verify GitHub Release page has correct artifacts and checksums

## Environment Variables

### CI/CD Variables

| Variable | Purpose | Example |
|----------|---------|---------|
| `BUILD_NUMBER` | CI build number | `42` |
| `CI` | Indicates running in CI | `true` |
| `GITHUB_REF` | Git reference | `refs/tags/v1.0.0` |
| `GITHUB_RUN_NUMBER` | GitHub Actions run number | `123` |

### Override Version

```bash
# Use custom version
dotnet build -p:Version=1.2.3

# Override from environment
$env:BuildNumber = "42"
dotnet build -p:BuildNumber=42
```

## See Also

- [Build Guide](BUILD_GUIDE.md)
- [Semantic Versioning 2.0.0](https://semver.org/)
- [GitHub Release Workflow](.github/workflows/build-vs-extension.yml)

---

**Last Updated:** 2024
**Maintainer:** SimpleModelServer Contributors
