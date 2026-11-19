# Remaining work (SimpleModelServer), 2026-11-19

Status as of 2026-11-19. Sources: installer-tokensaver-mcp.md and phase5-tests-gates.md.

## Installer with TokenSaver MCP (in progress)

- [ ] U5: build the MSI locally from TOKENSAVER_DIR; build the installer with the component; compile the .iss with ISCC. CI covers the PR build.
- [ ] Install and uninstall through the checkbox on a real machine (manual, not done).
- [ ] Done when: the CI installer job passes with the TOKENSAVER_READ_TOKEN secret set (not verified).
- [ ] User action: create TOKENSAVER_READ_TOKEN (fine-grained, read-only on rsperry79/TokenSaver) and add it as a repository secret.

## Phase 5 tests and gates (in progress)

- [ ] U3: validate the quality-gate YAML locally. The first CI run is the current check.
- [ ] U4 end to end: clean Release build with 0 warnings; tests pass; format passes; vulnerability audit clean; merged coverage at or above 49%.
- [ ] Done when: the quality gate runs on every push and PR in CI (not yet verified).

## Housekeeping

- [ ] Local changes: confirm the working tree and the pointer in mcp match pushed commits before any merge.
