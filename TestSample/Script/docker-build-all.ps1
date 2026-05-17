#!/usr/bin/env pwsh
# TestSample/Script/docker-build-all.ps1
# 모든 언어 샘플의 Dockerfile 빌드 검증.
#
# 사용:
#   pwsh ./TestSample/Script/docker-build-all.ps1
#   pwsh ./TestSample/Script/docker-build-all.ps1 -Run
#
# -Run 옵션: 빌드 성공 시 컨테이너를 실제 실행하여 출력 확인.

param(
    [switch]$Run
)

$ErrorActionPreference = "Stop"
$root = Join-Path $PSScriptRoot ".."
$languages = @("csharp", "java", "kotlin", "javascript", "typescript", "php", "python", "go", "rust", "cpp")
$results = @()

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: docker not found in PATH" -ForegroundColor Red
    exit 1
}

foreach ($lang in $languages) {
    $dir = Join-Path $root $lang
    if (-not (Test-Path $dir)) {
        Write-Host "[SKIP] $lang (not found)" -ForegroundColor Yellow
        continue
    }

    $tag = "codescan-sample-$lang"
    Write-Host "`n=== Building $lang ===" -ForegroundColor Cyan

    try {
        docker build -q -t $tag $dir | Out-Null
        Write-Host "[OK]  $lang built as $tag" -ForegroundColor Green
        $status = "ok"

        if ($Run) {
            Write-Host "--- $lang output ---" -ForegroundColor DarkGray
            docker run --rm $tag
        }
    } catch {
        Write-Host "[FAIL] $lang : $_" -ForegroundColor Red
        $status = "fail"
    }

    $results += [pscustomobject]@{ Language = $lang; Status = $status }
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
$results | Format-Table -AutoSize

$failCount = ($results | Where-Object { $_.Status -eq "fail" }).Count
if ($failCount -gt 0) {
    Write-Host "$failCount failure(s)" -ForegroundColor Red
    exit 1
}
Write-Host "All builds passed" -ForegroundColor Green
