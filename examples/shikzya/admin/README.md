# Admin: device registry

Reference admin pages for **your team** to register sites and devices per tenant.
Adapt into Shikzya's admin area + design system; they show the model and the
isolation rules.

## Model

```
Tenant (school)  ->  Site (one agent/PC)  ->  Devices (machines on that LAN)
```

- A school with 4 devices = 1 site + 4 device rows. A school with 2 = 1 site + 2.
- `device_id` is per-site (`PRIMARY KEY (site_id, device_id)`), so ids never clash
  across tenants.

## Pages

| File | Purpose |
|---|---|
| `sites.php` | list/create sites for a tenant; creating one generates the `site_token` for `install-agent.ps1` |
| `devices.php?site_id=N` | list/add/enable/disable devices on a site (ip/port/license/pull_times) |

## Isolation (important)

- `$tenantId` must come from the **authenticated session**, never the request.
- `devices.php` verifies the `site_id` belongs to `$tenantId` before listing or
  changing anything, so one tenant can't reach another's devices.
- Every insert stamps `tenant_id` + `site_id`; every read filters by them.

## Onboarding flow

1. **sites.php** → create the school's site → copy the install token.
2. Run on the school PC: `install-agent.ps1 -SiteToken <token> -ApiBaseUrl https://app.shikzya.com`.
3. **devices.php** → add each device with its static LAN IP + license + pull times.
4. The agent self-configures from `GET /devices`; new devices appear within minutes.

No return visit is needed to add devices later — just add rows here.
