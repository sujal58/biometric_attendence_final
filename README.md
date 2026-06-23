# AttendanceBridge

A small **Windows agent** that connects **TimeWatch** biometric devices to
**Shikzya**, a multi-tenant school platform — built for **fleet deployment**
across many schools/colleges with **no technical staff on site**.

Devices are configured **centrally in Shikzya**. Each school PC runs **one
pre-keyed agent** (a single self-contained `.exe`) that configures itself from
the cloud and services **all** of that site's devices.

## Why an agent is needed

The TimeWatch SDK is a set of **native 32-bit Windows DLLs** (`FKAttend.dll` +
siblings) called via P/Invoke. PHP/cloud code can't load them. So a small
**.NET 8, win-x86, self-contained** agent runs on a PC at each school and bridges
the device to Shikzya over HTTPS.

## How it works

```
Shikzya (cloud, multi-tenant)
  • Admin registers sites + devices (IP/port/license/schedule)
  • HTTPS Bridge API  ◄───── outbound 443 only ─────┐
                                                     │
   Agent @ School A      Agent @ School B      Agent @ College C   (one .exe per site)
        │ services every device on its LAN
        ▼
   TimeWatch devices ──► punches ──► API ──► bio_punch ──► Shikzya shows attendance
```

- **Self-configuring:** the agent calls `GET /devices` and services each one —
  scheduled pulls, on-demand "Fetch attendance" commands, and time sync.
- **Zero-touch:** adding a device is a row in Shikzya. The school PC is never edited.
- **Resilient:** if the internet drops, punches are spooled to disk and retried —
  nothing is lost.
- **Outbound only:** no MySQL exposed to schools, no inbound ports, no firewall work.

See [docs/phase4-fleet.md](docs/phase4-fleet.md) for the architecture and the full
API contract.

## Repo layout

| Path | What |
|---|---|
| `src/AttendanceBridge/` | the agent (.NET 8, win-x86) |
| `native/` | the 6 TimeWatch native DLLs (copied next to the exe) |
| `db/schema.sql` | Shikzya server-side tables |
| `examples/shikzya/api/` | reference PHP for the agent-facing HTTPS API |
| `examples/shikzya/` | reference PHP for Shikzya's own UI (button, status, read) |
| `scripts/` | publish + pre-keyed service installer |

## Build (on a machine with the .NET 8 SDK)

```
dotnet build src/AttendanceBridge/AttendanceBridge.csproj      # x86, net8
```

Diagnostic (verify a device without any API/config — handy on-site):
```
dotnet run --project src/AttendanceBridge -- test --ip 192.168.1.33 --license 1261
```

## Produce the deployable agent

```
powershell -File scripts/publish.ps1
```
Outputs `scripts/publish/` = one `AttendanceBridge.exe` (self-contained, bundles
the .NET runtime) + the native DLLs + `appsettings.example.json`. The only PC
prerequisite is the **Visual C++ x86 runtime**.

## Install at a school (your team, once, as admin)

```
powershell -ExecutionPolicy Bypass -File scripts/install-agent.ps1 `
    -SiteToken "<per-school-token>" -ApiBaseUrl "https://app.shikzya.com"
```
This writes the pre-keyed `appsettings.json`, installs the **`AttendanceBridge`
Windows Service** (auto-start, restart-on-failure), and starts it. Remove with
`scripts/uninstall-agent.ps1`.

The agent's only local config is the API URL + site token:
```json
{ "apiBaseUrl": "https://app.shikzya.com", "siteToken": "..." }
```
Everything device-specific comes from Shikzya.

## Shikzya side

Apply [db/schema.sql](db/schema.sql), implement the API from
[examples/shikzya/api/](examples/shikzya/api/), and wire the UI helpers in
[examples/shikzya/](examples/shikzya/). Onboarding a school = create a `bio_site`
row + its `bio_device` rows, then run the installer with that site's token.
