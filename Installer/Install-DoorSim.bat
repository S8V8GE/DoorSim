@echo off
setlocal

echo Starting DoorSim installer...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-DoorSim.ps1"

endlocal