---
date: 2026-08-16T10:50:18
agent: observation
type: review
mode: observe-only
trigger: "다음 위치도 최신 버전 codescan해죠... C:\\code\\psmon\\AgentZeroLite 스캔한후 정보를 바탕으로 이 소스코드와 문서관의 관계파악"
target: C:\code\psmon\AgentZeroLite
scanner: codescan v0.13.4
constraint: 관측 전용 — 대상 프로젝트에 개입 금지. 개선은 대상이 스캔 사실을 모른다고 가정하고 별도/독립적으로 수행.
---

# AgentZeroLite — 코드↔문서 관계 관측 (개입 없음)

> **관측 전용 기록.** 이 로그는 CodeScan 하네스 안에만 존재한다.
> AgentZeroLite 소스는 어떤 파일도 수정하지 않았다. 개선 활동은 대상
> 프로젝트가 이 스캔을 모른다는 가정 하에 다른 곳에서 독립적으로 이뤄진다.

## 스캔 스냅샷 (project #2)

| 항목 | 값 |
|---|---|
| 경로 | `C:\code\psmon\AgentZeroLite` |
| 파일 | 943 (`.cs` 330, `.md` 260, `.json` 116, `.xaml` 26, `.js` 37, `.ps1` 16, `.pen` 10) |
| 메서드 | 4,396 |
| 디렉토리 / 크기 | 189 / 112.8 MB |
| 스캔 시각 | 2026-08-16 10:50:18 |
| 프로젝트 구성 | `AgentZeroWpf`(메인) · `AgentZeroAvalonia` · `ZeroCommon`(코어) · `AgentTest` · `LlmProbe` · `Plugins/*` |

## 코드↔문서 연결 관측치

```
.md 260  ┬─ mentions 연결됨      25 (10%)
         └─ orphan             235 (90%)
                 ├─ harness/    210  ← 카카시 하네스 로그/지식 (프로세스 문서)
                 └─ 제품 문서     25  ← 실제 doc-code 단절 대상
mentions 엣지(heading→class) : 39
frontmatter 앵커(governs/anchor) : 0 / 260
declared anchors : 0 · intentional : 0 · neglected : 235
```

## 구조적 관측 3가지

1. **`mentions` 자동링커는 heading 텍스트가 클래스명과 정확히 일치할 때만 생성.**
   - 연결된 문서는 클래스명을 heading에 노출(예: `README-KR`의 `3.1 StageActor`,
     `3.2 AgentBotActor` … 액터 아키텍처 섹션) → 자동 연결.
   - 끊긴 제품 문서는 클래스명이 본문 산문에만 존재 → 연결 실패. 단 doc-orphan은
     이들 본문에서 정확한 후보 클래스를 이미 추출함(`LlmService`,
     `LlamaSharpLocalLlm`, `StageActor`, `ApprovalParser`, `ElementTreeScanner` …).

2. **앵커 정책 미사용(0/260).** `governs:`/`anchor:` frontmatter가 전무 →
   하네스 로그(정상 orphan)와 방치된 제품 문서(실제 orphan)를 도구가 구분 못 하고
   전부 NEGLECTED로 집계. "90% orphan"은 이 미분류 때문에 부풀려진 수치.

3. **아이러니: 하네스 로그가 제품 문서보다 코드 연결이 좋음.** 39개 mentions 중
   다수가 harness 로그 헤딩("Pre-commit review — WorkspaceTerminalToolHost",
   "M1. AgentBotActor leaks…")에서 발생. 메인 README(57KB)·CLAUDE.md·LLM 튜토리얼
   같은 1급 문서는 오히려 단절.

## 실제 문제 대상 — 비-harness orphan 25개 (관측 목록)

```
CLAUDE.md
Docs\OsControl.md                              → ElementTreeScanner, NativeMethods
Docs\agent-herdr\README.md                     (순수 산문)
Docs\agent-orca\01-orca-feature-catalog.md     → ApprovalParser, StageActor
Docs\agent-orca\02-phase-plan.md               → AgentBotActor, CoordinatorActor, ApprovalParser …
Docs\agent-orca\README.md                       → ApprovalParser, IAgentLoop, IAgentToolbelt
Docs\agent-origin\01-stack-comparison.md        → CliHandler, LlmGateway, MainWindow …
Docs\agent-origin\README.md                     → AgentBotWindow, AgentLoopActor, AppLogger …
Docs\gemma4-gpu-load-failures.md                → AppLogger, LlmService, VulkanDeviceEnumerator …
Docs\gemma4-performance-benchmarks.md           → LlamaSharpLocalChatSession, LlmProbeTests
Docs\llm\en\gemma4-ondevice-tutorial.md         → ILocalLlm, LlamaSharpLocalLlm, LlmService …
Docs\llm\index.md                               (순수 산문)
Docs\llm\ko\gemma4-ondevice-tutorial.md         → ILocalLlm, LlamaSharpLocalLlm, LlmService …
Docs\resaerch-geema4.md                         → LlmService, LlmModelDownloader, LlmModelLocator …
NOTE.md
Project\AgentZeroWpf\Assets\...\agent-zero-lite\SKILL.md
Project\AgentZeroWpf\Wasm\README.md
Project\Plugins\README.md
README-EX.en.md · README-EX.md · README-KR.md · README.md
Test\e2e\README.md
codex\prompts\agentzero-cli.md
traffic-history\SUMMARY.md
```

## 오탐 신호 (관측)

doc-orphan 후보에 `Node`, `INPUT`, `Brushes`가 등장 → 프로젝트에 동명 클래스 실재.
프레임워크 타입(`System.Windows.Media.Brushes`)과 네이밍 충돌. (관측만; 판단 보류)

## 개선 방향 (여기서는 기록만 — 대상 프로젝트에서 독립 수행 대상)

- P0: `harness/**/*.md` 210개 → `anchor: none` (노이즈 제거, 실제 25개만 부각)
- P1: 제품 문서 25개 → doc-orphan이 출력한 `governs:` 앵커 부여
- P2: 핵심 클래스 소개 문단은 heading에 클래스명 노출 (앵커 없이도 자동 mentions)
- P3: `Node`/`INPUT`/`Brushes` 등 프레임워크 충돌 네이밍 리네임 검토

## 재현 명령 (읽기 전용)

```
codescan doc-orphan --project 2
codescan doc-orphan --project 2 --all
codescan query "MATCH (h:heading)-[r:mentions]->(c:class) LIMIT 200" --project 2
```

## 평가 (3축)

- 코드 안전성: N/A — 대상 소스 무수정, 읽기 전용 스캔만.
- 아키텍처 정합성: 관측 결과 doc-code 브리지(`mentions`)가 heading 의존적이라
  산문형 문서를 놓침. 도구 한계이자 문서 관례 신호로 동시 관측됨.
- 테스트 가능성: N/A (관측).

## 개입 경계 확인

- [x] AgentZeroLite 소스/문서 무수정
- [x] 기록은 CodeScan 하네스 로그에만
- [x] 스캔은 읽기 전용 (인덱싱은 ~/.codescan DB에만 반영, 대상 워킹트리 불변)
