<?php
/**
 * Shikzya side: read one school's attendance for a day, mapped to people.
 *
 * bio_punch.enroll_number is the DEVICE-side user id, not the student id. The
 * bio_enroll_map table maps (tenant_id, device_id, enroll_number) -> a person in
 * your system. Rows with no mapping still appear (person_id is NULL) so staff can
 * assign them - never silently dropped.
 */
header('Content-Type: application/json');

$dsn = 'mysql:host=localhost;port=3306;dbname=shikzya;charset=utf8mb4';
$pdo = new PDO($dsn, 'shikzya_user', 'secret', [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);

$tenantId = $_GET['tenant_id'] ?? '';
$date     = $_GET['date'] ?? date('Y-m-d');

$sql = '
    SELECT p.punch_time, p.enroll_number, p.verify_label, p.io_mode,
           m.person_type, m.person_id
      FROM bio_punch p
      LEFT JOIN bio_enroll_map m
        ON  m.tenant_id     = p.tenant_id
        AND m.device_id     = p.device_id
        AND m.enroll_number = p.enroll_number
     WHERE p.tenant_id = ?
       AND DATE(p.punch_time) = ?
     ORDER BY p.punch_time';

$stmt = $pdo->prepare($sql);
$stmt->execute([$tenantId, $date]);

echo json_encode($stmt->fetchAll(PDO::FETCH_ASSOC));
