# SimpleModelServer

Ollama-compatible draft-pair router (`SimpleModelServer.Server`, Windows service `SimpleModelServer`), plus config, providers, a MAUI chat client, VS and VS Code extensions, and an Inno Setup 6 installer.

## Build and test

- Solution: `SimpleModelServer.slnx`. Use the .NET 10 SDK (CI uses `10.0.x`). The .NET 11 preview SDK fails `dotnet test` on `Server.Tests` with a VSTest/MTP error.
- Chat app needs the MAUI workload: `dotnet workload install maui-windows`.
- `dotnet build SimpleModelServer.slnx -c Release`
- `dotnet test SimpleModelServer.slnx -c Release --no-build`
- Installer: `pwsh installer/build-installer.ps1 [-Version x.y.z] [-SkipChat] [-SkipVsExtension] [-SkipVsCodeExtension]`
- Router port 11435 and the service both bind it. Stop the service before running a dev instance.

## Layout

- `src/Server/SimpleModelServer.Server`: router and service host (`--service`).
- `src/Config`: `AppConfig` parsing, `DefaultConfig` (first-run stock config).
- `src/Contracts`: shared DTOs and interfaces.
- `src/Providers`: Ollama and LM Studio backends.
- `src/Clients/SimpleModelServer.Chat.Maui`: desktop chat client.
- `src/Extensions`: VS and VS Code extensions.
- `installer/`: Inno Setup script and build script.
- `.github/workflows/ci.yml`: the only workflow.

## Key facts

- Ports: router 11435, Ollama 11434.
- Service: name `SimpleModelServer`, exe `SimpleModelServer.Server.exe`, args `--service`, LocalSystem.
- Runtime config: `%ProgramData%\SimpleModelServer\SimpleModelServer.config.json`, or under `SIMPLEMODELSERVER_WORKSPACE` when set. The server writes the stock config once, if missing, and never overwrites it.
- Every project references `Microsoft.CodeAnalysis`. Keep its version equal across projects that reference each other, or restore fails with NU1605.
- A new test project must be added to `SimpleModelServer.slnx`, or CI silently skips it.

## Git

- Write commit messages to a file and commit with `git commit -F <file>`.
- Commit and push only when asked.

## Where detail lives

- Config, model schemas, and model naming: `.claude/rules/config.md`. Loads for `src/Config`, `src/Contracts`, `src/Providers`, and server `Configuration` folders.
- Installer and CI: `.claude/rules/installer.md` (loads for `installer/` and `.github/workflows/`).
- Resolved failures and their causes: `docs/pitfalls.md`. Check it before debugging install, config, model pull, or test discovery.
