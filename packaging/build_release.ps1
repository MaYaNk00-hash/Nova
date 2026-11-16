# NOVA Desktop Companion — Production Release Build Script
# Compiles self-contained x64 binaries, runs tests, and packages Inno Setup installer.

param (
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectRoot = Resolve-Path "$ScriptDir\.."

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host " NOVA Desktop Companion — Release Build & Packaging" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "Project Root : $ProjectRoot"
Write-Host "Timestamp    : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

# 1. Check Inno Setup ISCC compiler
$IsccCandidates = @(
    "C:\Users\mayan\AppData\Local\Programs\Inno Setup\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 7\ISCC.exe"
)

$IsccPath = $null
foreach ($path in $IsccCandidates) {
    if (Test-Path $path) {
        $IsccPath = $path
        break
    }
}

if (-not $IsccPath) {
    # Check if iscc is on PATH
    $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        $IsccPath = $cmd.Source
    }
}

if (-not $IsccPath) {
    Write-Error "Inno Setup compiler (ISCC.exe) not found. Please install Inno Setup 6 or 7."
    exit 1
}

Write-Host "Found Inno Setup : $IsccPath" -ForegroundColor Green

# 2. Run Test Suite
if (-not $SkipTests) {
    Write-Host "`n--> [1/4] Running automated test suite..." -ForegroundColor Yellow
    Push-Location $ProjectRoot
    try {
        dotnet build tests\Nova.Tests\Nova.Tests.csproj -c Release -r win-x64 --no-restore --nologo
        $testExe = "$ProjectRoot\tests\Nova.Tests\bin\Release\net8.0-windows10.0.19041.0\win-x64\Nova.Tests.exe"
        if (-not (Test-Path $testExe)) {
            $testExe = "$ProjectRoot\tests\Nova.Tests\bin\Release\net8.0-windows10.0.19041.0\Nova.Tests.exe"
        }
        if (Test-Path $testExe) {
            & $testExe
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Automated test suite failed with exit code $LASTEXITCODE. Aborting build."
                exit 1
            }
        }
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "`n--> [1/4] Skipping test suite as requested." -ForegroundColor DarkGray
}

# 3. Clean and Publish Self-Contained Win-x64 Release
Write-Host "`n--> [2/4] Publishing self-contained win-x64 binaries..." -ForegroundColor Yellow
$PublishDir = "$ProjectRoot\packaging\publish\win-x64"
$ArtifactsDir = "$ProjectRoot\artifacts"

if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

if (-not (Test-Path $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

Push-Location $ProjectRoot
try {
    dotnet publish src\Nova\Nova.csproj `
        -c Release `
        -r win-x64 `
        --no-restore `
        --self-contained true `
        -p:PublishReadyToRun=false `
        -p:PublishSingleFile=false `
        -o $PublishDir `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed with exit code $LASTEXITCODE."
        exit 1
    }
}
finally {
    Pop-Location
}

# Verify output executable exists in publish directory
$PublishedExe = "$PublishDir\Nova.exe"
if (-not (Test-Path $PublishedExe)) {
    Write-Error "Published executable not found at: $PublishedExe"
    exit 1
}
$pubSizeMB = [math]::Round((Get-Item $PublishedExe).Length / 1MB, 2)
Write-Host "Published Nova.exe size : $pubSizeMB MB" -ForegroundColor Green

# 4. Compile Inno Setup Installer
Write-Host "`n--> [3/4] Compiling Inno Setup installer..." -ForegroundColor Yellow
$IssScript = "$ProjectRoot\packaging\installer\NovaSetup.iss"
if (-not (Test-Path $IssScript)) {
    Write-Error "Installer script not found at: $IssScript"
    exit 1
}

& "$IsccPath" "$IssScript"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compiler failed with exit code $LASTEXITCODE."
    exit 1
}

# 5. Verify Final Installer Artifact
Write-Host "`n--> [4/4] Verifying generated installer artifact..." -ForegroundColor Yellow
$InstallerExe = "$ArtifactsDir\NovaSetup-0.1.0-x64.exe"
if (-not (Test-Path $InstallerExe)) {
    Write-Error "Expected installer not found at: $InstallerExe"
    exit 1
}

$installerItem = Get-Item $InstallerExe
$installerSizeMB = [math]::Round($installerItem.Length / 1MB, 2)

Write-Host "`n====================================================" -ForegroundColor Green
Write-Host " BUILD & PACKAGING COMPLETE!" -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Green
Write-Host "Installer Artifact : $($installerItem.FullName)"
Write-Host "Installer File Size: $installerSizeMB MB ($($installerItem.Length) bytes)"
Write-Host "Target Architecture: Windows x64 (Self-Contained .NET 8)"
Write-Host "====================================================" -ForegroundColor Green
