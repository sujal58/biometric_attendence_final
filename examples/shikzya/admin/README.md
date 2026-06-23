# Admin UI (runnable)

A small browser-based admin app to register sites + devices per tenant and view
attendance. It auto-creates the database tables on first load.

## Run it (one command, then all browser)

1. Edit **`examples/shikzya/config.php`** with your MySQL details (the database
   must exist; tables are created automatically).
2. Start a web server pointed at the `examples/shikzya` folder. Easiest:
   ```
   php -S localhost:8000 -t examples/shikzya
   ```
   (or drop the `examples/shikzya` folder into your existing Shikzya web host —
   it's just PHP.)
3. Open **http://localhost:8000/admin/** in your browser.

That's it — no SQL to run, no other terminal steps.

## Use it

- **Dashboard** — counts for the current tenant.
- **Sites & Devices** — create a site (you get an **install token**), then add the
  school's devices (IP, license, pull times). Use the token in
  `install-agent.ps1` on that school's PC.
- **Attendance** — view punches for a day, mapped to people.

Switch the **Tenant** (top right) to see another school's data — every page is
isolated to the selected tenant.

## Pages

| File | Purpose |
|---|---|
| `index.php` | dashboard |
| `sites.php` | create sites + get install tokens |
| `devices.php?site_id=N` | add/enable/disable a site's devices |
| `attendance.php` | view a day's punches |

> This is reference UI — adapt it into Shikzya's admin area + design system and
> replace the tenant switcher with your real session/login.
