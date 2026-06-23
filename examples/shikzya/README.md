# Shikzya integration

Two parts:

- **`api/`** — the HTTPS API the **agents** call (devices, punches, commands,
  command result, heartbeat). See [api/README.md](api/README.md).
- **this folder** — examples for **Shikzya's own UI** (the school-facing app):
  queue a fetch, poll its status, read attendance.

## Data flow

```
School clicks "Fetch attendance" in Shikzya
        │  trigger_fetch.php  -> INSERT bio_fetch_command (pending)
        ▼
The school's agent (GET /api/bridge/v1/commands) claims it, pulls from the
device, POSTs punches, and reports the result (POST .../result)
        │
        ▼
command_status.php  (UI polls until done/error)
read_attendance.php (bio_punch ⨝ bio_enroll_map -> show attendance)
```

Scheduled pulls (per device `pull_times`) and a periodic heartbeat happen
automatically — no button needed.

## Files

| File | Purpose |
|---|---|
| `trigger_fetch.php` | queue a fetch command for a device (the button) |
| `command_status.php` | poll a command's status/result by id |
| `read_attendance.php` | read a school's punches for a day, joined to people |
| `api/` | the agent-facing HTTPS API + server schema usage |

## Onboarding a school (your team)

1. Insert a `bio_site` row with a random `site_token`.
2. Insert `bio_device` rows for that site (ip/port/license/pull_times).
3. Run `install-agent.ps1 -SiteToken <token> -ApiBaseUrl https://app.shikzya.com`
   on the school PC. The agent self-configures. Adding more devices later = more
   rows; no return visit.

`enroll_number` is the device-side id — map people in `bio_enroll_map` and show
unmapped ones in an admin screen.
