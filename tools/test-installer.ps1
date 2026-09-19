$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testDir = Join-Path $projectRoot 'artifacts\installer-smoke'
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk'
$shortcutFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'CaptureDesk'
if ((Test-Path $testDir) -or (Test-Path $registry) -or (Test-Path $shortcutFolder)) {
    throw 'An existing install or test directory exists; refusing to overwrite it.'
}
$settings = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'CaptureDesk\settings.json'
$settingsHash = if (Test-Path $settings) { (Get-FileHash $settings).Hash } else { '' }
$setup = Join-Path $projectRoot 'dist\0.4\release\CaptureDesk-0.4-win-x64-setup.exe'
$process = Start-Process $setup -ArgumentList "/S /D=$testDir" -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw 'Install failed' }
if ((Get-ItemProperty $registry).DisplayVersion -ne '0.4') { throw 'Wrong installed version' }
if (-not (Test-Path (Join-Path $shortcutFolder 'CaptureDesk.lnk'))) { throw 'Missing Start Menu shortcut' }
$payload = Join-Path $projectRoot 'dist\0.4\installer-payload'
foreach ($file in Get-ChildItem $payload -File -Recurse) {
    $installed = Join-Path $testDir ([IO.Path]::GetRelativePath($payload, $file.FullName))
    if ((Get-FileHash $file.FullName).Hash -ne (Get-FileHash $installed).Hash) { throw "Installed file differs: $($file.Name)" }
}
$report = Join-Path $projectRoot 'artifacts\installer-verification.txt'
$process = Start-Process (Join-Path $testDir 'CaptureDesk.App.exe') -ArgumentList "--verify-ui `"$report`"" -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw 'Installed app verification failed' }
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
'PASS installer file hashes, installed application regression, uninstall, user file and settings preservation'
