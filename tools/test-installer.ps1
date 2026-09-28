$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testDir = Join-Path $projectRoot "artifacts\installer-smoke-$PID"
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk'
$shortcutFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'CaptureDesk'
if ((Test-Path $testDir) -or (Test-Path $registry) -or (Test-Path $shortcutFolder)) {
    throw 'An existing install or test directory exists; refusing to overwrite it.'
}
$settings = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'CaptureDesk\settings.json'
$settingsHash = if (Test-Path $settings) { (Get-FileHash $settings).Hash } else { '' }
$setup = Join-Path $projectRoot 'dist\0.5\release\CaptureDesk-0.5-win-x64-setup.exe'
$process = Start-Process $setup -ArgumentList "/S /D=$testDir" -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw 'Install failed' }
if ((Get-ItemProperty $registry).DisplayVersion -ne '0.5') { throw 'Wrong installed version' }
if (-not (Test-Path (Join-Path $shortcutFolder 'CaptureDesk.lnk'))) { throw 'Missing Start Menu shortcut' }
$payload = Join-Path $projectRoot 'dist\0.5\installer-payload'
foreach ($file in Get-ChildItem $payload -File -Recurse) {
    $installed = Join-Path $testDir ([IO.Path]::GetRelativePath($payload, $file.FullName))
    if ((Get-FileHash $file.FullName).Hash -ne (Get-FileHash $installed).Hash) { throw "Installed file differs: $($file.Name)" }
}
$report = Join-Path $projectRoot 'artifacts\installer-verification.txt'
$process = Start-Process (Join-Path $testDir 'CaptureDesk.App.exe') -ArgumentList "--verify-ui `"$report`"" -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw 'Installed app verification failed' }
$instanceReport = Join-Path $projectRoot 'artifacts\installer-single-instance.txt'
Remove-Item -LiteralPath $instanceReport -ErrorAction SilentlyContinue
$running = Start-Process (Join-Path $testDir 'CaptureDesk.App.exe') -ArgumentList @('--verify-single-instance', $instanceReport) -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path $instanceReport) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
    if (-not (Test-Path $instanceReport)) { throw 'Installed app did not acquire its single-instance lock' }
    $blockedInstall = Start-Process $setup -ArgumentList "/S /D=$testDir" -WindowStyle Hidden -PassThru
    if (-not $blockedInstall.WaitForExit(10000)) { $blockedInstall.Kill(); throw 'Running-process install check timed out' }
    if ($blockedInstall.ExitCode -eq 0) { throw 'Installer allowed an update while CaptureDesk was running' }
    $blockedUninstall = Start-Process (Join-Path $testDir 'Uninstall.exe') -ArgumentList '/S' -WindowStyle Hidden -PassThru
    if (-not $blockedUninstall.WaitForExit(10000)) { $blockedUninstall.Kill(); throw 'Running-process uninstall check timed out' }
    # NSIS launches a temporary child uninstaller, so the bootstrap process exit code cannot report un.onInit aborts.
    Start-Sleep -Seconds 3
    if (-not (Test-Path $registry) -or -not (Test-Path (Join-Path $testDir 'CaptureDesk.App.exe'))) {
        throw 'Uninstaller removed CaptureDesk while it was running'
    }
    $activator = Start-Process (Join-Path $testDir 'CaptureDesk.App.exe') -ArgumentList @('--verify-single-instance', $instanceReport) -WindowStyle Hidden -PassThru
    if (-not $activator.WaitForExit(10000)) { $activator.Kill(); throw 'Second installed instance did not exit' }
    if ($activator.ExitCode -ne 0) { throw 'Second installed instance failed to notify the first instance' }
    if (-not $running.WaitForExit(10000)) { throw 'First installed instance did not receive activation' }
    if ($running.ExitCode -ne 0) { throw 'First installed instance failed after activation' }
    Start-Sleep -Seconds 1
    if (-not (Test-Path $registry) -or -not (Test-Path (Join-Path $testDir 'CaptureDesk.App.exe'))) {
        throw 'Blocked uninstaller resumed after CaptureDesk exited'
    }
}
finally {
    if (-not $running.HasExited) { $running.Kill() }
}
# User-created files must survive uninstall, including files inside the app directory.
[IO.File]::WriteAllText((Join-Path $testDir 'keep-user-file.txt'), 'Preserve on uninstall')
$process = Start-Process (Join-Path $testDir 'Uninstall.exe') -ArgumentList '/S' -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) { throw 'Uninstall timed out' }
$deadline = [DateTime]::UtcNow.AddSeconds(30)
while ((Test-Path $registry) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
if ((Test-Path $registry) -or (Test-Path (Join-Path $testDir 'CaptureDesk.App.exe')) -or (Test-Path $shortcutFolder)) { throw 'Uninstall did not remove owned files and entries' }
if (-not (Test-Path (Join-Path $testDir 'keep-user-file.txt'))) { throw 'Uninstall deleted user file' }
$afterHash = if (Test-Path $settings) { (Get-FileHash $settings).Hash } else { '' }
if ($afterHash -ne $settingsHash) { throw 'User configuration changed' }
Get-Content $report
'PASS installer file hashes, installed application regression, running-process guards, uninstall, user file and settings preservation'
Remove-Item -LiteralPath (Join-Path $testDir 'keep-user-file.txt')
Remove-Item -LiteralPath $testDir
