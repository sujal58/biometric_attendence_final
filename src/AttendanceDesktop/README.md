# Shikzya Device Tool (desktop)

A simple **double-click desktop app** for schools and technicians. It builds to a
**single self-contained `ShikzyaDeviceTool.exe`** — carry it on a pendrive, copy it
to any Windows desktop, double-click. No install, no service, and **no
prerequisites**: the .NET runtime *and* the native device DLLs are bundled inside
the one file (the native DLLs use only standard Windows system libraries — no VC++
redistributable needed; verify on a clean PC to be sure).

The PC must be on the **same network as the device** (it talks to the device via
the 32-bit SDK).

## What it does

- Handles **multiple devices** for one client (tenant). Pick a device from the
  dropdown for info/sync; **Fetch all devices** pulls every device in turn.
- **On open:** auto-fetches all devices into the table (with a Device column).
- **Refresh info / Sync time** — show the selected device's info, or set its clock.
- **Settings** — the technician sets the client `tenantId`, the **push target**
  (MySQL / API / Both), the **MySQL connection string**, the API URL + site token,
  and the **device list** (Name / IP / Port / Machine# / Password / License /
  DeviceId). Saved to `desktop-settings.json` next to the exe.
- Fetched punches are pushed to **MySQL directly** (idempotent upsert into
  `bio_punch`, tenant + device tagged; tables auto-create) and/or the **Shikzya
  API**, per the push target.

### Security
The MySQL connection string is stored **DPAPI-encrypted** (current-user) in
`desktop-settings.json` — never plaintext. Use a **restricted DB user**
(INSERT/UPDATE on `bio_*` only) and `SslMode=Required`. For untrusted sites, use
the **API** push target so no DB credentials sit on the PC.

## Build the .exe

```
powershell -File scripts\publish-desktop.ps1
```
Output: a single `scripts\publish-desktop\ShikzyaDeviceTool.exe` (everything bundled
inside). Copy just that one file to the school PC / a pendrive and double-click it.

## When to use which

- **Desktop tool (this):** the school opens an app and clicks Fetch — simplest,
  manual, no background service. Good when staff are happy to open an app.
- **Agent / service** (`AttendanceBridge`): unattended, scheduled + on-demand from
  Shikzya, runs headless. Good for hands-off sites.

Both reuse the same device code and push to the same API, so you can mix per site.
