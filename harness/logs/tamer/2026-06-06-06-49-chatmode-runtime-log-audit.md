---
date: 2026-06-06T06:49:00+09:00
agent: tamer
type: review
mode: log-eval
trigger: "Chat모드 최근 작동 개발로그를 파악.."
---

# ChatMode 런타임 디버그 로그 감사 (2026-05-29 ~ 2026-06-06)

## 실행 요약

대상은 하네스 평가로그가 아닌 **CodeScan TUI ChatMode가 실제 작동하면서 남긴 런타임 디버그 로그**.
로그 경로: `~/.codescan/logs/` (AppPaths가 관리하는 중앙 저장소).

집계:
- 채팅 세션 로그: **23건** (`chat-YYYYMMDD_HHMMSS.log`)
- LLM 네이티브 로그: `llama-native.log` (590 KB, 7,382줄)
- TUI 부트스트랩 로그: `tui-startup.log` (가장 최근 2건의 콜드 스타트 트레이스)
- 기간: 2026-05-29 14:10 ~ 2026-06-06 06:43 (9일)

## 모델 사용 분포

| 모델 | 세션 수 | 사용일 |
|------|---------|--------|
| `gemma-4-E4B-it-UD-Q4_K_XL.gguf` | 20 | 05-29, 05-30, 06-01, 06-06 |
| `NVIDIA-Nemotron3-Nano-4B-Q4_K_M.gguf` | 3 | 06-03 (모두 같은 날) |

Nemotron 3 Nano는 커밋 `38d3ead` (2026-06-03) 으로 추가되었고 로그에 정확히 그날 진입 — 도입 직후 시험 사용 흔적이며 이후로는 다시 Gemma 4로 회귀. 모든 세션의 `project=(none)`. 즉 ChatMode 자체는 특정 프로젝트에 바인딩되지 않고, 채팅 도중 `project_tree` / `db_search`로 외부(`C:\code\psmon\AgentZeroLite`)를 조회하는 형태.

## 네이티브 런타임 환경 (llama-native.log)

- 백엔드: **Vulkan** (PreferCuda=True지만 Vulkan0 적재 성공 → Vulkan 우선)
- 디바이스: AMD Radeon(TM) 8060S Graphics, 64,033 MiB free
- CPU 폴백: AVX512 ggml-cpu.dll 적재됨 (AllowFallback=True)
- 모델 메타: Gemma 4 E4B-It, context_length=131072, block_count=42, embedding_length=2560
- 로그 형식은 LLamaSharp의 NativeLibraryConfig + llama_model_loader 덤프 그대로 — 커밋 `c19eb37 feat(chat): Vulkan GPU + model-aware ctx + native log capture` 에서 캡처 활성화된 결과물.

## 세션 흐름 패턴 (모든 로그 공통)

```
[session] start  model=… project=…
[user]  …                    ← 입력
[raw]   {"tool": "…", …}     ← 모델 원본 출력 (JSON tool call)
[tool]  …({…})               ← 파싱된 호출
[result] {"ok":true, …}      ← Toolbelt 응답
…(반복: project_tree → db_search → read_file)…
[raw]   {"tool":"done", …}
[done]  …                    ← 최종 응답
```

이 구조는 `Services/Llm/Tools/AgentChatLoop.cs` + `ChatSessionLogger.cs` 의 산출물이며, 디버그/포렌식 측면에서 raw/parsed/result 3단계가 분리되어 있어 모델 출력의 토큰 단위 오류와 도구 계약 위반을 구분해서 추적할 수 있음.

## 시계열로 본 ChatMode 진화 — 로그가 보여주는 것

### 1. 2026-05-29 첫 세션 (`chat-20260529_141001.log`) — 경로 환각 실패
```
[user]   AgentZeroLite 분석해 볼래?
[tool]   db_search({query:"AgentZeroLite", type:"file", limit:5})
[result] (5건, project-relative path만 반환, abs_path 없음)
[user]   응 해당파일 확인해 해 설명해죠
[tool]   read_file({path:"harness\\knowledge\\_shared\\agentzerolite-architecture.md", …})
[result] {ok:false, error:"file not found: …"}
[done]   죄송합니다. 요청하신 파일 … 찾을 수 없습니다.
```
모델이 `db_search`로 받은 project-relative 경로를 CodeScan 작업 디렉토리 기준으로 해석 → 외부 프로젝트의 파일을 못 찾음. 같은 날 커밋된 `f5b8e46 feat(chat): in-TUI model download + abs_path lookup for db_search` 가 직접 대응한 결손.

### 2. 2026-06-03 Nemotron 도입 직후 (`chat-20260603_131340.log`) — 도구 인자 누락 + 응답 반복
```
[raw]   {"tool":"project_tree", "args":{"max_depth":3}}   ← project_id 없음
[result] (project_id=1 자동 선택해 응답)
…
[user]  리드미 읽어서 요약
[done]  AgentZeroLite.ps1은 … (직전 답을 거의 그대로 재출력)
```
Nemotron이 Gemma 대비 (1) tool args에서 `project_id` 같은 선택 인자를 일관되게 누락하고 (2) "다른 주제 요청"에도 컨텍스트를 이어 동일 응답을 반복하는 패턴이 보임. Gemma 4 → Nemotron 전환은 같은 날만 시험되고 다시 Gemma로 돌아간 이유가 로그상에서 관찰됨.

### 3. 2026-06-06 오늘 세션 (`chat-20260606_064308.log`) — 안정 동작
```
[user]   AgentZero 코드분석
[tool]   project_tree({project_id:"most-recent"})    ← project_id 명시
[tool]   db_search({query:"AgentZeroWpf", type:"file", limit:5})
[result] (abs_path 포함된 결과 ✓)
[tool]   read_file({path:"C:\\code\\psmon\\AgentZeroLite\\…\\App.xaml.cs", start:1, end:50})
[result] (성공)
[done]   AgentZeroWpf.xaml.cs 파일의 앞부분을 확인했습니다…
```
05-29 시점에 실패하던 동일 시나리오가 정상 종료. (a) `abs_path` 필드 도입, (b) `project_tree` 호출 시 `project_id:"most-recent"` 가이드, (c) Gemma 네이티브 chat-template 마커 적용(`07527fb`) 의 누적 효과.

## 반복 이슈 패턴 (전체 23개 세션 교차 분석)

| 패턴 | 발생 세션 | 비고 |
|------|----------|------|
| `0 hits` 후 hint 재시도 권유 | 5건 (05-29 1건, 05-30 4건) | `ac979aa feat(chat): … 0-hit recovery` 으로 hint 메시지 합류. 06-06엔 미발생 |
| `file not found` | 06-03 4건, 그 외 산발 | Nemotron 세션에 집중 — 모델 차이의 영향 |
| `project=(none)` | **23건 전부** | ChatMode가 명시적 프로젝트 바인딩 UX를 갖지 않음 — UX 갭 |
| 동일 답변 반복 | 06-03 Nemotron 1건 | 모델 특성(템플릿 마커) 의심 |
| Gemma 4 chat-template 정렬 후 안정 | 06-06 | `07527fb`, `ad37e24`, `445fa79` 누적 효과 가시화 |

## 결과 — 핵심 발견

1. **로그 시스템 자체는 production-ready**: raw/parsed/result 3단 분리, 모델/프로젝트/시간 메타 포함, ASCII만 UTF-8로 안정 출력. 디버깅 자산으로 충분.
2. **모델 전환 결정의 근거가 로그에 남는다**: Nemotron 3-Nano 단발 시험 → Gemma 4 회귀가 같은 사용자 시나리오의 성공/실패로 명확히 비교 가능.
3. **세션 시작 시 프로젝트 미지정이 일관된 사용자 의도와 어긋남**: 100% 세션이 `project=(none)`이면서 100% 세션이 외부 프로젝트 분석을 시도. ChatMode 시작 시 "최근 프로젝트 자동 선택" 또는 "프로젝트 picker" 도입이 다음 개선 후보.
4. **하네스 로그와의 정합 격차**: 2026-05-19 이후 하네스 `logs/`에는 어떤 활동도 기록되지 않은 반면, `~/.codescan/logs/`에는 9일간 23건의 사용자-주도 채팅 활동 누적. 정원지기가 평가해야 할 활동의 **상당 부분이 정원 바깥에서** 일어나고 있음.

## 3축 평가

| 축 | 평가 | 근거 |
|----|------|------|
| 워크플로우 개선도 | **B** | 05-29 실패 → 06-06 성공의 회귀 추적이 가능. 다만 정원(harness) 안에 ChatMode 전용 워크플로우/지식 부재 |
| Claude 스킬 활용도 | **2/5** | 런타임 로그는 풍부하지만 어떤 harness 스킬도 이 로그를 소스로 활용하지 않음 (graph-curator/test-sentinel 모두 정적 코드 대상) |
| 하네스 성숙도 | **L2** | knowledge/agents/engine 3-layer가 갖춰져 있으나 ChatMode/LLM 도메인이 빠져 있음. `Services/Llm/*`은 어떤 에이전트 책임 영역에도 매핑되지 않음 |

## 다음 단계 제안

- **chat-mode-observer 에이전트 신설 검토**: `~/.codescan/logs/chat-*.log` 를 입력으로 받아 모델 전환 효과, 도구 호출 실패율, 환각 패턴을 주기 리포팅. 정적 코드 검사 에이전트들이 다루지 못하는 사용자 행동/모델 행동 도메인.
- **llm-host-keeper 또는 model-catalog-keeper 검토**: `Services/Llm/{LlmHost,ModelLocator,ChatModelCatalog,GpuEnumerator,CtxRecommender}.cs` + `Tools/ChatTemplate*.cs` 가 어느 기존 에이전트(regex-safety-guard, aot-compatibility-scout 등) 책임 외곽에 있음. 새 카테고리.
- **knowledge/ 보강**: `knowledge/chat-mode/` 또는 `knowledge/llm-runtime/` 에 (a) AgentChatLoop의 tool-call 계약, (b) Gemma 4 / Nemotron chat-template 차이, (c) `project=(none)` UX 갭을 문서화. 위 신설 에이전트의 판단 기준이 됨.
- **engine/ 추가 검토**: `chat-session-postmortem` 워크플로우 — 세션 종료 시 로그 자동 분석 → 트렌드 보고. 단, 자동 실행 트리거는 사용자가 명시 요청한 경우로 제한.

> 위 제안은 모두 **사용자 승인 전 제안 단계**. 적용은 별도 트리거(`새 에이전트 추가해`, `하네스를 업데이트해`)로 진행.
