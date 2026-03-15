<#
.SYNOPSIS
    CodeScan - Windows Build & Deploy Script
.DESCRIPTION
    Builds Release, publishes to D:\Util\CodeScan, and adds to user PATH.
.USAGE
    .\Script\deploy-win.ps1
    .\Script\deploy-win.ps1 -DeployPath "C:\Tools\CodeScan"
#>

param(
    [string]$DeployPath = "D:\Util\CodeScan"
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

# 3. Build & Publish (framework-dependent, no trimming)
Write-Host "[2/4] Building Release..." -ForegroundColor Yellow
dotnet publish "$ProjectDir\CodeScan.csproj" `
    -c Release `
    -o $DeployPath `
    --no-self-contained `
    -p:PublishAot=false `
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

# 5. Add to PATH (User scope, persistent)
$CurrentPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($CurrentPath -split ";" | Where-Object { $_.Trim() -eq $DeployPath }) {
    Write-Host "[4/4] PATH already contains: $DeployPath" -ForegroundColor DarkGray
} else {
    $NewPath = "$CurrentPath;$DeployPath"
    # Use Registry API to preserve REG_EXPAND_SZ (SetEnvironmentVariable breaks it to REG_SZ)
    $regKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment", $true)
    $regKey.SetValue("Path", $NewPath, [Microsoft.Win32.RegistryValueKind]::ExpandString)
    $regKey.Close()
    $env:Path = "$env:Path;$DeployPath"
    Write-Host "[4/4] Added to user PATH: $DeployPath" -ForegroundColor Green
    Write-Host "      Restart terminal for PATH to take effect globally." -ForegroundColor DarkGray
}

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
Write-Host "  Usage (from any directory):" -ForegroundColor White
Write-Host "    codescan --help"
Write-Host "    codescan list D:\Code\MyProject --tree --detail"
Write-Host "    codescan search ""HttpClient"""
Write-Host "    codescan tui"
Write-Host ""
