# CodeScan - AGENT.md

## Overview

CodeScan is a CLI/TUI code scanner that traverses directories, analyzes source files at the class:method level with git blame integration, extracts comments, and stores everything in a local SQLite database for fast search and retrieval. Designed as a single native binary via .NET AOT.

- **CLI mode** (`codescan list/search/projects`): For AI/automation pipelines
- **TUI mode** (`codescan tui`): Interactive terminal UI for human users

## Tech Stack

| Item | Choice |
|------|--------|
| Language | C# (.NET 10.0) |
| Deployment | Native AOT (`PublishAot`, `TrimMode=link`) |
| TUI Framework | [Terminal.Gui v2 beta](https://gui-cs.github.io/Terminal.Gui/docs/index.html) (NetDriver) |
| Embedded DB | SQLite + FTS5 trigram (`Microsoft.Data.Sqlite`) |
| Platforms | Windows (PowerShell), Linux (Bash) |

## Project Structure

```
CodeScan/
├── Program.cs                       # Entry point, CLI parsing, all commands
├── CodeScan.csproj                  # Project config (AOT, dependencies)
├── codescan.db                      # SQLite database (auto-created)
│
├── Commands/
│   ├── ListCommand.cs               # list: scan + analyze + index to DB
│   ├── SearchCommand.cs             # search: hybrid FTS5 + git log
│   └── ProjectsCommand.cs           # projects: list indexed projects
│
├── Models/
│   ├── FileEntry.cs                 # File/dir info (path, size, methods, comments)
│   ├── MethodEntry.cs               # Class:Method + git blame metadata
│   └── CommentBlock.cs              # Comment text + nearby code context
│
├── Services/
│   ├── DirectoryScanner.cs          # Recursive traversal (recent-first sort)
│   ├── SourceAnalyzer.cs            # Multi-language class/method extraction
│   ├── CommentExtractor.cs          # Comment extraction with code context
│   ├── GitBlameService.cs           # Git blame per method (porcelain parser)
│   ├── GitLogSearchService.cs       # Git log --grep hybrid search
│   ├── ProjectDocFinder.cs          # Auto-find README/AGENT/CLAUDE.md
│   ├── TreeFormatter.cs             # Tree/flat output with method sub-trees
│   ├── SqliteStore.cs               # SQLite DB: schema, insert, FTS5 search
│   ├── IResultStore.cs              # Storage interface (legacy)
│   └── FileResultStore.cs           # File-based log storage (devmode)
│
├── Tui/
│   └── TuiApp.cs                    # Terminal.Gui UI (browse, scan, search, projects)
│
└── Prompt/
    ├── 00-First.md                  # Requirements document
    └── TestFileDB/                  # DevMode log output directory
```

## CLI Commands

```
codescan v0.2.0 - Code Scanner & Indexer

Commands:
  list <path>       Scan directory, analyze, and index to DB
  search <query>    Search indexed methods, files, docs, comments + git log
  projects          List all indexed projects
  tui               Interactive TUI mode (user mode)
  help [command]    Show help

Global Options:
  -h, --help     Show help
  -v, --version  Show version
  --verbose      Verbose output
  --devmode      Also save results to Prompt/TestFileDB/ as log files
```

### `list` Options

| Option | Short | Description |
|--------|-------|-------------|
| `--include` | `-i` | Include extensions (comma-sep, e.g. `.cs,.js`) |
| `--exclude` | `-e` | Exclude directories (comma-sep) |
| `--depth` | `-d` | Max traversal depth |
| `--tree` | | Tree format output |
| `--stats` | `-s` | Include file/size statistics |
| `--detail` | | Analyze class:method + git blame + comments per source file |

### `search` Options

| Option | Short | Description |
|--------|-------|-------------|
| `--type` | `-t` | Filter: `method`, `file`, `doc`, `comment`, `commit` |
| `--limit` | `-l` | Max results (default: 30) |

### Examples

```bash
# Scan and index a project
codescan list ./src --tree --detail --stats

# Search (hybrid: DB + git log)
codescan search "HttpClient"
codescan search "TODO" --type comment
codescan search "SSE" --type method --limit 10
codescan search "authentication" --type comment

# View indexed projects
codescan projects

# Interactive TUI
codescan tui
```

## Multi-Language Source Analysis

| Language | Extensions | Class Detection | Method Detection |
|----------|-----------|----------------|-----------------|
| C# | `.cs` | class/struct/record/interface | access+return+name |
| Java | `.java` | class/interface/enum | access+return+name |
| Kotlin | `.kt`, `.kts` | class/object/data class/sealed class | fun/suspend fun |
| JavaScript | `.js`, `.jsx` | class | function/arrow/const/export |
| TypeScript | `.ts`, `.tsx` | class | function/arrow/const/export |
| PHP | `.php` | class/interface/trait | function |
| Python | `.py` | class (indent) | def/async def (indent-based) |

All use `[GeneratedRegex]` for AOT safety.

## Comment Extraction

Extracts comments from all supported languages with nearby code context:

| Style | Languages |
|-------|-----------|
| `//` single-line | C#, Java, Kotlin, JS/TS, PHP |
| `/* */` block | C#, Java, Kotlin, JS/TS, PHP |
| `#` single-line | Python, PHP |
| `"""` / `'''` docstring | Python |

Each comment stores:
- `comment`: The comment text (cleaned of syntax markers)
- `nearby_code`: Up to 3 lines of code after the comment
- `before_code`: 1 line of code before the comment
- `start_line` / `end_line`: Line range in source file

## Database (SQLite + FTS5)

File: `codescan.db` (auto-created in working directory)

### Schema

| Table | Purpose |
|-------|---------|
| `projects` | Indexed project paths + last scan info (for re-indexing) |
| `scans` | Scan history per project |
| `files` | File list per scan (path, size, extension, depth) |
| `methods` | Class:method per file + git blame (author, date, commit msg) |
| `comments` | Comment blocks per file + nearby code context |
| `project_docs` | README/AGENT/CLAUDE.md content per scan |
| `search_index` | FTS5 virtual table (trigram tokenizer for CJK substring search) |

### Search Strategy (Hybrid)

```
search "query"
  1. FTS5 trigram search (3+ char terms)
  2. LIKE fallback (2-char terms, e.g. Korean 2-syllable words)
  3. git log --grep (commits not in DB blame)
  → Results merged, deduplicated
```

### Search Types

| Type | Source | Content |
|------|--------|---------|
| `method` | DB | Method name + commit message |
| `file` | DB | File name + path |
| `doc` | DB | README/AGENT/CLAUDE.md content |
| `comment` | DB | Comment text + nearby code |
| `commit` | Git | Commit message + changed files |

## Key Design Decisions

### .md files always included
When `--include` filter is used, `.md` files are always added. Source code and documentation are inseparable context.

### Recent-first sorting
Files and directories are sorted by last modification time (most recent first). Directories appear before files.

### Project doc auto-detection
`ProjectDocFinder` searches for README.md, AGENT.md, or CLAUDE.md (parent directories have priority, then BFS depth 2). Found doc content is saved to DB and appended to log files.

### Git blame without process overhead
`GitBlameService.FindGitRoot()` walks up the directory tree looking for `.git/` folder — no `git` process spawned. If no `.git` found, blame is skipped entirely. Blame results are cached per commit hash (porcelain format optimization).

### Default directory exclusions
`.git`, `.vs`, `.idea`, `bin`, `obj`, `node_modules`, `.next`, `dist`, `build`, `__pycache__`

### Access errors
Inaccessible directories/files are silently skipped (no crash).

## TUI Architecture

Built on Terminal.Gui v2 beta (NetDriver for CJK Unicode support). Single `MainView : Toplevel` with state machine:

```
RootSelect ──→ DirBrowse ──→ ScanOptions ──→ Scanning ──→ Results
    │                                            ↑            │
    │                                            └────────────┘
    ├──→ [Search] ──→ SearchInput ──→ SearchResults
    └──→ [Projects] ──→ Project list ──→ DirBrowse
```

### Key Bindings
| Key | Action |
|-----|--------|
| `Q` / `ㅂ` | Back (exit at root) |
| `H` / `ㅎ` | Go to home (root select) |
| `Enter` | Select / Enter directory |
| `Up/Down` | Navigate / Scroll |
| `Tab` | Navigate scan options / search fields |
| `ESC` | Blocked (prevents accidental exit) |

### Background Scanning
Scan runs in `Task.Run()`. Progress streams to UI via `Application.Invoke()` wrapped in `SafeInvoke()` (never throws). Mouse input disabled at OS level via `kernel32.dll SetConsoleMode`.

### Color Scheme
Black background, white text. Title: bright yellow. Hints: bright cyan. Focus: black on cyan.

## Output Format

### Tree with --detail
```
├── AskBotController.cs  (61.0 KB)
│   └── [Global]
│       ├── StreamChat()  L69-153  [2025-09-17 psmon] SSE multi-session fix
│       ├── SendMessage()  L161-244  [2026-01-25 psmon] AskBot LLM-EX upgrade
│       └── CreateShareLink()  L339-601  [2025-10-22 psmon] Share feature improvement
```

Format: `MethodName()  L{start}-{end}  [{date} {author}] {commit message}`

## Build & Run

```powershell
# Build
dotnet build

# Scan and index
dotnet run -- list D:\Code\MyProject --tree --detail --stats

# Search
dotnet run -- search "HttpClient" --type method
dotnet run -- search "TODO" --type comment

# View projects
dotnet run -- projects

# TUI
dotnet run -- tui

# AOT publish (single binary)
dotnet publish -c Release
```

## Testing

- **Windows**: PowerShell (`dotnet run -- ...`)
- **Linux**: WSL Bash
- **Sample project**: `D:\Code\AI\memorizer-v1` (426 files, 960 methods, 194 .md)
- **DB verification**: `codescan projects` shows indexed stats
