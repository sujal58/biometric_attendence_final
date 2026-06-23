<#
  Produces the school/technician desktop tool as a single self-contained .exe
  (ShikzyaDeviceTool.exe) into a `publish-desktop` folder. Run on a machine with
  the .NET 8 SDK. Copy the folder to the school PC and double-click the exe -
  no install.
#>
param([string]$Out = "$PSScriptRoot\publish-desktop")
$ErrorActionPreference = "Stop"
$proj = "$PSScriptRoot\..\src\AttendanceDesktop\AttendanceDesktop.csproj"
dotnet publish $proj -c Release -p:PublishSingleFile=true -p:DebugType=none -o $Out
Write-Host "Published to $Out  ->  ShikzyaDeviceTool.exe (double-click; no install)."
