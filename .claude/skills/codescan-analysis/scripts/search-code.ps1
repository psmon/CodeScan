<#
.SYNOPSIS
    codescan으로 다중 타입 코드 검색을 수행한다.

.DESCRIPTION
    지정한 쿼리로 여러 검색 타입(method, file, doc, comment, commit)을 한번에 검색한다.
    프로젝트 ID를 지정하면 해당 프로젝트 내에서만 검색한다.

.PARAMETER Query
    검색할 키워드 또는 문자열. 필수.

.PARAMETER ProjectId
    특정 프로젝트 내 검색. 생략 시 전체 프로젝트 대상.

.PARAMETER Types
    검색할 타입 목록. 기본값: method, file.
    가능한 값: method, file, doc, comment, commit

.PARAMETER Limit
    타입별 최대 결과 수. 기본값 20.

.EXAMPLE
    .\search-code.ps1 -Query "OrderService"
    .\search-code.ps1 -Query "WebSocket" -ProjectId 1 -Types method,file,comment
    .\search-code.ps1 -Query "TODO" -Types comment -Limit 50
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$Query,

    [int]$ProjectId = 0,

    [string[]]$Types = @("method", "file"),

    [int]$Limit = 20
)

$ErrorActionPreference = "Stop"

function Write-Section($title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
    Write-Host ""
}

Write-Host "Searching for: '$Query'" -ForegroundColor Green
if ($ProjectId -gt 0) {
    Write-Host "Project: $ProjectId" -ForegroundColor Green
}
Write-Host "Types: $($Types -join ', ')" -ForegroundColor Green
Write-Host ""

foreach ($type in $Types) {
    Write-Section "Search Results - Type: $type"

    $args = @("search", $Query, "-t", $type, "-l", $Limit)
    if ($ProjectId -gt 0) {
        $args += @("-p", $ProjectId)
    }

    $result = & codescan @args 2>&1
    if ($result) {
        Write-Host $result
    } else {
        Write-Host "(no results)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "Search complete." -ForegroundColor Green
