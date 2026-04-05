<#
.SYNOPSIS
    프로젝트의 최근 변경 이력을 분석한다.

.DESCRIPTION
    지정한 프로젝트 경로에서 git 이력을 기반으로 최근 변경사항을 분석한다.
    커밋 이력, 변경된 파일 통계, 저자별 활동을 포함한다.

.PARAMETER ProjectPath
    분석할 프로젝트의 루트 경로. 필수.

.PARAMETER Days
    최근 N일간의 이력 분석. 기본값 7.

.PARAMETER Count
    표시할 최근 커밋 수. 기본값 20.

.PARAMETER Pull
    실행 전에 git pull을 수행할지 여부. 기본값 false.

.EXAMPLE
    .\analyze-changes.ps1 -ProjectPath D:\myproject
    .\analyze-changes.ps1 -ProjectPath D:\myproject -Days 30 -Count 50
    .\analyze-changes.ps1 -ProjectPath D:\myproject -Pull
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$ProjectPath,

    [int]$Days = 7,

    [int]$Count = 20,

    [switch]$Pull
)

$ErrorActionPreference = "Stop"

function Write-Section($title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
    Write-Host ""
}

if (-not (Test-Path $ProjectPath)) {
    Write-Host "Error: Path '$ProjectPath' does not exist." -ForegroundColor Red
    exit 1
}

Push-Location $ProjectPath
try {
    # git repo 확인
    if (-not (Test-Path ".git")) {
        Write-Host "Error: '$ProjectPath' is not a git repository." -ForegroundColor Red
        exit 1
    }

    Write-Host "Analyzing: $ProjectPath" -ForegroundColor Green
    Write-Host "Period: last $Days days | Max commits: $Count" -ForegroundColor Green

    # 선택적 git pull
    if ($Pull) {
        Write-Section "Pulling Latest Changes"
        git pull 2>&1
    }

    # 최근 커밋 이력
    Write-Section "Recent Commits (last $Count)"
    git log --oneline -$Count 2>&1

    # 기간별 변경사항
    $sinceDate = (Get-Date).AddDays(-$Days).ToString("yyyy-MM-dd")
    Write-Section "Changes Since $sinceDate"
    git log --oneline --since="$sinceDate" 2>&1

    # 변경된 파일 통계
    Write-Section "Changed Files (last 5 commits)"
    git diff --stat HEAD~5 2>&1

    # 변경된 파일 목록
    Write-Section "Changed File List (last 5 commits)"
    git diff --name-only HEAD~5 2>&1

    # 저자별 활동
    Write-Section "Author Activity (last $Days days)"
    git shortlog -sn --since="$sinceDate" 2>&1

    # 가장 많이 변경된 파일 (hotspot)
    Write-Section "Hotspot Files (most changed in last $Days days)"
    git log --since="$sinceDate" --name-only --pretty=format: 2>&1 |
        Where-Object { $_ -ne "" } |
        Group-Object |
        Sort-Object Count -Descending |
        Select-Object -First 15 |
        ForEach-Object { Write-Host ("{0,4}  {1}" -f $_.Count, $_.Name) }

} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Analysis complete." -ForegroundColor Green
