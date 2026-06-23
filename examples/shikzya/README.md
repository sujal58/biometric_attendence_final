# Shikzya integration examples

Reference PHP for wiring the multi-tenant Shikzya platform to the bridge. Adapt
the DB connection and auth to your framework; treat these as the contract, not
drop-in files.

## Data flow

```
School clicks "Fetch attendance" in Shikzya
        │
        ▼
trigger_fetch.php  ──INSERT──►  bio_fetch_command (status=pending, tenant_id, device_id)
                                        │
              the school's bridge (serve) polls this table for its tenant+device
                                        │
                                        ▼
                       bridge pulls from the device, writes bio_punch,
                       marks the command done (records_read / inserted)
                                        │
command_status.php  ◄──SELECT──  bio_fetch_command   (UI polls until done/error)
read_attendance.php ◄──SELECT──  bio_punch ⨝ bio_enroll_map   (show attendance)
```

The school can also fetch from the **local web page** the bridge hosts
(`http://<school-pc>:8080/`) — same effect, no Shikzya round-trip.

## Files

| File | Purpose |
|---|---|
| `trigger_fetch.php` | Queue a fetch command for a school's device (the button). |
| `command_status.php` | Poll a command's status/result by id. |
| `read_attendance.php` | Read a school's punches for a day, joined to `bio_enroll_map`. |

## Multi-tenancy

Every row carries `tenant_id` (the school). Always filter by the logged-in
school's `tenant_id`. Each school's bridge is configured with its own `tenantId`
in `appsettings.json`, so a command/punch for school A is only ever handled by
school A's bridge.

## Mapping device users to people

`bio_punch.enroll_number` is the id on the device, not your student/staff id.
Populate `bio_enroll_map` (per `tenant_id` + `device_id`) to map them. Show
unmapped enroll numbers in an admin screen so staff can assign them.
