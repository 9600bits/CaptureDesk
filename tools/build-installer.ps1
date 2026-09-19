param(
    [Parameter(Mandatory=$true)][string]$MakeNsis,
    [string]$DotNet = "$env:ProgramW6432\dotnet\dotnet.exe"
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$payload = Join-Path $projectRoot 'dist\0.4\installer-payload'
$output = Join-Path $projectRoot 'dist\0.4\release'
& $DotNet publish (Join-Path $projectRoot 'src\CaptureDesk.App\CaptureDesk.App.csproj') -p:PublishProfile=Installer
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
# Include the notices for the exact runtime versions selected by the SDK.
$assets = Get-Content (Join-Path $projectRoot 'src\CaptureDesk.App\obj\project.assets.json') -Raw | ConvertFrom-Json
$deps = Get-Content (Join-Path $payload 'CaptureDesk.App.deps.json') -Raw | ConvertFrom-Json
$packageRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
foreach ($library in $deps.libraries.PSObject.Properties.Name) {
    if ($library -match '^runtimepack\.(Microsoft\.(?:NETCore|WindowsDesktop)\.App\.Runtime\.win-x64)/(.+)$') {
        $packageName = $Matches[1].ToLowerInvariant()
        $package = Join-Path $packageRoot "$packageName\$($Matches[2])"
        foreach ($notice in Get-ChildItem -LiteralPath $package -File | Where-Object Name -Match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$') {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $payload "licenses\$packageName-$($notice.Name).txt")
        }
    }
}
# Generate an exact uninstall manifest; never recursively delete an install directory.
$manifest = Join-Path $output 'uninstall-files.nsh'
$lines = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
    'Delete "$INSTDIR\' + [IO.Path]::GetRelativePath($payload, $_.FullName) + '"'
})
$lines += @(Get-ChildItem -LiteralPath $payload -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    'RMDir "$INSTDIR\' + [IO.Path]::GetRelativePath($payload, $_.FullName) + '"'
})
[IO.File]::WriteAllLines($manifest, $lines, [Text.UTF8Encoding]::new($true))
$setup = Join-Path $output 'CaptureDesk-0.4-win-x64-setup.exe'
& $MakeNsis /INPUTCHARSET UTF8 "/DPAYLOAD=$payload" "/DOUTPUT=$setup" "/DUNINSTALL_FILES=$manifest" (Join-Path $projectRoot 'installer\CaptureDesk.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$hash  $([IO.Path]::GetFileName($setup))`n")
Get-Item -LiteralPath $setup | Select-Object FullName, Length
