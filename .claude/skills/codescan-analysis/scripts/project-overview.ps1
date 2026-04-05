<#
.SYNOPSIS
    codescan 프로젝트 요약 및 최근 git 활동을 조회한다.

.DESCRIPTION
    지정한 프로젝트의 codescan 요약 정보와 최근 git 커밋 이력을 함께 출력한다.
    프로젝트 ID를 지정하지 않으면 등록된 전체 프로젝트 목록을 보여준다.

.PARAMETER ProjectId
    codescan 프로젝트 ID. 생략 시 전체 프로젝트 목록 출력.

.PARAMETER GitLogCount
    최근 git 커밋 표시 개수. 기본값 10.

.EXAMPLE
    .\project-overview.ps1
    .\project-overview.ps1 -ProjectId 1
    .\project-overview.ps1 -ProjectId 1 -GitLogCount 20
#>
param(
    [int]$ProjectId = 0,
    [int]$GitLogCount = 10
)

$ErrorActionPreference = "Stop"

function Write-Section($title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
    Write-Host ""
}

# 프로젝트 ID 미지정 시 전체 목록 출력
if ($ProjectId -eq 0) {
    Write-Section "Registered Projects"
    codescan projects
    exit 0
}

# 프로젝트 요약 조회
Write-Section "Project Summary (ID: $ProjectId)"
$projectInfo = codescan project $ProjectId 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Project ID $ProjectId not found." -ForegroundColor Red
    Write-Host "Run 'codescan projects' to see available projects."
    exit 1
}
Write-Host $projectInfo

# codescan project 출력에서 경로 추출
$projectPath = ($projectInfo | Select-String -Pattern "Path\s*:\s*(.+)$" | ForEach-Object { $_.Matches[0].Groups[1].Value.Trim() })

if (-not $projectPath -or -not (Test-Path $projectPath)) {
    Write-Host "Warning: Project path not found or inaccessible. Skipping git history." -ForegroundColor Yellow
    exit 0
}

# 최근 git 활동
Write-Section "Recent Git Activity (last $GitLogCount commits)"
Push-Location $projectPath
try {
    git log --oneline -$GitLogCount 2>&1
    Write-Host ""

    # 최근 활동 저자 통계
    Write-Section "Active Authors (last 30 days)"
    git shortlog -sn --since="30 days ago" 2>&1
} finally {
    Pop-Location
}
