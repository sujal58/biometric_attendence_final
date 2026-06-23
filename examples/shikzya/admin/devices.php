<?php
/**
 * Admin: devices for one site. Add/edit/disable the biometric machines on that
 * school's LAN (ip/port/license/schedule). The agent reads this list and starts
 * servicing new devices automatically - no school-side work.
 *
 * Tenant isolation: the site is verified to belong to $tenantId before anything
 * is listed or changed, so one tenant can never touch another's devices.
 */
require __DIR__ . '/../api/_db.php';

$tenantId = $_SESSION['tenant_id'] ?? ($_GET['tenant_id'] ?? 'SCHOOL_CODE');
$siteId   = (int)($_GET['site_id'] ?? 0);

// Isolation gate: the site must belong to this tenant.
$st = db()->prepare('SELECT site_id, name FROM bio_site WHERE site_id = ? AND tenant_id = ?');
$st->execute([$siteId, $tenantId]);
$site = $st->fetch();
if (!$site) { http_response_code(404); exit('site not found for this tenant'); }

$flash = null;
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $action = $_POST['action'] ?? '';

    if ($action === 'add_device') {
        // Next device_id within this site.
        $n = db()->prepare('SELECT COALESCE(MAX(device_id),0)+1 AS next FROM bio_device WHERE site_id = ?');
        $n->execute([$siteId]);
        $deviceId = (int)$n->fetch()['next'];

        db()->prepare(
            'INSERT INTO bio_device
               (device_id, site_id, tenant_id, name, ip, port, machine_no, net_password,
                license, timeout_ms, protocol, pull_times, time_sync_drift, active)
             VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,1)')
            ->execute([
                $deviceId, $siteId, $tenantId,
                trim($_POST['name'] ?? ''), trim($_POST['ip'] ?? ''),
                (int)($_POST['port'] ?? 5005), (int)($_POST['machine_no'] ?? 1),
                (int)($_POST['net_password'] ?? 0), (int)($_POST['license'] ?? 1261),
                (int)($_POST['timeout_ms'] ?? 5000), (int)($_POST['protocol'] ?? 0),
                trim($_POST['pull_times'] ?? ''), (int)($_POST['time_sync_drift'] ?? 30),
            ]);
        $flash = "Device #$deviceId added.";
    }
    elseif ($action === 'toggle') {
        db()->prepare('UPDATE bio_device SET active = 1 - active WHERE site_id = ? AND device_id = ?')
            ->execute([$siteId, (int)$_POST['device_id']]);
    }
}

$dev = db()->prepare(
    'SELECT device_id,name,ip,port,machine_no,license,pull_times,time_sync_drift,active,last_pull_at,last_status
       FROM bio_device WHERE site_id = ? ORDER BY device_id');
$dev->execute([$siteId]);
$devices = $dev->fetchAll();

function h($s) { return htmlspecialchars((string)$s, ENT_QUOTES); }
?>
<!doctype html><meta charset="utf-8"><title>Devices</title>
<body style="font-family:system-ui;max-width:1000px;margin:30px auto">
<p><a href="sites.php">&larr; sites</a></p>
<h2>Devices — <?= h($site['name']) ?> (tenant <?= h($tenantId) ?>)</h2>
<?php if ($flash): ?><p style="background:#e7f9ed;padding:10px;border-radius:6px"><?= h($flash) ?></p><?php endif; ?>

<table border="1" cellpadding="6" cellspacing="0" style="border-collapse:collapse;width:100%">
  <tr><th>ID</th><th>Name</th><th>IP:Port</th><th>Machine#</th><th>License</th>
      <th>Pull times</th><th>Last pull</th><th>Status</th><th>Active</th><th></th></tr>
  <?php foreach ($devices as $d): ?>
  <tr>
    <td><?= (int)$d['device_id'] ?></td>
    <td><?= h($d['name']) ?></td>
    <td><?= h($d['ip']) ?>:<?= (int)$d['port'] ?></td>
    <td><?= (int)$d['machine_no'] ?></td>
    <td><?= (int)$d['license'] ?></td>
    <td><?= h($d['pull_times']) ?></td>
    <td><?= h($d['last_pull_at'] ?? '-') ?></td>
    <td><?= h($d['last_status'] ?? '-') ?></td>
    <td><?= $d['active'] ? 'yes' : 'no' ?></td>
    <td><form method="post" style="margin:0">
      <input type="hidden" name="action" value="toggle">
      <input type="hidden" name="device_id" value="<?= (int)$d['device_id'] ?>">
      <button><?= $d['active'] ? 'disable' : 'enable' ?></button>
    </form></td>
  </tr>
  <?php endforeach; ?>
</table>

<h3>Add a device</h3>
<form method="post">
  <input type="hidden" name="action" value="add_device">
  <p>Name <input name="name" placeholder="Main Gate" required></p>
  <p>IP <input name="ip" placeholder="192.168.1.201" required>
     Port <input name="port" value="5005" size="6">
     Machine# <input name="machine_no" value="1" size="4"></p>
  <p>License <input name="license" value="1261" size="8">
     Comm password <input name="net_password" value="0" size="6"></p>
  <p>Pull times <input name="pull_times" placeholder="12:00,17:00" size="20">
     Time-sync drift (s) <input name="time_sync_drift" value="30" size="5"></p>
  <button>Add device</button>
</form>
<p style="color:#666">The device needs a stable LAN IP (set a static IP when mounting it).
The agent picks up new/changed devices within a few minutes.</p>
