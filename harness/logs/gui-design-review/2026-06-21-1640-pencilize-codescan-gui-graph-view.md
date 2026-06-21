---
date: 2026-06-21T16:40:00+09:00
agent: web-gui-design-reviewer
engine: gui-design-review
type: creation
mode: log-eval
trigger: "codescan gui 뷰(codescan gui start) 펜슬화 — codescan-gui-view.pen"
---

# 펜슬화 정정 — 실제 개선 대상은 `codescan gui` (CodeScan Graph)

## 정정 사항

사용자 정정: `gui` 명령 = 뷰 개선을 의미하며, **개선 대상은 `codescan gui start`로
뜨는 로컬 구동 웹뷰**(`Commands/GuiCommand.cs`가 임베드 HTML로 서빙하는
"CodeScan Graph" 단일 페이지)다. 앞서 펜슬화한 `Home/harness-view/`는 별개의
**harness-view 파트**이므로:

- 기존 `codescan-gui-view.pen`(harness-view 내용) → **`codescan-harness-view.pen`로
  rename**(참조 보관용, 개선 대상 아님)
- 실제 gui 뷰를 **새 `codescan-gui-view.pen`로 구성**(개선 베이스라인)

## 대상 분석 — Commands/GuiCommand.cs

`codescan gui start [--port 8085]` → `TcpListener` 기반 로컬 HTTP 서버
(127.0.0.1)가 임베드된 단일 HTML "CodeScan Graph"를 서빙. API: `/api/search`,
`/api/graph`, `/api/query`, `/api/projects`. **harness-view와 완전히 다른 UI이며
자체 팔레트**(teal `--accent #0B7285`, `--accent2 #7048E8`)를 사용한다.

레이아웃: `grid-template-columns: 330px minmax(420px,1fr) 330px`
- Header(54px): "CodeScan Graph" + 서브타이틀 + stats(우)
- Aside(330): Project/Search(+Clear)/Keyword Type/Graph Depth/Query Samples
  셀렉트 + [Graph Search·Query·Keyword] + 체크박스 2 + Legend chips + 결과 List
- Stage(가변): 2D/3D 그래프 캔버스 + 툴바[2D·3D·Fit·Reset Camera] + hintbar
- Detail(330): 선택 노드 tag/title/meta + KV + Relationships

노드 팔레트: project #0B7285, directory #5C7CFA, file #2F9E44, class #F08C00,
method #C2255C, comment #7048E8, doc #087F5B, author #495057, type #B7791F,
module #1971C2

## 결과 — 생성물

`harness/knowledge/design/codescan-gui-view.pen` (신규)

- **토큰**: gui 자체 팔레트 21종(g-bg/surface/stage/ink/muted/line/accent/accent2 +
  노드 9색) + font 토큰
- **화면 "CodeScan Graph — gui" (1440×760)**:
  - Header(타이틀+서브+stats "24 nodes, 31 edges")
  - Aside: 셀렉트 5종 + Search row(input+Clear) + 액션 버튼 3(Graph Search primary) +
    체크박스 2(checked) + Legend chips 6(class/method/type/file/module/comment) +
    결과 List 5건(SqliteStore 선택 상태)
  - Stage: 2D 그래프 — 노드 12개(원=class/method/type/project/module/comment,
    라운드렉트=file) + 엣지 14(contains/has_method/uses_type/creates/imports 등) +
    라벨 + 페인트 그리드 + 툴바[2D active·3D·Fit·Reset Camera] + hintbar
  - Detail: CLASS / SqliteStore / Services/SqliteStore.cs + KV(ID·Kind·Path·Detail) +
    Relationships 5(has_method·creates·imports)

`harness/knowledge/design/codescan-harness-view.pen` (rename, 참조 보관)
- 이전 세션의 harness-view Dashboard 펜슬화 — gui 개선과 무관, 참고용

## 평가 (Case W 3축 — codescan gui 베이스라인)

| 축 | 점수 | 근거 |
|----|------|------|
| W1 디자인 요소 커버리지 | 19/35 | 기능 충실하나 토큰 없는 raw CSS, 셀렉트/버튼 상태 최소. 다크모드는 3D 스테이지에만 한정 |
| W2 인터랙션 충실도 | 24/35 | 2D/3D 전환·드래그·줌·노드픽 등 캔버스 인터랙션은 풍부. 단 폼 컨트롤 포커스/전환 빈약 |
| W3 창의적 확장 & 완성도 | 22/30 | 3D 성운 배경·스캔빔 등 독창적. 빈 상태("No selection") 처리 양호, 정렬 여백은 거침 |
| **Total** | **65/100 (C+)** | W1 우선 — 토큰화 + 폼/버튼 디자인 시스템화, 라이트/다크 일관 |

3축 운영 평가: 코드 미수정(.pen 정의만) 안전 · GuiCommand.cs HTML과 1:1 정합 ·
Playwright(`codescan gui start` → 127.0.0.1:PORT 캡처)로 Before/After 재현 가능

## 환경 특이사항

헤드리스 Pencil 스크린샷이 텍스트·근백색 채움을 렌더하지 않음(노드 fill·content·
geometry는 batch 반환과 snapshot으로 검증). 3열 폭 330/780/330 = 1440 정합 확인.
사용자 라이브 캔버스에서 정상 렌더.

## 다음 단계 — 디자인 개선 (이 .pen 기반)

- W1: `--accent`/`--line`/노드색을 시맨틱 토큰으로 일원화, 폼 컨트롤·버튼 상태
  (hover/focus/active/disabled) 정의, 라이트 테마에도 일관 적용
- W2: 셀렉트/인풋 `focus-visible` 링, 결과 list 키보드 내비, 노드 선택 전환 모션,
  `prefers-reduced-motion`로 3D 애니메이션 가드
- W3: Legend/Detail 정보 위계 정리, 빈/로딩/에러 상태 일관 카피
- 개선안은 Before/After 목업으로 제시 → `GuiCommand.cs`의 임베드 HTML 수정은
  코드 작업이므로 사용자 승인 후 진행(역할 경계)
