# SimpleModelServer.Loader — mDNS Service Discovery

This document is the contract for the mDNS (RFC 6762 / DNS-SD) service advertisement
`SimpleModelServer.Loader` publishes for its router. It exists so anything consuming the advertisement
(future TokenSaver.MCP auto-discovery, the VS Code extension, manual debugging with `dns-sd` /
`avahi-browse` / Bonjour Browser) has one place to look up the exact service type and TXT record
shape instead of reading `MdnsAdvertiser.cs`.

## Scope

**Advertise-only.** `SimpleModelServer.Loader` broadcasts its own router endpoint so other tools on the
LAN can find it. It does **not** browse for or connect to other `SimpleModelServer.Loader` instances —
there is no peer discovery, capacity sharing across machines, or request forwarding. (This is a
deliberate scope decision, not a gap: see "Prior art" below for the fuller pattern this was scoped
down from.)

## Service record

| Field | Value |
|---|---|
| Service type | `_draftmodel._tcp.local.` |
| Instance name | `SimpleModelServer {sanitized-hostname}` (override via `global.mdnsServiceName`) |
| Port | `global.routerPort` (default `11435`) — the same port the HTTP router listens on |
| Address | Best physical LAN IPv4 address on the machine (see "Address selection" below) |

### TXT record properties

| Key | Value | Notes |
|---|---|---|
| `app` | `draftmodel-loader` | Stable identifier for filtering — matches `MdnsAdvertiser.AppId` |
| `hostname` | Sanitized machine hostname | See "Instance name sanitization" below |
| `version` | `0.1` | Advertisement schema version, not the SimpleModelServer.Loader assembly version |
| `path` | `/health` | Suggested path to probe first — see `docs/router-contract.md` |

## Instance name sanitization

DNS-SD instance labels can't contain arbitrary characters or a domain suffix. `Environment.MachineName`
(or any hostname) is passed through `MdnsAdvertiser.SafeInstanceLabel`, which:

1. Strips everything after the first `.` (drops any domain suffix).
2. Replaces any character that isn't `[a-zA-Z0-9-]` with `-`.
3. Truncates to 63 characters.

This exactly mirrors ECU-Toolkit's `discovery.py` `_safe_hostname` helper, which exists for the
same reason: an mDNS instance name with an embedded dot or invalid character breaks resolution for
some clients even though nothing in the local process would otherwise reject it.

## Address selection

The advertised address is chosen by walking `NetworkInterface.GetAllNetworkInterfaces()` and
picking the best candidate IPv4 unicast address, using the same heuristic and priority order as
ECU-Toolkit's `discovery.py` `_get_local_ip`:

- Skip interfaces whose name/description matches virtualization, VPN, or tunnel adapters (Hyper-V,
  WSL, VMware, VirtualBox, Tailscale, ZeroTier, Radmin, Hamachi, generic `tun`/`tap`, etc.) — these
  routinely outrank the real LAN adapter in enumeration order but aren't reachable from other
  machines.
- Skip loopback, link-local (`169.254.0.0/16`), and `172.16.0.0/12` (Hyper-V Default Switch /
  Docker NAT squat on this range entirely).
- Prefer `192.168.0.0/16` and `10.0.0.0/8` (common home/office LAN ranges) over other private
  ranges.
- If nothing suitable is found, fall back to `127.0.0.1` — the service still advertises (useful
  for same-machine discovery/testing) but won't be reachable from other hosts.

## Lifecycle

- Advertising starts only after the router has actually bound its HTTP port — see the `onBound`
  callback passed from `DraftPairApp` to `RouterHttpServer.StartAsync`. A process that detects an
  already-running router (see `IsRouterAlreadyRunningAsync` in `docs/router-contract.md`) and skips
  its own bind does **not** advertise, since it isn't the process actually serving the router.
- Advertising stops when `DraftPairApp` is disposed (process shutdown).
- A failure to start advertising (e.g. multicast blocked by network policy, no suitable adapter) is
  logged as a warning and does not affect router startup or availability — mDNS is a discovery
  convenience layered on top of the router, not a dependency of it working. Direct connections to
  a known `host:port` are unaffected either way.

## Configuration

`SimpleModelServer.config.json` → `global`:

```json
{
  "global": {
    "mdnsEnabled": true,
    "mdnsServiceName": ""
  }
}
```

- `mdnsEnabled` (bool, default `true`) — set `false` to disable advertising entirely (e.g. networks
  where multicast is blocked or undesirable).
- `mdnsServiceName` (string, default `""`) — overrides the advertised instance name. Empty uses
  `"SimpleModelServer {sanitized-hostname}"`.

## Prior art / why this design

RomRaider's `ECU-Toolkit` (`ECU-Toolkit/src/ecu_def_toolkit/sync/discovery.py`, built on Python's
`zeroconf` package) implements the fuller pattern this was modeled on: advertise **and** browse, so
multiple machines running the toolkit can find each other, sync shared data, and offload heavy
tasks (Ghidra analysis, LLM inference) to whichever peer has the right capabilities. SimpleModelServer's
mDNS support intentionally implements only the advertise half — SimpleModelServer.Loader doesn't have
sync/offload semantics to discover peers for; adding a browse side is future work if/when
cross-machine routing becomes a real requirement.

## Tests

`src/SimpleModelServer.Loader.Tests/MdnsAdvertiserTests.cs` covers instance-label sanitization, config
parsing (`mdnsEnabled` / `mdnsServiceName`), and the advertise/stop lifecycle. Advertise/stop tests
tolerate environments where multicast isn't actually available (locked-down CI sandboxes) — the
contract is "never throws past `Advertise()`/`Stop()`", not "always succeeds," matching how
`DraftPairApp.StartMdnsAdvertising` treats a thrown exception as a logged warning rather than a
fatal error.
