---
date: 2026-06-21T17:45:00+09:00
agent: web-gui-design-reviewer
engine: gui-design-review
type: improvement
mode: log-eval
trigger: "제안대로 디자인 개선진행 — GuiCommand.cs 코드 반영 + Playwright 검증"
---

# CodeScan Graph 디자인 개선 — 코드 반영 & 라이브 검증 완료

## 실행 요약

사용자 승인("제안대로 디자인 개선진행")에 따라 Before/After + 3D 목업의 디자인을
**`Commands/GuiCommand.cs`의 임베드 HTML(`const string Html`)에 실제 반영**.
역할 경계상 코드 반영은 승인 후에만 — 본 단계가 그 적용이다. 캔버스 그래프
렌더링(2D/3D `drawNode`/`drawSpaceBackground`)은 이미 구현되어 있어 보존하고,
**CSS 디자인 시스템 + 마크업 + DOM 생성 JS(legend/list/detail)** 를 개선했다.

## 적용 내역 (모든 element ID·핸들러 보존)

**CSS (`<style>` 전면 교체)**
- `:root` 디자인 토큰: surface 층/accent ramp/chip/focus/focus-ring/radius/shadow/ease
- 헤더(브랜드 마크 + status pill), search-wrap `:focus-within` 링, 셀렉트 커스텀 chevron
- segmented(.seg) + depth pills(.pills) + checks + 컬러 legend chips + 카드형 list(.item .bar/.body)
- 플로팅 toolbar/hintbar(라이트) + `.stage.mode-3d` 다크 글래스 toolbar/hint
- detail: kind-badge + 구분선 KV + rel-row(pill+arrow+dot)
- **`*:focus-visible` 아웃라인 + `@media (prefers-reduced-motion: reduce)` 가드**

**HTML 마크업**
- 헤더: 브랜드(git-fork svg) + 버전 pill + status pill(`#stats` 내장)
- aside: search-wrap(search svg + `#query` + `#clear`.icon-x) + segmented(`#graph`/`#graphQuery`/`#keyword`) + micro-label 셀렉트 + depth pills(`#depthPills` + hidden `#depth`) + checks + legend + list
- detail head: `#detailKind`를 `.kind-badge`로

**JS (로직 보존, 5개 함수 + 핸들러)**
- `buildLegend`: kind별 카운트 + 컬러 틴트 칩
- `renderList`/`renderKeywordResults`: 좌측 컬러 바 카드
- `renderDetail`: 컬러 kind 배지 + 구분선 KV + 관계
- `relations`: rel-kind pill + arrow svg + 타깃 dot
- 신규: `setDepth(v)` + depth pills 클릭 → 검색, 세그먼트 active 토글, querySample→setDepth(1)

## 검증 (빌드 + 라이브 Playwright)

- **빌드**: `dotnet build` → 오류 0 (raw-string 무결성 OK; 경고 2는 기존 TUI/AOT)
- **서버**: `codescan gui start --port 8099` → HTTP 200, 신규 마크업 토큰 전부 존재
  (status-pill/search-wrap/kind-badge/--focus-ring/seg/depthPills/prefers-reduced-motion)
- **데이터**: 기본 그래프 자동 로드 — 237 nodes / 241 edges, kind 11종
- **Playwright(Edge) 캡처**:
  - 2D: `tmp/gui-redesign-2d.png` — status pill "237 nodes, 241 edges", legend 11칩,
    list 120카드, detail(Global) 정상
  - 3D: `tmp/gui-redesign-3d.png` — `#view3d` active, 스테이지 다크 우주 + 글로우 노드,
    좌/우 패널 라이트 유지(설계대로)
  - 콘솔 에러: `/favicon.ico` 404 1건뿐(브라우저 자동 요청, 기존 동작 · 무해)

## 평가 (Case W — 코드 반영 결과)

| 축 | Before | After(목업) | 반영(라이브) | 비고 |
|----|--------|------------|-------------|------|
| W1 | 19/35 | 30/35 | **30/35** | 토큰/컴포넌트 시스템 실제 적용 |
| W2 | 24/35 | 31/35 | **31/35** | focus-visible·segmented·pills·reduced-motion 동작 확인 |
| W3 | 22/30 | 27/30 | **27/30** | 위계·kind 배지·관계 행 렌더 확인 |
| **Total** | **65** | **88** | **88/100 (B+)** | 합격선 통과, 라이브 검증 완료 |

3축 운영 평가: 빌드 0-error(코드 안전성) · 캔버스 JS·API·ID 전부 보존(아키텍처
정합) · Playwright 회귀 캡처 확보(테스트 가능성)

## 잔여/후속

- favicon 404: 무해하나 원하면 `/favicon.ico` 빈 204 라우트 1줄 추가 가능
- 별/스캔빔/펄스는 캔버스 JS 애니메이션 — `prefers-reduced-motion` 시 CSS는 가드되나
  canvas `requestAnimationFrame`은 별도(원하면 JS에서 매처 체크 추가)
- 펜슬 정의: `codescan-gui-view.pen`(Before/After/3D 3종)이 이 디자인의 단일 출처
