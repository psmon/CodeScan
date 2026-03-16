<#
.SYNOPSIS
    CodeScan - Windows Build & Deploy Script
.DESCRIPTION
    Builds Release, publishes to ~/.codescan/bin, and adds to user PATH.
.USAGE
    .\Script\deploy-win.ps1
    .\Script\deploy-win.ps1 -DeployPath "C:\Tools\CodeScan"
#>

param(
    [string]$DeployPath = (Join-Path $env:USERPROFILE ".codescan\bin")
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $PSScriptRoot

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  CodeScan - Build & Deploy (Windows)" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Project : $ProjectDir"
Write-Host "  Deploy  : $DeployPath"
Write-Host ""

# 1. Clean bin/obj
Write-Host "[1/4] Cleaning..." -ForegroundColor Yellow
Remove-Item -Recurse -Force "$ProjectDir\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$ProjectDir\obj" -ErrorAction SilentlyContinue

# 2. Create deploy dir
if (-not (Test-Path $DeployPath)) {
    New-Item -ItemType Directory -Path $DeployPath -Force | Out-Null
}

# 3. Build & Publish (self-contained: .NET 런타임 없는 PC에서도 실행 가능)
Write-Host "[2/4] Restoring & Building Release (self-contained)..." -ForegroundColor Yellow
dotnet restore "$ProjectDir\CodeScan.csproj" -r win-x64
if ($LASTEXITCODE -ne 0) {
    Write-Host "Restore failed!" -ForegroundColor Red
    exit 1
}
dotnet publish "$ProjectDir\CodeScan.csproj" `
    -c Release `
    -o $DeployPath `
    --no-restore `
    --self-contained `
    -r win-x64 `
    -p:PublishAot=false `
    -p:PublishSingleFile=true `
    -p:TrimMode="" `
    -p:IlcOptimizationPreference=""
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# 4. Verify
$ExePath = Join-Path $DeployPath "codescan.exe"
if (Test-Path $ExePath) {
    $FileSize = [math]::Round((Get-Item $ExePath).Length / 1KB, 1)
    Write-Host "[3/4] Built: codescan.exe ($FileSize KB)" -ForegroundColor Green
} else {
    $DllPath = Join-Path $DeployPath "codescan.dll"
    if (Test-Path $DllPath) {
        Write-Host "[3/4] Built: codescan.dll (use: dotnet codescan.dll)" -ForegroundColor Green
    } else {
        Write-Host "Build output not found!" -ForegroundColor Red
        exit 1
    }
}

# 5. Add to PATH via PowerShell Profile (no registry manipulation)
$ProfilePath = $PROFILE.CurrentUserAllHosts  # ~\Documents\PowerShell\profile.ps1
$PathLine = "`$env:Path += `";$DeployPath`""
$MarkerComment = "# [CodeScan] PATH"

$NeedAdd = $true
if (Test-Path $ProfilePath) {
    $ProfileContent = Get-Content $ProfilePath -Raw
    if ($ProfileContent -match [regex]::Escape($MarkerComment)) {
        $NeedAdd = $false
    }
}

if ($NeedAdd) {
    # Ensure profile directory exists
    $ProfileDir = Split-Path $ProfilePath -Parent
    if (-not (Test-Path $ProfileDir)) {
        New-Item -ItemType Directory -Path $ProfileDir -Force | Out-Null
    }
    # Append PATH entry to profile
    Add-Content -Path $ProfilePath -Value "`n$MarkerComment`n$PathLine"
    Write-Host "[4/4] Added to PowerShell Profile: $ProfilePath" -ForegroundColor Green
    Write-Host "      New PowerShell sessions will recognize 'codescan' automatically." -ForegroundColor DarkGray
} else {
    Write-Host "[4/4] PowerShell Profile already contains CodeScan PATH." -ForegroundColor DarkGray
}

# Apply to current session immediately
$env:Path += ";$DeployPath"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Deploy complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Version check
& $ExePath --version 2>$null
if ($LASTEXITCODE -ne 0) {
    dotnet (Join-Path $DeployPath "codescan.dll") --version 2>$null
}

Write-Host ""
Write-Host "  Deploy path: $DeployPath" -ForegroundColor White
Write-Host "  Data path:   $env:USERPROFILE\.codescan\db\" -ForegroundColor White
Write-Host "  Logs path:   $env:USERPROFILE\.codescan\logs\" -ForegroundColor White
Write-Host ""
Write-Host "  Usage (from any directory):" -ForegroundColor White
Write-Host "    codescan --help"
Write-Host "    codescan list D:\Code\MyProject --tree --detail"
Write-Host "    codescan search ""HttpClient"""
Write-Host "    codescan tui"
Write-Host ""
