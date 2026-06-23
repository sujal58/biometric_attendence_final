<?php
/**
 * Devices for one site. Add/enable/disable the biometric machines on that
 * school's LAN. Isolation: the site is verified to belong to $tenantId before
 * anything is listed or changed.
 */
$title = 'Devices'; $active = 'sites';
require __DIR__ . '/_header.php';

$siteId = (int)($_GET['site_id'] ?? 0);
$flash = null;

try {
    $st = db()->prepare('SELECT site_id, name FROM bio_site WHERE site_id = ? AND tenant_id = ?');
    $st->execute([$siteId, $tenantId]);
    $site = $st->fetch();
    if (!$site) { echo '<div class="err">Site not found for this tenant.</div>'; require __DIR__ . '/_footer.php'; exit; }

    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $action = $_POST['action'] ?? '';
        if ($action === 'add_device') {
            $next = db()->prepare('SELECT COALESCE(MAX(device_id),0)+1 AS n FROM bio_device WHERE site_id = ?');
            $next->execute([$siteId]);
            $deviceId = (int)$next->fetch()['n'];
            db()->prepare(
                'INSERT INTO bio_device
                   (device_id,site_id,tenant_id,name,ip,port,machine_no,net_password,license,timeout_ms,protocol,pull_times,time_sync_drift,active)
                 VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,1)')
                ->execute([
                    $deviceId, $siteId, $tenantId,
                    trim($_POST['name'] ?? ''), trim($_POST['ip'] ?? ''),
                    (int)($_POST['port'] ?? 5005), (int)($_POST['machine_no'] ?? 1),
                    (int)($_POST['net_password'] ?? 0), (int)($_POST['license'] ?? 1261),
                    5000, 0, trim($_POST['pull_times'] ?? ''), (int)($_POST['time_sync_drift'] ?? 30),
                ]);
            $flash = "Device #$deviceId added.";
        } elseif ($action === 'toggle') {
            db()->prepare('UPDATE bio_device SET active = 1 - active WHERE site_id = ? AND device_id = ?')
                ->execute([$siteId, (int)$_POST['device_id']]);
        }
    }

    $dv = db()->prepare(
        'SELECT device_id,name,ip,port,machine_no,license,pull_times,active,last_pull_at,last_status
           FROM bio_device WHERE site_id = ? ORDER BY device_id');
    $dv->execute([$siteId]);
    $devices = $dv->fetchAll();
} catch (Throwable $e) {
    echo '<div class="err">' . h($e->getMessage()) . '</div>'; require __DIR__ . '/_footer.php'; exit;
}
?>
<p><a href="sites.php" style="color:#94a3b8">&larr; sites</a></p>
<h1><?= h($site['name']) ?> — devices</h1>
<?php if ($flash): ?><div class="flash"><?= h($flash) ?></div><?php endif; ?>

<table>
  <tr><th>ID</th><th>Name</th><th>IP : Port</th><th>Machine#</th><th>License</th>
      <th>Pull times</th><th>Last pull</th><th>Status</th><th>Active</th><th></th></tr>
  <?php foreach ($devices as $d): ?>
  <tr>
    <td><?= (int)$d['device_id'] ?></td><td><?= h($d['name']) ?></td>
    <td><?= h($d['ip']) ?> : <?= (int)$d['port'] ?></td>
    <td><?= (int)$d['machine_no'] ?></td><td><?= (int)$d['license'] ?></td>
    <td><?= h($d['pull_times']) ?></td>
    <td><?= h($d['last_pull_at'] ?? '-') ?></td><td><?= h($d['last_status'] ?? '-') ?></td>
    <td><?= $d['active'] ? 'yes' : 'no' ?></td>
    <td><form method="post" style="margin:0"><input type="hidden" name="action" value="toggle">
      <input type="hidden" name="device_id" value="<?= (int)$d['device_id'] ?>">
      <button class="ghost"><?= $d['active'] ? 'disable' : 'enable' ?></button></form></td>
  </tr>
  <?php endforeach; ?>
  <?php if (!$devices): ?><tr><td colspan="10" style="color:#94a3b8">No devices yet — add one below.</td></tr><?php endif; ?>
</table>

<div class="panel">
  <h3>Add a device</h3>
  <form method="post">
    <input type="hidden" name="action" value="add_device">
    <div class="row">
      <div><label>Name</label><input name="name" placeholder="Main Gate" required></div>
      <div><label>IP address</label><input name="ip" placeholder="192.168.1.201" required></div>
      <div><label>Port</label><input name="port" value="5005" size="6"></div>
      <div><label>Machine #</label><input name="machine_no" value="1" size="4"></div>
    </div>
    <div class="row">
      <div><label>License</label><input name="license" value="1261" size="8"></div>
      <div><label>Comm password</label><input name="net_password" value="0" size="6"></div>
      <div><label>Pull times</label><input name="pull_times" placeholder="12:00,17:00"></div>
      <div><label>Drift (s)</label><input name="time_sync_drift" value="30" size="5"></div>
      <button>Add device</button>
    </div>
  </form>
  <p style="color:#94a3b8">Give the device a stable static IP when mounting it. The agent picks up new devices within a few minutes.</p>
</div>
<?php require __DIR__ . '/_footer.php'; ?>
