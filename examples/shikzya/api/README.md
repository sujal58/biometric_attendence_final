# Bridge API (agent-facing)

Reference implementation of the HTTPS API the agents call. Adapt the DB
connection + token storage to your Shikzya framework; this is the contract.

## Routing

Mount the folder so that `https://<host>/api/bridge/v1/*` reaches `index.php`.
With Apache, the included `.htaccess` does this (and preserves the `Authorization`
header). With Nginx/PHP-FPM, route `/api/bridge/v1` to `index.php` with PATH_INFO,
or `try_files` to `index.php`. The router also works if you wire each path in your
framework and just call the matching `handle_*` function.

## Endpoints

| Method + path | Purpose |
|---|---|
| `GET /devices` | device list for the calling site |
| `POST /punches` | upload punches (idempotent; server de-duplicates) |
| `GET /commands` | pending fetch commands (claimed = marked `running`) |
| `POST /commands/{id}/result` | report a command's outcome |
| `POST /heartbeat` | agent health ping |

All requests authenticate with `Authorization: Bearer <siteToken>`; the server
resolves the site + tenant from the token (`require_site()`).

## Provisioning a school (your team, in Shikzya)

1. Create a `bio_site` row: `tenant_id`, `name`, a random `site_token`, `active=1`.
2. Create `bio_device` rows for that `site_id` (ip/port/license/pull_times...).
3. Install the agent on the school PC with that token:
   `install-agent.ps1 -SiteToken <token> -ApiBaseUrl https://app.shikzya.com`

That's it — the agent self-configures from `GET /devices`.

## Notes

- **Stuck commands:** `GET /commands` marks rows `running`. Add a sweep that
  resets `running` rows older than a few minutes back to `pending` (or `error`)
  so a crashed agent doesn't strand a request.
- **Token storage:** store a hash of `site_token` in production and compare hashes.
