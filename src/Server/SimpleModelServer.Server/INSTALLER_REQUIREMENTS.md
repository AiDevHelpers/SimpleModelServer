# SimpleModelServer.Loader — Installer Requirements

This document exists so that an AI (or a person) can build a new installer for
`SimpleModelServer.Loader` — for a different platform, packaging format, or deployment model —
without having to re-derive these requirements from the source each time. It describes what
the *current* Windows installer (`build/scripts/install-draftmodel-service.ps1`) does and why,
so the essential behavior can be reproduced faithfully in a different form.

If you are an AI asked to write a new installer (e.g. a Linux systemd unit installer, an
Inno Setup / MSI package, a Docker image, a macOS launchd plist), read this whole document
first. The existing PowerShell script (`build/scripts/install-draftmodel-service.ps1`) is the
reference implementation — cross-check any new installer's steps against it.

## What is being installed

`SimpleModelServer.Loader` is a long-running background process (installed as a Windows Service on
Windows) that:

1. Ensures a local Ollama instance is running and healthy.
2. Ensures three specific models are pulled: a "draft" model, a "main" model, and an
   embedding model (names come from config, with defaults — see Configuration below).
3. Runs an HTTP router (`ServiceRouter`, via `HttpListener`) that proxies/fans out requests
   to both models and exposes an Ollama-API-compatible and OpenAI-API-compatible surface.

An installer's job is: get the published binary onto disk, get a working config file in
place, register the process to run automatically (as a service/daemon), open whatever
network reservations are required for the process to bind its listen port, and start it —
then verify it actually came up, not just that the OS reports the process as "started".

## Required build step

The app is published as a self-contained, single-runtime-identifier build:

```powershell
dotnet publish src/SimpleModelServer.Loader/SimpleModelServer.Loader.csproj `
    --configuration Release `
    --runtime <RID>            # win-x64 on Windows; use the correct RID for the target platform
    --self-contained true `
    --output <staging-dir>
```

Then copy/mirror the staging output into the final install directory (the current script
uses `robocopy /MIR`; any file-mirroring copy that also removes stale files from a previous
version works). Do not skip the "mirror" semantics — old installer runs may have left files
that no longer belong (this matters for upgrades).

## Install directory

No hardcoded requirement — the current default is `%ProgramFiles%\SimpleModelServer` on Windows
(`C:\Program Files\SimpleModelServer`). A new installer should pick a platform-appropriate
equivalent (e.g. `/opt/draftmodel` or `/usr/local/lib/draftmodel` on Linux) and should allow
the caller to override it.

## Config file: location and precedence

Config file name: `SimpleModelServer.config.json`. Resolution order (see
`src/SimpleModelServer.Config/DefaultConfig.cs`, `LoaderConfigPath()`):

1. `$DRAFTMODEL_WORKSPACE/.tokensaver/SimpleModelServer.config.json` if the
   `DRAFTMODEL_WORKSPACE` environment variable is set (workspace-scoped config, used by the
   companion `TokenSaver.MCP`/editor-integration scenario).
2. Otherwise, `<CommonApplicationData>/SimpleModelServer/SimpleModelServer.config.json` — on Windows this
   is `%ProgramData%\SimpleModelServer\SimpleModelServer.config.json`. A new installer targeting another OS
   should use that platform's equivalent common/shared app-data directory (e.g.
   `/etc/draftmodel/` or `/var/lib/draftmodel/` on Linux).
3. Otherwise, alongside the executable.

**Critical behavior**: `DefaultConfig.EnsureConfig(path)` only writes the stock config **if
the file does not already exist** — it never overwrites an existing config. This means:

- A fresh install can either leave config generation to the app's first run (it will write
  the stock config itself), OR the installer can write the config file itself up front if it
  needs to bake in install-time settings (e.g. network restrictions — see below). Both are
  valid; the current script does the latter when `-Local` is specified specifically because
  waiting for the app's first run to generate the file, then patching it, is a race with the
  app already having started serving requests.
- An installer flag that means "reset config to defaults" (the current script's `-Clean`)
  must delete the existing file first, so `EnsureConfig` regenerates the stock version (or so
  the installer's own stock-write logic applies cleanly).
- A "reinstall" or "upgrade" flow should **not** delete or overwrite an existing config file
  by default — that would silently discard the user's settings (models chosen, ports,
  network restrictions, agent prompts, etc.).

## Config schema (global section)

The fields under `global` that any installer/config-writer needs to know about:

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `defaultAgentType` | string | `"code"` | Which agent profile (`agents.*`) is active by default |
| `gpuLayers` | int or `"auto"` | `"auto"` | GPU layer offload; `"auto"` triggers hardware detection |
| `cpuThreads` | int or `"auto"` | `"auto"` | CPU thread count; `"auto"` triggers hardware detection |
| `draftModel` | string | `"qwen2.5-coder-0.5b-chat"` | Small/fast model name |
| `mainModel` | string | `"qwen2.5-coder-3b-chat"` | Larger/quality model name |
| `embedModel` | string | `"nomic-embed-text"` (via model role fallback) | Embedding model name |
| `ollamaPort` | int | `11434` | Port the installer/app expects Ollama itself to listen on |
| `routerPort` | int | `11435` | Port the router's `HttpListener` binds |
| `routerHost` | string | `"localhost"` | Hostname/IP the router binds. `"*"` or `"+"` = all interfaces (requires a URL ACL reservation on Windows — see below) |
| `routerModelName` | string | `"HelperModel"` | Virtual model name the router advertises over the OpenAI-compatible API |
| `contextSize` | int | unset | Default `num_ctx` override |
| `agentType` | string | unset | Force a specific agent type regardless of per-request signals |
| `trustedNetworks` | string array | `[]` (unrestricted) | **Network access control — see next section.** |

The stock config also has a `models` section (per-model `base`/`temperature`/`num_ctx`/
`keep_alive`/`stop` settings) and an `agents` section (system prompts per agent type). An
installer that writes a config from scratch should include at minimum the `global` section
with the fields above; the app will use built-in defaults for anything else that's missing
(see `AppConfig.ParseGlobal` / `Agent()` fallback behavior in `SimpleModelServer.Library`).

## Network access control: `global.trustedNetworks`

Added to support restricting which clients may use the router. This is enforced **inside the
running process** (`DraftPairApp.HandleRequestAsync`, via
`SimpleModelServer.Library.Networking.TrustedNetworkList`), not by the OS firewall — an installer
does not need to configure firewall rules for this to work, but should understand what it
does and does not cover:

- **Empty array (default)**: unrestricted — any client that can reach the listening socket is
  served. This is the historical/backward-compatible default.
- **Non-empty array**: only clients whose IP falls within one of the listed networks are
  served; everything else gets HTTP 403. **Loopback (127.0.0.1/::1) is always allowed**
  regardless of this setting, specifically so a misconfigured list can never lock out the
  local machine itself.
- Accepted entry syntax, two forms:
  - **Dotted-quad shorthand**: the mask width is inferred from how many trailing octets are
    `0`. `"192.168.0.0"` → trailing two zero octets → `/16` (255.255.0.0). `"192.168.1.0"` →
    trailing one zero octet → `/24` (255.255.255.0). `"10.0.0.0"` → `/8`. An address with no
    trailing zero octet (e.g. `"127.0.0.1"`) is treated as an exact single-host `/32` match.
  - **Explicit CIDR**: `"192.168.0.0/24"` is also accepted and takes precedence over the
    shorthand's inference — use this whenever the shorthand's inferred mask isn't the one you
    want (e.g. you want `/24` on `192.168.0.0` instead of the shorthand's inferred `/16`).
  - `"*"` or an empty string matches everything (equivalent to an unrestricted list, but
    explicit).
- This is IPv4-only today (`TrustedNetwork.Parse` rejects non-`InterNetwork` addresses).
  IPv4-mapped IPv6 addresses (`::ffff:a.b.c.d`, seen on dual-stack listeners) are normalized
  back to IPv4 before matching, so this still works correctly when `routerHost` binds
  dual-stack.
- This setting only matters at all if `routerHost` is bound to something reachable from
  outside loopback (`"*"`/`"+"`, or a specific non-loopback IP). If `routerHost` stays
  `"localhost"` (the default), only same-machine processes can connect regardless of
  `trustedNetworks`.

**An installer offering a "local only" mode** (the current script's `-Local` switch) should
populate `trustedNetworks` with ranges appropriate to the platform's local-virtualization
networking (Hyper-V/WSL2/Docker on Windows commonly draw from `172.16.0.0/12`; home/LAN and
many Hyper-V "External"/"Internal" switch configs use `192.168.0.0/16`) — pick ranges
appropriate to whatever the target platform's equivalent local-networking stack uses (Docker
Desktop, Podman, WSL, etc. differ by OS). Do not simply leave `trustedNetworks` empty and call
that "local only" — that is actually the unrestricted default and does not restrict anything.
An installer must **write these values into the config itself** before first start, rather
than opening broad OS-level network ACLs/firewall rules "to be safe" — the network-level
gate belongs in `trustedNetworks`, not in a broader-than-necessary OS ACL.

## Service registration (Windows specifics; adapt per-platform)

- Service name used by the current installer: `SimpleModelServerLoader`.
- Binary invoked with the `--service` argument, which the app (`Program.cs`) detects via
  `WindowsServiceHelpers.IsWindowsService()` and routes into a `Microsoft.Extensions.Hosting`
  generic host with `UseWindowsService()`, running `SimpleModelServerWorker` (a `BackgroundService`).
- **Startup timing is important.** `SimpleModelServerWorker.ExecuteAsync` deliberately does *not*
  await the full Ollama/model/router bring-up sequence before returning — it kicks that work
  off on a detached background task and returns immediately, so the SCM (or equivalent
  service manager) sees "started" right away. This was a real, observed bug: the SCM's
  service-start timeout (commonly ~30s) was being exceeded by the bring-up sequence under a
  non-interactive service session, even when Ollama and models were already fully cached,
  producing `Event 7009: "A timeout was reached... while waiting for the service to
  connect."` A new installer for a different service manager (systemd, launchd) should verify
  the equivalent "service considered started" signal is sent promptly and not gated on
  Ollama/model readiness — and should prefer whatever native "notify when actually ready"
  mechanism the platform offers (e.g. systemd `Type=notify` + `sd_notify(READY=1)`) over a
  fire-and-forget approach, if available, since that's more correct than what Windows'
  service model allows for.
  - Corollary: **do not treat "service reports running" as "router is ready."** The current
    installer separately polls `GET /health` on the router port (up to ~2 minutes) after the
    service reports running, and treats that as the real readiness signal. Any new installer
    should do the same rather than declaring success the moment the OS says the process
    started.
- Auto-restart on failure is configured (the current script sets restart actions at 5s/10s/
  30s via `sc.exe failure ... reset= 86400 actions= restart/5000/restart/10000/restart/30000`).
  Reproduce equivalent "restart on crash with backoff" behavior in any new service manager.
- **Service account**: currently runs as `LocalSystem` (Windows default when no `obj=` is
  passed to `sc.exe create`). This was deliberately kept (not switched to a named user
  account) after diagnosing a real startup failure — see the "Known pitfalls" section below.
  If a new installer targets a different account/user model, verify Ollama's binary and model
  storage are reachable by whatever account the service runs as; do not assume the account
  that will run the service has the same `PATH`/environment/model-directory visibility as an
  interactive user session.
- **Idempotency / upgrade behavior**: the current script distinguishes a fresh install from
  an upgrade (exe already present *and* service already registered) and, on upgrade, replaces
  only the binaries — it does not re-run `sc.exe create`/`sc.exe config` in that case, to avoid
  clobbering any registration customizations. A new installer should preserve this distinction:
  first install vs. upgrade are different operations, and upgrade should be closer to a no-op
  for service registration.

## Network/port reservation (Windows-specific; find the platform equivalent)

`HttpListener` on Windows requires a URL ACL reservation before a non-admin account can bind
the listen prefix (`netsh http add urlacl url=http://<host>:<port>/ user=Everyone`). Two
things a new installer must get right here, both of which were bugs in earlier iterations of
this script and were fixed:

1. **The ACL must be derived from the actual configured `routerHost`/`routerPort`**, not
   hardcoded to `localhost:11435`. If `-Local`/manual config changes the port or binds all
   interfaces, the ACL registration has to follow that, or the service will fail to bind with
   an access-denied error despite the installer claiming success.
2. **The URL ACL is not a network access control.** `netsh http add urlacl ... user=Everyone`
   only governs which *local Windows accounts* are permitted to register/bind that URL
   prefix — it has nothing to do with which *remote network clients* can connect once the
   listener is bound. Do not conflate the two, and do not treat "granting the URL ACL to
   Everyone" as if it were "opening the router to the network" — actual network-level
   exposure is controlled by `routerHost` (loopback-only vs. all-interfaces) and by
   `trustedNetworks` (see above). A platform without an equivalent ACL concept (most non-
   Windows platforms don't need one for a normal socket bind) can skip this step entirely,
   but should still get the derive-from-config principle right for whatever platform-specific
   privileged-port/bind step it does need (e.g. `setcap`/`authbind` for binding low ports on
   Linux, if ever needed — not currently required since the default port is 11435).
3. Register an event-log/logging source (the current script does
   `New-EventLog -Source SimpleModelServer -LogName Application` on Windows) if the target platform
   has an equivalent centralized log destination, so operators can find fatal startup errors
   without needing to run the exe interactively. **Currently the app only logs to Console** via
   a plain `Microsoft.Extensions.Logging` console logger configured in `Program.cs` — there is
   no Event Log or VS Code sink wired up right now, which means Console output (and therefore
   all `logger.Log*` calls) is completely invisible once the process is running under the SCM
   (no console session). A new installer's logging story needs to account for this gap — either
   by wiring up a real Event Log/file sink in the app, or by making sure whatever this section
   sets up captures stdout/stderr some other way (see pitfall #3 below, which was hit for
   exactly this reason).

## Known pitfalls (do not repeat these)

These were real bugs found and fixed while building the current installer/service — a new
installer implementation should not reintroduce them:

1. **Don't let service-start success depend on Ollama/model bring-up finishing.** Fixed by
   moving that work off the service-start critical path (see "Startup timing" above).
2. **Don't assume the service account can see the same `ollama` binary, `OLLAMA_MODELS`
   directory, and GPU/driver stack as an interactive user session.** These were investigated
   at length for this app: PATH differences, per-user vs. machine-wide installs, and
   session-0 isolation were all considered as possible causes of a real service-start failure
   before the actual root cause (an unrelated `InvalidCastException` crash during host
   startup, see #3) was found. Verify all of these explicitly for whatever platform a new
   installer targets, rather than assuming parity with an interactive shell.
3. **A single bad explicit cast in host-builder configuration can crash the process before
   any of your own error handling or logging even runs.** (`(HostBuilderContext)
   services.AddSingleton(...)` was an invalid cast that always threw, and did so during
   `Host.Build()`, before `SimpleModelServerWorker.ExecuteAsync` — and therefore before its `try`/
   `catch` — ever ran.) Any new installer's job includes making sure app-side startup crashes
   are actually observable (event log entry, or a captured stdout/stderr redirect a person can
   read) — don't rely solely on the app's own internal logging catching everything, since a
   crash during host construction can happen before that logging is wired up.
4. **`Start-Service`/equivalent "start succeeded" signals can lie or be uninformative.**
   `Start-Service` on Windows reported a generic failure with no actionable detail; the real
   error was only found via the System event log's Service Control Manager entries (event IDs
   7000/7009), not the Application log, and not the command's own output. When building a new
   installer, always verify by an independent, real signal (health-check HTTP call, checking
   the process is actually alive and listening) rather than trusting the service manager's own
   immediate return code.
5. **Don't hardcode network-facing values (ports, hosts, ACLs) that should come from config.**
   Both the URL ACL registration and the health-check poll had this bug at different points
   during development — hardcoded `localhost:11435` instead of reading the actual configured
   `routerHost`/`routerPort`. Read the config once, reuse the resolved values everywhere in
   the installer.

## GPU detection / driver / CUDA setup

The current script (`install-draftmodel-service.ps1`) detects GPU hardware before touching
the service and, for NVIDIA GPUs, tries to make sure the machine can actually use it:

- Enumerates GPUs (`Win32_VideoController` on Windows; use the platform equivalent
  elsewhere, e.g. `lspci`/`glxinfo` on Linux) and classifies by vendor string
  (NVIDIA / AMD / neither).
- **Important distinction to preserve**: `global.gpuLayers = "auto"` in
  `SimpleModelServer.config.json` is resolved by the *app itself* at runtime (hardware detection
  happens in-process) — the installer does not need to and should not write a specific
  `gpuLayers` value. The installer's job is only to make sure the driver/runtime the app
  will detect is actually present and current, not to pre-compute the value itself.
- **Ollama already bundles its own CUDA/ROCm runtime libraries.** The only strict
  requirement for GPU offload to work is a current vendor driver (NVIDIA driver 550+ is the
  version this script checks for via `nvidia-smi --query-gpu=driver_version`). A separate
  CUDA Toolkit install is *not* required for SimpleModelServer.Loader/Ollama to use the GPU.
- The current script installs the CUDA Toolkit anyway (via `winget install -e --id
  Nvidia.CUDA --silent ...`) because that was explicitly requested for this environment —
  treat that as optional, environment-specific behavior, not a hard requirement to
  reproduce, unless the new installer's target environment has the same requirement (e.g.
  something else on the machine needs `nvcc`/the toolkit directly).
- Driver install/update is attempted via Windows Update's `Drivers` category (using the
  `PSWindowsUpdate` PowerShell module, installed on demand from PSGallery if missing) rather
  than downloading a specific driver package — there is no reliable generic winget package
  ID for "the NVIDIA driver matching this GPU" (winget packages are per-product, e.g.
  GeForce Experience, not a bare driver), so Windows Update's own hardware-matched driver
  delivery is the most robust unattended path available on Windows today. **A driver
  install/update may require a reboot before the GPU is usable** — the script does not force
  a reboot, it warns and asks the operator to re-run after rebooting. A new installer for a
  different OS should use that platform's equivalent driver-management channel (e.g. distro
  package manager + `nvidia-driver-XXX` metapackage on Linux) rather than trying to hand-roll
  a vendor-specific silent installer download.
- AMD GPUs are detected and reported, but no automatic driver/ROCm install is attempted —
  ROCm's Windows support and unattended-install story is not solid enough to automate
  reliably; the script just warns and points at AMD's driver download page.
- Provide a switch to skip this whole step (`-SkipGpuSetup` in the current script) since GPU
  detection/driver installs can be slow, can trigger a reboot requirement, and aren't always
  wanted (e.g. CI/test installs, machines already known to be CPU-only).

## Minimal checklist for a new installer

1. Publish a self-contained build for the target platform/architecture.
2. Stop any existing running instance before overwriting files (for upgrades).
3. Mirror-copy the published output to the install directory.
4. Ensure the config directory exists; do not overwrite an existing config file unless the
   user explicitly asked for a reset/clean.
5. If offering a "restrict to local networks" option, write `global.trustedNetworks` directly
   into the config with platform-appropriate local/virtualization ranges — do not leave it
   empty and call that restricted, and do not compensate by opening broad OS-level network
   ACLs instead.
6. Register whatever platform-specific listen-port reservation is required, deriving
   host/port from the actual config file, not hardcoded values.
7. Register the process as an auto-starting, auto-restarting background service/daemon,
   preserving prior registration on upgrades rather than re-creating it.
8. Ensure a log destination exists that will capture a fatal startup crash even if it happens
   before the app's own logging pipeline is fully initialized.
9. Start the service, then independently verify readiness via `GET /health` on the configured
   router port — do not treat "service manager says running" as sufficient confirmation.
10. Report the resolved install directory, config path, service name, and management commands
    to the person running the installer.
11. Detect GPU vendor/driver before doing anything else disruptive, and offer a way to skip
    it. Remember `gpuLayers: "auto"` is resolved by the app at runtime, not the installer —
    the installer's job is only to make sure a current driver (not necessarily a full CUDA
    Toolkit — Ollama bundles its own) is present so the app's own detection has something to
    find. See "GPU detection / driver / CUDA setup" above for the full rationale.
