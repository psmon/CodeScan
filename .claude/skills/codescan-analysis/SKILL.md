---
name: codescan-analysis
description: |
  codescan CLI와 git CLI를 활용한 소스코드 분석 스킬.
  등록된 프로젝트의 코드 구조, 메서드, 클래스, 변경 이력을 탐색·분석할 때 사용한다.
  다음 상황에서 반드시 이 스킬을 사용할 것:
  - "코드 분석", "소스 분석", "코드 검색", "코드 구조 파악"
  - "메서드 검색", "클래스 구조", "프로젝트 구조 분석"
  - "최근 변경사항", "코드 변경 이력", "git log 분석"
  - "codescan으로 검색", "코드스캔", "코드 스캔"
  - "프로젝트 스캔", "코드 인덱싱", "코드베이스 탐색"
  - "소스 업데이트", "프로젝트 업데이트", "코드 최신화", "소스 동기화"
  - 특정 프로젝트 소스코드에 대한 질문이나 분석 요청이면 적극적으로 트리거
  - 다른 스킬에서 코드 레벨 분석이 필요할 때도 트리거
---

# CodeScan Analysis Skill

`codescan` CLI와 `git` CLI로 소스코드를 탐색·분석하는 스킬이다.
codescan은 다국어(C#, Java, Kotlin, JS, TS, PHP, Python) 소스코드를 클래스:메서드 수준까지 인덱싱하고, SQLite FTS5 기반 전문검색과 git blame 통합을 제공하는 네이티브 바이너리 도구다.

이 스킬의 PowerShell 스크립트들은 `scripts/` 디렉토리에 있으며, 반복적인 분석 작업을 자동화한다.

---

## 1. codescan CLI 명령어 레퍼런스

### 프로젝트 관리

| 명령어 | 설명 | 예시 |
|--------|------|------|
| `codescan projects` | 등록된 전체 프로젝트 목록 조회 | `codescan projects` |
| `codescan project <id>` | 프로젝트 요약 (파일수, 메서드수, 확장자, 저자 등) | `codescan project 1` |
| `codescan project <id> --detail` | 프로젝트 상세 (전체 파일, 메서드, git blame, 문서) | `codescan project 1 --detail` |
| `codescan project-addinfo <id> <desc>` | 프로젝트에 AI 친화적 설명 추가 | `codescan project-addinfo 1 "Spring Boot API"` |
| `codescan project-update <id> [opts]` | 프로젝트 경로/설명/소스 업데이트 | `codescan project-update 1 --path D:\new` |
| `codescan project-delete <id> [-f]` | 프로젝트 DB에서 삭제 (소스 파일은 유지) | `codescan project-delete 1 -f` |

**project-update 옵션:**
- `--path <path>` — 프로젝트 루트 경로 변경
- `--addinfo <text>` — 프로젝트 설명 변경
- `--source` — 소스 업데이트 (git pull + 전체 재스캔). git이 없거나 pull 실패 시 경고 출력 후 현재 소스로 스캔 진행

```powershell
# 소스 업데이트 (git pull → full scan → DB 재인덱싱)
codescan project-update 1 --source

# 소스 업데이트 + 설명 변경 동시에
codescan project-update 1 --source --addinfo "Updated description"
```

### 스캔 & 인덱싱

| 명령어 | 설명 | 예시 |
|--------|------|------|
| `codescan scan [path]` | 프로젝트 스캔 및 DB 등록 (`list --detail --tree --stats` 단축) | `codescan scan D:\myproject` |
| `codescan list <path>` | 커스텀 옵션으로 디렉토리 스캔 및 DB 등록 | `codescan list D:\myproject` |
| `codescan list <path> --detail --tree --stats` | 전체 분석 (메서드 추출 + git blame + 트리 + 통계) | `codescan list ./src --detail --tree --stats` |

**list 옵션:**
- `-i, --include <exts>` — 포함할 확장자 (쉼표 구분, 예: `.cs,.java`)
- `-e, --exclude <dirs>` — 제외할 디렉토리 (쉼표 구분, 예: `bin,obj`)
- `-d, --depth <n>` — 최대 탐색 깊이
- `--tree` — 트리 형식 출력
- `-s, --stats` — 파일/크기 통계 포함
- `--detail` — 클래스:메서드 분석 + git blame 포함

### 검색

| 명령어 | 설명 | 예시 |
|--------|------|------|
| `codescan search <query>` | 전체 프로젝트 대상 하이브리드 검색 | `codescan search "WebSocket"` |
| `codescan search <query> -p <id>` | 특정 프로젝트 내 검색 | `codescan search "Order" -p 1` |
| `codescan search <query> -t <type>` | 타입별 필터 검색 | `codescan search "TODO" -t comment` |

**검색 타입(-t):** `method`, `file`, `doc`, `comment`, `commit`
**결과 제한(-l):** 기본 30건, `-l 50` 등으로 조절

### TUI (사용자 직접 사용)

`codescan tui`는 인터랙티브 터미널 UI로, 사용자가 직접 실행하여 프로젝트를 브라우징·스캔·검색할 수 있다.
이 스킬에서는 TUI를 사용하지 않는다. 사용자에게 TUI 사용을 안내할 때는 다음과 같이 한다:

> "TUI 모드에서 직접 탐색하시려면 터미널에서 `codescan tui`를 실행하세요. 프로젝트 브라우징, 스캔, 검색을 GUI로 할 수 있습니다."

### 글로벌 옵션

| 옵션 | 설명 |
|------|------|
| `--verbose` | 상세 출력 모드 |
| `--devmode` | `~/.codescan/logs/`에 로그 파일 저장 |
| `-h, --help` | 도움말 |
| `-v, --version` | 버전 확인 |

---

## 2. 분석 워크플로우

### 2.1 프로젝트 등록 및 현황 파악

분석 전에 먼저 등록된 프로젝트를 확인한다. 미등록 프로젝트는 `scan`으로 등록한다.

```powershell
# 등록된 프로젝트 목록 확인
codescan projects

# 새 프로젝트 스캔 및 등록 (scan = list --detail --tree --stats)
codescan scan D:\myproject

# 프로젝트 요약 조회
codescan project <id>

# 프로젝트 설명 추가
codescan project-addinfo <id> "프로젝트 설명"
```

또는 `scripts/scan-project.ps1` 스크립트로 자동화할 수 있다.

### 2.2 프로젝트 소스 업데이트

이미 등록된 프로젝트의 소스를 최신화하고 DB를 재인덱싱한다.
수동으로 git pull + scan을 할 필요 없이 `--source` 한 번으로 처리된다.

```powershell
# 프로젝트 소스 업데이트 (git pull → full scan → DB 재인덱싱)
codescan project-update <id> --source
```

또는 `scripts/update-source.ps1` 스크립트로 자동화할 수 있다.

**동작 순서:**
1. 프로젝트 경로에서 git 저장소 확인
2. `git pull`로 최신 소스 가져오기 (실패 시 경고 후 계속 진행)
3. 전체 스캔: 디렉토리 순회 + 메서드/코멘트 분석 + git blame
4. DB에 새 스캔 레코드 저장 (기존 데이터는 유지, 누적 방식)

**주의:** 현재 스캔 데이터는 누적(append) 방식이므로 반복 실행 시 이력이 쌓인다.
조회 시 최신 스캔만 표시되지만, 검색은 전체 스캔 데이터를 대상으로 한다.

### 2.3 코드 탐색 (어디에 뭐가 있는지 찾기)

**순서: codescan으로 위치 파악 → 실제 파일 읽기**

codescan은 코드 네비게이션 도구다. 전체 디렉토리를 풀스캔하기 전에 codescan으로 위치를 먼저 파악하면 토큰을 절약할 수 있다.

```powershell
# 1단계: 메서드/파일 검색으로 위치 파악
codescan search "OrderService" -p 1 -t method
codescan search "Controller" -p 1 -t file

# 2단계: 특정 경로 상세 스캔
codescan list D:\myproject\src --include ".java" --tree --detail
```

codescan 결과에서 경로를 확인한 뒤, Read 도구로 실제 파일을 읽어 코드를 분석한다.

또는 `scripts/search-code.ps1` 스크립트로 여러 타입을 한번에 검색할 수 있다.

### 2.4 변경 이력 분석 (최근에 뭐가 바뀌었는지)

```powershell
# 최신 소스 가져오기
cd D:\myproject; git pull

# 최근 커밋 이력
git log --oneline -20

# 특정 기간 변경사항
git log --oneline --since="2026-03-01" --until="2026-04-01"

# 특정 파일/디렉토리 변경 이력
git log --oneline -10 -- src/main/java/com/example/order/

# 변경된 파일 목록 및 diff
git diff --name-only HEAD~5
git show <commit-hash> --stat
```

또는 `scripts/analyze-changes.ps1` 스크립트로 변경 이력 분석을 자동화할 수 있다.

### 2.5 크로스 프로젝트 검색

```powershell
# 전체 프로젝트 대상 검색 (프로젝트 지정 안 함)
codescan search "WebSocket"
codescan search "authentication" -t method
```

또는 `scripts/cross-project-search.ps1`로 프로젝트별 분류된 검색 결과를 얻을 수 있다.

---

## 3. PowerShell 스크립트

`scripts/` 디렉토리에 반복 작업을 자동화하는 스크립트가 준비되어 있다.
Bash에서 실행할 때는 `powershell -ExecutionPolicy Bypass -File <script>` 형태로 호출한다.

| 스크립트 | 용도 | 주요 파라미터 |
|----------|------|---------------|
| `project-overview.ps1` | 프로젝트 요약 + 최근 git 활동 | `-ProjectId <id>` |
| `search-code.ps1` | 다중 타입 코드 검색 | `-Query <text> [-ProjectId <id>] [-Types <list>]` |
| `analyze-changes.ps1` | 변경 이력 분석 | `-ProjectPath <path> [-Days <n>] [-Count <n>]` |
| `cross-project-search.ps1` | 전체 프로젝트 통합 검색 | `-Query <text> [-Type <type>]` |
| `scan-project.ps1` | 프로젝트 스캔/등록 자동화 | `-Path <path> [-Include <exts>] [-Exclude <dirs>]` |
| `update-source.ps1` | 프로젝트 소스 업데이트 (git pull + 재스캔) | `-ProjectId <id>` |

---

## 4. 보안 가드레일

이 스킬은 **읽기 전용(read-only)** 분석 도구다.

### 허용
- `git pull` (최신 소스 가져오기)
- `git log`, `git diff`, `git show`, `git blame` (이력 조회)
- `git branch -a`, `git status` (상태 확인)
- `codescan` CLI 명령 (list, search, project, projects, project-addinfo, project-update)
- Read 도구로 소스 파일 읽기

### 금지
- `git commit`, `git push`, `git merge`, `git rebase` 등 변경 반영 명령
- `git checkout`으로 브랜치 전환 (분석 대상 코드가 바뀔 수 있음)
- 분석 대상 소스 파일 수정 (Edit, Write 도구 사용 금지)

사용자가 커밋이나 푸시를 요청하면:
> "이 스킬은 읽기 전용 분석 도구입니다. 소스코드 변경/반영은 해당 프로젝트에서 직접 수행해주세요."

---

## 5. 사용 예시

### API 코드 분석
```
사용자: "백엔드에서 주문 관련 API 코드 찾아줘"
→ 1. codescan projects (프로젝트 목록 확인)
→ 2. codescan search "주문" -p <id> -t method
→ 3. codescan search "Order" -p <id> -t file
→ 4. 검색 결과에서 경로 확인 후 Read로 실제 코드 읽기
```

### 최근 변경사항 분석
```
사용자: "이 프로젝트 최근 변경사항 분석해줘"
→ 1. cd <project-path> && git pull
→ 2. git log --oneline -20
→ 3. git diff --stat HEAD~5
→ 4. 변경된 주요 파일을 Read로 읽어 변경 내용 요약
```

### 프로젝트 구조 파악
```
사용자: "프로젝트 구조 알려줘"
→ 1. codescan project <id>
→ 2. codescan list <project-path>/src --tree --depth 3
```

### 프로젝트 소스 업데이트
```
사용자: "프로젝트 소스 최신화해줘" / "코드 업데이트하고 다시 스캔해줘"
→ 1. codescan project-update <id> --source
   (git pull + full scan + DB 재인덱싱이 자동으로 수행)
→ 2. codescan project <id> (업데이트 결과 확인)
```

### 새 프로젝트 등록
```
사용자: "이 코드베이스를 codescan에 등록해줘"
→ 1. codescan scan <path>
→ 2. codescan project-addinfo <id> "프로젝트 설명"
→ 3. codescan project <id> (등록 결과 확인)
```
