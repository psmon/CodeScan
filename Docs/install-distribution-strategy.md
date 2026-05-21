# CodeScan 간편 설치 배포 전략 (v1 확정안)

## 문서 목적

이 문서는 CodeScan 빌드 산출물을 운영체제별 패키지 채널로 배포하기 위한 **v1 확정 전략**이다.

이전 판은 검토 문서였으나, 본 판부터 v1 배포 결정이 확정되었다. 후속 운영 중 변경이 필요한 항목은 본 문서를 직접 갱신하고 `harness/docs/`에 변경 기록을 남긴다.

CodeScan의 지식, 아키텍처, 간편 설치, 배포 운영 같은 기술 주제 문서는 `Docs/` 하위에 보관한다.

## 구현 산출물 위치

본 전략의 1차 구현은 다음 경로에 스캐폴드되어 있다. 자세한 운영 절차는 각 README 참고.

| 구성 요소 | 경로 |
|----------|------|
| CI 릴리즈 워크플로우 | `.github/workflows/release.yml` |
| Windows 직접 인스톨러 | `Script/install-win.ps1` |
| Unix 직접 인스톨러 | `Script/install.sh` |
| 패키지 매니페스트 통합 README | `packaging/README.md` |
| winget 매니페스트 | `packaging/winget/` |
| Homebrew tap formula | `packaging/homebrew/` |
| npm 래퍼 패키지 | `packaging/npm/codescan-cli/` |
| 기존 로컬 배포 스크립트 (변경 없음) | `Script/deploy-win.ps1`, `Script/deploy-linux.sh` |

## 목표

- 사용자가 플랫폼별 익숙한 패키지 매니저로 CodeScan을 설치할 수 있게 한다.
- 설치 명령은 최대한 짧고 예측 가능하게 유지한다.
- 설치 후 `codescan` 명령이 PATH에서 바로 동작해야 한다.
- CLI, TUI, GUI 기능은 동일한 단일 바이너리에서 제공한다.
- 패키지 채널은 단순 설치 진입점으로 보고, 실제 산출물은 GitHub Release 중심으로 관리한다.
- 사용자 데이터(`~/.codescan/`)는 설치/제거/업그레이드와 분리해 영구 보존한다.

## 현재 상태

- CodeScan은 .NET 10 기반 Native AOT 단일 실행 파일을 목표로 한다.
- Windows PowerShell 환경에서 우선 테스트되고 있다.
- Linux 계열 환경은 현재 직접 빌드해서 사용할 수 있다.
- macOS/Linux 호환 CLI 사용법과 스킬 명령 래퍼는 준비 예정이다.
- Windows 배포 스크립트는 `Script/deploy-win.ps1`로 로컬 설치를 지원한다.
- Linux 배포 스크립트는 `Script/deploy-linux.sh`로 로컬 설치를 지원한다.

## v1 확정 의사결정 요약

| 항목 | 결정 |
|------|------|
| 원본 배포 지점 | GitHub Release |
| asset 명명 규칙 | `codescan-{rid}.{ext}` (`win-x64.zip`, `linux-x64.tar.gz`, `linux-arm64.tar.gz`, `osx-x64.tar.gz`, `osx-arm64.tar.gz`) |
| 체크섬 | `checksums.txt` (SHA256, 모든 asset 포함) |
| SBOM | `sbom.cdx.json` (CycloneDX) — Release asset에 포함 |
| 버전 정책 | `version.txt` 단일 소스 → tag `v{version}` → Release title 동일 |
| Windows 채널 | winget (portable zip) |
| Linux 채널 | npm 글로벌 래퍼 (`@psmon/codescan-cli` — 짧은 `codescan-cli`는 제3자 squat) |
| macOS 채널 | Homebrew tap (`psmon/homebrew-codescan`) |
| 보조 설치 경로 | 직접 스크립트 인스톨러 (Win PowerShell / Unix shell) |
| 코드 서명 (Windows) | v1 미적용, v1.x 후속 평가 |
| Notarization (macOS) | v1 미적용, v1.x 후속 평가 |
| Linux libc | glibc 우선 (Ubuntu 22.04 LTS 빌드 기준), musl 미지원 (v2 검토) |
| 아키텍처 | Linux: x64 + arm64 / macOS: arm64만 / Windows: x64만 (Intel Mac과 Windows arm64는 v2 재평가) |
| 사용자 데이터 위치 | `~/.codescan/` — 설치 도구와 분리 |
| 텔레메트리 | 없음 (opt-in 포함) |
| 자동 마이그레이션 | DB 스키마 변경 시 자동 백업 후 마이그레이션 |

## 제안 배포 채널

| 플랫폼 | 채널 | 사용자 설치 경험 | 역할 |
|--------|------|------------------|------|
| Windows | winget | `winget install psmon.CodeScan` | 공식 Windows 설치 경로 |
| Linux 계열 | npm | `npm install -g @psmon/codescan-cli` | 배포 진입점 및 바이너리 설치 래퍼 |
| macOS | Homebrew | `brew install psmon/codescan/codescan` | 공식 macOS 설치 경로 (tap 운영) |

## 공통 배포 원칙

1. GitHub Release를 원본 배포 지점으로 둔다.
2. 각 플랫폼 패키지는 GitHub Release의 버전별 바이너리를 다운로드하거나 포함한다.
3. 버전은 `version.txt`와 GitHub Release 태그를 일치시킨다.
4. 패키지 매니저별 manifest는 최소 정보만 유지하고, 복잡한 설치 로직은 스크립트나 release asset 규칙으로 분리한다.
5. 설치 검증은 `codescan --version`, `codescan --help`, `codescan query --help`, `codescan gui --help`를 기준으로 한다.
6. 모든 채널은 동일한 GitHub Release asset을 가리킨다 — 채널별 별도 빌드 금지.
7. 사용자 데이터(`~/.codescan/`)는 설치/제거 흐름에서 절대 건드리지 않는다.

## 산출물 구조

GitHub Release에는 다음 asset을 제공한다.

```text
codescan-win-x64.zip
codescan-linux-x64.tar.gz
codescan-linux-arm64.tar.gz
codescan-osx-arm64.tar.gz
checksums.txt
sbom.cdx.json
```

> v1에서 `codescan-osx-x64`(Intel Mac)는 제외되었다. GitHub Actions `macos-13` 러너 풀이 좁아 빌드가 큐에서 막히는 일이 잦았기 때문이다. Intel Mac은 v2에서 재평가한다. Intel Mac 사용자는 소스에서 직접 빌드(`Script/deploy-linux.sh` 응용)하거나 Rosetta를 통해 `osx-arm64` 빌드를 실행할 수 있다.

각 압축 파일 내부 구조는 다음을 표준으로 한다.

```text
codescan/
├── codescan(.exe)      실행 파일
├── README.md           간단 사용법 (GitHub README의 요약)
├── LICENSE
├── VERSION             version.txt 사본
└── THIRD_PARTY_NOTICES 의존성 라이선스 모음
```

Windows는 `.exe`, Linux/macOS는 실행 권한이 있는 `codescan` 바이너리를 제공한다.

`checksums.txt` 형식:

```text
<sha256>  codescan-win-x64.zip
<sha256>  codescan-linux-x64.tar.gz
...
```

## 버전 및 태그 정책

- `version.txt`가 단일 소스 of truth. 빌드 시 MSBuild 태스크가 패치 버전을 자동 증가시킨다.
- Git tag: `v{version}` (예: `v0.1.42`).
- GitHub Release title: `v{version}` 동일.
- 모든 채널 manifest는 동일한 `version`을 참조한다.
- Pre-release(`-alpha`, `-beta`, `-rc`)는 Release에 `prerelease=true`로 게시하고, 패키지 채널 PR은 stable에만 전송한다.

## 사용자 데이터 디렉토리 분리

설치 디렉토리 vs 사용자 데이터 디렉토리를 명확히 분리한다.

| 종류 | 위치 | 관리 주체 |
|------|------|----------|
| 실행 바이너리 | 패키지 매니저 표준 경로 (winget portable / npm bin / brew prefix) | 패키지 매니저 |
| 사용자 DB | `~/.codescan/db/codescan.db` | CodeScan (`AppPaths.cs`) |
| 로그 | `~/.codescan/logs/` | CodeScan |
| 사용자 설정 | `~/.codescan/config/` | CodeScan |

설치/제거/업그레이드 흐름은 사용자 데이터 디렉토리를 **건드리지 않는다**. 사용자가 명시적으로 `codescan reset` 같은 명령을 실행한 경우에만 데이터를 삭제한다.

## 보안 및 공급망

- v1: 코드 서명/notarization 미적용. README에 다음 안내를 포함한다.
  - Windows: SmartScreen 경고가 발생할 수 있으며 "추가 정보 → 실행"으로 진행한다.
  - macOS: 첫 실행 시 `xattr -d com.apple.quarantine $(which codescan)` 또는 시스템 설정 → 개인정보 보호 및 보안에서 허용한다.
- 모든 다운로드 경로(스크립트 인스톨러, npm postinstall)는 다운로드 직후 `checksums.txt`와 SHA256을 대조한다.
- SBOM(`sbom.cdx.json`)을 Release asset에 포함한다 — 공급망 감사 대비.
- v1.x 후속 평가:
  - Windows: EV 코드 서명 인증서 도입 비용 vs 사용자 신뢰 가치
  - macOS: Apple Developer ID + notarization 도입
- GitHub Release URL 외 다른 출처는 공식 채널이 아니다 — README에 명시.

## 텔레메트리

CodeScan은 어떠한 사용 데이터도 외부로 전송하지 않는다. opt-in 텔레메트리도 v1에는 포함하지 않는다. 향후 도입 검토 시 별도 RFC를 통해 결정한다.

## Windows: winget 전략

### 설치 경험

```powershell
winget install psmon.CodeScan
codescan --version
```

### 패키징 방향

- 패키징 형식: **portable zip** (winget manifest의 `InstallerType: portable`).
- 대상 asset: GitHub Release의 `codescan-win-x64.zip`.
- winget은 자체적으로 portable 바이너리를 `WinGet\Packages\` 하위에 추출하고 PATH에 등록한다.
- 설치 후 `codescan.exe`가 PATH에서 즉시 실행 가능해야 한다.

### Manifest 핵심 필드

```yaml
PackageIdentifier: psmon.CodeScan
PackageVersion: 0.1.0
InstallerType: portable
Commands:
  - codescan
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/psmon/CodeScan/releases/download/v{version}/codescan-win-x64.zip
    InstallerSha256: <sha256>
    NestedInstallerType: portable
    NestedInstallerFiles:
      - RelativeFilePath: codescan\codescan.exe
        PortableCommandAlias: codescan
```

### 검토 결과 및 결정

| 항목 | 결정 |
|------|------|
| 패키지 식별자 | `psmon.CodeScan` |
| 설치 방식 | portable zip (msi/exe installer는 v2 검토) |
| 코드 서명 | v1 미적용, README에 SmartScreen 안내 |
| 업그레이드 시 `~/.codescan/db` | 데이터 디렉토리 분리 원칙에 따라 자동 보존 |
| 런타임 데이터 위치 | `~/.codescan/` 고정 |

### 장점

- Windows 사용자에게 가장 자연스러운 설치 경로다.
- PowerShell 기반 테스트와 운영 문서화가 쉽다.
- 기존 Windows 배포 스크립트와 연결하기 쉽다.

### 리스크 및 대응

| 리스크 | 대응 |
|--------|------|
| manifest 제출/업데이트 절차 | CI/CD에서 자동 PR 생성 (`microsoft/winget-pkgs`) |
| unsigned binary SmartScreen 경고 | README/Release notes 명시, v1.x에서 코드 서명 평가 |
| PATH 처리 | winget portable이 자동 처리, 검증 명령으로 확인 |

## Linux: npm 전략

### 설치 경험

```bash
npm install -g @psmon/codescan-cli
codescan --version
```

> 반드시 스코프 붙은 `@psmon/codescan-cli`로 설치한다. 짧은 `codescan-cli`는 무관한 제3자가 npm에 선점한 상태이며 설치 즉시 죽는 깨진 패키지이므로 절대 사용하지 않는다.

### 패키징 방향

npm 패키지는 JavaScript CLI 자체가 아니라 플랫폼별 CodeScan 바이너리를 설치하는 얇은 래퍼다.

**v1 채택 방식: postinstall 다운로드**

1. `postinstall`에서 OS/arch를 판별한다.
2. GitHub Release에서 해당 `codescan-{rid}.tar.gz`를 다운로드한다.
3. `checksums.txt`를 같이 받아 SHA256 검증한다.
4. 패키지 내부 `vendor/`에 압축 해제한다.
5. `bin/codescan.js`가 `vendor/codescan/codescan`을 `execFileSync`로 실행한다.

**v2 검토: optional dependencies sub-package 방식**

플랫폼별 sub-package(`codescan-cli-linux-x64`, `codescan-cli-darwin-arm64` 등)를 publish하고 root 패키지의 `optionalDependencies`에 등록한다. postinstall 다운로드를 제거할 수 있어 기업망/오프라인 환경에 강해진다.

### 패키지 구조

```text
codescan-cli/
├── bin/codescan.js           실행 진입점 (자식 프로세스로 vendor 바이너리 호출)
├── scripts/install.js         postinstall: OS/arch 감지 → 다운로드 → 검증 → 압축 해제
├── scripts/uninstall.js       preuninstall: vendor/ 정리
├── vendor/                    (postinstall에서 생성, gitignore)
├── package.json
└── README.md
```

`package.json` 핵심:

```json
{
  "name": "@psmon/codescan-cli",
  "version": "0.1.0",
  "bin": { "codescan": "bin/codescan.js" },
  "scripts": {
    "postinstall": "node scripts/install.js",
    "preuninstall": "node scripts/uninstall.js"
  },
  "engines": { "node": ">=18" }
}
```

> 짧은 이름 `codescan-cli`는 2026-03경 무관한 npm 사용자(`o-town`)가 v1.0.0으로 선점했으므로 우리는 사용할 수 없다. 스코프 `@psmon`을 publish 시점에 자동 생성하여 점유한다. publish 명령은 `npm publish --access public`이어야 한다 (스코프 패키지 기본값은 private).

### 검토 결과 및 결정

| 항목 | 결정 |
|------|------|
| 패키지명 | `@psmon/codescan-cli` (스코프 패키지 — 짧은 `codescan-cli`는 제3자 squat으로 확보 불가) |
| 설치 방식 | postinstall 다운로드 (v1) → optional deps (v2) |
| 프록시/오프라인 대응 | `HTTPS_PROXY`/`HTTP_PROXY` 환경변수 존중, 실패 시 수동 설치 URL 출력 |
| glibc/musl | glibc만 지원 (musl/Alpine 사용자는 직접 빌드 안내) |
| 아키텍처 | linux-x64, linux-arm64 |
| 제거 시 정리 | preuninstall에서 vendor/ 삭제, `~/.codescan/`은 보존 |

### 장점

- Linux 사용자뿐 아니라 Node.js가 있는 CI 환경에서 접근성이 좋다.
- npm global bin으로 PATH 연결이 단순하다.
- macOS도 npm 경로로 보조 지원 가능하다.

### 리스크 및 대응

| 리스크 | 대응 |
|--------|------|
| CodeScan은 Node.js 도구가 아님 — 정체성 혼란 | README 첫 줄에 "npm은 설치 진입점일 뿐"임을 명시 |
| postinstall 네트워크 차단 | 실패 시 명확한 수동 설치 안내 + URL 출력 |
| libc 차이 | glibc 전용 명시, musl은 미지원 |
| arm64 누락 | v1부터 arm64 포함 |

## macOS: Homebrew 전략

### 설치 경험

```bash
brew tap psmon/codescan
brew install codescan
codescan --version
```

또는 한 줄:

```bash
brew install psmon/codescan/codescan
```

공식 Homebrew core 진입은 v2 이후 안정성/사용자 수 확보 후 검토한다.

### 패키징 방향

- tap 저장소: `psmon/homebrew-codescan`
- Formula는 GitHub Release의 macOS tarball을 다운로드한다.
- v1은 Apple Silicon(arm64)만 처리한다. Intel Mac은 v2에서 재평가.

### Formula 핵심

```ruby
class Codescan < Formula
  desc "CLI/TUI/GUI source-code scanner with FTS5 search and git blame"
  homepage "https://github.com/psmon/CodeScan"
  version "0.1.0"
  license "MIT"

  on_macos do
    on_arm do
      url "https://github.com/psmon/CodeScan/releases/download/v#{version}/codescan-osx-arm64.tar.gz"
      sha256 "<sha256>"
    end
    on_intel do
      odie "CodeScan v1 does not ship an Intel Mac binary. Build from source or use Rosetta with the arm64 build."
    end
  end

  def install
    bin.install "codescan/codescan"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/codescan --version")
  end
end
```

### 검토 결과 및 결정

| 항목 | 결정 |
|------|------|
| tap 저장소 | `psmon/homebrew-codescan` |
| Formula 위치 | `Formula/codescan.rb` |
| notarization | v1 미적용, README에 `xattr -d` 안내 |
| arch | arm64만 (Intel은 v2 재평가) |
| quarantine | 사용자 안내 + 향후 notarization으로 해결 |
| brew audit | tap에서는 강제 아님, core 진입 전 통과 목표 |

### 장점

- macOS 개발자에게 자연스러운 설치 경로다.
- 버전 업그레이드 경험이 좋다.
- tap으로 시작하면 공식 core보다 운영 부담이 낮다.

### 리스크 및 대응

| 리스크 | 대응 |
|--------|------|
| macOS AOT 빌드 환경 필요 | GitHub Actions macOS runner 사용 |
| Runner 비용/시간 | tag push 시에만 빌드, 평소엔 캐싱 |
| notarization 필요성 | v1 미적용, 사용자 안내 |

## 보조: 직접 스크립트 인스톨러

패키지 매니저를 사용하지 않는 환경(에어갭, 자동화 스크립트, 학습 목적)을 위해 직접 설치 스크립트를 제공한다.

### Windows

```powershell
iwr https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install-win.ps1 -OutFile install-win.ps1
.\install-win.ps1
```

원라이너(보안 권장 안 됨, 데모용):

```powershell
iwr https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install-win.ps1 -UseB | iex
```

### Linux/macOS

```bash
curl -fsSL https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install.sh -o install.sh
sh install.sh
```

원라이너(보안 권장 안 됨, 데모용):

```bash
curl -fsSL https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install.sh | sh
```

### 스크립트 인스톨러 동작 명세

1. OS와 CPU 아키텍처를 감지한다 (`uname -sm` / `$env:PROCESSOR_ARCHITECTURE`).
2. 지원되지 않는 조합이면 명확한 에러와 직접 빌드 안내를 출력한다.
3. `version.txt` 또는 인자로 받은 버전의 GitHub Release에서 해당 asset과 `checksums.txt`를 다운로드한다.
4. SHA256을 대조한다 — 불일치 시 중단.
5. 설치 대상:
   - Unix: `$HOME/.local/bin/codescan` (없으면 디렉토리 생성, PATH 안내)
   - Windows: `$HOME\.codescan\bin\codescan.exe` (PATH 등록 안내 또는 `setx PATH` 옵션)
6. 기존 설치가 있으면 버전 비교 후 교체.
7. 실패 시 수동 설치 URL과 명령을 출력한다.
8. 사용자 데이터 디렉토리(`~/.codescan/`)는 건드리지 않는다.

## CI/CD 워크플로우

GitHub Actions로 다음 파이프라인을 구성한다.

### Workflow: `release.yml` (tag push 트리거)

```yaml
on:
  push:
    tags: ['v*']
  workflow_dispatch:

jobs:
  build:
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
            archive: zip
          - os: ubuntu-22.04
            rid: linux-x64
            archive: tar.gz
          - os: ubuntu-22.04
            rid: linux-arm64
            archive: tar.gz
          - os: macos-13
            rid: osx-x64
            archive: tar.gz
          - os: macos-14
            rid: osx-arm64
            archive: tar.gz
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Publish AOT
        run: dotnet publish -c Release -r ${{ matrix.rid }} --self-contained
      - name: Package
        run: |
          # 압축 + SHA256 산출 → artifact 업로드
      - uses: actions/upload-artifact@v4

  sbom:
    runs-on: ubuntu-latest
    steps:
      - uses: CycloneDX/gh-dotnet-generate-sbom@v1
      - uses: actions/upload-artifact@v4

  release:
    needs: [build, sbom]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/download-artifact@v4
      - name: Generate checksums.txt
        run: sha256sum codescan-* > checksums.txt
      - uses: softprops/action-gh-release@v2
        with:
          files: |
            codescan-*
            checksums.txt
            sbom.cdx.json

  publish-winget:
    needs: release
    runs-on: windows-latest
    steps:
      - uses: vedantmgoyal9/winget-releaser@main
        with:
          identifier: psmon.CodeScan
          token: ${{ secrets.WINGET_TOKEN }}

  publish-homebrew:
    needs: release
    runs-on: ubuntu-latest
    steps:
      - name: Update tap formula
        # psmon/homebrew-codescan repo의 Formula/codescan.rb 업데이트 PR

  publish-npm:
    needs: release
    runs-on: ubuntu-latest
    steps:
      - uses: actions/setup-node@v4
      - name: Publish
        run: npm publish
        env:
          NODE_AUTH_TOKEN: ${{ secrets.NPM_TOKEN }}
```

### 단계별 자동화 적용 순서

v1 초기에는 `build`, `sbom`, `release`까지만 자동화하고 `publish-*` 잡은 수동(workflow_dispatch)으로 운영한다. v1.x 후속 릴리즈에서 자동 publish로 전환한다.

## 설치 후 검증 체크리스트

### 공통 명령 검증

```bash
codescan --version
codescan --help
codescan scan --help
codescan search --help
codescan graph --help
codescan query --help
codescan gui --help
codescan tui --help
```

### 실 데이터 동작 검증

```bash
# 스캔 한 사이클
codescan scan .

# 검색
codescan search "main" --type method

# 그래프 쿼리
codescan query "MATCH (m:Method) RETURN m LIMIT 5"
```

### GUI 검증

```bash
codescan gui start --port 8085
```

브라우저에서 `http://127.0.0.1:8085/` 접속 후 Keyword, Graph Search, Query, 2D/3D view를 확인한다.

### 데이터 디렉토리 검증

설치/제거 후 다음이 보존되는지 확인:

```text
~/.codescan/db/codescan.db
~/.codescan/logs/
~/.codescan/config/
```

### 제거/재설치 시 데이터 보존 확인

```bash
# 제거
winget uninstall psmon.CodeScan          # Windows
npm uninstall -g @psmon/codescan-cli     # Linux
brew uninstall codescan                  # macOS

# ~/.codescan/ 디렉토리가 그대로 남아있는지 확인
ls ~/.codescan/db/

# 재설치 후 기존 인덱스로 검색 가능한지 확인
codescan search "main"
```

## 업그레이드 및 롤백 정책

### 업그레이드

- 패키지 매니저 표준 흐름을 사용한다 (`winget upgrade`, `npm update -g`, `brew upgrade`).
- DB 스키마가 변경된 경우 첫 실행 시 자동 마이그레이션을 수행한다.
- 마이그레이션 전 `~/.codescan/db/codescan.db.backup-{timestamp}`로 자동 백업한다.
- 마이그레이션 실패 시 백업으로부터 자동 롤백하고 사용자에게 안내한다.

### 다운그레이드 / 롤백

- 패키지 매니저별 표준 흐름 (`winget install --version`, `npm install -g @psmon/codescan-cli@x.y.z`, `brew install codescan@x.y.z`).
- 신규 스키마 → 구버전 호환은 보장하지 않는다. 다운그레이드 시 백업 DB를 수동 복구한다.
- 후속 검토: `codescan db restore <backup-path>` 명령 도입.

## v2 검토 후보

v1 운영 결과에 따라 다음을 검토한다.

- **Windows 코드 서명** (EV 인증서)
- **macOS notarization** (Apple Developer ID)
- **musl/Alpine 지원** (별도 빌드 또는 `linux-musl-x64` asset)
- **Windows arm64** asset 추가
- **npm optional dependencies** sub-package 방식
- **Homebrew core** Formula 제출
- **MSI/EXE installer** (Windows 전통 설치 형식)
- **자동 publish** (winget/brew/npm 전부 자동)
- **opt-in 텔레메트리** RFC

## 결론

v1 채택 우선순위와 단계:

1. **GitHub Release 파이프라인** — asset 명명, checksum, SBOM 자동화
2. **직접 스크립트 인스톨러** — Windows PowerShell + Unix shell
3. **winget 매니페스트 제출** — `psmon.CodeScan`
4. **Homebrew tap 운영** — `psmon/homebrew-codescan`
5. **npm 래퍼 publish** — `@psmon/codescan-cli` (postinstall 다운로드 방식, `npm publish --access public`)

각 단계는 직전 단계의 GitHub Release 결과를 의존한다. 패키지 매니저 channel은 수동 publish로 시작하여 안정화 후 CI에 통합한다.
