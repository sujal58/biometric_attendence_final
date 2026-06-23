<?php
require_once __DIR__ . '/../lib.php';
if (session_status() === PHP_SESSION_NONE) session_start();

// Tenant context. In real Shikzya this is the logged-in school; here a switcher.
if (isset($_GET['tenant'])) $_SESSION['tenant_id'] = trim($_GET['tenant']);
$tenantId = $_SESSION['tenant_id'] ?? 'demo';
$active = $active ?? '';
?>
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title><?= h($title ?? 'Attendance Admin') ?></title>
<style>
  :root{--bg:#0f172a;--card:#1e293b;--line:#334155;--txt:#e2e8f0;--mut:#94a3b8;--acc:#38bdf8;--ok:#22c55e;--err:#ef4444}
  *{box-sizing:border-box} body{margin:0;font-family:system-ui,Segoe UI,Roboto,sans-serif;background:#0b1220;color:var(--txt)}
  .topbar{display:flex;align-items:center;gap:18px;background:var(--bg);padding:12px 20px;border-bottom:1px solid var(--line)}
  .brand{font-weight:700} nav{display:flex;gap:6px;flex:1}
  nav a{color:var(--mut);text-decoration:none;padding:7px 12px;border-radius:8px}
  nav a.on,nav a:hover{background:var(--card);color:var(--txt)}
  .tenant{display:flex;align-items:center;gap:6px} .tenant input{width:130px}
  main{max-width:1000px;margin:24px auto;padding:0 16px}
  h1{font-size:22px} table{width:100%;border-collapse:collapse;background:var(--card);border-radius:10px;overflow:hidden}
  th,td{padding:9px 12px;border-bottom:1px solid var(--line);text-align:left;font-size:14px}
  th{background:#172033;color:var(--mut);font-weight:600} tr:last-child td{border-bottom:0}
  input,select{background:#0b1220;border:1px solid var(--line);color:var(--txt);padding:8px;border-radius:8px}
  button,.btn{background:var(--acc);color:#04263a;border:0;padding:8px 14px;border-radius:8px;font-weight:600;cursor:pointer;text-decoration:none;display:inline-block}
  .btn.ghost{background:transparent;border:1px solid var(--line);color:var(--txt)}
  .cards{display:flex;gap:14px;margin:16px 0} .card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:18px 22px;min-width:150px}
  .card .n{font-size:30px;font-weight:700} .card div:last-child{color:var(--mut)}
  .flash{background:#0d2a1a;border:1px solid #1c5b39;color:#9ff0c0;padding:10px 12px;border-radius:8px;margin:12px 0;word-break:break-all}
  .err{background:#2a0d0d;border:1px solid #5b1c1c;color:#ffb4b4;padding:10px 12px;border-radius:8px;margin:12px 0}
  .panel{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:16px;margin-top:16px}
  .row{display:flex;gap:10px;flex-wrap:wrap;align-items:end;margin:6px 0} label{display:block;color:var(--mut);font-size:12px;margin-bottom:3px}
  code{background:#0b1220;padding:2px 6px;border-radius:5px}
</style>
</head>
<body>
<header class="topbar">
  <div class="brand">Attendance Admin</div>
  <nav>
    <a class="<?= $active==='dash'?'on':'' ?>" href="index.php">Dashboard</a>
    <a class="<?= $active==='sites'?'on':'' ?>" href="sites.php">Sites &amp; Devices</a>
    <a class="<?= $active==='att'?'on':'' ?>" href="attendance.php">Attendance</a>
  </nav>
  <form class="tenant" method="get" action="index.php">
    <label style="margin:0">Tenant</label>
    <input name="tenant" value="<?= h($tenantId) ?>">
    <button class="ghost">switch</button>
  </form>
</header>
<main>
