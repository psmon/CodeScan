---
date: 2026-09-18T04:30+09:00
agent: cli-ux-inspector
type: review
mode: execution
trigger: "codescan tui 도움말이 없는것같고 도움말대로 작동하나 검수"
---

# codescan tui — 도움말 부재 및 프로젝트 추가/삭제 검수

## 실행 요약

`codescan tui`의 도움말 유무와, 도움말이 약속한 대로 동작하는지를 검수했다.
사용자 요청의 초점은 **스캔 대상 프로젝트의 추가/삭제가 실제로 되는가**였다.

검수 방법은 소스 읽기가 아니라 **구동**이었다. Terminal.Gui `FakeDriver`로 실제
`MainView`를 띄우고 키를 올려 `_mode` 전이와 DB 효과를 읽었다
(방법 전문: [[cli-surface-contract]]).
대상 프로젝트는 `C:\code\psmon\ScreenSaver`를 새로 스캔해 만들었고, 검수 후 삭제했다.

## 결과 — 결함 7건

| # | 결함 | 등급 | 상태 |
|---|------|------|------|
| 1 | TUI 확인 다이얼로그를 **키보드로 확정할 수 없음** → 마우스 비활성이라 삭제 불가 | Critical | 수정 |
| 2 | 모달 위에서 전역 `Q`가 배경 화면을 이동시킴 | Moderate | 수정 |
| 3 | 전역 Esc 차단으로 다이얼로그에 **취소 키가 없음** | Moderate | 수정 |
| 4 | `[Enter] Run Scan` / `[Enter] Keyword Search` / `[Enter] Save`가 초기 포커스에서 무동작 | Moderate | 수정 |
| 5 | `codescan tui --help`가 도움말 대신 **TUI를 실행** | Moderate | 수정 |
| 6 | 크래시해도 exit code 0 | Moderate | 수정 |
| 7 | `codescan help tui`가 한 줄, TUI 내부에 도움말 키 없음 | Info | 수정 |

부수 확인: TUI 스캔은 `--devmode` 없이도 항상 `~/.codescan/logs/`에 로그를 쓴다.
동작을 바꾸는 대신 도움말에 명시했다.

### 1. 삭제 확정 불가 (Critical)

`MessageBox.Query`(Terminal.Gui 2.0.0)는 버튼의 `Selecting`/`Accepting`이 발화해도
`-1`("아무것도 클릭 안 됨")을 반환했다. 재현:

```
buttons=["Delete" focus=True default=True, "Cancel" focus=False default=False]
>> Selecting FIRED (Cancel=False)
>> Accepting FIRED (Cancel=True)
MessageBox.Query returned -1        # TUI는 == 0 일 때만 삭제한다
MessageBox.Clicked static = -1
```

같은 환경에서 직접 만든 `Dialog` + `Button`은 Enter에 정상 반응했다(대조군).
이 앱은 `Application.IsMouseDisabled = true`이므로, 확정 수단이 아예 없었다.

→ `MainView.Confirm()`으로 교체(버튼을 직접 배선). 결과가 우리 손에 들어와
   확정·취소 양쪽을 회귀 검증할 수 있게 됐다.

### 2~3. 모달 격리

`Application.KeyDown` 전역 핸들러가 다이얼로그 위에서도 발화했다.

```
[modal open] Application.Top = Dialog("Delete Project")
[modal open] Q 누른 후 -> 배경 mode = ProjectDetail → Projects
```

→ 핸들러 진입부에 `if (!ReferenceEquals(Application.Top, this)) return;`

### 4. `IsDefault` 부재

TUI의 어느 버튼에도 `IsDefault`가 없었고, 각 화면은 CheckBox/TextField에 포커스를 두고 열린다.

```
[scan opts] focus=chkTree, btnExecute.IsDefault=False → Enter 후 mode = ScanOptions (무동작)
[search   ] focus=txtSearch, btnSearch.IsDefault=False → Enter 후 mode = SearchInput (무동작)
```

→ 화면별로 `IsDefault`를 켜고 `HideOptions()`에서 모두 끈다
   (한 뷰에 둘 이상이면 Enter를 두고 다툰다).

## 검증

수정 전후를 같은 하네스로 대조했다.

| 검증 | 수정 전 | 수정 후 |
|------|---------|---------|
| TUI 삭제 (Enter 확정) | `mode=ProjectDetail`, DB에 잔존 True | `mode=Projects`, 잔존 **False** |
| 삭제 취소 (Esc) | 불가 (Esc 차단) | 다이얼로그 닫힘, 잔존 True |
| 삭제 취소 (Tab→Cancel→Enter) | — | 다이얼로그 닫힘, 잔존 True |
| 모달 위 `Q` | 배경 `ProjectDetail → Projects` | 배경 `ProjectDetail` 유지 |
| 다이얼로그 종료 후 키 복구 | — | `Q → Projects`, `F1 → Help` |
| `[Enter] Run Scan` | `ScanOptions` (무동작) | `Scanning` |
| `[Enter] Keyword Search` | `SearchInput` (무동작) | `SearchResults` |
| F1/`?` 도움말 | 없음 | 모든 화면에서 열리고 `Q`로 원래 화면 복귀 |

**프로젝트 추가(스캔)는 수정 전부터 정상**이었다. TUI 스캔 결과가 CLI `scan`과 행 수까지 동일:
files 70, methods 185, comments 179, graph_nodes 601, graph_edges 900, search_index 442.

**삭제 SQL도 정상**이었다. `SqliteStore.DeleteProject`(CLI/TUI 공용) 실행 후
대상 프로젝트 행 0, DB 전체 고아 행 0 (files/methods/comments/project_docs/search_index/scans/graph_nodes/graph_edges).
문제는 SQL이 아니라 **확정 UI**에 있었다.

전체 테스트: 170 통과 / 2 실패. 실패 2건은 변경 이전부터 존재하며(stash 후 재현),
`C:\Users\psmon\.git` 때문에 `%TEMP%` 상위에 실제 git 루트가 있어 생기는
**환경 의존 테스트** 문제다. 제품 버그 아님.

## 평가

| 축 | 판정 | 근거 |
|----|------|------|
| 워크플로우 개선도 | A | "읽어서 판단" 대신 "띄워서 판단"이 성립. 결함 7건 중 6건이 구동으로만 드러났고, 수정 후 동일 하네스로 회귀 대조까지 끝냈다 |
| Claude 스킬 활용도 | 3/5 | codescan-analysis로 표면 탐색. playwright-e2e는 터미널 앱에 무력했고, ConPTY 자작 드라이버는 부모에 콘솔이 없어 실패 — 헤드리스 FakeDriver가 유일한 실효 경로였다 |
| 하네스 성숙도 | L3 | 터미널 표면에 담당자가 생겼고 방법이 knowledge로 남았다. 아직 엔진에 편입되지 않아 수동 트리거에만 반응한다 |

## 다음 단계 제안

1. **실 터미널 1회 확인** — 헤드리스는 `MessageBox` 결함을 재현했지만 FakeDriver 한정 현상일
   가능성을 완전히 배제하지 못한다. 수정본은 자작 다이얼로그라 그 위험에서 벗어났으나,
   실제 터미널에서 삭제·취소를 한 번 눌러보면 계약이 닫힌다.
2. **환경 의존 테스트 격리** — `SourceUpdateServiceTests` 2건. `.git` 조상이 없는 위치에
   임시 디렉토리를 만들거나, 발견된 루트를 기준으로 단언하도록 고친다.
3. **`--help` 전수 점검** — `tui`에서 드러난 함정(커맨드 뒤 플래그가 전역 파서에 닿지 않음)이
   `semantic`, `graph-edit` 등 다른 명령에도 남아 있는지 검표원으로 한 번 훑는다.
4. **TUI 스캔의 로그 기록** — 지금은 항상 쓴다. 스캔 옵션에 `[ ] Save log file` 체크박스를 두어
   CLI `--devmode`와 계약을 맞추는 선택지가 있다.
