@echo off
rem Builds PlayTimer.exe with the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to install.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [PlayTimer] csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:"%~dp0assets\PlayTimer.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0PlayTimer.exe" "%~dp0src\*.cs"
if errorlevel 1 (
  echo [PlayTimer] Build failed.
  exit /b 1
)
echo [PlayTimer] Built: %~dp0PlayTimer.exe
