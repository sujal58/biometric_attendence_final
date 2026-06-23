<#
  Stops and removes the AttendanceBridge scheduled task.
  Run from an ADMIN PowerShell:
      powershell -ExecutionPolicy Bypass -File scripts\uninstall-task.ps1
#>
param([string]$TaskName = "AttendanceBridge")

try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue } catch {}
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
Write-Host "Removed scheduled task '$TaskName'."
