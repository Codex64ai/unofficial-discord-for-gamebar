# Measures what DiscordBar (app + its own WebView2 processes) is using right now,
# and shows the widget's recent power-state log. Run on the device at any time:
#   powershell -ExecutionPolicy Bypass -File .\Measure-DiscordBar.ps1
# Optional: -Seconds 10 for a longer CPU sample.
param([int]$Seconds = 5)

$procs = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'DiscordBar.exe' -or
    ($_.Name -eq 'msedgewebview2.exe' -and $_.CommandLine -like '*DiscordBar.Widget*')
}
if (-not $procs) { Write-Host "DiscordBar is not running."; exit }

$ids = $procs.ProcessId
$before = (Get-Process -Id $ids -ErrorAction SilentlyContinue | ForEach-Object { $_.TotalProcessorTime.TotalMilliseconds } | Measure-Object -Sum).Sum
Write-Host "Sampling for $Seconds s..."
Start-Sleep $Seconds
$p = Get-Process -Id $ids -ErrorAction SilentlyContinue
$after = ($p | ForEach-Object { $_.TotalProcessorTime.TotalMilliseconds } | Measure-Object -Sum).Sum

$cpu = ($after - $before) / ($Seconds * 1000) / [Environment]::ProcessorCount * 100
$ram = ($p | Measure-Object WorkingSet64 -Sum).Sum / 1MB

Write-Host ""
Write-Host ("CPU: {0:N1}% of all cores    RAM: {1:N0} MB    processes: {2}" -f $cpu, $ram, $p.Count)
Write-Host ""
Write-Host "Per process:"
$procs | ForEach-Object {
    $gp = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
    $kind = if ($_.Name -eq 'DiscordBar.exe') { 'app' } elseif ($_.CommandLine -match '--type=(\w+)') { $Matches[1] } else { 'browser' }
    if ($gp) { "  {0,-10} {1,6:N0} MB" -f $kind, ($gp.WorkingSet64 / 1MB) }
}

$log = Get-ChildItem "$env:LOCALAPPDATA\Packages\DiscordBar.Widget_*\LocalState\power.log" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($log) {
    Write-Host ""
    Write-Host "Recent power states (power.log):"
    Get-Content $log.FullName -Tail 12 | ForEach-Object { "  $_" }
}
