param([switch]$Update)

$ErrorActionPreference = 'Stop'
$failed = $false
try {
    $winget = Get-Command winget.exe -ErrorAction Stop
    $operation = if ($Update) { 'upgrade' } else { 'install' }
    foreach ($package in @('yt-dlp.yt-dlp', 'Gyan.FFmpeg', 'DenoLand.Deno')) {
        Write-Host "`n$operation $package"
        & $winget.Source $operation --id $package --exact --source winget
        if ($LASTEXITCODE -ne 0) {
            Write-Host "WinGet returned $LASTEXITCODE for $package. Review the output above."
            $failed = $true
        }
    }
} catch {
    Write-Host "`n$($_.Exception.Message)"
    Write-Host 'Install or update App Installer from Microsoft Store, then try Setup tools again.'
    $failed = $true
}

Write-Host "`nReturn to YouTube MP3 when setup finishes."
[void](Read-Host 'Press Enter to close')
if ($failed) { exit 1 }
