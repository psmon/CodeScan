<#
.SYNOPSIS
    CodeScan — Windows direct installer (downloads pre-built release from GitHub).

.DESCRIPTION
    Downloads the requested CodeScan release asset for win-x64 from GitHub,
    verifies SHA256 against checksums.txt, installs to ~/.codescan/bin, and
    registers PATH via the current-user PowerShell profile.

    User data at ~/.codescan/{db,logs,config} is never modified.

.PARAMETER Version
    Release version to install (e.g. "0.3.94"). Default: "latest".

.PARAMETER InstallDir
    Installation directory. Default: $HOME\.codescan\bin

.PARAMETER Repo
    GitHub repository (owner/name). Default: psmon/CodeScan

.PARAMETER NoPath
    Skip PATH registration.

.EXAMPLE
    iwr https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install-win.ps1 -OutFile install-win.ps1
    .\install-win.ps1

.EXAMPLE
    .\install-win.ps1 -Version 0.3.94
#>

[CmdletBinding()]
param(
    [string]$Version = 'latest',
    [string]$InstallDir = (Join-Path $env:USERPROFILE '.codescan\bin'),
    [string]$Repo = 'psmon/CodeScan',
    [switch]$NoPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

function Write-Step($msg)  { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "    $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "    $msg" -ForegroundColor Yellow }
function Write-Err($msg)   { Write-Host "    $msg" -ForegroundColor Red }

# 1. Detect architecture
$arch = $env:PROCESSOR_ARCHITECTURE
if ($arch -ne 'AMD64') {
    Write-Err "Unsupported architecture: $arch (only AMD64/x64 supported in v1)."
    Write-Err "For arm64, please build from source: https://github.com/$Repo"
    exit 1
}
$rid = 'win-x64'
$assetName = "codescan-$rid.zip"

# 2. Resolve release tag
Write-Step "Resolving release"
if ($Version -eq 'latest') {
    $apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
} else {
    $tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
    $apiUrl = "https://api.github.com/repos/$Repo/releases/tags/$tag"
}
try {
    $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ 'User-Agent' = 'codescan-installer' }
} catch {
    Write-Err "Failed to fetch release info from $apiUrl"
    Write-Err $_.Exception.Message
    exit 1
}
$tag     = $release.tag_name
$ver     = $tag.TrimStart('v')
$assetUrl = ($release.assets | Where-Object { $_.name -eq $assetName }).browser_download_url
$sumsUrl  = ($release.assets | Where-Object { $_.name -eq 'checksums.txt' }).browser_download_url
if (-not $assetUrl) {
    Write-Err "Asset '$assetName' not found in release '$tag'."
    Write-Err "Available assets:"
    $release.assets | ForEach-Object { Write-Err "  - $($_.name)" }
    exit 1
}
Write-Ok "Tag: $tag"
Write-Ok "Asset: $assetName"

# 3. Download to temp
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("codescan-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
$assetPath = Join-Path $tmp $assetName
Write-Step "Downloading asset"
Invoke-WebRequest -Uri $assetUrl -OutFile $assetPath
Write-Ok "Downloaded to $assetPath"

# 4. Verify SHA256
Write-Step "Verifying checksum"
if (-not $sumsUrl) {
    Write-Warn2 "checksums.txt not found in release — skipping verification (NOT RECOMMENDED)."
} else {
    $sumsPath = Join-Path $tmp 'checksums.txt'
    Invoke-WebRequest -Uri $sumsUrl -OutFile $sumsPath
    $expected = (Get-Content $sumsPath | Where-Object { $_ -match [regex]::Escape($assetName) } |
                 ForEach-Object { ($_ -split '\s+')[0] } | Select-Object -First 1)
    if (-not $expected) {
        Write-Err "Could not find $assetName entry in checksums.txt"
        exit 1
    }
    $actual = (Get-FileHash -Algorithm SHA256 -Path $assetPath).Hash.ToLower()
    if ($expected.ToLower() -ne $actual) {
        Write-Err "Checksum mismatch!"
        Write-Err "  expected: $expected"
        Write-Err "  actual:   $actual"
        exit 1
    }
    Write-Ok "SHA256 verified: $actual"
}

# 5. Install
Write-Step "Installing to $InstallDir"
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}
$extractDir = Join-Path $tmp 'extract'
Expand-Archive -Path $assetPath -DestinationPath $extractDir -Force
$srcExe = Get-ChildItem -Path $extractDir -Recurse -Filter 'codescan.exe' | Select-Object -First 1
if (-not $srcExe) {
    Write-Err "codescan.exe not found inside $assetName"
    exit 1
}
$dstExe = Join-Path $InstallDir 'codescan.exe'
Copy-Item -Path $srcExe.FullName -Destination $dstExe -Force
# Optional companions
foreach ($f in 'VERSION', 'README.md', 'LICENSE') {
    $src = Get-ChildItem -Path $extractDir -Recurse -Filter $f -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($src) { Copy-Item -Path $src.FullName -Destination (Join-Path $InstallDir $f) -Force }
}
Write-Ok "Installed: $dstExe"

# 6. PATH (via PowerShell profile, no registry write)
if (-not $NoPath) {
    Write-Step "Registering PATH"
    $profilePath = $PROFILE.CurrentUserAllHosts
    $marker      = '# [CodeScan] PATH'
    $line        = "`$env:Path += `";$InstallDir`""
    $needAdd = $true
    if (Test-Path $profilePath) {
        $content = Get-Content $profilePath -Raw
        if ($content -and ($content -match [regex]::Escape($marker))) { $needAdd = $false }
    }
    if ($needAdd) {
        $dir = Split-Path $profilePath -Parent
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Add-Content -Path $profilePath -Value "`n$marker`n$line"
        Write-Ok "Appended PATH to $profilePath"
    } else {
        Write-Ok "PATH already registered in profile"
    }
    $env:Path += ";$InstallDir"
}

# 7. Verify
Write-Step "Verifying install"
try {
    & $dstExe --version
    Write-Ok "codescan is working"
} catch {
    Write-Warn2 "codescan --version failed (binary may still work via other commands)"
}

# 8. Cleanup
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  CodeScan $ver installed" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Install dir : $InstallDir"
Write-Host "  Data dir    : $env:USERPROFILE\.codescan\ (preserved across installs)"
Write-Host ""
Write-Host "  Open a NEW PowerShell window so PATH takes effect, then:"
Write-Host "    codescan --help"
Write-Host ""
