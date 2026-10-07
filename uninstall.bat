@echo off
chcp 65001 >nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PlayTimer /f >nul 2>&1
taskkill /im PlayTimer.exe /f >nul 2>&1
echo 자동 실행을 해제했습니다.
