---
date: 2026-06-21T17:15:00+09:00
agent: web-gui-design-reviewer
engine: gui-design-review
type: improvement
mode: log-eval
trigger: "개선시작 — codescan gui (CodeScan Graph) 디자인 개선"
---

# CodeScan Graph 디자인 개선 — Before/After 목업

## 실행 요약

`codescan-gui-view.pen`에 **개선안(After) 화면을 원본 옆에 추가**하여 Before/After
비교를 구성. 원본(`Commands/GuiCommand.cs` 임베드 HTML)은 보존하고, 동일 정보
구조 위에서 디자인 시스템을 재설계했다. 코드(HTML) 반영은 역할 경계상 사용자
승인 후 진행한다.

- Before: "CodeScan Graph — gui" (원본 충실 재현)
- After: "CodeScan Graph — Improved (After)" (개선안)

## 개선 내용 (축별)

### W1 — 디자인 요소 커버리지 (토큰화 + 컴포넌트 시스템)
- **토큰 추가**: accent ramp(`g-accent`/`g-accent-strong`/`g-accent-soft`),
  surface 층(`g-surface`/`g-surface-2`), `g-chip`, `g-focus`/`g-focus-ring`,
  `g-ink-2`, 노드 soft 틴트 — raw CSS 하드코딩을 시맨틱 토큰으로
- **헤더**: 브랜드 마크(teal 라운드 + git-fork 글리프) + 버전 pill + **상태 pill**
  (good dot + "24 nodes · 31 edges") — 평문 stats를 구조화
- **셀렉트/버튼 일원화**: 38px·radius10·surface-2 셀렉트, segmented 컨트롤,
  일관된 라운드/보더
- **결과 카드**: kind 색상 좌측 바 + kind 라벨 + name + path — 평이한 list item을
  스캔 가능한 카드로
- **레전드**: 색상 틴트 토글 칩 + 카운트(off 상태 시각화 포함)

### W2 — 인터랙션 충실도 (포커스/모션 어포던스)
- **focus-visible 링**: 검색 필드에 `g-focus` 보더 + `g-focus-ring` 외곽 spread
  shadow로 포커스 상태를 명시 (원본엔 포커스 표현 없음)
- **segmented 컨트롤 3종**: 모드(Graph/Query/Keyword), 뷰(2D/3D),
  Depth 필 그룹(0–4) — 토글 상태가 명확
- **호버 어포던스**: Relationship 행에 hover 배경(accent-soft) 1개 시연
- **플로팅 툴바**: 그림자 깊이 + 아이콘 버튼(Fit/Reset) + Count/Hint pill
- (모션·`prefers-reduced-motion`은 정적 .pen 외 코드 단계 항목으로 기록)

### W3 — 창의적 확장 & 완성도 (정보 위계)
- **micro-label** 섹션 헤더(PROJECT/KEYWORD TYPE/…)로 위계 정리
- **Detail**: 노드 색과 일치하는 kind 배지(CLASS=오렌지) + 구분선 KV +
  Relationship 행(kind pill + arrow + 타깃 dot + chevron)
- **그래프**: 선택 노드 halo 링 + 노드 drop-shadow 깊이 + 선택 라벨 pill +
  hot-edge 강조

## 평가 (Case W — Before → After)

| 축 | Before | After | 델타 근거 |
|----|--------|-------|----------|
| W1 디자인 요소 커버리지 | 19/35 | **30/35** | 토큰 체계 + 셀렉트/버튼/카드/칩 컴포넌트 일원화 |
| W2 인터랙션 충실도 | 24/35 | **31/35** | focus-visible 링 + segmented 3종 + hover + 플로팅 툴바 |
| W3 창의적 확장 & 완성도 | 22/30 | **27/30** | micro-label 위계 + 컬러 kind 배지 + 관계 행 재설계 |
| **Total** | **65/100 (C+)** | **88/100 (B+)** | 합격선 70 통과 |

3축 운영 평가: 코드 미수정(.pen 목업) 안전 · 원본 정보구조 유지(정합) ·
`codescan gui start` 캡처와 Before/After 대조로 검증 가능

## 다음 단계 — 코드 반영 (승인 필요)

After 디자인을 `Commands/GuiCommand.cs`의 임베드 HTML(`const string Html`)에
반영하려면 코드 작업이 필요(역할 경계). 승인 시:
1. `:root` CSS 변수에 토큰 추가(라이트 기준), 셀렉트/버튼/세그먼트 스타일
2. 검색 필드 `:focus-visible` 링 + `prefers-reduced-motion` 가드
3. 헤더 상태 pill, 결과 카드, Detail kind 배지/관계 행 마크업
4. `codescan gui start`로 기동 → Playwright 캡처 → After 목업과 대조

> 환경 특이사항: 헤드리스 Pencil 스크린샷은 텍스트/근백색 미렌더. 노드 데이터·
> 레이아웃(헤더60 + 3열 340/760/340)은 snapshot으로 검증. 라이브 캔버스 확인 권장.

## 추가 — 3D 모드 화면 (세 번째 스크린)

사용자 요청으로 `gui`의 **3D 모드**를 별도 스크린("CodeScan Graph — 3D mode")으로 표현.

**라이브러리 이해(핵심)**: 3D는 three.js/WebGL 라이브러리가 **아니라**, 동일
2D `<canvas>` 위 **손수 구현한 yaw/pitch 원근 투영**(`worldToScreen`) + 절차적
우주 연출이다. 그래서 After 스크린을 Copy 후 **스테이지(캔버스)만** 다크 우주로
교체했다(좌/우 패널은 라이트 유지 — CSS가 `.stage.mode-3d`/`canvas.space-mode`만
재스타일하는 것과 정합).

스테이지 재구성:
- 배경: `#060914` + 라디얼 네뷸라 3겹(보라 좌상 / 핑크 우상 / 시안 하단) — 스크린샷 검증됨
- `drawSpaceBackground` 별필드 58개(트윙클 색 3종, opacity 분포),
  `drawSpaceGrid` 시안 궤도 타원 링 5 + 보라 스포크 8 + 시안 스캔빔
- 엣지: 시안 글로우(hot=store는 밝은 시안 + 강한 글로우)
- 노드: 라디얼 그라데이션(white→base→dark) + 시안/핑크 글로우 섀도, 선택 노드는
  핑크 글로우 + 시안 halo 링
- 라벨: 네온(시안 / 선택=핑크) + 다크 텍스트섀도
- 툴바: 다크 글래스, **3D 활성**(시안), Fit/Reset 아이콘 + Hint/Count pill(다크)

화면 3종 정리: **Before(2D 라이트) · After(2D 라이트 개선) · 3D mode(다크 우주)**.
코드 반영 시 `prefers-reduced-motion`으로 3D 별/스캔빔/펄스 애니메이션을 가드.
