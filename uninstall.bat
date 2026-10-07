@echo off
rem Double-click to uninstall PlayTimer (or use Settings > Apps).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
echo.
pause
