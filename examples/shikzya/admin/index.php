<?php
$title = 'Dashboard'; $active = 'dash';
require __DIR__ . '/_header.php';

try {
    $n = function (string $sql) use ($tenantId) {
        $st = db()->prepare($sql); $st->execute([$tenantId]); return (int)$st->fetch()['c'];
    };
    $nSites = $n('SELECT COUNT(*) c FROM bio_site WHERE tenant_id = ?');
    $nDev   = $n('SELECT COUNT(*) c FROM bio_device WHERE tenant_id = ? AND active = 1');
    $nToday = $n('SELECT COUNT(*) c FROM bio_punch WHERE tenant_id = ? AND punch_time >= CURDATE()');
} catch (Throwable $e) {
    echo '<div class="err">' . h($e->getMessage()) . '</div>';
    require __DIR__ . '/_footer.php'; exit;
}
?>
<h1>Dashboard — tenant <?= h($tenantId) ?></h1>
<div class="cards">
  <div class="card"><div class="n"><?= $nSites ?></div><div>Sites</div></div>
  <div class="card"><div class="n"><?= $nDev ?></div><div>Active devices</div></div>
  <div class="card"><div class="n"><?= $nToday ?></div><div>Punches today</div></div>
</div>
<p>
  <a class="btn" href="sites.php">Manage sites &amp; devices</a>
  <a class="btn ghost" href="attendance.php">View attendance</a>
</p>
<p style="color:#94a3b8">New here? Create a <b>Site</b> (you get an install token), install the agent on
that school's PC with the token, then add the school's <b>devices</b> (IP, license, schedule).</p>
<?php require __DIR__ . '/_footer.php'; ?>
