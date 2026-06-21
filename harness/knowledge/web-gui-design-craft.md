# web-gui-design-craft.md — 웹 GUI 디자인·인터랙션 검수 기준

CodeScan의 정적 웹 뷰어(`Home/harness-view/`)의 **디자인 완성도와 인터랙션 품질**을
검수·개선하기 위한 도메인 지식. pencil-creator 디자인 하네스의 평가 철학을
정적 SPA 뷰어 맥락으로 번역한 것이다.

> 원천: `C:\code\psmon\pencil-creator`
> - `.../harness/knowledge/design-craft.md` — Case W(Web) 3축 평가
> - `.../harness/agents/design-evaluator.md` — 디자인 평가 에이전트
> - `.../skills/pencil-design/SKILL.md` — Pencil MCP .pen 생성 워크플로우

---

## 1. 대상 정의

| 항목 | 값 |
|------|-----|
| 대상 | `Home/harness-view/` (vanilla JS, no framework) |
| CSS | `css/main.css`(레이아웃) · `css/components.css`(1681줄, 컴포넌트) · `css/md.css` |
| 렌더링 | `js/views/*`(11개 화면) · `js/components/*`(pen-renderer, md-viewer, spec-card …) |
| 빌드 | `harness-view-build` 스킬 — BUILDER(인덱스) / CREATOR(코드) / DEV SERVER(127.0.0.1:8765) |
| 코드 적용 | **이 에이전트는 코드를 직접 고치지 않는다** — CREATOR 모드에 위임 |

---

## 2. Case W (Web) 3축 — 100점

pencil-creator의 Case W를 뷰어 검수용으로 적용한다.

### W1. 디자인 요소 커버리지 (35점)
화면이 일관된 **디자인 시스템**으로 구성되었는가.

| 점수 | 기준 |
|------|------|
| 0 | 색상·간격이 화면마다 제각각, 하드코딩된 매직값 산재 |
| 15 | 부분적 일관성. 일부 컴포넌트만 재사용 |
| 25 | 디자인 토큰(CSS 변수)으로 색/간격/타이포 통일, 컴포넌트 재사용 |
| 35 | 토큰 + 다크모드 + 상태(hover/active/focus/disabled) 전 영역 정의 + 반응형 일관 |

### W2. 인터랙션 충실도 (35점)
**마이크로 인터랙션과 전환**이 의도를 전달하는가.

| 점수 | 기준 |
|------|------|
| 0 | 정적. 상태 변화에 시각 피드백 없음 |
| 15 | hover만 존재. 전환 없이 급격히 바뀜 |
| 25 | transition 적용 + 로딩/빈 상태 처리 + 포커스 가시성 |
| 35 | 의미 있는 모션(진입/강조/전환) + 키보드 내비 + `prefers-reduced-motion` 존중 |

### W3. 창의적 확장 & 완성도 (30점)
요구를 넘어선 **사용성 향상**이 있는가.

| 점수 | 기준 |
|------|------|
| 0 | 최소 구현, 거친 마감 |
| 10 | 동작은 하나 디테일(여백/정렬/대비) 미흡 |
| 20 | 정렬·여백·계층 정돈, 빈/에러/로딩 상태 일관 |
| 30 | 검색 강조·키보드 단축·딥링크·스켈레톤 등 사용성 확장 + 픽셀 단위 마감 |

> 합격선: 총 70점. 60점 미만 축은 우선 개선 대상.

---

## 3. 디자인 토큰 체계 (W1의 핵심)

현재 뷰어는 색을 하드코딩한다(`#2563EB`, `#E5E7EB`, `#6B7280` …).
개선의 출발점은 **토큰화**다.

```css
:root {
  /* color — semantic, not literal */
  --c-bg: #F0F2F5;          --c-surface: #FFFFFF;
  --c-border: #E5E7EB;      --c-text: #111827;
  --c-text-muted: #6B7280;  --c-accent: #2563EB;
  --c-accent-soft: #EFF6FF;
  /* spacing scale (4px base) */
  --sp-1: 4px; --sp-2: 8px; --sp-3: 12px; --sp-4: 16px; --sp-6: 24px; --sp-8: 32px;
  /* radius / shadow / motion */
  --r-sm: 6px; --r-md: 8px; --r-lg: 14px;
  --shadow-card: 0 1px 2px rgba(0,0,0,.06);
  --ease: cubic-bezier(.4,0,.2,1); --dur: .18s;
}
@media (prefers-color-scheme: dark) {
  :root { --c-bg:#0F1115; --c-surface:#1A1D23; --c-border:#2A2E37; --c-text:#E5E7EB; --c-text-muted:#9CA3AF; --c-accent-soft:#16223A; }
}
```

> 원칙: **시맨틱 네이밍**(`--c-accent`)이지 리터럴(`--blue-600`)이 아니다 →
> 다크모드/테마 전환 시 토큰 값만 바꾸면 전 화면이 따라온다.

---

## 4. 인터랙션 패턴 (W2의 핵심)

| 패턴 | 적용처 | 지침 |
|------|--------|------|
| 전환(transition) | hover/active/모달/사이드바 | `transition: <prop> var(--dur) var(--ease)`. 색·변형만, layout 속성 애니메이션 회피 |
| 진입 모션 | view 전환, 카드 등장 | opacity+translateY 8px, 150~250ms. 과하지 않게 |
| 포커스 가시성 | 모든 클릭 요소 | `:focus-visible { outline: 2px solid var(--c-accent); outline-offset: 2px }` |
| 로딩/빈 상태 | 비동기 로더 | 스켈레톤 또는 스피너 + "결과 없음" 빈 상태 카피 |
| 모션 존중 | 전역 | `@media (prefers-reduced-motion: reduce){ *{animation:none!important; transition:none!important} }` |

---

## 5. 접근성(a11y) 체크리스트

- [ ] 본문 텍스트 대비 ≥ 4.5:1, 큰 텍스트 ≥ 3:1 (WCAG AA)
- [ ] 모든 인터랙티브 요소 키보드 포커스 가능 + `:focus-visible` 링
- [ ] 모달: 포커스 트랩 + Esc 닫기 + 열릴 때 포커스 이동
- [ ] 아이콘 전용 버튼에 `aria-label`
- [ ] `prefers-reduced-motion` 분기 존재
- [ ] 색만으로 의미 전달하지 않음(아이콘/텍스트 병기)

---

## 6. Pencil MCP 목업 워크플로우 (제안 근거 시각화)

개선안을 말로만 제시하지 않고 **.pen 목업**으로 보여준다.

```
1. get_editor_state(include_schema: true)   — 스키마 확보(필수 선행)
2. get_screenshot                            — 현재 뷰어 캡처와 대조
3. batch_design                              — Before/After 카드 또는 토큰 팔레트 시안 작성
4. get_screenshot                            — 목업 검증
5. export_nodes                              — 필요 시 산출물 추출
```

> 목업은 **근거**다. 실제 CSS/JS 적용은 [[gui-design-review]] 엔진을 통해
> `harness-view-build` CREATOR에 위임한다. 역할 경계를 지킨다.

---

## 7. 위임 경계

| 상황 | 위임처 |
|------|--------|
| 코드(CSS/JS) 실제 수정 | `harness-view-build` 스킬 CREATOR 모드 |
| 인덱스/콘텐츠 갱신 | `harness-view-build` 스킬 BUILDER 모드 |
| 로컬 확인 서버 | `harness-view-build` 스킬 DEV SERVER (127.0.0.1:8765) |
| 브라우저 캡처·인터랙션 회귀 | `playwright-e2e` 스킬 (기본 채널 Edge) |
| .pen 목업 생성 | Pencil MCP (이 세션 연결됨) / pencil-design 패턴 |
