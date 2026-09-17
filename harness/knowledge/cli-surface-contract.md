# CLI/TUI 표면 계약 (CLI Surface Contract)

CodeScan이 사람에게 보여주는 두 표면 — `Program.cs`(CLI)와 `Tui/TuiApp.cs`(TUI) — 이
스스로 광고한 것을 지키게 하는 규칙과, 그것을 **구동해서** 확인하는 방법.

---

## 1. 구조적 함정 세 가지

### 1-1. 커맨드 단어 뒤의 `--help`는 전역 파서에 닿지 않는다

`ParseGlobalOptions`는 첫 비(非)플래그 인자를 만나면 `commandFound = true`로 두고
그 뒤 인자를 전부 통과시킨다. 따라서 `codescan <cmd> --help`의 `--help`는
`GlobalOptions.ShowHelp`를 켜지 못한다.

→ **모든 명령 핸들러가 스스로 `-h/--help`를 처리해야 한다.**
빠뜨리면 그 명령은 `--help`에 도움말 대신 **실제 동작을 실행**한다.
TUI처럼 전체 화면을 점유하는 명령에서는 특히 치명적이다 (스크립트·CI에서 멈춘다).

### 1-2. `Application.KeyDown`은 모달 위에서도 발화한다

`Application.KeyDown`에 건 전역 핸들러는 다이얼로그가 떠 있는 동안에도 호출된다.
가드가 없으면:
- 전역 `Q`(뒤로)가 **다이얼로그 뒤 배경 화면**을 이동시킨다 — 확인창은 그대로 떠 있고 뒤만 바뀐다
- 전역에서 `key.Handled = true`로 삼킨 Esc가 다이얼로그에 닿지 않아 **취소 키가 사라진다**

→ 핸들러 첫 줄에서 `if (!ReferenceEquals(Application.Top, this)) return;`로 물러선다.

### 1-3. `[Enter] ...` 약속에는 `IsDefault`가 필요하다

화면 진입 시 포커스는 보통 CheckBox나 TextField에 있다. 그 상태에서 Enter는
포커스된 컨트롤이 소비할 뿐, 실행 버튼까지 가지 않는다.
힌트바가 `[Enter] Run Scan` / `[Enter] Keyword Search` / `[Enter] Save`를 약속한다면
해당 버튼에 `IsDefault = true`가 있어야 한다.

→ 단, **한 뷰 안에 IsDefault 버튼이 둘 이상이면 서로 Enter를 두고 다툰다.**
   MainView처럼 여러 화면이 Visible 토글로 공존하는 구조에서는
   `HideOptions()`에서 전부 끄고, 각 `ShowXxx()`에서 그 화면 것만 켠다.

---

## 2. 서드파티 위젯은 검증한 만큼만 믿는다

`MessageBox.Query`(Terminal.Gui 2.0.0)는 버튼의 `Selecting`/`Accepting`이 발화해도
호출자에게 `-1`("아무것도 클릭 안 됨")을 돌려주는 것이 헤드리스 재현에서 확인됐다.
같은 환경에서 직접 만든 `Dialog` + `Button`은 정상 동작했다.
이 앱은 마우스를 끄고 있어(`Application.IsMouseDisabled = true`),
키보드 확정이 안 되면 확인 다이얼로그는 **빠져나갈 수만 있고 확정할 수는 없는 창**이 된다.

→ 파괴적 동작의 확인창은 `MainView.Confirm()`처럼 **버튼을 직접 배선한 다이얼로그**를 쓴다.
   결과가 우리 손에 있으면 헤드리스로 확정/취소 양쪽을 회귀 테스트할 수 있다.

---

## 3. 헤드리스 구동 방법

TUI는 읽지 말고 **띄워서** 검수한다.

### 3-1. xUnit(VSTest)에서는 띄울 수 없다

VSTest 호스트에서 `Application.Init`은 다음으로 죽는다:

```
System.TypeLoadException : Could not load type
'System.Diagnostics.CodeAnalysis.MemberNotNullWhenAttribute' from assembly
'Microsoft.TestPlatform.CoreUtilities' ... at Terminal.Gui.ConfigurationManager.Initialize()
```

→ TUI 구동 하네스는 `CodeScan.csproj`를 참조하는 **별도 콘솔 프로젝트**로 만든다
   (레포 밖, 스크래치 디렉토리에 두고 검수가 끝나면 버린다).

### 3-2. 구동 골격

```csharp
Application.Init(new FakeDriver());
var main = new MainView();
Application.Begin(main);                    // Application.Top = main

// 비공개 상태는 리플렉션으로 읽는다: _mode, _listItems, _dirEntries, _listView ...
// 행 활성화 = 해당 엔트리 키의 인덱스로 SelectedItem을 맞추고 Enter를 올린다
listView.SelectedItem = entries.IndexOf("__DELETE__");
listView.SetFocus();
Application.RaiseKeyDownEvent(Key.Enter);
```

- 화면 전이 핸들러는 **동기**라 실행 루프 없이도 `RaiseKeyDownEvent`만으로 검증된다.
- 모달은 중첩 `Application.Run`으로 들어가므로 `Application.Iteration`에 훅을 걸고,
  `FakeConsole.PushMockKeyPress(KeyCode.Null)`를 몇 개 넣어 루프가 돌게 한다.
- 스캔은 `Task.Run`으로 돌고 완료 통지가 `Application.Invoke` 큐에 쌓인다.
  실행 루프가 없으면 `_scanning`은 true로 남는다 — **DB 행 수로 완료를 판정**한다.

### 3-3. ConPTY는 이 환경에서 쓸 수 없다

부모 프로세스에 콘솔이 없으면 `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`로 띄운 자식도
콘솔을 얻지 못하고, `Console.WindowWidth` / `TreatControlCAsInput`이
`IOException: 핸들이 잘못되었습니다`로 죽는다 (TUI의 `tui-crash.log`에 남는 그 오류다).
실 터미널 확인이 꼭 필요하면 사람에게 넘긴다.

---

## 4. 파괴적 동작 검수 프로토콜

1. **버릴 수 있는 대상**을 새로 스캔해 만든다 (`codescan scan <작은 프로젝트>`)
2. 삭제 전 대상 프로젝트의 행 수를 테이블별로 센다
3. 삭제 후 두 가지를 본다
   - 대상 프로젝트 행: `projects / scans / graph_nodes / graph_edges / architecture_analysis` = 0
   - **DB 전체 고아 행**: 부모가 사라진 `files / methods / comments / project_docs / search_index` = 0
4. 검수 종료 시 원상 복구 (테스트 프로젝트 삭제, 생성된 로그 정리)

`SqliteStore.DeleteProject`는 CLI `project-delete`와 TUI `[Delete Project]`가
**공유**한다. 한쪽에서 검증하면 SQL 완전성은 양쪽에 대해 검증된 것이지만,
확정 UI(다이얼로그)는 표면마다 따로 확인해야 한다.

---

## 5. 알려진 환경 의존 테스트

`SourceUpdateServiceTests`의 `FindGitRoot_ReturnsNull_ForNonGitDirectory`와
`Execute_SucceedsWithWarning_ForNonGitDir`는 "`%TEMP%`의 상위에는 `.git`이 없다"를
전제한다. 홈 디렉토리 자체가 git 저장소인 머신(`C:\Users\<user>\.git`)에서는
`FindGitRoot`가 **정확하게** 그 루트를 찾아내므로 두 테스트가 실패한다.
제품 버그가 아니다 — 테스트가 격리되지 않은 것이다.
