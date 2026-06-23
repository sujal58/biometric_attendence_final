<?php
/**
 * Admin: sites for one tenant. A "site" is one school location running one agent.
 * Creating a site generates the site_token you pass to install-agent.ps1.
 *
 * Tenant isolation: $tenantId comes from the logged-in session in real Shikzya.
 * Every query below is scoped to it, so a tenant only ever sees its own sites.
 */
require __DIR__ . '/../api/_db.php';

// In Shikzya, take this from the authenticated session - NEVER from the request.
$tenantId = $_SESSION['tenant_id'] ?? ($_GET['tenant_id'] ?? 'SCHOOL_CODE');

$flash = null;
if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'create_site') {
    $name = trim($_POST['name'] ?? '');
    if ($name !== '') {
        $token = bin2hex(random_bytes(32)); // 64 hex chars
        db()->prepare('INSERT INTO bio_site (tenant_id, name, site_token, active) VALUES (?,?,?,1)')
            ->execute([$tenantId, $name, $token]);
        $flash = "Site created. Install token (shown once): $token";
    }
}

$sites = db()->prepare(
    'SELECT site_id, name, site_token, active, last_seen_at, agent_version
       FROM bio_site WHERE tenant_id = ? ORDER BY site_id');
$sites->execute([$tenantId]);
$rows = $sites->fetchAll();

function h($s) { return htmlspecialchars((string)$s, ENT_QUOTES); }
?>
<!doctype html><meta charset="utf-8"><title>Sites</title>
<body style="font-family:system-ui;max-width:820px;margin:30px auto">
<h2>Sites — tenant <?= h($tenantId) ?></h2>
<?php if ($flash): ?><p style="background:#e7f9ed;padding:10px;border-radius:6px"><?= h($flash) ?></p><?php endif; ?>

<table border="1" cellpadding="8" cellspacing="0" style="border-collapse:collapse;width:100%">
  <tr><th>ID</th><th>Name</th><th>Agent last seen</th><th>Version</th><th>Active</th><th>Devices</th></tr>
  <?php foreach ($rows as $r): ?>
  <tr>
    <td><?= (int)$r['site_id'] ?></td>
    <td><?= h($r['name']) ?></td>
    <td><?= h($r['last_seen_at'] ?? 'never') ?></td>
    <td><?= h($r['agent_version'] ?? '-') ?></td>
    <td><?= $r['active'] ? 'yes' : 'no' ?></td>
    <td><a href="devices.php?site_id=<?= (int)$r['site_id'] ?>">manage</a></td>
  </tr>
  <?php endforeach; ?>
</table>

<h3>Add a site</h3>
<form method="post">
  <input type="hidden" name="action" value="create_site">
  <input name="name" placeholder="e.g. Greenfield High - Main Campus" required style="width:60%">
  <button>Create site &amp; token</button>
</form>
<p style="color:#666">Then install the agent on that school's PC:<br>
<code>install-agent.ps1 -SiteToken &lt;token&gt; -ApiBaseUrl https://app.shikzya.com</code></p>
