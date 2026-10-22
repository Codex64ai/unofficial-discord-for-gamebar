# Run on the Ally: pushes DiscordBar's power.log to the PC every 5 seconds until you close the window.
#   powershell -ExecutionPolicy Bypass -File .\Send-Log.ps1
$pc = "http://192.168.50.13:8099/log"
$log = Get-ChildItem "$env:LOCALAPPDATA\Packages\DiscordBar.Widget_*\LocalState\power.log" | Select-Object -First 1
if (-not $log) { Write-Host "power.log not found"; exit }
Write-Host "Sending $($log.FullName) to $pc every 5 s. Close this window to stop."
while ($true) {
    try {
        $body = [System.IO.File]::ReadAllText($log.FullName)
        Invoke-RestMethod -Method Post -Uri $pc -Body $body -ContentType "text/plain" | Out-Null
        Write-Host ("{0:HH:mm:ss} sent {1} bytes" -f (Get-Date), $body.Length)
    } catch { Write-Host ("{0:HH:mm:ss} send failed: {1}" -f (Get-Date), $_.Exception.Message) }
    Start-Sleep 5
}
