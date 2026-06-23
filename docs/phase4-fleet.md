# Phase 4 — Fleet model (zero-touch, multi-tenant, multi-device)

Goal: deploy biometric devices across many schools/colleges with **no technical
person at the school**. Device configuration lives **centrally in Shikzya**; each
school PC runs **one tiny pre-keyed agent** that configures itself from the cloud
and services all of that site's devices.

## Architecture

```
Shikzya (cloud, multi-tenant)
  ├── Admin UI: register tenants, sites, devices (IP/port/license/schedule)
  └── Bridge API (HTTPS, token-auth)
            ▲   ▲   ▲
            │   │   │   outbound 443 only (nothing exposed at the school)
   ┌────────┘   │   └─────────┐
   │            │             │
 Agent@SchoolA Agent@SchoolB Agent@CollegeC      <- one self-contained .exe per site
   │ services all devices on its LAN
   ▼
 TimeWatch devices on the school LAN
```

- **One agent per site.** Identified by a single opaque **site token** baked into
  its installer. The token maps (server-side) to a tenant + site.
- **Self-configuring.** On start, and periodically, the agent calls the API for
  its device list and services every device: scheduled pulls, on-demand fetch,
  time sync. Adding a device = a row in Shikzya; the agent picks it up.
- **Outbound HTTPS only.** No MySQL exposed to schools, no firewall/urlacl work.
- **Field install:** your installer physically mounts the device, sets its IP and
  registers it in Shikzya (technical, once). The **school PC software is
  zero-touch**: run the pre-keyed installer → it installs + auto-starts → done.

## Local agent config (the ONLY thing on the school PC)

Baked into the installer per school; no device details here:

```json
{
  "apiBaseUrl": "https://app.shikzya.com",
  "siteToken": "<opaque-per-site-secret>",
  "logging": { "directory": "logs" }
}
```

Everything device-specific (IP, license, schedule, tenant) comes from the API.

## Bridge API contract (Shikzya implements; agent consumes)

All requests send the site token: `Authorization: Bearer <siteToken>`. The server
derives tenant + site from the token. All payloads JSON over HTTPS.

### `GET /api/bridge/v1/devices`
Returns the devices this site must service.
```json
{ "devices": [
  { "deviceId": 1, "name": "Main Gate", "ip": "192.168.1.33", "port": 5005,
    "machineNo": 1, "netPassword": 0, "license": 1261, "timeoutMs": 5000,
    "protocol": 0, "pullTimes": ["12:00","17:00"], "timeSyncMaxDriftSeconds": 30,
    "active": true } ] }
```

### `POST /api/bridge/v1/punches`
Uploads decoded punches after a pull. Server upserts (idempotent) and tags with
the token's tenant. Recommended server-side unique key:
`(tenant_id, device_id, punch_time, enroll_number, in_out_mode)`.
```json
{ "deviceId": 1, "punches": [
  { "enrollNumber": 1, "punchTime": "2026-06-23T15:30:00",
    "verifyMode": 268435456, "verifyLabel": "FP",
    "inOutMode": 2321, "ioMode": 17, "doorMode": 9, "temperature": null } ] }
```
Response: `{ "inserted": 12 }`

### `GET /api/bridge/v1/commands`
Pending on-demand fetches (the Shikzya "Fetch attendance" button) for this site.
```json
{ "commands": [ { "id": 99, "deviceId": 1 } ] }
```

### `POST /api/bridge/v1/commands/{id}/result`
```json
{ "ok": true, "recordsRead": 14, "recordsInserted": 12, "message": "ok" }
```

### `POST /api/bridge/v1/heartbeat` (monitoring)
```json
{ "agentVersion": "1.0.0", "devices": [
  { "deviceId": 1, "lastPullAt": "2026-06-23T17:00:05", "lastStatus": "read 14, inserted 0" } ] }
```

## Reliability

- **Offline spool.** School internet is unreliable, so the agent pulls regardless
  of connectivity and **queues unsent punches to a local file/SQLite**, retrying
  upload until the API confirms. No punches lost during an outage.
- **Token rotation/revocation** handled in Shikzya; a revoked token stops an agent.
- **Idempotent uploads** — re-sending the same batch never duplicates rows.

## What we keep from Phases 1-3

The proven device layer is unchanged: interop (`FkAttend`), `LogPoller`, bit-packed
decode (`FkLogDecoder`), dedup, `FetchService`, time sync. Phase 4 swaps the
*edges*: config source (local JSON → API), single-device → device loop,
MySQL-direct → API client + offline spool, Task Scheduler → bundled service.

## Packaging

- **.NET 8, published `win-x86`, self-contained, single-file** → one `.exe` that
  bundles the runtime (no .NET install, no targeting pack on the school PC).
  Ship: `AttendanceBridge.exe` + the 6 native TimeWatch DLLs + `appsettings.json`.
- Wrap in a small **pre-keyed installer** (Inno Setup / MSI) that drops the files,
  writes the per-school `siteToken`, installs a **Windows Service**, and starts it.
- Requirement on the PC: only the **VC++ x86 runtime** (the native DLLs need it).

## Build sequence

1. **Interop date fix** — replace `DateTime` P/Invoke marshalling with explicit
   OLE-date `double` (`ToOADate`/`FromOADate`). Required for .NET Core; also works
   on .NET Framework, so we verify it on hardware before retargeting.
2. **Retarget to .NET 8** (`net8.0-windows`, `win-x86`).
3. **API client + multi-device agent** — fetch device list, loop devices, upload
   punches, poll commands, heartbeat.
4. **Offline spool** + retry.
5. **Single-file publish + pre-keyed installer + Windows Service**.
6. **Shikzya side:** the API endpoints above + an admin device registry (your PHP
   team; this repo provides the contract and example handlers).
