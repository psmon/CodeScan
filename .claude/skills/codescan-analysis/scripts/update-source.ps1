<#
.SYNOPSIS
    등록된 프로젝트의 소스를 최신화하고 DB를 재인덱싱한다.

.DESCRIPTION
    codescan project-update --source를 실행하여 git pull + 전체 재스캔을 수행한다.
    git이 없거나 pull이 실패하면 경고 출력 후 현재 소스로 스캔을 진행한다.

.PARAMETER ProjectId
    업데이트할 프로젝트 ID. 필수.

.EXAMPLE
    .\update-source.ps1 -ProjectId 1
    .\update-source.ps1 -ProjectId 2
#>
param(
    [Parameter(Mandatory=$true)]
    [int]$ProjectId
)

$ErrorActionPreference = "Stop"

function Write-Section($title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
    Write-Host ""
}

# 프로젝트 존재 여부 확인
Write-Section "Checking Project #$ProjectId"
$projectInfo = codescan project $ProjectId 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Project #$ProjectId not found." -ForegroundColor Red
    Write-Host "Run 'codescan projects' to see available projects." -ForegroundColor Yellow
    exit 1
}
Write-Host $projectInfo

# 소스 업데이트 실행
Write-Section "Updating Source"
codescan project-update $ProjectId --source 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Source update failed." -ForegroundColor Red
    exit 1
}

# 업데이트 결과 확인
Write-Section "Updated Project Info"
codescan project $ProjectId 2>&1

Write-Host ""
Write-Host "Source update complete." -ForegroundColor Green
