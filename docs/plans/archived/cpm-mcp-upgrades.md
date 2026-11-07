# Plan: central package management, upgrades, MCP 2.x migration

Status: done. Branch `phase1-security` (PR #1 into `dev`).

Decisions (from the user):
- Conflicting pins take the newest version: coverlet.collector 10.1.0, Logging.Abstractions 10.0.14, Microsoft.NET.Test.Sdk 18.11.0, Moq 4.21.0.
- Fold in the uncommitted upgrade: Microsoft.CodeAnalysis 5.9.0, xunit.v3 4.0.2, xunit.runner.visualstudio 4.0.1, Microsoft.Extensions.Logging group 10.0.14, MessagePack 3.1.12.
- MAUI: MauiVersion property in Directory.Packages.props, value 10.0.120; Microsoft.Maui.Controls uses it.
- ModelContextProtocol 1.4.1 -> 2.3.1 (ModelContextProtocol.AspNetCore too).

Units:

- [x] U1. Central package management. Directory.Packages.props added; Version attributes removed from all PackageReference items. Commit `8e925ec`.
- [x] U2. Upgrades, one group at a time.
  - Test stack, Testing Platform opt-in: commit `a01d983`.
  - Logging group 10.0.14: commit `8e07dac`.
  - MAUI 10.0.120: commit `7651b45`. Accepted by user: 2 CS0618 warnings in MAUI's generated XamlTypeInfo.g.cs. This breaks the zero-warning rule for this build.
  - TRX report extension 2.5.1 and CI `--report-trx` restored: commit `602b95b`.
- [x] U3. MCP 2.x. Bumped to 2.3.1 (commit `6331510`). No source migration was needed: the solution builds with 0 errors and 0 warnings, and all tests pass.

Verification: solution build and `dotnet test SimpleModelServer.slnx --no-build` (863 passed) after each unit. Vulnerability audit clean.

Departures from the plan:
- The plan said the worktree's local global.json pins 10.0.403 and is never committed. A tracked global.json (SDK 10.0.100, `test.runner` = Microsoft.Testing.Platform) was committed in `a01d983`, because the Testing Platform needs the runner setting.
- Mid-plan scope additions, both recorded above: the Testing Platform opt-in (needed by xunit v4) and the TRX report extension (requested by the user).
- U3 needed no code changes, so the McpUpstreamRegistry.cs and McpAdminTools.cs migration listed above was not performed.
