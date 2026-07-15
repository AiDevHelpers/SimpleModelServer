# Instructions for an AI agent: connecting to a shared SimpleModelServer.Loader router

This file is meant to be copied to `%ProgramData%\SimpleModelServer\AI_INSTRUCTIONS_REMOTE_ROUTER.md`
on any machine that should use a **shared, remote** SimpleModelServer.Loader instance instead of
running its own local one. It exists because programmatically writing MCP client config files
(`claude_desktop_config.json`, VS Code `settings.json`, etc.) from an installer or background
service has proven unreliable in practice — malformed JSONC, races with the editor/app
rewriting the same file, per-tool schema differences, etc. Instead, this file gives an AI
coding agent (Claude Code, Continue, or a person working with either) the exact information
needed to wire up the connection itself, using whatever native mechanism that specific tool
actually supports, and to verify the result — rather than relying on a script to blindly
edit JSON.

**If you are an AI agent and a user points you at this file, read the whole thing, then follow
the steps in the "What to actually do" section below.**

## Important: what this router is (and is not)

`SimpleModelServer.Loader`'s router is an **Ollama-API-compatible and OpenAI-API-compatible HTTP
endpoint** (`POST /api/generate`, `POST /v1/chat/completions`, `GET /v1/models`, etc.) — it is
a model-inference proxy, not an MCP server. It does not speak the Model Context Protocol
(no tools/resources/prompts). **Do not attempt to register it as an `mcpServers` entry** in
Claude Code's or Continue's MCP configuration — that will not work and is a category error.

The actual MCP server in this system is `TokenSaver.MCP` (a separate, stdio-based process
per machine). `TokenSaver.MCP` is the thing that gets registered as an MCP server; it in turn
calls the router (`SimpleModelServer.Loader`) as its backend model-inference endpoint via its own
`OllamaUrl` configuration setting. So "connecting to a shared router" means: keep running a
normal, local `TokenSaver.MCP` on this machine (registered as an MCP server exactly as usual —
do not change that part), but point *that local instance's* `OllamaUrl` at the shared router's
address instead of `http://localhost:11435`.

## Values to fill in

Before following the steps below, get these two values from whoever is sharing their
SimpleModelServer.Loader instance (or from the shared machine's `SimpleModelServer.config.json`,
`global.routerHost` / `global.routerPort`):

- `<ROUTER_HOST>` — the hostname or IP of the machine running the shared router (e.g.
  `192.168.1.50`). It must be reachable on your network, and the shared router's
  `global.routerHost` must be bound to something other than `localhost` (e.g. `"*"` or `"+"`),
  and its `global.trustedNetworks` (if restricted) must include a range that covers this
  machine's IP. If you can't reach it, that's the first thing to check — not a config typo
  here.
- `<ROUTER_PORT>` — the shared router's port, default `11435`.

## What to actually do

1. **Confirm reachability first**, before touching any config. From this machine, run:

   ```
   curl http://<ROUTER_HOST>:<ROUTER_PORT>/health
   ```

   You should get a `200 OK` JSON response. If this fails (connection refused, timeout), stop
   here — the network/firewall/trustedNetworks configuration needs fixing on the router's
   side first. Do not proceed to edit any local config if this check fails; a config change
   that points at an unreachable host is worse than no change, because it silently breaks
   local inference that was previously working against `localhost`.

2. **Point this machine's local `TokenSaver.MCP` at the shared router**, by editing
   `TokenSaver.MCP`'s own config (not the MCP client's config) — this is a small, well-known
   settings file. Location follows `TokenSaverConfig` resolution (see
   `TokenSaver.Library/Configuration/TokenSaverConfig.cs`); by default it's the
   `appsettings.json` alongside `TokenSaver.MCP.exe`, or wherever `SimpleModelServerServiceInstalled`
   auto-discovery would otherwise point it. Set:

   ```json
   {
     "TokenSaver": {
       "OllamaUrl": "http://<ROUTER_HOST>:<ROUTER_PORT>",
       "OllamaModel": "HelperModel"
     }
   }
   ```

   `OllamaModel` should match the shared router's `global.routerModelName` (default
   `"HelperModel"`) — leave it as-is unless the person sharing the router tells you otherwise.

   Do **not** touch this machine's own `SimpleModelServer.config.json` or attempt to stop/uninstall
   its local `SimpleModelServer.Loader` service as part of this step unless the user explicitly asks
   for that — pointing `TokenSaver.MCP` at a remote router does not require removing the
   local one, and leaving it running is a safe fallback if the remote one becomes unreachable
   later. If the user does want the local SimpleModelServer.Loader service removed/disabled after
   switching over, that is a separate, explicit action — ask before doing it.

3. **Restart `TokenSaver.MCP`** (or the AI tool that spawns it) so the new `OllamaUrl` takes
   effect. If it's registered as a stdio MCP server inside Claude Code, Continue, etc., that
   usually just means restarting the tool/reloading the window — it does not require touching
   the MCP server registration itself, since the registration (command/args to launch
   `TokenSaver.MCP.exe`) doesn't change; only what `TokenSaver.MCP` talks to internally
   changes.

4. **Verify it actually worked**, don't just assume the config took effect:
   - Call `TokenSaver.MCP`'s own health/diagnostic tool if available (e.g. `ContextDiag`) to
     confirm which `OllamaUrl` it's actually using at runtime.
   - Send one real request through the normal chat/tool-call path and confirm you get a
     response — not just that the process started without error.
   - If it fails, check in this order: (a) is the shared router still reachable via the same
     `curl .../health` check from step 1, (b) does the shared router's `trustedNetworks`
     (if restricted) actually include this machine's current IP — IPs can change on DHCP
     networks, (c) is `OllamaModel` spelled exactly as the shared router's
     `routerModelName`.

## Why this file exists instead of a script doing it automatically

Writing MCP client configuration files programmatically (the approach
`ProviderAutoRegistrationService`/`AiProviderConfigService` in this repo already attempts for
local, same-machine registration) is fragile: different tools use different file formats and
locations, some are JSONC with comments/trailing commas that strict JSON parsers reject, and
concurrent edits by the user's editor can race with an automated writer and clobber each
other's changes. For the specific case of "point an existing, already-working local
installation at a different remote endpoint" — a single string value in a small settings
file — it's more reliable to hand an AI agent (or a person) precise, unambiguous instructions
and let it make the one small edit with full context, than to have an installer script try to
detect and safely rewrite every possible client's config format for what is fundamentally a
one-line change.
