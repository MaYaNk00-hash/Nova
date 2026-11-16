# Test installed Nova lifecycle and single-instance enforcement
$ErrorActionPreference = "Stop"

$installedExe = "$env:LOCALAPPDATA\Programs\NOVA\Nova.exe"
if (-not (Test-Path $installedExe)) {
    Write-Error "Installed executable not found: $installedExe"
    exit 1
}

Write-Host "--> Launching installed Nova.exe..." -ForegroundColor Cyan
$proc = Start-Process -FilePath $installedExe -ArgumentList "--compact" -PassThru
Start-Sleep -Seconds 3

try {
    $proc.Refresh()
    Write-Host "Primary Process ID       : $($proc.Id)"
    Write-Host "Primary HasExited        : $($proc.HasExited)"
    $wsMB = [math]::Round($proc.WorkingSet64 / 1MB, 2)
    Write-Host "Working Set RAM Usage    : $wsMB MB"

    if ($proc.HasExited) {
        Write-Error "Primary process exited unexpectedly!"
        exit 1
    }

    # Test single-instance enforcement
    Write-Host "`n--> Testing duplicate instance launch..." -ForegroundColor Yellow
    $proc2 = Start-Process -FilePath $installedExe -ArgumentList "--compact" -PassThru -Wait
    Write-Host "Duplicate instance exit code: $($proc2.ExitCode)"

    $activeProcs = @(Get-Process -Name Nova -ErrorAction SilentlyContinue)
    Write-Host "Active Nova processes count : $($activeProcs.Count)"

    if ($activeProcs.Count -ne 1) {
        Write-Error "Single-instance enforcement failed! Expected 1 process, found $($activeProcs.Count)."
        exit 1
    }

    Write-Host "`n--> Verifying runtime log creation..." -ForegroundColor Cyan
    $logPath = "$env:LOCALAPPDATA\Nova\Logs\nova-runtime.log"
    if (Test-Path $logPath) {
        $lastLog = Get-Content $logPath -Tail 5
        Write-Host "Recent Log Entries:"
        $lastLog | ForEach-Object { Write-Host "   $_" -ForegroundColor DarkGray }
    } else {
        Write-Host "Log file not found at: $logPath" -ForegroundColor DarkYellow
    }

    Write-Host "`n--> Single-instance & runtime execution test PASSED!" -ForegroundColor Green
}
finally {
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
        Write-Host "Cleaned up primary test process." -ForegroundColor DarkGray
    }
}

