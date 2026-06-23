<?php
/**
 * Sites for the current tenant. Creating a site generates the site_token you pass
 * to install-agent.ps1. Tenant isolation: everything is scoped to $tenantId
 * (from the session; in real Shikzya, the logged-in school).
 */
$title = 'Sites & Devices'; $active = 'sites';
require __DIR__ . '/_header.php';

$flash = null; $err = null;
try {
    if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'create_site') {
        $name = trim($_POST['name'] ?? '');
        if ($name !== '') {
            $token = bin2hex(random_bytes(32));
            db()->prepare('INSERT INTO bio_site (tenant_id, name, site_token, active) VALUES (?,?,?,1)')
                ->execute([$tenantId, $name, $token]);
            $flash = "Site \"$name\" created. Install token (copy it now — shown once):\n$token";
        }
    }
    $st = db()->prepare(
        'SELECT site_id,name,site_token,active,last_seen_at,agent_version FROM bio_site WHERE tenant_id = ? ORDER BY site_id');
    $st->execute([$tenantId]);
    $rows = $st->fetchAll();
} catch (Throwable $e) {
    echo '<div class="err">' . h($e->getMessage()) . '</div>';
    require __DIR__ . '/_footer.php'; exit;
}
?>
<h1>Sites &amp; Devices</h1>
<?php if ($flash): ?><div class="flash"><?= nl2br(h($flash)) ?></div><?php endif; ?>

<table>
  <tr><th>ID</th><th>Name</th><th>Agent last seen</th><th>Version</th><th>Active</th><th>Devices</th></tr>
  <?php foreach ($rows as $r): ?>
  <tr>
    <td><?= (int)$r['site_id'] ?></td>
    <td><?= h($r['name']) ?></td>
    <td><?= h($r['last_seen_at'] ?? 'never') ?></td>
    <td><?= h($r['agent_version'] ?? '-') ?></td>
    <td><?= $r['active'] ? 'yes' : 'no' ?></td>
    <td><a class="btn ghost" href="devices.php?site_id=<?= (int)$r['site_id'] ?>">manage devices</a></td>
  </tr>
  <?php endforeach; ?>
  <?php if (!$rows): ?><tr><td colspan="6" style="color:#94a3b8">No sites yet — add one below.</td></tr><?php endif; ?>
</table>

<div class="panel">
  <h3>Add a site (a school location running one agent)</h3>
  <form method="post" class="row">
    <input type="hidden" name="action" value="create_site">
    <div style="flex:1"><label>Site name</label>
      <input name="name" placeholder="Greenfield High - Main Campus" required style="width:100%"></div>
    <button>Create site &amp; token</button>
  </form>
  <p style="color:#94a3b8">Then on that school's PC (admin PowerShell):<br>
  <code>install-agent.ps1 -SiteToken &lt;token&gt; -ApiBaseUrl https://app.shikzya.com</code></p>
</div>
<?php require __DIR__ . '/_footer.php'; ?>
