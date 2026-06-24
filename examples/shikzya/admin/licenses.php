<?php
/**
 * Super admin: license keys + registered devices per school (tenant).
 * Generate a key on school registration; register the school's attendance
 * devices by serial+MAC (obtained from the desktop tool's info screen); revoke
 * a key or a device. Tenant-scoped via the header's $tenantId.
 */
$title = 'Licenses'; $active = 'lic';
require __DIR__ . '/_header.php';

$flash = null;
try {
    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $a = $_POST['action'] ?? '';
        if ($a === 'create_license') {
            $key = bin2hex(random_bytes(32));
            db()->prepare('INSERT INTO bio_license (license_key, tenant_id, client_name, status, expires_at) VALUES (?,?,?,\'active\',?)')
                ->execute([$key, $tenantId, trim($_POST['client_name'] ?? '') ?: null, trim($_POST['expires_at'] ?? '') ?: null]);
            $flash = "License created (copy it now): $key";
        } elseif ($a === 'revoke_license') {
            db()->prepare("UPDATE bio_license SET status='revoked' WHERE license_key=? AND tenant_id=?")
                ->execute([$_POST['license_key'], $tenantId]);
        } elseif ($a === 'register_device') {
            $serial = trim($_POST['serial'] ?? ''); $lk = trim($_POST['license_key'] ?? '');
            if ($serial !== '' && $lk !== '') {
                db()->prepare('INSERT INTO bio_reg_device (serial, mac, tenant_id, license_key, label, status)
                               VALUES (?,?,?,?,?,\'active\')
                               ON DUPLICATE KEY UPDATE mac=VALUES(mac), tenant_id=VALUES(tenant_id),
                                 license_key=VALUES(license_key), label=VALUES(label), status=\'active\'')
                    ->execute([$serial, trim($_POST['mac'] ?? '') ?: null, $tenantId, $lk, trim($_POST['label'] ?? '') ?: null]);
                $flash = "Device $serial registered.";
            }
        } elseif ($a === 'revoke_device') {
            db()->prepare("UPDATE bio_reg_device SET status='revoked' WHERE serial=? AND tenant_id=?")
                ->execute([$_POST['serial'], $tenantId]);
        }
    }

    $lics = db()->prepare('SELECT license_key, client_name, status, expires_at, created_at FROM bio_license WHERE tenant_id=? ORDER BY created_at DESC');
    $lics->execute([$tenantId]); $lics = $lics->fetchAll();
    $devs = db()->prepare('SELECT serial, mac, label, license_key, status, last_seen_at FROM bio_reg_device WHERE tenant_id=? ORDER BY registered_at DESC');
    $devs->execute([$tenantId]); $devs = $devs->fetchAll();
} catch (Throwable $e) {
    echo '<div class="err">' . h($e->getMessage()) . '</div>'; require __DIR__ . '/_footer.php'; exit;
}
?>
<h1>Licenses — tenant <?= h($tenantId) ?></h1>
<?php if ($flash): ?><div class="flash"><?= h($flash) ?></div><?php endif; ?>

<table>
  <tr><th>License key</th><th>Client</th><th>Status</th><th>Expires</th><th></th></tr>
  <?php foreach ($lics as $l): ?>
  <tr>
    <td><code><?= h($l['license_key']) ?></code></td>
    <td><?= h($l['client_name'] ?? '') ?></td>
    <td><?= h($l['status']) ?></td>
    <td><?= h($l['expires_at'] ?? '—') ?></td>
    <td><?php if ($l['status'] === 'active'): ?>
      <form method="post" style="margin:0"><input type="hidden" name="action" value="revoke_license">
        <input type="hidden" name="license_key" value="<?= h($l['license_key']) ?>">
        <button class="ghost">revoke</button></form><?php endif; ?></td>
  </tr>
  <?php endforeach; ?>
  <?php if (!$lics): ?><tr><td colspan="5" style="color:#94a3b8">No licenses yet.</td></tr><?php endif; ?>
</table>

<div class="panel">
  <h3>Generate a license for this school</h3>
  <form method="post" class="row">
    <input type="hidden" name="action" value="create_license">
    <div><label>Client name</label><input name="client_name" placeholder="Greenfield High"></div>
    <div><label>Expires (YYYY-MM-DD, optional)</label><input name="expires_at" placeholder="2027-12-31"></div>
    <button>Generate key</button>
  </form>
</div>

<h3 style="margin-top:22px">Registered devices</h3>
<table>
  <tr><th>Serial</th><th>MAC</th><th>Label</th><th>License</th><th>Status</th><th>Last seen</th><th></th></tr>
  <?php foreach ($devs as $d): ?>
  <tr>
    <td><?= h($d['serial']) ?></td><td><?= h($d['mac'] ?? '') ?></td><td><?= h($d['label'] ?? '') ?></td>
    <td><code><?= h(substr($d['license_key'], 0, 12)) ?>…</code></td>
    <td><?= h($d['status']) ?></td><td><?= h($d['last_seen_at'] ?? '—') ?></td>
    <td><?php if ($d['status'] === 'active'): ?>
      <form method="post" style="margin:0"><input type="hidden" name="action" value="revoke_device">
        <input type="hidden" name="serial" value="<?= h($d['serial']) ?>">
        <button class="ghost">revoke</button></form><?php endif; ?></td>
  </tr>
  <?php endforeach; ?>
  <?php if (!$devs): ?><tr><td colspan="7" style="color:#94a3b8">No devices registered.</td></tr><?php endif; ?>
</table>

<div class="panel">
  <h3>Register a device (serial + MAC from the desktop tool's info / Test device)</h3>
  <form method="post" class="row">
    <input type="hidden" name="action" value="register_device">
    <div><label>Serial</label><input name="serial" required></div>
    <div><label>MAC</label><input name="mac" placeholder="AA:BB:CC:DD:EE:FF"></div>
    <div><label>Label</label><input name="label" placeholder="Main Gate"></div>
    <div><label>License key</label><input name="license_key" required style="width:280px"></div>
    <button>Register</button>
  </form>
</div>
<?php require __DIR__ . '/_footer.php'; ?>
