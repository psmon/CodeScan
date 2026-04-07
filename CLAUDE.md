# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CodeScan is a CLI/TUI code scanner in C# (.NET 10.0) that analyzes source files at the class:method level with git blame integration, stores results in SQLite with FTS5 full-text search, and ships as a single native AOT binary.

## Build & Run

```bash
dotnet build                                    # Debug build
dotnet publish -c Release                       # AOT single binary
dotnet run -- scan /path/to/project             # Scan & index a project
dotnet run -- search "query" --type method      # Search indexed data
dotnet run -- tui                               # Interactive TUI
```

Deploy scripts: `Script/deploy-win.ps1` (Windows), `Script/deploy-linux.sh` (Linux). These build AOT and install to `~/.codescan/bin`.

## Testing

```bash
dotnet test                                          # All tests
dotnet test --filter "FullyQualifiedName~SourceAnalyzerTests"  # Test class
dotnet test --filter "Name=CSharp_ClassAndMethod"    # Single test
```

Tests are xUnit in `Tests/`. Two test files cover the critical parsers:
- `SourceAnalyzerTests.cs` — multi-language class/method extraction (40+ tests)
- `CommentExtractorTests.cs` — comment extraction with context (40+ tests)

Tests use inline string arrays as input, no external test fixtures.

## Architecture

**Entry point**: `Program.cs` — CLI argument parsing and command routing.

**Data flow**: `DirectoryScanner` traverses files → `SourceAnalyzer` extracts classes/methods → `CommentExtractor` extracts comments → `GitBlameService` enriches with blame data → `SqliteStore` persists to `~/.codescan/db/codescan.db` (FTS5 indexed).

**Key services**:
- `SourceAnalyzer` / `CommentExtractor` — regex-based parsers for 7 languages (C#, Java, Kotlin, JS, TS, PHP, Python). All regexes use `[GeneratedRegex]` for AOT safety.
- `SqliteStore` — schema management, CRUD, FTS5 trigram search with LIKE fallback for short terms.
- `GitLogSearchService` — hybrid search combining FTS5 results with live `git log --grep`.
- `AppPaths` — centralizes all `~/.codescan/` path management.
- `TuiApp` — Terminal.Gui v2 (NetDriver) state-machine UI.

**Commands** (in `Commands/`): `ListCommand` (scan+index), `SearchCommand` (hybrid search), `ProjectsCommand`, `ProjectCommand`, `ProjectAddInfoCommand`, `ProjectUpdateCommand`, `ProjectDeleteCommand`.

## Key Design Decisions

- **Centralized storage**: All data under `~/.codescan/` (db/, logs/), not the working directory. Managed by `AppPaths.cs`.
- **Version auto-bump**: `version.txt` is incremented on each build via MSBuild task in `CodeScan.csproj`.
- **Markdown always indexed**: `.md` files are always included even when `--include` filters are active.
- **Recent-first sorting**: Files/dirs sorted by modification time (newest first).
- **Git root detection**: Walks directory tree for `.git/` folder — no subprocess spawned.
- **Default exclusions**: `.git`, `.vs`, `.idea`, `bin`, `obj`, `node_modules`, `.next`, `dist`, `build`, `__pycache__`.

## AgentZero Skill Update

When asked "AgentZero 스킬업데이트":
- Source: `D:\code\AI\agent-win\.claude\skills\agent-zero` (SKILL.md + scripts/)
- Target: `.claude\skills\agent-zero` (this project)
- Copy source SKILL.md and scripts/ to target, overwriting.
