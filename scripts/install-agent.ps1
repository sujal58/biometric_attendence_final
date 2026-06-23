<#
  Installs the Shikzya Attendance Bridge agent on a school PC as a Windows
  Service. Pre-keyed: you pass the per-school site token + API URL, it writes the
  config and starts the service. The school does nothing.

  Run from an ADMIN PowerShell:
    powershell -ExecutionPolicy Bypass -File install-agent.ps1 `
        -SiteToken "abc123..." -ApiBaseUrl "https://app.shikzya.com"

  -Source defaults to a `publish` folder next to this script (the single-file
  publish output: AttendanceBridge.exe + the native DLLs + appsettings.example.json).
#>
param(
  [Parameter(Mandatory=$true)][string]$SiteToken,
  [Parameter(Mandatory=$true)][string]$ApiBaseUrl,
  [string]$InstallDir  = "C:\AttendanceBridge",
  [string]$Source      = "$PSScriptRoot\publish",
  [string]$ServiceName = "AttendanceBridge"
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path "$Source\AttendanceBridge.exe")) {
  throw "AttendanceBridge.exe not found in '$Source'. Publish first (see scripts\publish.ps1) or pass -Source."
}

Write-Host "Installing to $InstallDir ..."
New-Item -ItemType Directory -Force $InstallDir | Out-Null
Copy-Item "$Source\*" $InstallDir -Recurse -Force

# Pre-keyed config (the only per-school setting).
$cfg = [ordered]@{
  apiBaseUrl           = $ApiBaseUrl
  siteToken            = $SiteToken
  deviceRefreshSeconds = 300
  commandPollSeconds   = 15
  heartbeatSeconds     = 300
  spoolRetrySeconds    = 60
  spoolDirectory       = "spool"
  logging              = @{ directory = "logs" }
} | ConvertTo-Json -Depth 5
Set-Content -Path "$InstallDir\appsettings.json" -Value $cfg -Encoding UTF8

$exe = Join-Path $InstallDir "AttendanceBridge.exe"

# Recreate the service cleanly.
if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
  Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
  & sc.exe delete $ServiceName | Out-Null
  Start-Sleep -Seconds 2
}
New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" `
  -DisplayName "Shikzya Attendance Bridge" -StartupType Automatic | Out-Null

# Auto-restart on failure.
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

Start-Service $ServiceName
Write-Host "Done. Service '$ServiceName' is running and starts automatically on boot."
Write-Host "Logs: $InstallDir\logs"
Write-Host "Note: the PC needs the Visual C++ x86 runtime for the native device DLLs."
