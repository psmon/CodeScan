---
date: 2026-06-17T06:11:00+09:00
agent: aot-compatibility-scout
type: review
mode: log-eval
trigger: "비대화형 ask cli 기능을 하나 추가해줘 (+ 배포모드 LLama.Native.NativeApi 예외)"
---

# 단일 파일 배포 네이티브 로드 폭발 수정 + 비대화형 `ask` CLI 추가

## 실행 요약

두 가지를 처리했다.

1. **배포 모드 런타임 폭발 수정** — 디버그에서는 Chat AI가 되는데 `deploy-win.ps1`로
   설치한 단일 파일 바이너리에서 `LLama.Native.NativeApi threw an exception`이 발생.
   `~/.codescan/logs/llama-native.log` 분석으로 근본 원인 확정:
   - `PublishSingleFile=true` 빌드는 번들된 어셈블리의 `Assembly.Location`이 **빈 문자열**.
   - LLamaSharp의 네이티브 자동 탐색이 exe 폴더를 못 잡고 **CWD 기준 `./runtimes`** 로 붕괴.
   - codescan은 PATH 설치되어 임의 프로젝트 폴더(`C:\code\psmon\AgentZeroLite` 등)에서
     실행되므로 그곳엔 `runtimes/`가 없어 **모든 백엔드 로드 실패** → NativeApi throw.
   - 수정: `LlmHost.ConfigureNativeLogOnce()`에서 첫 네이티브 호출 전에
     `NativeLibraryConfig.All.WithSearchDirectory(AppContext.BaseDirectory)` 호출.
     `AppContext.BaseDirectory`는 단일 파일에서도 exe 폴더를 정확히 가리킨다.

2. **비대화형 `ask` CLI 추가** — TUI Chat과 동일한 `AgentChatLoop`를 콘솔에서 구동.
   - `Commands/AskCommand.cs` 신규, `Program.cs`에 `ask` 라우팅/도움말 등록.
   - 옵션: `--model`(경로 또는 카탈로그 id), `--gpu`(Vulkan 오프로드 레이어),
     `--project`, `--ctx`, `--max-tokens`, `--quiet`.
   - `--list-models`로 다운로드/지원 모델 목록(id·경로·상태·auto 표시)을 먼저 조회 후
     그 값을 `--model`에 넘기는 흐름.
   - 답변은 stdout 스트리밍, 모델/툴/진행 메시지는 stderr → `-q > out.txt`로 답변만 캡처.

## 결과

- `Services/Llm/LlmHost.cs` — exe 디렉터리 검색 경로 고정.
- `Commands/AskCommand.cs` — 신규 비대화형 ask 명령(+`--list-models`).
- `Program.cs` — `ask` 라우팅, 메인/서브 도움말 등록.
- 배포 검증(단일 파일, foreign CWD `C:\Windows\Temp`):
  - `ask --list-models` → 카탈로그 3종 중 2종 downloaded 정상 표기.
  - `ask "..." --gpu 999` (Gemma) → 네이티브 로드 성공, 응답 생성, exit 0.
  - `ask "..." --model nvidia-nemotron-... --gpu 999` → 모델 선택/템플릿 자동 적용, exit 0.
  - native 로그: `SearchDirectories: { C:\Users\psmon\.codescan\bin\, ./ }` 로
    바뀌고 `Successfully loaded 'C:\Users\...\bin\runtimes\...\llama.dll'` 확인.

## 평가

| 축 | 평가 |
|----|------|
| 코드 안전성 | A — try/catch로 search-dir 설정 실패 시 기존 탐색으로 폴백. AOT 무관(런타임 설정). |
| 아키텍처 정합성 | A — TUI ChatView와 동일한 LlmHost/AgentChatLoop 재사용, 중복 구현 없음. |
| 테스트 가능성 | B — 배포 바이너리로 수동 e2e 검증 완료. 자동 테스트는 단일 파일 환경 의존이라 미작성. |

## 다음 단계 제안

- `deploy-linux.sh`로도 동일 수정이 동작하는지 리눅스 단일 파일 배포에서 한 번 확인.
- `ask`의 비대화형 경로를 `CODESCAN_LLM_SMOKE` 게이트 아래 스모크 테스트로 추가 검토
  (단, 단일 파일 빈 `Assembly.Location` 회귀는 publish 산출물에서만 재현됨에 유의).
