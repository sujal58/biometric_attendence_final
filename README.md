# AttendanceBridge

Middleware that connects a **TimeWatch Bio-27** biometric attendance device to
**Shikzya**, a multi-tenant school management platform. One bridge is deployed
per school (on the LAN with the device); all schools' data lands in MySQL that
Shikzya reads, tagged by tenant.

## Why a bridge is needed

The TimeWatch SDK talks to the device through **native 32-bit DLLs**
(`FKAttend.dll` plus siblings), called via P/Invoke. PHP can't load those
in-process, and neither can any 64-bit program. So this small **x86 .NET
Framework 4.8** agent sits in the middle: it owns the device session, pulls
attendance into MySQL, and exposes both a local web page and an on-demand
command queue so schools can fetch on demand.

## How it works

```
School fetches  ─┬─►  Shikzya button ──► bio_fetch_command (DB)  ─┐
(on demand)      └─►  local web page at http://<school-pc>:8080/ ─┤
                                                                  ▼
                                          AttendanceBridge `serve` agent
                                          (also pulls at scheduled times)
                                                                  │ pulls via FKAttend.dll
                                                                  ▼
                                          TimeWatch device on the school LAN
                                                                  │
                                                                  ▼
                                          MySQL bio_punch (tenant-tagged)
                                                                  ▼
                                          Shikzya reads & shows attendance
```

## Commands

```
AttendanceBridge.exe info    connect, print device info, sync the clock
AttendanceBridge.exe pull    connect, sync clock, pull attendance into MySQL once
AttendanceBridge.exe poll    keep pulling on a fixed interval until Ctrl+C
AttendanceBridge.exe serve   unattended agent: scheduled pulls + on-demand fetch
                             commands from Shikzya + local web UI (use in production)
```

`info` needs no database. `pull` / `poll` / `serve` require a configured
`database.connectionString`. From the repo root you can also use `.\bridge.cmd <command>`.

## Configure

```
cd src/AttendanceBridge
copy appsettings.example.json appsettings.json
```

Key settings in `appsettings.json` (git-ignored — holds per-site values):

| Section | Setting | Meaning |
|---|---|---|
| `tenant` | `tenantId` | The school's id in Shikzya. Tags every punch/command. |
| `device` | `ipAddress`/`netPort` | Device on the LAN (SDK defaults `192.168.1.33`/`5005`). |
| `device` | `netPassword` / `license` | Comm password (`0` unless set) / SDK license (try `1261`). |
| `database` | `connectionString` | MySQL Shikzya reads from. `deviceId` defaults to `machineNo`. |
| `schedule` | `pullTimes` | Local times to auto-pull, e.g. `["12:00","17:00"]`. |
| `command` | `pollSeconds` | How often `serve` checks for Shikzya fetch commands. |
| `web` | `url` | Local web UI prefix (`http://localhost:8080/`, or `http://+:8080/` for LAN). |

## Database

The bridge **auto-creates its tables** on first DB use (`CREATE TABLE IF NOT
EXISTS`). You only need the database in the connection string to already exist.
[db/schema.sql](db/schema.sql) is the single source of truth:

| Table | Purpose |
|---|---|
| `bio_punch` | raw attendance punches (tenant-tagged; bridge writes, Shikzya reads) |
| `bio_enroll_map` | maps a device `enroll_number` to a student/staff person |
| `bio_device` | per-device connection details + last-pull health |
| `bio_fetch_command` | on-demand fetch queue (Shikzya inserts; bridge services) |
| `bio_bridge_log` | the bridge's own operational log |

Every row carries `tenant_id` so one shared database holds many schools. Pulls
read the **whole** log and de-duplicate on
`dedup_key = SHA1(tenant_id|device_id|enroll_number|punch_time|in_out_mode)`, so
re-pulls never create duplicates. The device log is never cleared unless
`poll.emptyAfterPull` is set. `enroll_number` is the device-side id — map people
in `bio_enroll_map`. Verify/in-out codes are bit-packed by the device and stored
both raw and decoded (`verify_label`, `io_mode`, `door_mode`).

## Production deployment (per school)

1. Build Release and copy the output folder to a stable path (e.g. `C:\AttendanceBridge`).
2. Put a configured `appsettings.json` next to the exe.
3. Install the agent so it runs at boot (admin PowerShell):
   ```
   powershell -ExecutionPolicy Bypass -File scripts\install-task.ps1 -ExePath C:\AttendanceBridge\AttendanceBridge.exe
   ```
   This registers a scheduled task that runs `serve` at startup and restarts it
   if it stops. Remove with `scripts\uninstall-task.ps1`.

### Letting school staff open the web page from another PC
By default the UI binds to `http://localhost:8080/` (that PC only). For LAN
access set `web.url` to `http://+:8080/` and, once as admin:
```
netsh http add urlacl url=http://+:8080/ user=Everyone
netsh advfirewall firewall add rule name="AttendanceBridge" dir=in action=allow protocol=TCP localport=8080
```
Staff then open `http://<school-pc-ip>:8080/`.

## Shikzya integration

See [examples/shikzya/](examples/shikzya/) for reference PHP: queue a fetch
command (the button), poll its status, and read a school's attendance joined to
`bio_enroll_map`.

## Requirements

- Windows with the **.NET Framework 4.8** runtime + the **Visual C++ x86 redistributable**.
- To build: Visual Studio 2022 / Build Tools with the **.NET desktop development** workload.
- The `native/` folder must contain the TimeWatch SDK DLLs (copied next to the exe automatically):
  ```
  FKAttend.dll  FKViaDev.dll  LFWViaDev.dll  FaceDataConv.dll  FpDataConv.dll  FKPwdEncDec.dll
  ```

## Build & run

```
dotnet build
.\src\AttendanceBridge\bin\x86\Debug\net48\AttendanceBridge.exe info
```
The project is x86-only (the native DLLs are 32-bit). A run that throws
`BadImageFormatException` means it built/ran 64-bit.
