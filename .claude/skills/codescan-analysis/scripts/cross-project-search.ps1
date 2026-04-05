<#
.SYNOPSIS
    등록된 전체 프로젝트를 대상으로 통합 검색한다.

.DESCRIPTION
    codescan에 등록된 모든 프로젝트를 대상으로 검색하고,
    결과를 프로젝트별로 분류하여 출력한다.

.PARAMETER Query
    검색할 키워드 또는 문자열. 필수.

.PARAMETER Type
    검색 타입 필터. 가능한 값: method, file, doc, comment, commit
    생략 시 전체 타입 대상.

.PARAMETER Limit
    최대 결과 수. 기본값 30.

.EXAMPLE
    .\cross-project-search.ps1 -Query "WebSocket"
    .\cross-project-search.ps1 -Query "authentication" -Type method
    .\cross-project-search.ps1 -Query "TODO" -Type comment -Limit 50
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$Query,

    [string]$Type = "",

    [int]$Limit = 30
)

$ErrorActionPreference = "Stop"

function Write-Section($title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
    Write-Host ""
}

Write-Host "Cross-Project Search: '$Query'" -ForegroundColor Green
if ($Type) {
    Write-Host "Type filter: $Type" -ForegroundColor Green
}
Write-Host ""

# 전체 프로젝트 목록 조회
Write-Section "Registered Projects"
codescan projects 2>&1

# 통합 검색 실행
Write-Section "Search Results"
$searchArgs = @("search", $Query, "-l", $Limit)
if ($Type) {
    $searchArgs += @("-t", $Type)
}

$result = & codescan @searchArgs 2>&1
if ($result) {
    Write-Host $result
} else {
    Write-Host "(no results found)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Search complete." -ForegroundColor Green
