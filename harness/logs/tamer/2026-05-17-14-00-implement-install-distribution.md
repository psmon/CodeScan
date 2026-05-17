---
date: 2026-05-17T14:00:00+09:00
agent: tamer
type: creation
mode: log-eval
trigger: "Docs/install-distribution-strategy.md 진행"
---

# install-distribution-strategy v1 구현 1차

## 실행 요약

`Docs/install-distribution-strategy.md` v1 확정안에 따라, 5가지 산출물 군을 1차 스캐폴드로 생성했다.

1. **CI 릴리즈 워크플로우** — `.github/workflows/release.yml`
   - matrix: win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64
   - publish: self-contained single-file (AOT는 deploy 스크립트와 동일하게 v1 비활성, v2 후보)
   - SBOM: CycloneDX (`sbom.cdx.json`)
   - 산출물 + `checksums.txt` + SBOM을 GitHub Release에 업로드
   - `publish-winget` / `publish-homebrew` / `publish-npm` 잡은 `if: false`로 게이트 (v1 수동 운영)

2. **직접 설치 스크립트**
   - `Script/install-win.ps1` — Release asset 다운로드, SHA256 검증, `~/.codescan/bin` 설치, PowerShell profile에 PATH 등록
   - `Script/install.sh` — POSIX shell, OS/arch 자동 감지, glibc 외 환경 경고, `~/.local/bin` 설치, PATH 가이드 출력
   - 두 스크립트 모두 `~/.codescan/` 사용자 데이터 디렉토리는 절대 건드리지 않음

3. **winget 매니페스트 스캐폴드**
   - `packaging/winget/manifests/p/psmon/CodeScan/0.0.0/` 에 3종 (installer/locale/version)
   - portable type, `psmon.CodeScan` 식별자
   - 버전·SHA256 토큰화 (`{VERSION}`, `{SHA256}`) — CI/수동 제출 시 치환

4. **Homebrew tap formula**
   - `packaging/homebrew/codescan.rb` — arm64/x64 분기, MIT, caveats에 notarization 미적용 안내
   - `packaging/homebrew/update-formula.sh` — checksums.txt에서 SHA256 추출 후 formula 치환

5. **npm 래퍼 패키지** (`codescan-cli`)
   - `package.json` — `bin: codescan`, `postinstall`/`preuninstall` 훅, Win 제외 (winget 권장)
   - `bin/codescan.js` — vendor 바이너리로 args 포워딩
   - `scripts/install.js` — OS/arch 감지 → Release 다운로드 → SHA256 검증 → vendor/ 추출
   - `scripts/uninstall.js` — vendor/ 정리 (사용자 데이터는 보존)

6. **packaging 통합 README** — `packaging/README.md` (각 채널 흐름과 시크릿 안내)

## 결과

### 신규 파일

| 경로 | 종류 |
|------|------|
| `.github/workflows/release.yml` | CI |
| `Script/install-win.ps1` | 인스톨러 |
| `Script/install.sh` | 인스톨러 |
| `packaging/README.md` | 문서 |
| `packaging/winget/README.md` | 문서 |
| `packaging/winget/manifests/p/psmon/CodeScan/0.0.0/*.yaml` (3개) | manifest |
| `packaging/homebrew/README.md` | 문서 |
| `packaging/homebrew/codescan.rb` | formula |
| `packaging/homebrew/update-formula.sh` | 빌드 도우미 |
| `packaging/npm/codescan-cli/README.md` | 문서 |
| `packaging/npm/codescan-cli/package.json` | npm |
| `packaging/npm/codescan-cli/bin/codescan.js` | npm |
| `packaging/npm/codescan-cli/scripts/install.js` | npm |
| `packaging/npm/codescan-cli/scripts/uninstall.js` | npm |

총 14개 파일. 사용자 데이터 디렉토리(`~/.codescan/`)와 기존 `deploy-win.ps1` / `deploy-linux.sh`는 변경 없음.

### 검증되지 않은 부분 (실측 미실시)

- `release.yml`은 실제 tag push로 실행되어야 검증 가능 (드라이런 안 함).
- `install-win.ps1` / `install.sh`는 첫 GitHub Release가 게시된 뒤에야 end-to-end 검증 가능.
- npm postinstall의 GitHub API/다운로드 경로는 첫 실제 release 후 검증 필요.
- AOT 활성 빌드는 v1에서 의도적으로 비활성. v2에서 Terminal.Gui/SQLite AOT 호환 평가 별도 수행 필요.

## 평가

정원지기 3축 평가:

| 축 | 결과 | 비고 |
|----|------|------|
| 워크플로우 개선도 | A- | 문서만 있던 배포 전략이 실제 CI/스크립트/매니페스트 스캐폴드로 구체화. 다만 실측 미실시. |
| Claude 스킬 활용도 | 3/5 | 정원 전문가(regex/aot/test-sentinel) 호출 없이 직접 구현. 다음 단계에서 aot-compatibility-scout로 AOT 재활성 평가 권장. |
| 하네스 성숙도 | L2 | knowledge 2개, agents 4명, engine 1개. engine에 `release-readiness` 같은 워크플로우 추가 시 L3. |

## 다음 단계 제안

- **실측 1**: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishAot=false -p:PublishSingleFile=true` 로컬 실행 — 산출물 크기와 동작 확인
- **실측 2**: 첫 pre-release 태그(예: `v0.4.0-rc1`)를 푸시하여 release.yml 실제 동작 검증
- **실측 3**: 게시된 release로 `install-win.ps1` / `install.sh` E2E 검증
- **에이전트 활용**: `aot-compatibility-scout`로 csproj `PublishAot=true` 활성 시 IL/AOT 경고 베이스라인 수집 → v2 AOT 마이그레이션 준비
- **engine 추가 후보**: `release-readiness` — 시험관 + AOT 정찰병 + 정규식 수문장 3명이 릴리즈 직전 사전 점검
