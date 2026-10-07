@echo off
rem Double-click to install PlayTimer. The real work is done by install.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause
