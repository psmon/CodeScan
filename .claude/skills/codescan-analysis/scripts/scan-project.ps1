<#
.SYNOPSIS
    프로젝트를 codescan에 스캔하고 등록한다.

.DESCRIPTION
    지정한 경로의 프로젝트를 codescan scan으로 스캔하여 DB에 등록한다.
    scan은 list --detail --tree --stats의 단축 명령이다.

.PARAMETER Path
    스캔할 프로젝트 경로. 필수.

.PARAMETER Include
    포함할 확장자 (쉼표 구분). 예: ".cs,.java"

.PARAMETER Exclude
    제외할 디렉토리 (쉼표 구분). 예: "bin,obj,node_modules"

.PARAMETER Depth
    최대 탐색 깊이.

.EXAMPLE
    .\scan-project.ps1 -Path D:\myproject
    .\scan-project.ps1 -Path D:\myproject -Include ".cs,.ts" -Exclude "bin,obj"
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$Path,

    [string]$Include = "",
    [string]$Exclude = "",
    [int]$Depth = 0
)

$ErrorActionPreference = "Stop"

function Write-Section($title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
    Write-Host ""
}

if (-not (Test-Path $Path)) {
    Write-Host "Error: Path '$Path' does not exist." -ForegroundColor Red
    exit 1
}

# 스캔 실행 (scan = list --detail --tree --stats 단축)
Write-Section "Scanning Project"
Write-Host "Path: $Path" -ForegroundColor Green

$scanArgs = @("scan", $Path)
if ($Include) { $scanArgs += @("-i", $Include) }
if ($Exclude) { $scanArgs += @("-e", $Exclude) }
if ($Depth -gt 0) { $scanArgs += @("-d", $Depth) }

& codescan @scanArgs 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Scan failed." -ForegroundColor Red
    exit 1
}

# 등록된 프로젝트 확인
Write-Section "Registered Projects"
codescan projects 2>&1

Write-Host ""
Write-Host "Scan complete. Use 'codescan project-addinfo <ID> <description>' to add a project description." -ForegroundColor Green
