@echo off
chcp 65001 >nul
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe를 찾을 수 없습니다. .NET Framework 4.x가 필요합니다.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0PlayTimer.exe" "%~dp0src\*.cs"
if errorlevel 1 (
  echo 빌드 실패
  exit /b 1
)
echo 빌드 완료: %~dp0PlayTimer.exe
