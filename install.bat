@echo off
chcp 65001 >nul
if not exist "%~dp0PlayTimer.exe" call "%~dp0build.bat" || exit /b 1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PlayTimer /t REG_SZ /d "\"%~dp0PlayTimer.exe\"" /f >nul
start "" "%~dp0PlayTimer.exe"
echo 로그인할 때마다 PlayTimer가 자동으로 실행됩니다.
