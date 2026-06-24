<?php
/**
 * Shared helpers for the admin UI and the agent API: DB connection (with
 * automatic table creation), site-token auth, and small output helpers.
 */

function cfg(): array
{
    static $c = null;
    if ($c === null) $c = require __DIR__ . '/config.php';
    return $c;
}

function db(): PDO
{
    static $pdo = null;
    if ($pdo === null) {
        $d = cfg()['db'];
        try {
            $pdo = new PDO(
                "mysql:host={$d['host']};port={$d['port']};dbname={$d['name']};charset=utf8mb4",
                $d['user'], $d['pass'],
                [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION, PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC]
            );
        } catch (PDOException $e) {
            throw new RuntimeException(
                "Cannot connect to MySQL. Edit examples/shikzya/config.php with your DB details. (" . $e->getMessage() . ")");
        }
        ensure_schema($pdo);
    }
    return $pdo;
}

/** Runs db/schema.sql once (CREATE TABLE IF NOT EXISTS) so there is no manual SQL step. */
function ensure_schema(PDO $pdo): void
{
    static $done = false;
    if ($done) return;
    $done = true;

    $sql = @file_get_contents(__DIR__ . '/../../db/schema.sql');
    if ($sql === false) return;

    foreach (explode(';', $sql) as $stmt) {
        $stmt = trim($stmt);
        if ($stmt === '') continue;
        // skip chunks that are only comments
        $hasSql = false;
        foreach (explode("\n", $stmt) as $line) {
            $line = trim($line);
            if ($line !== '' && strpos($line, '--') !== 0) { $hasSql = true; break; }
        }
        if ($hasSql) { try { $pdo->exec($stmt); } catch (PDOException $e) { /* ignore */ } }
    }
}

function bearer_token(): ?string
{
    $h = $_SERVER['HTTP_AUTHORIZATION'] ?? ($_SERVER['REDIRECT_HTTP_AUTHORIZATION'] ?? '');
    if (preg_match('/Bearer\s+(.+)/i', $h, $m)) return trim($m[1]);
    return null;
}

function require_site(): array
{
    $token = bearer_token();
    if (!$token) json_error(401, 'missing token');
    $st = db()->prepare('SELECT site_id, tenant_id FROM bio_site WHERE site_token = ? AND active = 1');
    $st->execute([$token]);
    $site = $st->fetch();
    if (!$site) json_error(401, 'invalid token');
    return $site;
}

/** Resolves a license key (the desktop tool's bearer) to its row, or null. */
function find_license(?string $token): ?array
{
    if (!$token) return null;
    $st = db()->prepare('SELECT license_key, tenant_id, client_name, status, expires_at FROM bio_license WHERE license_key = ?');
    $st->execute([$token]);
    $row = $st->fetch();
    return $row ?: null;
}

/** 401/403 unless the license is present, active and not expired. */
function require_valid_license(?array $lic): array
{
    if (!$lic) json_error(401, 'invalid license key');
    if (($lic['status'] ?? '') !== 'active') json_error(403, 'license revoked — contact your administrator');
    if (!empty($lic['expires_at']) && $lic['expires_at'] < date('Y-m-d')) json_error(403, 'license expired');
    return $lic;
}

function read_json(): array
{
    $d = json_decode(file_get_contents('php://input'), true);
    return is_array($d) ? $d : [];
}

function json_out($data): void { header('Content-Type: application/json'); echo json_encode($data); exit; }
function json_error(int $code, string $msg): void { http_response_code($code); json_out(['error' => $msg]); }

function h($s): string { return htmlspecialchars((string)$s, ENT_QUOTES); }
