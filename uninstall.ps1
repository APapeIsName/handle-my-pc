# PlayTimer uninstaller. Removes the program, autostart, shortcut and the Settings > Apps entry.
# Your schedule and usage data in %LOCALAPPDATA%\PlayTimer are kept unless you pass -Purge.
param([switch]$Purge)

$AppName = 'PlayTimer'
$Dest = Join-Path $env:LOCALAPPDATA "Programs\$AppName"

Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $AppName -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk") -ErrorAction SilentlyContinue
Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName" -Recurse -ErrorAction SilentlyContinue
Remove-Item $Dest -Recurse -Force -ErrorAction SilentlyContinue
if ($Purge) { Remove-Item (Join-Path $env:LOCALAPPDATA $AppName) -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host '[PlayTimer] Uninstalled.'
if (-not $Purge) { Write-Host "[PlayTimer] Your schedule is kept in $env:LOCALAPPDATA\$AppName (run with -Purge to delete it)." }
