<?php
/**
 * Attendance for the current tenant on a day, mapped to people via bio_enroll_map.
 * Unmapped enroll numbers still show (person is blank) so staff can assign them.
 */
$title = 'Attendance'; $active = 'att';
require __DIR__ . '/_header.php';

$date = $_GET['date'] ?? date('Y-m-d');
try {
    $st = db()->prepare(
        'SELECT p.punch_time, p.device_id, p.enroll_number, p.verify_label, p.io_mode,
                m.person_type, m.person_id
           FROM bio_punch p
           LEFT JOIN bio_enroll_map m
             ON m.tenant_id = p.tenant_id AND m.device_id = p.device_id AND m.enroll_number = p.enroll_number
          WHERE p.tenant_id = ? AND DATE(p.punch_time) = ?
          ORDER BY p.punch_time DESC LIMIT 500');
    $st->execute([$tenantId, $date]);
    $rows = $st->fetchAll();
} catch (Throwable $e) {
    echo '<div class="err">' . h($e->getMessage()) . '</div>'; require __DIR__ . '/_footer.php'; exit;
}
?>
<h1>Attendance</h1>
<form method="get" class="row">
  <div><label>Date</label><input type="date" name="date" value="<?= h($date) ?>"></div>
  <button class="ghost">show</button>
</form>

<table style="margin-top:12px">
  <tr><th>Time</th><th>Device</th><th>Enroll #</th><th>Person</th><th>Verify</th><th>In/Out</th></tr>
  <?php foreach ($rows as $r): ?>
  <tr>
    <td><?= h($r['punch_time']) ?></td>
    <td><?= (int)$r['device_id'] ?></td>
    <td><?= (int)$r['enroll_number'] ?></td>
    <td><?= $r['person_id'] ? h($r['person_type'] . ' #' . $r['person_id']) : '<span style="color:#f59e0b">unmapped</span>' ?></td>
    <td><?= h($r['verify_label'] ?? '-') ?></td>
    <td><?= (int)$r['io_mode'] ?></td>
  </tr>
  <?php endforeach; ?>
  <?php if (!$rows): ?><tr><td colspan="6" style="color:#94a3b8">No punches for <?= h($date) ?>.</td></tr><?php endif; ?>
</table>
<?php require __DIR__ . '/_footer.php'; ?>
