---
date: 2026-06-17T06:33:00+09:00
agent: aot-compatibility-scout
type: review
mode: log-eval
trigger: "공식은 npm으로 인스톨함.. 하지만 동일한 오류발생함 (LLama.Native.NativeApi)"
---

# self-extract 단일 파일에서 LLamaSharp 네이티브 검색 경로 보강 (v0.11.1)

## 실행 요약

v0.11.0의 첫 수정(`WithSearchDirectory(AppContext.BaseDirectory)`)은 `deploy-win.ps1`
산출물(runtimes/ 가 exe 옆에 loose)에서만 동작했다. 그러나 **공식 배포 경로(npm)**는
릴리즈 워크플로우 빌드를 쓰고, 그건 `IncludeNativeLibrariesForSelfExtract=true` 라서
구성이 다르다 → 사용자에게 동일한 `NativeApi` 예외 재발.

릴리즈 플래그로 로컬 재현 + 분석한 결과:

- self-extract 빌드는 LLamaSharp 네이티브를 **exe 안에 임베드**하고, 런타임에
  `…/Temp/.net/codescan/<bundle-id>/` 로 추출한다. 이 추출 트리는
  `runtimes/<rid>/native/…` 구조를 **그대로 보존**한다.
- 그런데 `AppContext.BaseDirectory`는 추출 디렉터리가 아니라 **exe 폴더**를 가리킨다.
  거기엔 runtimes/ 가 없으므로 첫 수정만으로는 못 찾는다.
- .NET 호스트는 추출 디렉터리를 `NATIVE_DLL_SEARCH_DIRECTORIES` AppContext 항목에
  실어 둔다(기본 네이티브 로더가 쓰는 정식 목록). probe로 확인:
  `NDD = …/Temp/.net/probe/<id>/;` 이고 e_sqlite3도 그 경로에서 로드됨.

## 결과

- `Services/Llm/LlmHost.cs` — `NATIVE_DLL_SEARCH_DIRECTORIES`의 모든 항목 + `AppContext.BaseDirectory`
  를 중복 제거 후 LLamaSharp 검색 경로에 등록. 두 배포 방식 모두 커버.
- `version.txt` → 0.11.1.
- 검증(릴리즈 self-extract 빌드, foreign CWD `C:\Windows\Temp`):
  - `ask "..." --gpu 999` → exit 0, 응답 생성.
  - native 로그 검색 경로:
    `{ …/.net/codescan/vJ2zk8Rucew7/, …/codescan-relpub2/, ./ }`
    → `Successfully loaded '…/.net/codescan/vJ2zk8Rucew7/runtimes/win-x64/native/vulkan/llama.dll'`.

## 평가

| 축 | 평가 |
|----|------|
| 코드 안전성 | A — try/catch 폴백 유지. AppContext 조회만, 런타임 안전. |
| 아키텍처 정합성 | A — 코드 한 곳(LlmHost)에서 양쪽 배포 레이아웃을 모두 해결, 패키징 변경 불필요. |
| 테스트 가능성 | B — 릴리즈 self-extract 산출물로 수동 e2e 검증. CI는 모델 5GB 의존이라 자동화 곤란. |

## 다음 단계 제안

- 릴리즈 워크플로우 스모크가 SQLite만 검증하고 LLM 네이티브 로드는 검증 못 한다.
  모델 없이 백엔드 init만 건드리는 경량 self-check(예: `ask` 네이티브 프리로드 종료코드)
  추가를 검토하면 이 부류 회귀를 CI에서 잡을 수 있다.
- 리눅스/macOS self-extract에서도 `NATIVE_DLL_SEARCH_DIRECTORIES`가 동일하게 채워지는지
  한 번 확인(경로 구분자는 `Path.PathSeparator`로 이미 OS 무관 처리).
