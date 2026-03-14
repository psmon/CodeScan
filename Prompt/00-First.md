# CodeScan - 프로젝트 요구사항 (Phase 0)

## 개요

CLI 기반 코드 스캐너. 소스 코드를 스캔하여 내장 DB에 인덱싱하고, 이후 빠르게 검색할 수 있도록 하는 도구.

## 기술 스택

| 항목 | 선택 |
|------|------|
| 언어 | C# (.NET 10.0) |
| 배포 | Native AOT (`PublishAot`) |
| 내장 DB | **미정** (추후 채택) |
| 대상 플랫폼 | Windows (PowerShell), Linux (Bash) |


작업대상 : CodeScan.csproj

## 테스트 환경

- **로컬 (Windows)**: PowerShell
- **리눅스**: WSL Bash
- **테스트 대상 샘플**: `D:\Code\AI\memorizer-v1` (닷넷 + 프론트엔드 혼합 프로젝트)
  - Phase 1 성공 기준: 이 프로젝트 하위의 소스 파일이 정상적으로 탐색되는 것

### 탐색 핵심 원칙

- **소스 코드 + .md 파일은 항상 함께 탐색** (필수)
  - 코드만 탐색하는 것이 아니라, 문서(.md)와 소스 코드 두 가지 정보를 동시에 획득하는 것이 중요
  - .md 파일에는 프로젝트 구조, API 설명, 설계 의도 등 코드만으로 알 수 없는 컨텍스트가 포함됨
- 기본 탐색 시 `.md` 확장자는 제외 대상에서 항상 보호 (사용자가 명시적으로 제외하지 않는 한)

## 단계별 목표

### Phase 1 - 디렉토리 탐색 (현재 목표)
- 지정된 경로의 디렉토리/파일 트리 탐색
- 필터링 (확장자, .gitignore 패턴 등)
- 탐색 결과 출력

### Phase 1.5 - 파일 기반 임시 저장소 (DB 채택 전)
- 탐색 결과를 `Prompt/TestFileDB/` 하위에 파일(로깅) 형태로 저장
- `--devmode` 옵션이 활성화된 경우에만 동작
- 추후 내장 DB로 교체될 수 있음을 감안한 설계 (저장 인터페이스 추상화)
- 파일 구조 예시:
  ```
  Prompt/TestFileDB/
  ├── 2026-03-14_223000_list.log    # 타임스탬프_명령어.log
  └── 2026-03-14_224500_list.log
  ```

### Phase 2 - 내장 DB 인덱싱
- 내장 DB 선정 및 적용
- Phase 1.5의 파일 저장소를 DB로 교체
- 스캔 결과를 DB에 저장
- 증분 인덱싱 (변경된 파일만 재인덱싱)

### Phase 3 - 검색
- 인덱싱된 코드에서 빠른 검색
- 파일명, 내용, 심볼 등 다양한 검색 모드

---

## CLI 인터페이스 설계

- https://gui-cs.github.io/Terminal.Gui/docs/index.html - 이 라이브러리 채택할것

```
codescan [command] [options]
```

### 글로벌 옵션

| 옵션 | 단축 | 설명 |
|------|------|------|
| `--help` | `-h` | 도움말 출력 |
| `--version` | `-v` | 버전 출력 |
| `--verbose` | | 상세 로그 출력 |
| `--devmode` | | 개발 모드: 탐색/검색 결과를 `Prompt/TestFileDB/`에 파일로 기록 |

### 명령어

#### `scan` - 코드 스캔 및 인덱싱

```
codescan scan <path> [options]
```

| 옵션 | 단축 | 설명 | 기본값 |
|------|------|------|--------|
| `--include` | `-i` | 포함할 확장자 (예: `.cs,.js`) | 전체 |
| `--exclude` | `-e` | 제외할 패턴 (예: `bin,obj,node_modules`) | 없음 |
| `--respect-gitignore` | | .gitignore 규칙 적용 | `true` |
| `--depth` | `-d` | 최대 탐색 깊이 | 무제한 |

예시:
```bash
# Linux
codescan scan ./src --include ".cs,.razor" --exclude "bin,obj"

# Windows PowerShell
.\codescan scan .\src --include ".cs,.razor" --exclude "bin,obj"
```

#### `search` - 인덱스 검색 (Phase 3)

```
codescan search <query> [options]
```

| 옵션 | 단축 | 설명 | 기본값 |
|------|------|------|--------|
| `--type` | `-t` | 검색 유형: `file`, `content`, `symbol` | `content` |
| `--case-sensitive` | `-c` | 대소문자 구분 | `false` |
| `--limit` | `-l` | 결과 수 제한 | `20` |

예시:
```bash
codescan search "HttpClient" --type content --limit 10
codescan search "*.cs" --type file
```

#### `list` - 디렉토리 탐색 결과 출력 (Phase 1 핵심)

```
codescan list <path> [options]
```

| 옵션 | 단축 | 설명 | 기본값 |
|------|------|------|--------|
| `--include` | `-i` | 포함할 확장자 | 전체 |
| `--exclude` | `-e` | 제외할 패턴 | 없음 |
| `--depth` | `-d` | 최대 탐색 깊이 | 무제한 |
| `--tree` | | 트리 형태로 출력 | `false` |
| `--stats` | `-s` | 파일 수/크기 통계 포함 | `false` |

예시:
```bash
codescan list ./src --tree --include ".cs" --stats
```

#### `info` - DB/인덱스 상태 확인 (Phase 2+)

```
codescan info
```

- 인덱싱된 파일 수, DB 크기, 마지막 스캔 시간 등 출력

#### `help` - 도움말

```
codescan help [command]
codescan --help
codescan scan --help
```

- 명령어 없이 실행 시 전체 명령어 목록 표시
- 명령어 지정 시 해당 명령어 상세 도움말 표시

---

## 출력 예시 (Phase 1 - list)

```
$ codescan list ./src --tree --stats

./src
├── Controllers/
│   ├── HomeController.cs    (2.1 KB)
│   └── ApiController.cs     (4.3 KB)
├── Models/
│   ├── User.cs              (0.8 KB)
│   └── Product.cs           (1.2 KB)
└── Program.cs               (0.5 KB)

Files: 5 | Total: 8.9 KB | Extensions: .cs(5)
```

---

## DevMode 파일 저장 설계

- **목적**: DB 채택 전까지 탐색/검색 결과를 파일로 확인 및 검증
- **저장 위치**: `Prompt/TestFileDB/`
- **활성 조건**: `--devmode` 글로벌 옵션 사용 시에만 동작
- **설계 방향**: 저장 로직을 인터페이스(추상화)로 분리하여, 추후 DB 구현체로 교체 가능하도록 함

```
# devmode 사용 예시
codescan list D:\Code\AI\memorizer-v1 --tree --stats --devmode
# → 콘솔 출력 + Prompt/TestFileDB/2026-03-14_223000_list.log 에 결과 기록
```

---

## 메모

- 내장 DB는 AOT 호환성을 고려하여 선정 필요 (SQLite, LiteDB, RocksDB 등 후보)
- 단일 바이너리 배포가 목표이므로 외부 의존성 최소화
- 저장소 추상화: `IResultStore` → `FileResultStore` (현재) → `DbResultStore` (추후)


## 픽스

- 스캔잘됨..하지만 다되고나서 종료됨
- 종료조건이 좀 민감한것같음 
 - 키보드 좌우누르다보면 종료, 스크롤 움직이다보면 종료 - 간헐절 높은확률
 - 분석 다되고나서 종료 ( 정상로깅확인)
- 마우스움직이면 종료됨.. 
- git이 없는경우 skip 하는버전으로 , 마지막 수행에서 완료가 안떨어짐
- 정확하게는 git연결안된 프로젝트일때 git을 시도안하는것으로
- 로그작성확인은 했는데.. 그리고 이이후 메뉴진입또는 Q눌러도안되고 작동불가
- 별개로 완료 로그도 안나와서..이부분도 확인
- 추가로 현재 TUI 색상 가독성이 안좋음 색상교체해줄래? , 기본바탕검정..흰색글씨모드로
- 종료되면 첫화면가기 단축키도 추가..(스크롤은 마지막이 보이게 - 스트림에 맞춰 마지막 보여주기)
- 그리고 이전가기가 ESC누르면 완전탈출인듯... 무시해죠
- Q누를때 한글키 ㅂ도.. Q로 동일인식 
- 디렉토리 선택창에서 Q하고 H안먹음.. Q는 디렉토리 선택탈출하고 이전메뉴..H는 디렉토리 선택탈출하고 홈으로
- 엑세스할수 없습니다. 오류나오면 스킵하고 다음넘어갈것
-디렉토리 선택창에서는 여전히 H,Q 등의 단축키가 안먹힘... 오른쪽 클릭해 상단 단축키 를 통해 H,Q는 가능

- 파일분석후 첫번째 만나는 readme.md,agent.md,claude.md 셋중하나먼저 만나면 이내용도 로깅에 적재..상위디렉토리 우선
- 파일리스트는 보여줄때 가장최근 변경 폴더를 우선으로 보여죠(폴더가 파일보다 우선보여짐)
- C#만 함수분석 하는것으로 보임 php,java,korlin,js(타잎스르립트포함) 등도 함수파악해 git획득 코멘트 동일하게남길것
-진행 해죠 cli에 search + tui serarch 기능 도 함께 만들어 ..그리고 log를 시도해 인덱스화된 디렉토리 경로도 저장관리 추후 리인덱스용..  projects하면 인덱스된 경로..이것역시 내장 db에 관리해야할듯
- TUI 홈화면도 search,project 기능을 사용할수 있게 구현
- 
##

- dotnet run -- tui