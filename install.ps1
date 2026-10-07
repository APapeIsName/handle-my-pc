# PlayTimer installer.
#
#   One-line install (PowerShell):
#     irm https://raw.githubusercontent.com/APapeIsName/handle-my-pc/HEAD/install.ps1 | iex
#
#   Or extract the release zip / source zip and double-click install.bat.
#
# Installs to %LOCALAPPDATA%\Programs\PlayTimer for the current user only (no admin needed),
# starts it at logon, adds a Start menu shortcut and an entry in Settings > Apps.
# Kept ASCII-only so it runs the same through "irm | iex" and on Windows PowerShell 5.1.

$ErrorActionPreference = 'Stop'
$Repo = 'APapeIsName/handle-my-pc'
$AppName = 'PlayTimer'
$Dest = Join-Path $env:LOCALAPPDATA "Programs\$AppName"

function Say($msg) { Write-Host "[PlayTimer] $msg" }

# 1. Find the files to install: next to this script, or download them.
$Src = $PSScriptRoot
if (-not $Src -or -not (Test-Path (Join-Path $Src 'config.ini'))) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $tmp = Join-Path $env:TEMP ("PlayTimer-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    $zip = Join-Path $tmp 'PlayTimer.zip'
    try {
        Say 'Downloading the latest release...'
        Invoke-WebRequest -UseBasicParsing "https://github.com/$Repo/releases/latest/download/PlayTimer.zip" -OutFile $zip
    } catch {
        Say 'No release found. Downloading the source code instead...'
        Invoke-WebRequest -UseBasicParsing "https://github.com/$Repo/archive/HEAD.zip" -OutFile $zip
    }
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $found = Get-ChildItem -Path $tmp -Recurse -Filter 'config.ini' | Select-Object -First 1
    if (-not $found) { throw 'Downloaded archive does not contain PlayTimer.' }
    $Src = $found.DirectoryName
}

# 2. Build from source if there is no prebuilt exe (uses the compiler built into Windows).
$exe = Join-Path $Src 'PlayTimer.exe'
if (-not (Test-Path $exe)) {
    Say 'Building PlayTimer.exe...'
    & cmd /c "`"$(Join-Path $Src 'build.bat')`""
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw 'Build failed.' }
}

# 3. Stop a running copy, then copy files. Keep the user's existing config.ini.
Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
New-Item -ItemType Directory -Path $Dest -Force | Out-Null
Copy-Item $exe $Dest -Force
foreach ($f in 'uninstall.ps1', 'README.md') {
    $p = Join-Path $Src $f
    if (Test-Path $p) { Copy-Item $p $Dest -Force }
}
if (-not (Test-Path (Join-Path $Dest 'config.ini'))) {
    Copy-Item (Join-Path $Src 'config.ini') $Dest
}
Get-ChildItem $Dest | Unblock-File
$destExe = Join-Path $Dest 'PlayTimer.exe'

# 4. Start at logon.
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $AppName -Value "`"$destExe`""

# 5. Start menu shortcut.
$programs = [Environment]::GetFolderPath('Programs')
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path $programs "$AppName.lnk"))
$lnk.TargetPath = $destExe
$lnk.WorkingDirectory = $Dest
$lnk.Description = 'PlayTimer'
$lnk.Save()

# 6. Settings > Apps entry (uninstall from there).
$version = (Get-Item $destExe).VersionInfo.FileVersion
if (-not $version) { $version = '1.0.0' }
$key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
New-Item -Path $key -Force | Out-Null
$uninstall = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $Dest 'uninstall.ps1')`""
$values = @{
    DisplayName = 'PlayTimer'; DisplayIcon = $destExe; DisplayVersion = $version; Publisher = 'PlayTimer'
    InstallLocation = $Dest; UninstallString = $uninstall; QuietUninstallString = $uninstall
    URLInfoAbout = "https://github.com/$Repo"
}
foreach ($k in $values.Keys) { Set-ItemProperty -Path $key -Name $k -Value $values[$k] }
Set-ItemProperty -Path $key -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $key -Name NoRepair -Value 1 -Type DWord

# 7. Run it.
Start-Process $destExe
Say "Installed to $Dest"
Say 'PlayTimer is now running in the tray (bottom-right). It will start automatically when you log in.'
