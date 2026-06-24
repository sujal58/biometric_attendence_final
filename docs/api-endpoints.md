# Shikzya Bridge API — endpoint reference

The contract the **desktop tool** (`ShikzyaDeviceTool`) calls. The app holds **no
DB credentials**; it reads attendance from the biometric devices on the LAN and
POSTs to these endpoints. The Shikzya backend authenticates, validates, and writes
its own DB. Reference implementation: [examples/shikzya/api/index.php](../examples/shikzya/api/index.php).
Base path: `/api/bridge/v1`. All bodies are JSON (`Content-Type: application/json`).

## Authentication

Every desktop request sends the school's **license key** as a bearer token:

```
Authorization: Bearer <licenseKey>
```

The backend resolves the **tenant from the key** (it is never trusted from the body).
The key must be `active` and not past `expires_at`, or the request is rejected.

## Lifecycle (how control works)

1. Super admin registers a school → generates a **license key** (`bio_license`, one
   per school).
2. Super admin **registers that school's devices** by **serial + MAC**
   (`bio_reg_device`, `serial` UNIQUE → a device belongs to exactly one school).
   The serial/MAC are read from the desktop tool's **Refresh info / Test device**.
3. The app **activates** with the key, caches the allowed device list, and only
   pushes for **registered** devices. Revoking the key or a device (set
   `status='revoked'`) blocks it at the next check (the app has a 7-day offline grace).

---

## POST /activate

Validate the license and return the school's registered devices.

**Request**
```
POST /api/bridge/v1/activate
Authorization: Bearer <licenseKey>
{ "appVersion": "1.0.0" }
```

**200 Response**
```json
{
  "valid": true,
  "tenantId": "GREENFIELD",
  "expiresAt": "2027-12-31",          // or null
  "devices": [
    { "mac": "AA:BB:CC:DD:EE:FF", "serial": "FK6230001234" }
  ]
}
```

**Errors** — `401 {"error":"invalid license key"}`, `403 {"error":"license revoked …"}`,
`403 {"error":"license expired"}`.

```bash
curl -s https://app.shikzya.com/api/bridge/v1/activate \
  -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
  -d '{"appVersion":"1.0.0"}'
```

---

## POST /punches

Upload attendance for one device. Idempotent — re-sending the same punches inserts
no duplicates (unique key `tenant_id, device_id, enroll_number, punch_time, in_out_mode`).

**Request**
```
POST /api/bridge/v1/punches
Authorization: Bearer <licenseKey>
{
  "deviceSerial": "FK6230001234",
  "deviceMac": "AA:BB:CC:DD:EE:FF",
  "punches": [
    {
      "enrollNumber": 1,
      "punchTime": "2026-06-24T09:30:00",   // device local, ISO (no timezone)
      "verifyMode": 268435456,               // raw FK code
      "verifyLabel": "FP",                   // decoded (FP/FACE/CARD/…)
      "inOutMode": 2321,                     // raw FK code
      "ioMode": 17,                          // decoded low byte
      "doorMode": 9,                         // decoded high bytes
      "temperature": null                    // tenths of a degree, or null
    }
  ]
}
```

**200 Response** — `{ "inserted": 12 }` (count of NEW rows).

**Errors** — `401` invalid key; `403 {"error":"device '<serial>' is not registered
for your college"}` (serial not in this tenant's active devices); `403` revoked/expired.

```bash
curl -s https://app.shikzya.com/api/bridge/v1/punches \
  -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
  -d '{"deviceSerial":"FK6230001234","deviceMac":"AA:BB:CC:DD:EE:FF","punches":[…]}'
```

### PunchDto fields
| field | type | meaning |
|---|---|---|
| `enrollNumber` | int | device-side user id |
| `punchTime` | string | `yyyy-MM-ddTHH:mm:ss`, device local |
| `verifyMode` | int | raw verify code (bit-packed on newer firmware) |
| `verifyLabel` | string | decoded label (e.g. `FP`, `FACE`, `Card+FP`) |
| `inOutMode` | int | raw in/out code |
| `ioMode` | int | decoded in/out (low byte) |
| `doorMode` | int | decoded door mode (high bytes) |
| `temperature` | int? | tenths of a degree, or null |

---

## POST /users

Upload a device's enrolled-user roster → `bio_user` (so attendance can show names
and map device users to students).

**Request**
```
POST /api/bridge/v1/users
Authorization: Bearer <licenseKey>
{
  "deviceSerial": "FK6230001234",
  "users": [
    { "enrollNumber": 1, "name": "Asha R", "privilege": 0, "enabled": true }
  ]
}
```

**200 Response** — `{ "inserted": 245 }`. **Errors** — same as `/punches`.

### UserDto fields
| field | type | meaning |
|---|---|---|
| `enrollNumber` | int | device-side user id |
| `name` | string | user name on the device |
| `privilege` | int | 0 = user, 1 = manager |
| `enabled` | bool | user enabled on the device |

---

## Server-side rules (reference impl)

- Resolve tenant from the key (`bio_license`); reject if not `active` / expired.
- For `/punches` + `/users`: the `deviceSerial` must exist in `bio_reg_device` as
  `status='active'` for that tenant; otherwise `403`. Its row `id` is the device id
  written to `bio_punch` / `bio_user`. Update `bio_reg_device.last_seen_at`.
- `/punches` upsert is `INSERT … ON DUPLICATE KEY UPDATE id=id` (idempotent).
- Tables: `bio_license`, `bio_reg_device`, `bio_punch`, `bio_user`
  (see [db/schema.sql](../db/schema.sql)). Manage keys/devices in
  [examples/shikzya/admin/licenses.php](../examples/shikzya/admin/licenses.php).

## Note: headless agent endpoints (separate product)

The fleet **agent** uses the same base path with a **site token** instead of a
license key: `GET /devices`, `POST /punches` (`deviceId`), `GET /commands`,
`POST /commands/{id}/result`, `POST /heartbeat`. The router accepts either auth;
the license (desktop) flow is matched first. The desktop tool uses only the three
endpoints above.
