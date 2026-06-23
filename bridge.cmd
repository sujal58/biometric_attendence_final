@echo off
rem Convenience launcher: `bridge info` | `bridge pull` | `bridge poll`
rem Always builds first (incremental, fast) so edits to code or appsettings.json
rem are copied to the output folder, then forwards all arguments to the exe.
setlocal
dotnet build "%~dp0src\AttendanceBridge\AttendanceBridge.csproj" --nologo -v quiet || exit /b 1
set "EXE=%~dp0src\AttendanceBridge\bin\x86\Debug\net48\AttendanceBridge.exe"
"%EXE%" %*
