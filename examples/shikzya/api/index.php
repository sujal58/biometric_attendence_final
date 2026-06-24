<?php
/**
 * Front controller for the bridge API. Route /api/bridge/v1/* here. PHP 7.4+.
 * See docs/api-endpoints.md for the full reference.
 *
 * DESKTOP TOOL (auth = Bearer <licenseKey>):
 *   POST /activate   -> validate license, return tenant + the school's registered devices
 *   POST /punches    -> { deviceSerial, punches[] }  (device must be registered)
 *   POST /users      -> { deviceSerial, users[] }    (device roster -> bio_user)
 *
 * HEADLESS AGENT (auth = Bearer <siteToken>):
 *   GET  /devices, POST /punches (deviceId), GET /commands,
 *   POST /commands/{id}/result, POST /heartbeat
 */
require __DIR__ . '/_db.php';

// Path after the API base, robust to PATH_INFO vs a rewrite-to-index setup.
$base   = '/api/bridge/v1';
$uri    = parse_url($_SERVER['REQUEST_URI'] ?? '/', PHP_URL_PATH);
$path   = $_SERVER['PATH_INFO'] ?? (strpos($uri, $base) === 0 ? substr($uri, strlen($base)) : $uri);
if ($path === '' || $path === false) $path = '/';
$method = $_SERVER['REQUEST_METHOD'];

$lic = find_license(bearer_token());   // desktop license, or null

// ---- Desktop (license) endpoints ----
if ($path === '/activate' && $method === 'POST')          handle_activate(require_valid_license($lic));
elseif ($path === '/users' && $method === 'POST')         handle_users_license(require_valid_license($lic));
elseif ($path === '/punches' && $method === 'POST' && $lic) handle_punches_license(require_valid_license($lic));
// ---- Agent (site token) endpoints ----
elseif ($path === '/punches' && $method === 'POST')       handle_punches(require_site());
elseif ($path === '/devices' && $method === 'GET')        handle_devices(require_site());
elseif ($path === '/commands' && $method === 'GET')       handle_commands(require_site());
elseif ($method === 'POST' && preg_match('#^/commands/(\d+)/result$#', $path, $m))
                                                          handle_command_result(require_site(), (int)$m[1]);
elseif ($path === '/heartbeat' && $method === 'POST')     handle_heartbeat(require_site());
else json_error(404, 'not found');


// ---- Desktop (license) handlers ----

function handle_activate(array $lic): void
{
    $st = db()->prepare("SELECT mac, serial FROM bio_reg_device WHERE tenant_id = ? AND status = 'active'");
    $st->execute([$lic['tenant_id']]);
    $devices = array_map(fn($r) => ['mac' => $r['mac'], 'serial' => $r['serial']], $st->fetchAll());
    json_out(['valid' => true, 'tenantId' => $lic['tenant_id'], 'expiresAt' => $lic['expires_at'], 'devices' => $devices]);
}

/** A device must be registered (active) under this license's tenant. Returns its numeric id. */
function reg_device_or_403(array $lic, string $serial): array
{
    $st = db()->prepare("SELECT id FROM bio_reg_device WHERE serial = ? AND tenant_id = ? AND status = 'active'");
    $st->execute([$serial, $lic['tenant_id']]);
    $d = $st->fetch();
    if (!$d) json_error(403, "device '$serial' is not registered for your college");
    return $d;
}

function handle_punches_license(array $lic): void
{
    $in = read_json();
    $dev = reg_device_or_403($lic, $in['deviceSerial'] ?? '');
    $deviceId = (int)$dev['id'];
    $punches = $in['punches'] ?? [];

    $sql = 'INSERT INTO bio_punch
              (tenant_id,site_id,device_id,enroll_number,punch_time,verify_mode,verify_label,
               in_out_mode,io_mode,door_mode,raw_temperature)
            VALUES (?,0,?,?,?,?,?,?,?,?,?)
            ON DUPLICATE KEY UPDATE id = id';
    $st = db()->prepare($sql);
    $inserted = 0;
    db()->beginTransaction();
    foreach ($punches as $p) {
        $st->execute([
            $lic['tenant_id'], $deviceId, (int)$p['enrollNumber'], str_replace('T', ' ', $p['punchTime']),
            (int)$p['verifyMode'], $p['verifyLabel'] ?? null,
            (int)$p['inOutMode'], (int)($p['ioMode'] ?? 0), (int)($p['doorMode'] ?? 0), $p['temperature'] ?? null,
        ]);
        if ($st->rowCount() === 1) $inserted++;
    }
    db()->prepare('UPDATE bio_reg_device SET last_seen_at = NOW() WHERE id = ?')->execute([$deviceId]);
    db()->commit();
    json_out(['inserted' => $inserted]);
}

function handle_users_license(array $lic): void
{
    $in = read_json();
    $dev = reg_device_or_403($lic, $in['deviceSerial'] ?? '');
    $deviceId = (int)$dev['id'];
    $users = $in['users'] ?? [];

    $sql = 'INSERT INTO bio_user (tenant_id, device_id, enroll_number, name, privilege, enabled, updated_at)
            VALUES (?,?,?,?,?,?,NOW())
            ON DUPLICATE KEY UPDATE name=VALUES(name), privilege=VALUES(privilege), enabled=VALUES(enabled), updated_at=NOW()';
    $st = db()->prepare($sql);
    $n = 0;
    db()->beginTransaction();
    foreach ($users as $u) {
        $st->execute([$lic['tenant_id'], $deviceId, (int)$u['enrollNumber'], $u['name'] ?? null,
            (int)($u['privilege'] ?? 0), !empty($u['enabled']) ? 1 : 0]);
        $n++;
    }
    db()->commit();
    json_out(['inserted' => $n]);
}


function handle_devices(array $site): void
{
    $st = db()->prepare(
        'SELECT device_id,name,ip,port,machine_no,net_password,license,timeout_ms,protocol,pull_times,time_sync_drift,active
           FROM bio_device WHERE site_id = ? AND active = 1');
    $st->execute([$site['site_id']]);

    $devices = [];
    foreach ($st as $r) {
        $devices[] = [
            'deviceId'                => (int)$r['device_id'],
            'name'                    => $r['name'],
            'ip'                      => $r['ip'],
            'port'                    => (int)$r['port'],
            'machineNo'               => (int)$r['machine_no'],
            'netPassword'             => (int)$r['net_password'],
            'license'                 => (int)$r['license'],
            'timeoutMs'               => (int)$r['timeout_ms'],
            'protocol'                => (int)$r['protocol'],
            'pullTimes'               => array_values(array_filter(array_map('trim', explode(',', $r['pull_times'])))),
            'timeSyncMaxDriftSeconds' => (int)$r['time_sync_drift'],
            'active'                  => (bool)$r['active'],
        ];
    }
    db()->prepare('UPDATE bio_site SET last_seen_at = NOW() WHERE site_id = ?')->execute([$site['site_id']]);
    json_out(['devices' => $devices]);
}

function handle_punches(array $site): void
{
    $in = read_json();
    $deviceId = (int)($in['deviceId'] ?? 0);
    $punches  = $in['punches'] ?? [];

    $sql = 'INSERT INTO bio_punch
              (tenant_id,site_id,device_id,enroll_number,punch_time,verify_mode,verify_label,
               in_out_mode,io_mode,door_mode,raw_temperature)
            VALUES (?,?,?,?,?,?,?,?,?,?,?)
            ON DUPLICATE KEY UPDATE id = id';
    $st = db()->prepare($sql);
    $inserted = 0;

    db()->beginTransaction();
    foreach ($punches as $p) {
        $st->execute([
            $site['tenant_id'], $site['site_id'], $deviceId,
            (int)$p['enrollNumber'], str_replace('T', ' ', $p['punchTime']),
            (int)$p['verifyMode'], $p['verifyLabel'] ?? null,
            (int)$p['inOutMode'], (int)($p['ioMode'] ?? 0), (int)($p['doorMode'] ?? 0),
            $p['temperature'] ?? null,
        ]);
        if ($st->rowCount() === 1) $inserted++;
    }
    db()->prepare('UPDATE bio_device SET last_pull_at = NOW(), last_status = ? WHERE site_id = ? AND device_id = ?')
        ->execute(['read ' . count($punches) . ', inserted ' . $inserted, $site['site_id'], $deviceId]);
    db()->commit();

    json_out(['inserted' => $inserted]);
}

function handle_commands(array $site): void
{
    $sel = db()->prepare(
        "SELECT id, device_id FROM bio_fetch_command
          WHERE site_id = ? AND status = 'pending' ORDER BY id LIMIT 20");
    $sel->execute([$site['site_id']]);
    $cmds = $sel->fetchAll();

    if ($cmds) {
        $ids = array_column($cmds, 'id');
        $ph  = implode(',', array_fill(0, count($ids), '?'));
        db()->prepare("UPDATE bio_fetch_command SET status = 'running' WHERE id IN ($ph)")->execute($ids);
    }
    json_out(['commands' => array_map(
        fn($c) => ['id' => (int)$c['id'], 'deviceId' => (int)$c['device_id']], $cmds)]);
}

function handle_command_result(array $site, int $id): void
{
    $in = read_json();
    db()->prepare(
        "UPDATE bio_fetch_command
            SET status = ?, finished_at = NOW(), records_read = ?, records_inserted = ?, result_message = ?
          WHERE id = ? AND site_id = ?")
        ->execute([
            !empty($in['ok']) ? 'done' : 'error',
            (int)($in['recordsRead'] ?? 0), (int)($in['recordsInserted'] ?? 0),
            substr($in['message'] ?? '', 0, 255), $id, $site['site_id'],
        ]);
    json_out(['ok' => true]);
}

function handle_heartbeat(array $site): void
{
    $in = read_json();
    db()->prepare('UPDATE bio_site SET last_seen_at = NOW(), agent_version = ? WHERE site_id = ?')
        ->execute([substr($in['agentVersion'] ?? '', 0, 32), $site['site_id']]);

    foreach (($in['devices'] ?? []) as $d) {
        db()->prepare(
            'UPDATE bio_device SET last_pull_at = COALESCE(?, last_pull_at), last_status = ?
              WHERE site_id = ? AND device_id = ?')
            ->execute([
                !empty($d['lastPullAt']) ? str_replace('T', ' ', $d['lastPullAt']) : null,
                substr($d['lastStatus'] ?? '', 0, 255), $site['site_id'], (int)$d['deviceId'],
            ]);
    }
    json_out(['ok' => true]);
}
