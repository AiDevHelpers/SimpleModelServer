# Plan: TokenSaver MCP in the installer

Status: in progress.
Base: `dev` (includes PR #8).

Goal: the SimpleModelServer installer offers TokenSaver MCP as an optional component. It installs the TokenSaver MSI silently and removes it on uninstall.

Decisions (from the user):
- MSI source: build it from the TokenSaver source. CI checks out the TokenSaver repo with a read-only token (`TOKENSAVER_READ_TOKEN`, a repo secret the user creates). Local builds use the local folder `F:/source/mcp/TokenSaver` through `TOKENSAVER_DIR`.
- Component: optional checkbox, like the chat app.

Facts checked:
- `rsperry79/TokenSaver` is a private repo. The default CI token can't check it out.
- TokenSaver's pinned commit in the `mcp` repo is `5f6d0f43f0e9672d71a8c7aeda7bd96dd8c3087c`. Use it as the submodule pin.
- The MSI is a WiX v6 project, `src/Installers/TokenSaver.Setup/TokenSaver.Setup.wixproj`, with SDK `WixToolset.Sdk/6.0.0`. Its `PublishTokenSaverMcp` target publishes the self-contained TokenSaver.MCP. The MSI is written to `<TokenSaver>/build/dist/TokenSaver-Setup-<version>.msi`.
- `scripts/publish-tokensaver-for-wix.ps1 -Pack` needs the global `wix` CLI, which CI doesn't have. The plan calls `dotnet build` on the wixproj directly with `-p:MyAppVersion=<version>` instead.
- The TokenSaver build stages under `%TEMP%`, not `build/staging`.
- Product name `TokenSaver MCP`, UpgradeCode `9F2C7B3E-4E7A-4C2E-9C2E-2C6C6E6B7A11`, service `TokenSaverMCP`, HTTP port 11436 by default. No conflict with our ports: router 11435, Ollama 11434.
- Our `MsiInstallPresent` check matches only the display name `Simple Model Server`, so the TokenSaver MSI can't block our install.

Units (each: build, check, update status, commit, then start the next):

- [x] U1. Done (`.gitmodules`, pinned to 5f6d0f4). Add the TokenSaver submodule at `TokenSaver`, pinned to `5f6d0f43f0e9672d71a8c7aeda7bd96dd8c3087c`. URL `https://github.com/rsperry79/TokenSaver.git`.
- [ ] U2. `installer/build-installer.ps1`: resolve the TokenSaver folder (`TOKENSAVER_DIR` if set, else `TokenSaver/`). Fail with a clear message if the folder is missing and the component is wanted. Run `dotnet build <wixproj> -c Release -p:MyAppVersion=<version>`, with the version from our nbgv. Copy `TokenSaver-Setup-<version>.msi` to `build/staging/installer/tokensaver/TokenSaver-Setup.msi` (fixed name). Add `-SkipTokenSaver`, which builds without the component and skips the copy.
- [ ] U3. `installer/SimpleModelServer.iss`: add component `tokensaver` (checkbox, unchecked by default). Install the MSI to `{app}\tokensaver\TokenSaver-Setup.msi`. Run `msiexec /i "<msi>" /qn /norestart /l*v "{tmp}\tokensaver-msi.log"` from a `CurStepChanged` handler using `Exec` and `ResultCode`. Accept 0 and 3010. On other codes, show a `MsgBox` naming the log. On uninstall, remove only an MSI this installer installed: write a marker in the install directory when the install succeeds, and check for it in `CurUninstallStepChanged` before running `msiexec /x` on the same MSI path.
- [ ] U4. `.github/workflows/ci.yml`, `installer` job only. Two checkouts, so the read-only token never reaches the main repo: the default checkout, then a second checkout of `rsperry79/TokenSaver` into `TokenSaver` with `token: ${{ secrets.TOKENSAVER_READ_TOKEN }}`. A step before both checkouts fails with a clear message if the secret is empty. Fork PRs have no secrets: the installer job builds with `-SkipTokenSaver` in that case.
- [ ] U5. Verify: build the MSI locally from `TOKENSAVER_DIR`, build the installer with the component, and compile the `.iss` with ISCC. CI verifies the full build on the PR. Install and uninstall on a real machine is manual; note that it wasn't done.

Done when: the installer builds with and without the TokenSaver component, the MSI installs and uninstalls through the checkbox, and the CI installer job passes with the secret set.

Risks:
- The user must create `TOKENSAVER_READ_TOKEN` (fine-grained, read-only on `rsperry79/TokenSaver`) and add it as a secret in SimpleModelServer. I can't create it.
- Our installer now depends on the TokenSaver build. `-SkipTokenSaver` keeps the old path working.
- The MSI runs a Windows service (`TokenSaverMCP`). Installing it through our installer is a new side effect, so it's optional.
- The MSI's own version comes from TokenSaver's nbgv. Our version doesn't change it.
