# Plan: standalone TokenSaver MCP installer (.exe)

Status: in progress.
Base: `installer-tokensaver-mcp` (the TokenSaver MSI build is already in `build-installer.ps1`).

Goal: a standalone `TokenSaverMCP-Setup-<version>.exe` that installs TokenSaver MCP on its own, without the SimpleModelServer router, chat app, or extensions.

Decisions (from the user):
- Server: TokenSaver MCP.
- Form: Inno Setup `.exe`, built by our build script, with a checksum in `SHA256SUMS.txt`.

Design: the exe is a thin wrapper around the existing TokenSaver WiX MSI. It bundles the MSI and runs it silently. The MSI already installs and configures the `TokenSaverMCP` service, so the install logic isn't duplicated. The wrapper:
- installs with `msiexec /i ... /qn /norestart /l*v <log in ProgramData>`, accepting exit codes 0 and 3010;
- uninstalls with `msiexec /x` on the same MSI;
- requires admin, like the SimpleModelServer installer.

Units (each: build, check, update status, commit, then start the next):

- [ ] U1. `installer/TokenSaverMcp.iss`: Inno script. `[Files]` bundles `{#TokenSaverMsi}` as `{app}\TokenSaver-Setup.msi`. `[Code]` runs `msiexec` on install and on uninstall, using the same log path and exit-code rules as `SimpleModelServer.iss`. The exe name is `TokenSaverMCP-Setup-{#MyAppVersion}`.
- [ ] U2. `installer/build-installer.ps1`: after the TokenSaver MSI step, compile `TokenSaverMcp.iss` with `/DMyAppVersion`, `/DMyAppNumericVersion`, and `/DTokenSaverMsi`. Both exes go in `build/dist/installer/` and into the same `SHA256SUMS.txt`. Skipped with `-SkipTokenSaver`.
- [ ] U3. Verify: build the installer with TokenSaver, check that `TokenSaverMCP-Setup-*.exe` and its checksum are in `build/dist/installer/`. CI's existing upload step (`build/dist/installer/*.exe`) already picks it up; check that by reading the workflow, not by running it.

Done when: the build produces `SimpleModelServer-Setup-<v>.exe` and `TokenSaverMCP-Setup-<v>.exe` with checksums, and the TokenSaver exe installs and uninstalls the MSI. Install and uninstall on a real machine are not run here; say so.

Risks:
- The exe needs the same TokenSaver checkout as the MSI, so `-SkipTokenSaver` skips both TokenSaver exes.
- Inno's `{app}` for this exe is its own folder, so it doesn't share an install path with SimpleModelServer.
