---
name: gui-design-review
type: engine
triggers:
  - "GUI 디자인 리뷰 수행"
  - "뷰어 디자인 워크플로우"
  - "web gui 디자인 점검 수행"
  - "harness-view 디자인 개선 수행"
description: CodeScan 웹 뷰어(Home/harness-view/)의 디자인·인터랙션을 캡처→3축 평가→개선안→목업→위임 적용→재검증 순으로 오케스트레이션하는 워크플로우. 입력 범위(전체/변경분)를 분기한다.
participants:
  - web-gui-design-reviewer
---

# GUI Design Review 워크플로우

> 목적: `Home/harness-view/` 뷰어의 디자인 완성도와 인터랙션 품질을
> 측정 가능한 3축으로 끌어올린다. 코드는 위임으로만 바꾼다.

## 트리거

- "GUI 디자인 리뷰 수행" / "뷰어 디자인 워크플로우"
- "web gui 디자인 점검 수행" / "harness-view 디자인 개선 수행"
- 또는 뷰어(`css/`, `js/`) 변경 PR 직전 정기 점검

## 입력 범위 분기

| 범위 | 조건 | 대상 |
|------|------|------|
| 전체 | "전체"/명시 없음 | `Home/harness-view/` 전 화면 |
| 변경분 | "변경"/PR 컨텍스트 | `git diff` 로 변경된 `css/**`·`js/**` 화면만 |

## 절차

1. **로컬 서버 기동 (web-gui-design-reviewer → harness-view-build DEV SERVER)**
   - 127.0.0.1:8765 기동, 무인증

2. **현상 캡처 (web-gui-design-reviewer → playwright-e2e)**
   - 변경분 범위면 해당 화면만, 전체면 주요 화면 일괄
   - 데스크탑/모바일 뷰포트 양쪽 스크린샷

3. **3축 평가 (web-gui-design-reviewer)**
   - Case W: W1 커버리지(35) / W2 인터랙션(35) / W3 확장(30)
   - 60점 미만 축 우선 표시 (knowledge/web-gui-design-craft.md)

4. **개선안 + 근거 목업 (web-gui-design-reviewer → Pencil MCP)**
   - 토큰화 diff 시안 + 인터랙션 패치 시안 + a11y 위반 목록
   - `get_editor_state(include_schema:true)` 후 `batch_design`으로 Before/After 목업

5. **위임 적용 (→ harness-view-build CREATOR)**
   - 사용자 승인 후에만. 파일·치환 명세를 동봉하여 코드 적용 요청
   - 직접 CSS/JS 수정 금지

6. **재검증 (web-gui-design-reviewer → playwright-e2e)**
   - 적용 후 동일 화면 재캡처 → Before/After 대조 → 3축 재채점

7. **로그 기록** (필수)
   - `harness/logs/gui-design-review/{yyyy-MM-dd-HH-mm}-{title}.md`
   - 3축 점수 + 캡처 경로 + 위임 명세 + Before/After 포함

## 산출물

- Case W 3축 점수표 (적용 전/후)
- 토큰화 대상 매직값 목록 + `:root` 변수 시안
- 인터랙션·a11y 개선 패치 시안
- .pen 근거 목업
- harness-view-build CREATOR 위임 명세

## 중단 조건

- 로컬 서버 기동 실패 → 캡처 전에 원인부터 보고
- Pencil MCP 미연결 → 목업 단계 건너뛰고 텍스트 근거로 진행(로그에 명시)
- 사용자 승인 없음 → 5단계(코드 적용) 진입 금지, 제안 상태로 종료
