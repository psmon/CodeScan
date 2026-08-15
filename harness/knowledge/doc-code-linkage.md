---
name: doc-code-linkage
type: knowledge
description: 정책·지식 문서가 코드와 연결(anchor)돼야 최신화가 유지된다. 고아(orphan) 문서의 분류·전략과, 이를 지원하기 위해 CodeScan이 강화해야 할 부분.
anchor: none  # 이 문서는 방법론(정책)이라 특정 코드에 앵커되지 않는다(Type C). 아래 스키마 dogfooding.
governs: []
---

# Doc↔Code Linkage & Orphan Docs

> 문제의식: harness 정책/지식 문서가 코드 변경을 따라가지 못하고 낡는다.
> 왜 그런지 조사하고(코드스캔 무관, 수동), 고아 문서를 다루는 전략과
> 이를 지원하기 위해 CodeScan이 강화할 부분을 정리한다.
> 선행: [[graph-reconciliation]](코드↔문서 `mentions` 브리지), 실험 2(doc-stale 탐지).

## 1. 조사 — 왜 harness 정책 문서는 최신화가 안 되나

수동 조사(2026-08-15, v0.12.0 시점) 결과 네 가지 구조적 원인:

1. **이벤트 구동만 있고 코드→문서 역트리거가 없다.**
   harness 문서는 해당 agent/engine이 **실행될 때만** 갱신된다. 코드가 바뀌어도
   "이 정책 문서가 영향받았다"는 신호가 없다. 근거 — knowledge 문서 git 최종수정:
   대부분 **2026-05-17/18**에서 멈춤(`aot-rules` 05-17, `regex-patterns` 05-17,
   `language-analyzer-patterns`/`semantic-analyzer-docker` 05-18)인데 코드는 v0.7→v0.12.0.
   유일하게 이번 세션 산출물 `graph-reconciliation.md`만 08-15.

2. **정책 문서는 "규칙"으로 코드를 지배하지 클래스명을 쓰지 않는다 → 구조적 고아.**
   `aot-rules`("모든 정규식은 `[GeneratedRegex]`"), `regex-patterns`,
   `web-gui-design-craft`는 코드를 규율하지만 **특정 클래스/파일명을 거의 안 쓴다.**
   그래서 코드↔문서 `mentions`(heading→class) 브리지가 **이들을 연결하지 못한다** →
   코드-연결 기반 staleness 검사에 **보이지 않는다.**

3. **frontmatter가 불일치.** 일부 문서만 `--- name/type ---` frontmatter가 있고
   (`aot-rules`,`regex-patterns`,`actor-model-cross-toolkit`), 나머지는 없다
   (`language-analyzer-patterns`,`graph-curation-guide`,`web-gui-design-craft`).
   "이 문서가 무엇을 지배하는가"를 선언할 **표준 위치가 없다.**

4. **실제 stale 사례.** `semantic-analyzer-docker.md`는 여전히 "도커 우선 전략"으로
   서술하지만, 이번 세션에 전략이 **역전**됐다(무빌드 산출물 하베스트 우선, 도커는
   최후수단 — [[../.claude/skills/testsample-build/SKILL.md]] 참고). 문서가 코드/전략
   변화를 반영 못 함.

## 2. 고아(orphan) 문서 분류

코드 연결 관점에서 harness 지식문서는 3종으로 나뉜다:

| Type | 정의 | 해당 문서 | staleness 검사 |
|------|------|-----------|----------------|
| **A. 코드 앵커** | 실재하는 클래스/파일을 명시 | `language-analyzer-patterns`(9개 분석기+레지스트리), `semantic-analyzer-docker`(다수 클래스), `graph-curation-guide`(SqliteStore) | **가능** — 앵커가 깨지거나 변하면 stale |
| **B. 정책·규칙(규칙-고아)** | 규칙으로 코드를 지배하나 클래스명 없음 | `aot-rules`, `regex-patterns` | **불가(현재)** — 앵커를 명시해야 검사 가능 |
| **C. 순수 방법론(설계-고아)** | 설계 원칙, 특정 코드 무관 | `web-gui-design-craft`(단 `Home/harness-view/` 지배), `actor-model-cross-toolkit`, 이 문서 | **불필요** — 의도된 고아, 명시적으로 표시 |

핵심 구분: **"규칙-고아(B)"는 방치된 고아**(앵커를 붙여야 함)이고,
**"설계-고아(C)"는 정당한 고아**(앵커 없음을 선언하면 됨). 둘을 구별 못 하면
모든 고아가 똑같이 방치된다.

## 3. 전략 — 고아 문서 처리 & 최신화

1. **frontmatter 앵커 표준화.** 모든 knowledge/agents 문서에 다음을 강제:
   ```yaml
   ---
   name: <slug>
   type: knowledge | agent | engine
   governs: ["Services/Analyzers/**", "SqliteStore", "[GeneratedRegex]"]  # 파일 glob / 클래스 / 패턴
   anchor: auto | none   # none = 의도된 설계-고아(C), 검사 면제
   ---
   ```
   - Type B를 `governs:`로 **앵커화**해 Type A로 승격(예: `aot-rules` → `Services/Analyzers/**`, `**/*.cs` + `[GeneratedRegex]` 패턴; `regex-patterns` → `Services/Analyzers/Languages/**`).
   - Type C는 `anchor: none`으로 **정당한 고아** 선언 → 고아 경고에서 제외.

2. **코드→문서 역조정 트리거.** governed 경로의 코드가 (git 기준) 변경되면
   그 문서를 "재검토 필요"로 플래그. 지금까지 없던 역방향 신호.

3. **문서-리뷰 케이던스.** 릴리즈 시 또는 governed 영역을 건드릴 때 해당 문서를
   재대조하는 engine/의례. 이벤트 구동을 코드-변경 구동으로 보강.

4. **정본 단일화.** 방치되기 쉬운 이중 문서는 정본으로 리다이렉트(예: AGENT.md →
   CLAUDE.md). 정책 문서도 "지식은 harness/knowledge, 코드 사실은 CLAUDE.md" 경계 유지.

## 4. CodeScan 강화 로드맵 (이 조사에서 도출)

이 문제를 **CodeScan이 그래프로 검출**하게 하려면 강화할 부분:

1. **frontmatter 앵커 엣지.** `doc-meta` 노드는 이미 인덱싱되나, `governs:` 필드를
   파싱해 `doc -[governs]-> file|class` 엣지를 생성(heading `mentions`를 넘어). →
   **정책 문서도 1급으로 연결 가능**해짐. (규칙-고아 B 해소)

2. **`doc-orphan` 검출.** knowledge/policy 문서 중 코드 링크가 0(`mentions`도
   `governs`도 없음)이고 `anchor: none`도 아닌 것 → **방치 고아**로 플래그.
   `anchor: none`은 정당 고아로 통과.

3. **`doc-stale` = 앵커 드리프트 + git 날짜.** 문서의 governed 코드가 문서 최종
   커밋 이후 변경됨 → 재검토 플래그. (`mentions`/`governs` 엣지 + git mtime — 실험 2의
   재료가 이미 다 있음.)

4. **본문·코드펜스 참조 추출.** 현재 `mentions`는 heading→class만. 본문/코드펜스의
   클래스·파일 참조까지 링크하면 앵커 밀도가 커진다(예: `graph-curation-guide` 본문의
   `SqliteStore`가 heading이 아니라 놓침 → 본문 추출로 포착).

5. **frontmatter를 typed 그래프 메타로 승격.** `governs`/`anchor`를 엣지·플래그로.

→ 요약: **CodeScan은 "heading이 클래스를 말하면 연결"에서 "문서 frontmatter가
선언한 코드를 governs로 연결 + 본문 참조까지"로 확장**해야, 정책-고아 문서의
최신성까지 그래프로 감시할 수 있다.

## 5. 즉시 적용 (이번 갱신)

- 이 문서(`doc-code-linkage.md`) 신설 — 조사·분류·전략·CodeScan 로드맵 고정.
- 정책 문서에 앵커 frontmatter 부여: `aot-rules`(governs 분석기/전 `.cs`),
  `regex-patterns`(governs Languages 분석기) → Type B→A 승격.
- 설계-고아 표시: `web-gui-design-craft`(governs `Home/harness-view/`), `actor-model-cross-toolkit`(`anchor: none`).
- `semantic-analyzer-docker.md` — "도커 우선"에서 "**하베스트 우선, 도커 최후수단**"으로
  전략 역전을 반영(pivot 배너 + testsample-build 링크).

## 관련
- [[graph-reconciliation]] — 코드↔문서 `mentions`, 재조정
- [[../.claude/skills/testsample-build/SKILL.md]] — 무빌드 하베스트(도커 대안)
- 실험 2(doc-stale): 코드 그래프에 없는 심볼을 서술하는 문서 = stale
