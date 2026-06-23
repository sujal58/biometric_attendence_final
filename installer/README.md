# Double-click installer

Turns the agent into a friendly `ShikzyaAgentSetup.exe` — the school double-clicks
it, the token is already filled (or pasted once), clicks Next, and the Windows
service installs and starts. No terminal.

## Build it (once, by your team)

1. Install **Inno Setup** (free): https://jrsoftware.org/isinfo.php
2. Produce the agent:
   ```
   powershell -File scripts\publish.ps1
   ```
3. Open `installer\AttendanceBridge.iss` in Inno Setup → **Build**
   (or from a command prompt: `ISCC installer\AttendanceBridge.iss`).
   Output: `installer\Output\ShikzyaAgentSetup.exe`.

You build this **once**; the same `ShikzyaAgentSetup.exe` works for every school.

## Deploy to a school

- **Interactive:** send them `ShikzyaAgentSetup.exe`. They double-click → paste the
  **site token** from Shikzya admin → Next → done.
- **Pre-keyed / silent** (no input at the school): run with the token baked in —
  ```
  ShikzyaAgentSetup.exe /VERYSILENT /token=<siteToken> /apiurl=https://app.shikzya.com
  ```
  This is ideal for remote/unattended install, or for handing the school a
  ready-keyed file.

## What it does

- Copies the self-contained agent to `C:\Program Files (x86)\ShikzyaAttendanceBridge`.
- Writes `appsettings.json` with the API URL + site token.
- Installs the **AttendanceBridge** Windows Service (auto-start, restart on failure)
  and starts it — by calling the agent's own `install` verb.
- Uninstalling removes the service.

## Under the hood

The installer just calls the agent's self-install:
```
AttendanceBridge.exe install --token <siteToken> --api <url>
AttendanceBridge.exe uninstall
```
So even without Inno Setup, installation is a single command (run as Administrator).
The agent is a single self-contained `.exe` (runtime + native DLLs bundled) with no
prerequisites beyond Windows.
