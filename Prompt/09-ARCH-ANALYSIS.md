---
name: 09-arch-analysis
type: doc
anchor: none  # Prompt/ = 빌드 이력 프롬프트(설계-고아). 현재 코드 정책이 아니라 검사 면제.
---

# Architecture View — AI-outside 아키텍처 분석

웹 뷰어(`CodeScan View`)에 좌측 네비(LNB)를 도입하고 기존 그래프를 **Graph View**로,
새로 **Architecture View**를 추가한다. Architecture View는 스캔된 지식 그래프 + 코드를
분석해 아키텍처를 **Mermaid 다이어그램**으로 보여준다.

코드베이스에는 LLM 클라이언트가 없다(외부 프로세스는 docker/git뿐). 따라서 분석은
**AI-outside** 방식 — 외부 AI(Claude Code 등)가 구조화 입출력 CLI를 도구로 호출한다.

## 흐름

```
1) codescan arch bundle <project>            # CLI: 그래프+코드를 요약한 JSON 번들을 stdout으로
2) (AI가 번들을 읽고 Mermaid 다이어그램 + 레이어 서술 markdown을 생성)
3) codescan arch set <project> --diagram diagram.mmd --summary summary.md
4) codescan gui start                        # Architecture View가 저장분을 렌더
```

- `arch bundle`은 **결정론적 요약**이다: 상위 디렉토리, 디렉토리별 클래스, 외부 모듈 의존
  (`imports`), 타입 관계(`inherits_or_implements`/`uses_type`/`creates`), 문서 mentions,
  프로젝트 addinfo. 전체 그래프 덤프가 아니라 레이어 추론에 필요한 만큼만(크기 상한 적용).
- `arch set`은 Mermaid + 서술을 `architecture_analysis`에 upsert하고 프로젝트를
  `analysis_state='analyzed'`로 표시한다. Mermaid/서술은 파일 또는 `-`(stdin)로 받는다.
- 프로젝트를 **재스캔하면** 해당 분석은 `stale`로 내려간다(코드가 바뀜 → 재분석 권장).
  다이어그램은 유지되며 뷰에 stale 배지로 표시된다.

## AI에게 (이 문서를 읽는 LLM에게)

너는 코드의 관계를 잘 이해한다. `codescan arch bundle <id>`의 JSON을 읽고:
1. `directories` / `classesByDir` / `typeRelations` / `externalModules`로 **레이어/서브시스템**을 추론하라.
2. `graph TD`(flowchart) 또는 C4 형식의 **Mermaid** 다이어그램을 작성하라(레이어를 subgraph로 묶어라).
3. 레이어별 짧은 markdown 서술을 작성하라.
4. `codescan arch set <id> --diagram <file|-> --summary <file|->`로 저장하라.

## 관련 명령

- `codescan arch bundle <project>` — 분석 번들(JSON) 출력
- `codescan arch set <project> --diagram <file|-> [--summary <file|->] [--layers <file>] [--format mermaid]`
- `codescan arch show <project>` — 저장된 분석 출력
- `codescan arch status` — 프로젝트별 분석 상태(none/analyzed/stale)

## 저장/스키마

- `architecture_analysis(project_id UNIQUE, format, diagram, summary, layers, analyzed_at, source_scan)`
- `projects.analysis_state` — `null`/`none` | `analyzed` | `stale`
- 웹: `GET /api/architecture?project=<id>`, `GET /api/projects?analyzed=1`,
  `GET /assets/mermaid.js`(임베디드 mermaid 번들, 오프라인 안전)
