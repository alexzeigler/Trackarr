$ErrorActionPreference = 'Stop'
$project = Join-Path (Join-Path $PSScriptRoot 'Trackarr.MockServer') 'Trackarr.MockServer.csproj'
$restartDelay = 3
$shutdownRequested = $false

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
