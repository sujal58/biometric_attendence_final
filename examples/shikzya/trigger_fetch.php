<?php
/**
 * Shikzya side: a school clicks "Fetch attendance".
 *
 * This queues a command in bio_fetch_command. The school's bridge (running
 * `serve` on the school LAN) polls that table, pulls from the device, writes
 * punches, and marks the command done. Poll command_status.php for the result.
 *
 * Adapt the DB connection + auth to your Shikzya framework. tenant_id must be
 * the calling school's tenant.
 */
header('Content-Type: application/json');

$dsn = 'mysql:host=localhost;port=3306;dbname=shikzya;charset=utf8mb4';
$pdo = new PDO($dsn, 'shikzya_user', 'secret', [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);

$tenantId = $_POST['tenant_id'] ?? '';   // the school (from the logged-in session, ideally)
$deviceId = (int)($_POST['device_id'] ?? 1);
$user     = $_POST['user'] ?? 'web';

if ($tenantId === '') {
    http_response_code(400);
    echo json_encode(['error' => 'tenant_id required']);
    exit;
}

$stmt = $pdo->prepare(
    'INSERT INTO bio_fetch_command (tenant_id, device_id, requested_by) VALUES (?, ?, ?)'
);
$stmt->execute([$tenantId, $deviceId, $user]);

echo json_encode(['command_id' => (int)$pdo->lastInsertId(), 'status' => 'pending']);
