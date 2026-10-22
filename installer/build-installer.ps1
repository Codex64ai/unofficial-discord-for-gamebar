# Wraps the newest DiscordBar_*_x64_Test sideload package plus Setup.ps1 into a single
# self-extracting UnofficialDiscordForGamebar-Setup.exe using IExpress (ships with Windows).
# Usage: powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$pkgDir = Get-ChildItem (Join-Path $root 'DiscordBar\AppPackages') -Directory -Filter 'DiscordBar_*_x64_Test' |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $pkgDir) { throw 'No DiscordBar_*_x64_Test package found. Build the solution first.' }

$stage = Join-Path $env:TEMP 'DiscordBarInstallerStage'
$zipSrc = Join-Path $stage 'zip'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $zipSrc 'Dependencies\x64') | Out-Null

# Only what the Ally needs: the app, its cert, and the x64 frameworks.
Copy-Item (Join-Path $pkgDir.FullName '*.msix') $zipSrc
Copy-Item (Join-Path $pkgDir.FullName '*.cer')  $zipSrc
Copy-Item (Join-Path $pkgDir.FullName 'Dependencies\x64\*.appx') (Join-Path $zipSrc 'Dependencies\x64')
Compress-Archive -Path (Join-Path $zipSrc '*') -DestinationPath (Join-Path $stage 'DiscordBar.zip') -CompressionLevel Optimal
Copy-Item (Join-Path $PSScriptRoot 'Setup.ps1') $stage

$out = Join-Path $root "DiscordBar\AppPackages\UnofficialDiscordForGamebar-Setup.exe"
Remove-Item $out -Force -ErrorAction SilentlyContinue

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=Install Unofficial Discord for Gamebar?
DisplayLicense=
FinishMessage=
TargetName=$out
FriendlyName=Unofficial Discord for Gamebar Setup
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -File Setup.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
FILE0="Setup.ps1"
FILE1="DiscordBar.zip"
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
%FILE0%=
%FILE1%=
"@
$sedPath = Join-Path $stage 'DiscordBar.sed'
Set-Content -Path $sedPath -Value $sed -Encoding ASCII

& "$env:WINDIR\System32\iexpress.exe" /N /Q $sedPath | Out-Null
if (-not (Test-Path $out)) { throw 'IExpress did not produce the installer.' }
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
"Installer: $out ({0:N1} MB)" -f ((Get-Item $out).Length / 1MB)
