<?php
/**
 * Shikzya side: a school clicks "Fetch attendance" for one of its devices.
 *
 * Queues a command in bio_fetch_command. The device's site agent picks it up via
 * GET /api/bridge/v1/commands, pulls, uploads, and reports the result. Poll
 * command_status.php for the outcome.
 *
 * tenant_id should come from the logged-in school's session, not the request.
 */
header('Content-Type: application/json');
require __DIR__ . '/api/_db.php';

$deviceId = (int)($_POST['device_id'] ?? 0);
$tenantId = $_POST['tenant_id'] ?? '';
$user     = $_POST['user'] ?? 'web';

// The device must belong to this tenant; resolve its site.
$st = db()->prepare('SELECT site_id FROM bio_device WHERE device_id = ? AND tenant_id = ? AND active = 1 LIMIT 1');
$st->execute([$deviceId, $tenantId]);
$row = $st->fetch();
if (!$row) {
    http_response_code(404);
    echo json_encode(['error' => 'device not found for tenant']);
    exit;
}

$ins = db()->prepare(
    'INSERT INTO bio_fetch_command (tenant_id, site_id, device_id, requested_by) VALUES (?, ?, ?, ?)');
$ins->execute([$tenantId, $row['site_id'], $deviceId, $user]);

echo json_encode(['command_id' => (int)db()->lastInsertId(), 'status' => 'pending']);
