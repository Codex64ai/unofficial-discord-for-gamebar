# Unofficial Discord for Gamebar installer. Packed into UnofficialDiscordForGamebar-Setup.exe by build-installer.ps1.
# Runs next to DiscordBar.zip (the sideload package). Elevates, trusts the test
# certificate, installs any missing framework packages, then installs the app.

$ErrorActionPreference = 'Stop'
$Host.UI.RawUI.WindowTitle = 'Unofficial Discord for Gamebar Setup'

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host 'Asking for administrator rights (needed to trust the signing certificate)...'
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
    exit
}

$work = Join-Path $env:TEMP 'DiscordBarSetup'
try {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Unpacking...'
    Expand-Archive -Path (Join-Path $PSScriptRoot 'DiscordBar.zip') -DestinationPath $work -Force

    $cer  = Get-ChildItem $work -Filter *.cer  | Select-Object -First 1
    $msix = Get-ChildItem $work -Filter *.msix | Select-Object -First 1
    if (-not $cer -or -not $msix) { throw 'Package is incomplete (missing .cer or .msix).' }

    Write-Host "Trusting signing certificate ($($cer.Name))..."
    Import-Certificate -FilePath $cer.FullName -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

    Write-Host 'Checking framework packages...'
    foreach ($dep in Get-ChildItem (Join-Path $work 'Dependencies\x64') -Filter *.appx) {
        try {
            Add-AppxPackage -Path $dep.FullName -ErrorAction Stop
            Write-Host "  installed $($dep.Name)"
        } catch {
            Write-Host "  already present: $($dep.Name)"
        }
    }

    $existing = Get-AppxPackage -Name 'DiscordBar.Widget' -ErrorAction SilentlyContinue
    Write-Host "Installing $($msix.Name)..."
    if ($existing) { Write-Host "  (replacing version $($existing.Version))" }
    Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion -ErrorAction Stop

    $installed = Get-AppxPackage -Name 'DiscordBar.Widget'
    Write-Host ''
    Write-Host "Unofficial Discord for Gamebar $($installed.Version) is installed." -ForegroundColor Green
    Write-Host 'Press the Xbox button or Win+G, open the Widget menu, and pick "Unofficial Discord".'
    Write-Host 'If it is not in the menu yet, reboot once.'
} catch {
    Write-Host ''
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $env:DISCORDBAR_SETUP_NOPAUSE) {
    Write-Host ''
    Read-Host 'Press Enter to close' | Out-Null
}
