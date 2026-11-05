$ErrorActionPreference = 'Stop'
$mockServerDir = Join-Path $PSScriptRoot 'Trackarr.MockServer'
$project = Join-Path $mockServerDir 'Trackarr.MockServer.csproj'
$restartDelay = 3
$shutdownRequested = $false
$firstStart = $true

$dashboardPort = 9877
try {
    $settings = Get-Content (Join-Path $mockServerDir 'appsettings.json') -Raw | ConvertFrom-Json
    if ($settings.MockServer.RadarrPort) { $dashboardPort = $settings.MockServer.RadarrPort }
} catch {}
$dashboardUrl = "http://127.0.0.1:$dashboardPort/"

$null = Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action {
    $script:shutdownRequested = $true
}

trap {
    $shutdownRequested = $true
    break
}

Write-Host ''
Write-Host '  Trackarr Mock Server Launcher' -ForegroundColor Cyan
Write-Host '  Press Ctrl+C to stop.' -ForegroundColor DarkGray
Write-Host ''

while (-not $shutdownRequested) {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Starting mock server..." -ForegroundColor Yellow

    $proc = Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run', '--project', $project `
        -NoNewWindow -PassThru

    if ($firstStart) {
        $firstStart = $false
        Start-Job -ScriptBlock {
            param($url)
            for ($i = 0; $i -lt 30; $i++) {
                try {
                    $null = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 1
                    Start-Process $url
                    return
                } catch {
                    Start-Sleep -Seconds 1
                }
            }
        } -ArgumentList $dashboardUrl | Out-Null
    }

    try {
        $proc.WaitForExit()
    } catch {
        $shutdownRequested = $true
        if (-not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
        break
    }

    $exitCode = $proc.ExitCode

    if ($shutdownRequested) {
        break
    }

    if ($exitCode -eq 0) {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Mock server exited cleanly (code 0)." -ForegroundColor Green
        break
    }

    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Mock server crashed (exit code $exitCode). Restarting in $restartDelay seconds..." -ForegroundColor Red
    Start-Sleep -Seconds $restartDelay
}

Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Mock server stopped." -ForegroundColor DarkGray
