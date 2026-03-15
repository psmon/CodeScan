<#
.SYNOPSIS
    CodeScan - Windows AOT Native Build & Deploy
.DESCRIPTION
    Builds as single native binary (AOT), deploys to D:\Util\CodeScan, adds to PATH.
    Requires .NET 10 SDK with AOT workload + C++ build tools.
.USAGE
    .\Script\deploy-win-aot.ps1
    .\Script\deploy-win-aot.ps1 -DeployPath "C:\Tools\CodeScan"
#>

param(
    [string]$DeployPath = "D:\Util\CodeScan"
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $PSScriptRoot

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  CodeScan - AOT Native Build (Windows)" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Project : $ProjectDir"
Write-Host "  Deploy  : $DeployPath"
Write-Host "  Mode    : Native AOT (single binary)"
Write-Host ""

# 1. Clean
Write-Host "[1/5] Cleaning..." -ForegroundColor Yellow
Remove-Item -Recurse -Force "$ProjectDir\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$ProjectDir\obj" -ErrorAction SilentlyContinue

# 2. Create deploy dir
if (-not (Test-Path $DeployPath)) {
    New-Item -ItemType Directory -Path $DeployPath -Force | Out-Null
}

# 3. AOT Publish
Write-Host "[2/5] Publishing AOT native binary (this may take a few minutes)..." -ForegroundColor Yellow
dotnet publish "$ProjectDir\CodeScan.csproj" `
    -c Release `
    -r win-x64 `
    -o $DeployPath
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "AOT build failed. Falling back to standard publish..." -ForegroundColor Yellow
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
}

# 4. Verify
$ExePath = Join-Path $DeployPath "codescan.exe"
if (-not (Test-Path $ExePath)) {
    Write-Host "codescan.exe not found at $DeployPath" -ForegroundColor Red
    exit 1
}

$FileSize = [math]::Round((Get-Item $ExePath).Length / 1MB, 1)
Write-Host "[3/5] Built: codescan.exe ($FileSize MB)" -ForegroundColor Green

# 5. File count
$FileCount = (Get-ChildItem $DeployPath -File).Count
Write-Host "[4/5] Deploy directory: $FileCount files" -ForegroundColor DarkGray

# 6. Add to PATH
$CurrentPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($CurrentPath -split ";" | Where-Object { $_.Trim() -eq $DeployPath }) {
    Write-Host "[5/5] PATH already contains: $DeployPath" -ForegroundColor DarkGray
} else {
    $NewPath = "$CurrentPath;$DeployPath"
    # Use Registry API to preserve REG_EXPAND_SZ (SetEnvironmentVariable breaks it to REG_SZ)
    $regKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment", $true)
    $regKey.SetValue("Path", $NewPath, [Microsoft.Win32.RegistryValueKind]::ExpandString)
    $regKey.Close()
    $env:Path = "$env:Path;$DeployPath"
    Write-Host "[5/5] Added to user PATH: $DeployPath" -ForegroundColor Green
    Write-Host "      Restart terminal for PATH to take effect globally." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Deploy complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
& $ExePath --version
Write-Host ""
Write-Host "  Usage (from any directory):" -ForegroundColor White
Write-Host "    codescan --help"
Write-Host "    codescan list D:\Code\MyProject --tree --detail"
Write-Host "    codescan tui"
Write-Host ""
