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

- **Phase 1 (current): connect + device info + time sync.** A console app that
  connects over TCP/IP, prints the device identity / configuration / record
  counts, reads the device clock and corrects drift.
- **Phase 2: attendance log pull into MySQL** with idempotent de-duplication.
- **Phase 3: PHP consumption + a loopback REST API** the admin UI can trigger
  (test connection, sync time, pull now, read device info), plus a Windows
  Service host.

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
