<?php
/**
 * Shared helpers for the bridge API: PDO connection + site-token auth.
 * Adapt the connection + token storage to your Shikzya framework. In production
 * store a HASH of the site token, not the plaintext, and compare hashes.
 */

function db(): PDO
{
    static $pdo = null;
    if ($pdo === null) {
        $pdo = new PDO(
            'mysql:host=localhost;port=3306;dbname=shikzya;charset=utf8mb4',
            'shikzya_user', 'secret',
            [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION, PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC]
        );
    }
    return $pdo;
}

function bearer_token(): ?string
{
    $h = $_SERVER['HTTP_AUTHORIZATION'] ?? ($_SERVER['REDIRECT_HTTP_AUTHORIZATION'] ?? '');
    if (preg_match('/Bearer\s+(.+)/i', $h, $m)) return trim($m[1]);
    return null;
}

/** Resolves the site (and tenant) from the bearer token, or 401s. */
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

function read_json(): array
{
    $d = json_decode(file_get_contents('php://input'), true);
    return is_array($d) ? $d : [];
}

function json_out($data): void
{
    header('Content-Type: application/json');
    echo json_encode($data);
    exit;
}

function json_error(int $code, string $msg): void
{
    http_response_code($code);
    json_out(['error' => $msg]);
}
