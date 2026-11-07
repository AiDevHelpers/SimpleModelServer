---
paths:
  - "installer/**"
  - ".github/workflows/**"
---

# Installer and CI

- Inno Setup 6 script: `installer/SimpleModelServer.iss`. Driven by `installer/build-installer.ps1`, which passes `MyAppVersion`, `MyAppNumericVersion`, `ServerDir`, `ChatDir`, `VsExtensionVsix`, `VsCodeExtensionVsix`, `RepoRoot`, `OutputDir`, and `Has*` flags.
- `VersionInfoVersion` and `VersionInfoProductVersion` must be numeric (`x.y.z.w`). Display versions such as `1.0.0-rc1` go in `MyAppVersion` only.
- `AppId` is fixed. Never change it. The old WiX UpgradeCode is different, so `InitializeSetup` blocks install while an MSI install of the product is present.
- The service is registered with `sc.exe` and started with `net.exe` from `[Code]`. Stop it before files are copied, start it after. `sc.exe` does not run pipes; wrap `find` in `cmd /c`.
- The installer must not write `SimpleModelServer.config.json`. Uninstall keeps `%ProgramData%\SimpleModelServer`.
- Build outputs: `build/dist/installer/` contains `SimpleModelServer-Setup-<v>.exe`, `*.vsix`, and `SHA256SUMS.txt`. Staging is `build/staging/installer/`.
- Server-only compile: add `-SkipChat -SkipVsExtension -SkipVsCodeExtension`.
- Running the installer on a dev machine registers a real service and a URL ACL. Do it deliberately, and uninstall afterwards.
- CI (`ci.yml`): `build-and-test`, then `installer`, then `release` on `v*` tags only. Release uses `gh release upload --clobber` when the release exists, so re-runs don't fail.
- PowerShell: `$args` is automatic. Don't use it as a variable name.
