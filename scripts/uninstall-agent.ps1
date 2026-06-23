<#
  Stops and removes the Attendance Bridge service. Run from an ADMIN PowerShell.
  Pass -RemoveFiles to also delete the install folder.
#>
param(
  [string]$ServiceName = "AttendanceBridge",
  [string]$InstallDir  = "C:\AttendanceBridge",
  [switch]$RemoveFiles
)
if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
  Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
  & sc.exe delete $ServiceName | Out-Null
  Write-Host "Removed service '$ServiceName'."
} else {
  Write-Host "Service '$ServiceName' not found."
}
if ($RemoveFiles -and (Test-Path $InstallDir)) {
  Remove-Item $InstallDir -Recurse -Force
  Write-Host "Deleted $InstallDir."
}
