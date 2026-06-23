@echo off
rem Convenience launcher: `bridge info` | `bridge pull` | `bridge poll`
rem Builds once if the exe is missing, then forwards all arguments to it.
setlocal
set "EXE=%~dp0src\AttendanceBridge\bin\x86\Debug\net48\AttendanceBridge.exe"
if not exist "%EXE%" (
  echo Building AttendanceBridge...
  dotnet build "%~dp0src\AttendanceBridge\AttendanceBridge.csproj" || exit /b 1
)
"%EXE%" %*
