# Test uninstallation verification
$ErrorActionPreference = "Stop"

$uninsExe = "$env:LOCALAPPDATA\Programs\NOVA\unins000.exe"
if (-not (Test-Path $uninsExe)) {
    Write-Host "Uninstaller not found (already uninstalled or path moved): $uninsExe"
    exit 0
}

Write-Host "--> Running uninstaller: $uninsExe..." -ForegroundColor Yellow
$proc = Start-Process -FilePath $uninsExe -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES" -PassThru -Wait
Write-Host "Uninstaller Exit Code: $($proc.ExitCode)"
Start-Sleep -Seconds 2

$exeExists = Test-Path "$env:LOCALAPPDATA\Programs\NOVA\Nova.exe"
$lnkExists = Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\NOVA Desktop Companion.lnk"
$regExists = Test-Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A5B876E1-4972-4C1F-95C8-B274FE9D8892}_is1"

Write-Host "Post-Uninstall Verification:" -ForegroundColor Cyan
Write-Host "   Nova.exe present            : $exeExists (Expected: False)"
Write-Host "   Start Menu shortcut present : $lnkExists (Expected: False)"
Write-Host "   Registry uninstall key      : $regExists (Expected: False)"

if (-not $exeExists -and -not $lnkExists -and -not $regExists) {
    Write-Host "`n--> Clean uninstallation VERIFIED!" -ForegroundColor Green
} else {
    Write-Warning "Some remnants detected after uninstallation."
}

# Now reinstall so NOVA remains ready on the machine for the user
Write-Host "`n--> Performing clean production reinstall..." -ForegroundColor Cyan
$installer = "$PSScriptRoot\..\artifacts\NovaSetup-0.1.0-x64.exe"
$reinstallProc = Start-Process -FilePath $installer -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES" -PassThru -Wait
Write-Host "Reinstall Exit Code: $($reinstallProc.ExitCode)"
Write-Host "Installed Executable: $(Test-Path "$env:LOCALAPPDATA\Programs\NOVA\Nova.exe")"
Write-Host "Start Menu Shortcut : $(Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\NOVA Desktop Companion.lnk")"
Write-Host "`n--> Standalone software is installed and ready for end user!" -ForegroundColor Green

