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

- **On open:** connects to the device and shows its info + status (serial, model,
  user/fingerprint counts, device clock vs PC clock), then auto-fetches attendance.
- **Fetch attendance** button — pull the logs on demand into the table.
- **Sync time** — set the device clock to the PC.
- **Settings** — the technician sets the device (IP / port / license) and where to
  push punches (Shikzya API URL + the site token + this device's id). Saved to
  `desktop-settings.json` next to the exe.
- Fetched punches are **pushed to Shikzya** (via the API, tagged by tenant through
  the site token), so they appear in the platform. If no token is set, it just
  shows them locally.

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
