<#
  Produces the deployable single-file agent (self-contained net8 win-x86) into a
  `publish` folder next to install-agent.ps1. Run on a machine with the .NET 8 SDK.
#>
param([string]$Out = "$PSScriptRoot\publish")
$ErrorActionPreference = "Stop"
$proj = "$PSScriptRoot\..\src\AttendanceBridge\AttendanceBridge.csproj"
dotnet publish $proj -c Release -p:PublishSingleFile=true -p:DebugType=none -o $Out
Write-Host "Published to $Out"
Write-Host "Deploy with: install-agent.ps1 -SiteToken <token> -ApiBaseUrl <url>"
