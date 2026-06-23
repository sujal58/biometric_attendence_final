<#
  Registers a Windows Scheduled Task that runs the AttendanceBridge `serve` agent
  at system startup and restarts it if it ever stops. `serve` itself does the
  scheduled pulls, on-demand fetch commands, and the local web UI.

  Run from an ADMIN PowerShell:
      powershell -ExecutionPolicy Bypass -File scripts\install-task.ps1

  For production, deploy the built output folder somewhere stable (e.g.
  C:\AttendanceBridge) and pass -ExePath to point at it.
#>
param(
  [string]$ExePath  = "$PSScriptRoot\..\src\AttendanceBridge\bin\x86\Debug\net48\AttendanceBridge.exe",
  [string]$TaskName = "AttendanceBridge"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath)) {
  throw "Exe not found at '$ExePath'. Build first (dotnet build) or pass -ExePath."
}
$ExePath = (Resolve-Path $ExePath).Path
$workDir = Split-Path $ExePath

Write-Host "Registering scheduled task '$TaskName' -> $ExePath serve"

$action  = New-ScheduledTaskAction -Execute $ExePath -Argument "serve" -WorkingDirectory $workDir
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet `
  -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
  -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
  -ExecutionTimeLimit ([TimeSpan]::Zero)          # no time limit (runs forever)
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
  -Settings $settings -Principal $principal -Force | Out-Null

Start-ScheduledTask -TaskName $TaskName
Write-Host "Done. The agent is running and will start automatically on boot."
Write-Host "Logs: $workDir\logs   |   Stop/remove: scripts\uninstall-task.ps1"
