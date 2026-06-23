<?php
/**
 * Shikzya side: poll the result of a fetch command queued by trigger_fetch.php.
 * Returns status (pending|running|done|error) plus counts and a message.
 * The UI can poll this every couple of seconds until status is done/error.
 */
header('Content-Type: application/json');

$dsn = 'mysql:host=localhost;port=3306;dbname=shikzya;charset=utf8mb4';
$pdo = new PDO($dsn, 'shikzya_user', 'secret', [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);

$id = (int)($_GET['id'] ?? 0);

$stmt = $pdo->prepare(
    'SELECT status, records_read, records_inserted, result_message, requested_at, finished_at
       FROM bio_fetch_command WHERE id = ?'
);
$stmt->execute([$id]);
$row = $stmt->fetch(PDO::FETCH_ASSOC);

echo json_encode($row ?: ['status' => 'unknown']);
