# AttendanceBridge

Middleware that connects a **TimeWatch Bio-27** biometric attendance device to a
**PHP school management system**.

## Why a bridge is needed

The TimeWatch SDK talks to the device through **native 32-bit DLLs**
(`FKAttend.dll` plus siblings), called via P/Invoke. PHP can't load those
in-process, and neither can any 64-bit program. So this small **x86 .NET
Framework 4.8** service sits in the middle: it owns the device session, pulls
attendance data into the same MySQL database the school system already uses, and
exposes a tiny localhost API for admin actions.

## Roadmap

- **Phase 1 (done): connect + device info + time sync.** A console app that
  connects over TCP/IP, prints the device identity / configuration / record
  counts, reads the device clock and corrects drift.
- **Phase 2 (current): attendance log pull into MySQL** with idempotent
  de-duplication.
- **Phase 3: PHP consumption + a loopback REST API** the admin UI can trigger
  (test connection, sync time, pull now, read device info), plus a Windows
  Service host.

## Commands

```
AttendanceBridge.exe info    connect, print device info, sync the clock (Phase 1)
AttendanceBridge.exe pull    connect, sync clock, pull attendance logs into MySQL once
AttendanceBridge.exe poll    keep pulling on an interval until Ctrl+C
```

`info` needs no database. `pull` / `poll` require `database.connectionString` in
`appsettings.json`.

## Database (Phase 2)

The bridge **auto-creates its tables** on the first `pull` / `poll` (it runs the
bundled schema with `CREATE TABLE IF NOT EXISTS`). You only need to:

1. Make sure the **database** named in `database.connectionString` already
   exists (the bridge creates tables, not the database).
2. Point `database.connectionString` at it.

Applying [db/schema.sql](db/schema.sql) by hand is optional (kept as the single
source of truth and for manual setup). It creates:

| Table | Purpose |
|---|---|
| `bio_punch` | raw attendance punches (bridge writes, PHP reads) |
| `bio_enroll_map` | maps a device `enroll_number` to a student/staff person |
| `bio_device` | per-device connection details + last-pull health |
| `bio_bridge_log` | the bridge's own operational log |

The bridge pulls the **whole** log each cycle and relies on a unique
`dedup_key = SHA1(device_id|enroll_number|punch_time|in_out_mode)` so re-pulls
never create duplicates. It does **not** clear the device log unless
`poll.emptyAfterPull` is set. `enroll_number` is the device-side id, not the
student id — assign people in `bio_enroll_map`.

Phase 2 adds the **MySqlConnector** NuGet package; restore happens automatically
when you build the solution in Visual Studio.

## Requirements

- Windows with the **.NET Framework 4.8** runtime.
- To build: Visual Studio 2022 (or Build Tools) with the **.NET desktop
  development** workload, which includes the .NET Framework 4.8 targeting pack.
- The **Visual C++ x86 redistributable** the native DLLs depend on.
- The `native/` folder must contain the TimeWatch SDK DLLs (see below). They are
  copied next to the built `AttendanceBridge.exe` automatically.

## Configure

```
cd src/AttendanceBridge
copy appsettings.example.json appsettings.json
```

Edit `appsettings.json` with your real values:

| Setting | Meaning | Note |
|---|---|---|
| `device.ipAddress` / `device.netPort` | Device address on the LAN | SDK defaults `192.168.1.33` / `5005` |
| `device.machineNo` | Device ID | Usually `1` |
| `device.netPassword` | Comm password | `0` unless set on the device |
| `device.license` | SDK license integer | Start with `1261`; if connect fails, TimeWatch supplies it |
| `timeSync.maxDriftSeconds` | Clock correction threshold | Only re-sets the clock past this drift |

`appsettings.json` is git-ignored because it holds site-specific values.

## Build & run

Open `AttendanceBridge.sln` in Visual Studio (the **x86** solution platform) and
run, or from a Developer command prompt:

```
msbuild AttendanceBridge.sln /p:Configuration=Debug /p:Platform=x86
src\AttendanceBridge\bin\Debug\AttendanceBridge.exe
```

A successful run logs the device serial/model, record counts and the clock
drift. Without the physical device on the LAN the connection step fails with a
clear message (it must not throw `BadImageFormatException` — that would mean the
process is running 64-bit).

## `native/` DLLs

Copied from the vendor SDK; required at runtime next to the exe:

```
FKAttend.dll  FKViaDev.dll  LFWViaDev.dll  FaceDataConv.dll  FpDataConv.dll  FKPwdEncDec.dll
```
