# CodeScan

> English · [한국어](README-KO.md)

A fast CLI/TUI/GUI code scanner and indexer that analyzes source code at the class:method level with git blame integration, stores results in a local SQLite database with full-text and graph search, and provides command-line, terminal, and local web interfaces.

Built as a single native AOT binary with .NET 10.0.

<p align="center">
  <img src="Home/img/codescan.png" alt="CodeScan hero card" width="860" />
</p>

## Features

- **Multi-language analysis** — Extracts classes, methods, comments, and dependency hints across common source languages
- **Git blame integration** — Associates each method with its last author, date, and commit
- **Full-text search** — FTS5 with trigram tokenizer for substring and CJK language support
- **Hybrid search** — Combines indexed DB search with live `git log --grep` results
- **Graph search** — Neo4j-style source knowledge graph in embedded SQLite, **incrementally reconciled** on rescan (relationships are re-aligned, not reset); edge weight = corroborating-scan evidence that decays when unobserved, and curated edges survive
- **Cypher-like graph query** — Safe `MATCH ... WHERE ... LIMIT ...` subset with **variable-length paths** (`-[r*1..3]->`) and **actionable errors** that tell an AI how to rewrite an unsupported query
- **Code↔doc graph** — Markdown headings that name a code class are linked to it (`mentions` edges), so the graph answers "which docs discuss this class"
- **Hybrid dependency graphing** — Regex-first edges, plus optional **build-artifact harvest** (e.g. JVM `.class` inheritance, read with no toolchain) and language/project metadata probes
- **Interactive TUI** — Terminal.Gui v2 interface for browsing, scanning, keyword search, graph search, and graph query
- **Local web GUI** — Keyword/graph/query search with a 2D force-directed viewer (**drag-to-cluster magnet physics**, hubs sized by connection count), a **depth-lit 3D view** (Labels/Lines/Cluster toggles), and an **in-graph file preview**, on port 8085 by default
- **Project management** — Register, describe, update, and delete indexed projects
- **Single binary** — Native AOT compiled, no runtime dependency required

## Screenshots

### Web GUI Graph Viewer

The local GUI provides keyword search, graph search and Cypher-like query (with variable-hop and onboarding samples), and node/edge detail — including edge **weight**. Highlights:

- **2D force-directed graph** — drag a node and connected nodes follow like magnets (pull scales with edge weight); repulsion keeps clusters readable; hubs are drawn larger by connection count.
- **Depth-lit 3D view** — near nodes bright, far nodes dim (with a floor), for real depth; in-view **Labels / Lines / Cluster** toggles, where Cluster groups related nodes in 3D space.
- **In-graph file preview** — a node's `path` opens the real file in a VS Code-style popup (line numbers, method line-range highlight), so a human or AI can read the content behind a relationship without leaving the graph.

![CodeScan GUI graph viewer](Home/img/codescan-gui.png)

### Terminal UI

The TUI supports project browsing, scanning, project management, keyword search, and graph search from the terminal.

![CodeScan TUI home](Home/img/codescan-tui.png)

### TUI Scan Flow

Scanning can be launched from the terminal interface with method/comment extraction, git blame enrichment, and DB graph indexing.

![CodeScan TUI scan](Home/img/codescan-tui-scan.png)

### Designed for AI agents — CLI-first, not a chatbot

CodeScan does **not** host a language model. It is the indexing and retrieval layer an external AI agent (Claude Code, Codex, or an on-device SLM) drives through the CLI. Keeping the model out of the binary is deliberate: it keeps the Native AOT artifact small and dependency-free, so the same binary drops onto a laptop, a CI runner, or an SBC without shipping a multi-GB GGUF or GPU backend.

An agent composes CodeScan's structured retrieval commands to explore a codebase before reading source directly (which saves tokens):

- `codescan search "<query>" --type method|file|doc|comment` — FTS5 full-text lookup
- `codescan query "MATCH (c:class)-[r:uses_type]->(t:type) LIMIT 20"` — Cypher-like graph query for structured retrieval without SQL access
- `codescan graph "<query>" --depth 2` — neighborhood expansion around a match
- `codescan project <id>` / `codescan projects` — project context and indexed metadata

> An earlier build embedded an on-device Gemma/Nemotron chat loop directly in the TUI. It was removed to keep the CLI light; the retrieval layer is the durable value, and the model belongs on the agent side of the boundary (see [Why AOT? — Edge AI trend](#why-aot--edge-ai-trend-and-the-value-of-a-single-binary) below).

## Supported Languages

| Language | Extensions | Class / Type Detection | Method Detection | Dependency Hints |
|----------|-----------|------------------------|------------------|------------------|
| C# | `.cs` | class / struct / record / interface | access + return + name | using, inheritance/interface, `new`, type usage |
| Java | `.java` | class / interface / enum | access + return + name | import, extends/implements, `new`, type usage |
| Kotlin | `.kt`, `.kts` | class / object / data class / sealed class | fun / suspend fun | import, base type, constructor/type usage |
| JavaScript | `.js`, `.jsx` | class | function / arrow / const / export | import, extends/implements-style hints, `new`, type-like usage |
| TypeScript | `.ts`, `.tsx` | class | function / arrow / const / export | import, extends/implements, `new`, type annotations |
| PHP | `.php` | class / interface / trait | function | use, extends/implements, `new`, type hints |
| Python | `.py` | class (indent-based) | def / async def (indent-based) | import, base class, constructor-like calls |
| Go | `.go` | type struct/interface | graph dependency scan only | import, constructor/type usage |
| Rust | `.rs` | struct / enum / trait | graph dependency scan only | use, associated constructor/type usage |
| C/C++ | `.c`, `.cc`, `.cpp`, `.cxx`, `.h`, `.hpp`, `.hh`, `.hxx` | class / struct | graph dependency scan only | include, inheritance, `new`, type usage |

## Installation

<p align="center">
  <img src="Home/img/support-os.png" alt="CodeScan multi-OS release pipeline — win-x64, linux-x64, linux-arm64, osx-arm64 + SBOM + GitHub Release" width="900" />
</p>

<p align="center"><sub>One release pipeline → four native binaries → three package channels. <a href="https://github.com/psmon/CodeScan/actions/workflows/release.yml">GitHub Actions release.yml</a></sub></p>

### ⚡ Quick install — one line, every OS

CodeScan installs with **npm**, which works identically on **Windows, macOS, and Linux**. If you have [Node.js](https://nodejs.org/), you're one command away:

```bash
npm install -g @webnori/codescan-cli
```

On **Linux / macOS**, system Node root-owns npm's global folder, so add `sudo`:

```bash
sudo npm install -g @webnori/codescan-cli
```

Then verify and scan your first project:

```bash
codescan --version              # codescan v0.8.0 (or newer)
codescan scan /path/to/project  # index a codebase
codescan tui                    # interactive terminal UI
```

That's all most users need. The npm package is a thin wrapper — its `postinstall` step downloads the matching native binary (`win-x64`, `linux-x64`, `linux-arm64`, `osx-arm64`) from GitHub Releases. **No .NET runtime required.**

> **Use the scoped name `@webnori/codescan-cli`** (with the `@webnori/` prefix). The bare `codescan-cli` is an unrelated, broken squatted package — see [Install troubleshooting](#install-troubleshooting) if you hit issues.

<details>
<summary><b>No Node.js? Prefer a native package manager?</b> &nbsp;— winget · Homebrew · direct installer · build from source</summary>

<br>

**Windows — winget** (no Node.js needed, PATH handled automatically):

```powershell
winget install psmon.CodeScan
```

**macOS — Homebrew** (Apple Silicon / arm64):

```bash
brew install psmon/codescan/codescan
```

**Any OS — direct installer** (no package manager, pin a specific release):

```powershell
# Windows (PowerShell)
iwr https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install-win.ps1 -OutFile install-win.ps1
.\install-win.ps1                       # latest
.\install-win.ps1 -Version 0.8.0        # pinned
```

```bash
# Linux / macOS (bash)
curl -fsSL https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install.sh -o install.sh
sh install.sh                           # latest
sh install.sh --version 0.8.0           # pinned
```

The direct installers download the matching release asset from GitHub, verify SHA256 against `checksums.txt`, install to a user-local path (Win: `~/.codescan/bin`, Unix: `~/.local/bin`), and **never touch user data** under `~/.codescan/{db,logs,config}`.

**Build from source** — see [Build from source](#build-from-source) below.

</details>

### Install troubleshooting

<details>
<summary><b>Common install issues & channel notes</b> &nbsp;— npm name, sudo/EACCES, offline/proxy, Linux arch, winget local test, channel status</summary>

<br>

**⚠ Install the scoped name `@webnori/codescan-cli`.** An unrelated third party squatted the bare `codescan-cli` name on npm before we published. That package is broken (its own ESM/CJS mismatch crashes on `codescan` launch) and has nothing to do with CodeScan. If you installed it by mistake:

```bash
npm uninstall -g codescan-cli
npm install -g @webnori/codescan-cli
```

**`EACCES` / permission errors on Linux without sudo.** System-Node distros root-own npm's global prefix (`/usr/local/lib/node_modules/`), so non-root `npm install -g` fails. Either prefix with `sudo`, switch to **nvm/fnm**-managed Node (no sudo needed), or use the direct installer above.

**Offline / corporate proxy / air-gapped.** If `postinstall` cannot reach GitHub, set `CODESCAN_SKIP_DOWNLOAD=1` during install and grab the binary manually from the [latest release](https://github.com/psmon/CodeScan/releases/latest).

**Linux: x64 vs arm64.** The npm wrapper auto-detects your CPU architecture:

| `process.arch` | Asset downloaded |
|----------------|------------------|
| `x64` | `codescan-linux-x64.tar.gz` |
| `arm64` | `codescan-linux-arm64.tar.gz` |

v1 ships **glibc-based Linux only**. musl/Alpine support is a v2 candidate.

**Testing winget locally on Windows** (before the PR to `microsoft/winget-pkgs` merges). Run once in an elevated PowerShell to opt in to local manifests:

```powershell
winget settings --enable LocalManifestFiles
```

Then install from the in-repo manifest (no admin needed after the opt-in):

```powershell
winget install --manifest packaging\winget\manifests\p\psmon\CodeScan\0.8.0
codescan --version
```

Disable later with `winget settings --disable LocalManifestFiles` (elevated).

**Channel status (v1).** The GitHub Release pipeline is live and produces the binaries every channel pulls from.
- **npm (`@webnori/codescan-cli`)** — package at [`packaging/npm/codescan-cli/`](packaging/npm/codescan-cli/). Always use the scoped name; the bare `codescan-cli` is squatted by an unrelated third party.
- **Homebrew tap** — live at [`psmon/homebrew-codescan`](https://github.com/psmon/homebrew-codescan). `brew tap psmon/codescan && brew install codescan` works today on Apple Silicon Macs.
- **winget** — manifest at [`packaging/winget/manifests/p/psmon/CodeScan/`](packaging/winget/manifests/p/psmon/CodeScan/), pending PR to `microsoft/winget-pkgs`. Until merged, test locally as shown above.

**Why these channels?** npm is universally available across all OSes and serves all four binaries (`linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`) from a single wrapper — hence the primary path. winget gives Windows users a Node-free option; Homebrew is the macOS default (arm64 only in v1; Intel Macs build from source or use Rosetta). **Linux arm64 is a deliberate first-class target** — see [Why AOT? — Edge AI trend](#why-aot--edge-ai-trend-and-the-value-of-a-single-binary) for why arm64 SBC (Raspberry Pi / Jetson / Latte Panda) deployment matters.

</details>

### User data location

| OS | Binary install path | User data |
|----|--------------------|-----------|
| Windows | `%USERPROFILE%\.codescan\bin` (or winget-managed) | `%USERPROFILE%\.codescan\{db,logs,config}` |
| Linux | `~/.local/bin` (or npm-managed) | `~/.codescan/{db,logs,config}` |
| macOS | `$(brew --prefix)/bin` | `~/.codescan/{db,logs,config}` |

User data is preserved across install / upgrade / uninstall.

### Build from source

```bash
git clone https://github.com/psmon/CodeScan.git
cd CodeScan
dotnet build                        # debug build
dotnet publish -c Release           # release publish (single-file)
```

Prerequisites:

- [.NET 10.0 SDK](https://dotnet.microsoft.com/) (for building)
- Git (for blame integration)

Output: `bin/Release/net10.0/<rid>/codescan` (or `codescan.exe` on Windows).

### Developer deploy scripts

For repo developers who want to bypass GitHub Releases and install directly from a local checkout:

- **Windows:** `Script/deploy-win.ps1`
- **Linux:** `Script/deploy-linux.sh`

These do `dotnet publish` + install to `~/.codescan/bin` + register PATH — handy during local development but not the recommended path for users.

### Distribution strategy details

See [`Docs/install-distribution-strategy.md`](Docs/install-distribution-strategy.md) for the v1 confirmed plan (asset naming, signing posture, SBOM, CI flow, channel submission procedures).

## Usage

### Quick Start

```bash
# Scan current directory (register + analyze + display)
codescan scan

# Scan a specific path
codescan scan /path/to/project

# Search across all indexed projects
codescan search "HttpClient"

# Graph search
codescan graph "HttpClient"
codescan search "HttpClient" --graph --depth 2

# Cypher-like graph query
codescan query "MATCH (c:class)-[r:uses_type]->(t:type) WHERE t.label = 'HttpClient'"

# Launch interactive TUI
codescan tui

# Start local GUI viewer
codescan gui start --port 8085
```

### CLI Commands

| Command | Description |
|---------|-------------|
| `scan [path]` | Register and analyze a directory (shortcut for list with defaults) |
| `list <path>` | Scan with custom filtering and output options |
| `search <query>` | Hybrid full-text + git log search |
| `graph [query]` | Search and inspect source knowledge graph |
| `query <graph-query>` | Run the CodeScan Cypher-like graph query subset |
| `cypher <graph-query>` | Alias for `query` |
| `gui start|stop` | Start or stop the local web GUI viewer |
| `projects` | List all registered projects with stats |
| `project <id>` | Show project summary or `--detail` for full view |
| `project-addinfo <id> <text>` | Add an AI-friendly description to a project |
| `project-update <id>` | Update project path or description |
| `project-delete <id>` | Remove a project from the database |
| `tui` | Launch interactive terminal UI |
| `help [command]` | Show help for a specific command |

### Search Options

```bash
# Search methods
codescan search "async" --type method

# Search comments
codescan search "TODO" --type comment

# Search within a specific project
codescan search "config" --project 1

# Search the graph
codescan search "HttpClient" --graph --depth 2
codescan graph "SearchCommand" --project 1

# Treat a search argument as a graph query
codescan search "MATCH (f:file)-[r:imports]->(m:module) LIMIT 20" --query
```

### Graph Query

CodeScan supports a Cypher-like query subset for the graph data it actually stores. It is designed for CLI users, AI agents, and automation scripts that need structured graph retrieval without direct SQL access.

This is not full Cypher. It maps to CodeScan's SQLite-backed source graph and returns a `GraphData` result that CLI, TUI, and GUI can render.

Supported patterns:

```cypher
MATCH (n:kind)
MATCH (a:kind)-[r:edge_kind]->(b:kind)
```

Supported `WHERE` fields:

| Alias Type | Fields |
|------------|--------|
| Node aliases | `kind`, `label`, `path`, `detail` |
| Edge aliases | `kind`, `label` |

Supported operators:

| Operator | Example |
|----------|---------|
| `=` | `t.label = 'HttpClient'` |
| `CONTAINS` | `c.label CONTAINS 'Command'` |
| `STARTS WITH` | `m.label STARTS WITH 'System'` |
| `ENDS WITH` | `f.path ENDS WITH '.cs'` |

Supported clauses:

| Clause | Behavior |
|--------|----------|
| `WHERE ... AND ...` | Filters matched nodes/edges |
| `RETURN ...` | Accepted for readability, ignored by the renderer |
| `LIMIT <n>` | Limits matched seed nodes/edges |

Examples:

```bash
# Find class nodes
codescan query "MATCH (c:class) WHERE c.label CONTAINS 'Service' LIMIT 20"

# Find classes that use a type
codescan query "MATCH (c:class)-[r:uses_type]->(t:type) WHERE t.label = 'HttpClient'"

# Find file imports
codescan query "MATCH (f:file)-[r:imports]->(m:module) WHERE m.label CONTAINS 'System.Net'"

# Find author-to-method relationships and expand one neighbor hop
codescan query "MATCH (a:author)-[r:authored]->(m:method) WHERE a.label CONTAINS 'kim'" --depth 1

# `graph` auto-detects MATCH queries
codescan graph "MATCH (c:class)-[r:creates]->(t:type) LIMIT 30"
```

Common node kinds:

`project`, `directory`, `file`, `class`, `method`, `comment`, `doc`, `author`, `type`, `module`

Common edge kinds:

`contains`, `defines`, `authored`, `has_comment`, `documents`, `imports`, `inherits_or_implements`, `creates`, `uses_type`

### GUI

```bash
# Start on the default port
codescan gui start

# Start on a custom port
codescan gui start --port 8090

# Stop the GUI server
codescan gui stop
```

Open `http://127.0.0.1:8085/` after starting the GUI. The viewer provides keyword search, graph search, Cypher-like graph query, a Neo4jClient-like 2D graph canvas, and a controllable 3D graph view.

GUI graph controls:

| Control | Behavior |
|---------|----------|
| `Keyword` | Run full-text keyword search |
| `Graph Search` | Search graph nodes by keyword and expand neighbors |
| `Query` | Run `MATCH ...` graph query and render the result |
| 2D drag background | Pan the graph |
| 2D mouse wheel | Zoom around the cursor |
| 2D drag node | Reposition a node |
| Node click | Show node detail and visible relationships |
| Edge click | Show relationship detail |
| Legend chips | Toggle node kinds on/off |
| `Fit` | Fit visible nodes into the canvas |
| `Reset Camera` | Reset 2D viewport or 3D camera |
| 3D drag | Orbit camera |
| 3D Shift-drag / right-drag | Pan camera |
| 3D mouse wheel | Zoom camera |

### List Options

```bash
# Tree view with method details
codescan list /path/to/project --detail --tree

# Filter by extension
codescan list /path --include .ts,.tsx

# Limit depth and include git blame
codescan list /path --depth 3 --blame
```

## Data Storage

All data is stored under `~/.codescan/`:

```
~/.codescan/
├── db/
│   └── codescan.db      # SQLite database with FTS5 index
└── logs/
    └── *.log            # Scan logs (--devmode only)
```

### Database Tables

| Table | Contents |
|-------|----------|
| `projects` | Indexed projects with path, scan date, stats |
| `scans` | Scan history per project |
| `files` | File metadata (path, size, extension, depth) |
| `methods` | Class:method definitions with git blame data |
| `comments` | Comment blocks with surrounding code context |
| `project_docs` | Auto-discovered README / AGENT / CLAUDE.md content |
| `search_index` | FTS5 virtual table (trigram tokenizer) |
| `graph_nodes` | Source graph nodes: projects, directories, files, classes, methods, comments, docs, authors |
| `graph_edges` | Source graph relationships: contains, defines, authored, documents, comments, imports, creates, uses_type, inherits_or_implements |

### Graph Edge Rules

Structural edges:

| Edge | Meaning |
|------|---------|
| `project -[contains]-> directory/file` | Project file tree |
| `directory -[contains]-> directory/file` | Directory file tree |
| `file -[contains]-> class` | Class/type found in a source file |
| `class/file -[defines]-> method` | Method/function definition |
| `file -[has_comment]-> comment` | Comment block found in a source file |
| `author -[authored]-> method` | Git blame last-author relationship |
| `project -[documents]-> doc` | Auto-discovered project document |

Dependency hint edges:

| Edge | Source |
|------|--------|
| `file/class -[imports]-> module` | `using`, `import`, `use`, `#include` |
| `class -[inherits_or_implements]-> type` | Base class / interface / trait-style declarations |
| `class -[creates]-> type` | Constructor or constructor-like calls such as `new Type()` |
| `class -[uses_type]-> type` | Type annotations, fields, parameters, returns, or local declarations detected by regex strategy |

The dependency graph is intentionally hybrid. CodeScan first uses language-neutral regex strategies so graph edges exist even when the project cannot be built. It also probes for semantic analysis capability using project metadata:

| Language | Semantic Probe |
|----------|----------------|
| C# | `.sln`, `.csproj` for future Roslyn analyzers |
| Java | `pom.xml`, `build.gradle`, `build.gradle.kts` for future JDT/Spoon analyzers |
| TypeScript/JavaScript | `tsconfig.json`, `jsconfig.json` for future TypeScript Compiler API analyzers |
| Go | `go.mod`, `go.work` for future `go/packages` analyzers |
| Rust | `Cargo.toml` for future rust-analyzer/Cargo metadata analyzers |
| C/C++ | `compile_commands.json` for future Clang LibTooling analyzers |

Current semantic probes detect whether the required project model exists; regex remains the active fallback until a language-specific semantic strategy is added.

## Architecture

```
CodeScan/
├── Program.cs                  # Entry point and CLI routing
├── Commands/                   # Command implementations
├── Models/                     # Data structures (FileEntry, MethodEntry, CommentBlock, SourceDependency)
├── Services/                   # Core logic
│   ├── DirectoryScanner.cs     #   Recursive traversal with filtering
│   ├── SourceAnalyzer.cs       #   Multi-language class/method extraction
│   ├── SourceGraphAnalyzer.cs  #   Hybrid dependency edge extraction
│   ├── CommentExtractor.cs     #   Comment extraction with context
│   ├── GitBlameService.cs      #   Git blame per method
│   ├── GitLogSearchService.cs  #   Hybrid git log search
│   ├── GraphQuery.cs           #   Cypher-like MATCH query parser
│   ├── GraphModels.cs          #   Source graph DTOs
│   ├── SqliteStore.cs          #   SQLite DB with FTS5 full-text search
│   └── TreeFormatter.cs        #   Tree/flat output formatting
├── Tui/
│   └── TuiApp.cs               # Terminal.Gui v2 interactive UI
└── Script/                     # Deployment scripts (Windows/Linux)
```

## Dependencies

| Package | Purpose |
|---------|---------|
| [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite) | Embedded SQLite with FTS5 support |
| [Terminal.Gui v2](https://github.com/gui-cs/Terminal.Gui) | Cross-platform terminal UI framework |

## Design Highlights

- **Centralized storage** — All data under `~/.codescan/` regardless of where the tool is run
- **Recent-first sorting** — Files and directories sorted by modification time (newest first)
- **Smart defaults** — `.git`, `node_modules`, `bin`, `obj`, `dist`, `build`, `__pycache__` excluded automatically
- **Markdown always included** — `.md` files are always indexed even when `--include` filters are active
- **Git root detection** — Walks directory tree to find `.git/` without spawning subprocesses
- **Trigram FTS** — Enables effective substring search for CJK languages (Korean, Chinese, Japanese)
- **Regex-first graphing** — Produces dependency graph hints without requiring a successful build
- **Semantic-ready strategy layer** — Language-specific compiler analyzers can be added behind `ISourceDependencyStrategy`

## Why AOT? — Edge AI trend and the value of a single binary

> **TL;DR** — Through 2026 and into 2027, AI infrastructure is shifting from cloud-hosted frontier models toward *on-device SLMs (Small Language Models)* and *edge agents*. CodeScan is a .NET 10 Native AOT build — a **single binary with no runtime dependency** — designed to ride that wave: the same artifact runs on a developer laptop, a Raspberry Pi, or a drone-grade SBC.

### 2026: the year SLMs actually started running on the edge

Up to 2025, "edge LLM" was mostly demoware and benchmark posts. 2026 is when that changed:

- **Google Gemma 3 / 3n / 3 270M** — Gemma 3 has been measured at **14.5 tok/s on a Raspberry Pi** and survived a **12-hour Jetson run** with no memory leak or slowdown. The 270M variant uses just **0.75% of a Pixel 9 Pro battery for 25 conversations** thanks to INT4 quantization and Per-Layer Embedding (PLE) caching — small enough to land naturally on everyday devices. ([Gemma 3 270M announcement](https://developers.googleblog.com/en/introducing-gemma-3-270m/), [Gemma 3n overview](https://ai.google.dev/gemma/docs/gemma-3n))
- **NVIDIA Nemotron 3 Nano (4B and 30B-A3B)** — A hybrid Mixture-of-Experts design: 30B total parameters but only **3B active per forward pass**. The 4B variant, quantized to 4-bit, fits under **3 GB of VRAM** and runs on consumer RTX cards and Jetson-class edge boards. NVIDIA claims **9× throughput** over comparable open models. ([Nemotron 3 Nano Omni announcement](https://developer.nvidia.com/blog/nvidia-nemotron-3-nano-omni-powers-multimodal-agent-reasoning-in-a-single-efficient-open-model/), [Nemotron 3 Nano 4B hybrid architecture](https://news.skrew.ai/nvidia-nemotron-3-nano-4b-hybrid-architecture-edge-ai/))
- **3B parameters as the 2026 sweet spot** — With production-grade 3–8 bit quantization and a fresh wave of small NPUs landing on single-board computers, the community has converged on **~3B parameters as the practical sweet spot for SBC inference**. ([The Small Model Revolution 2026](https://dev.to/linou518/the-small-model-revolution-2026-3b-parameters-on-raspberry-pi-edge-ais-new-sweet-spot-3pp4))

### 2027 prediction: small devices ship with an LLM *by default*

Extrapolating the curve, the following is likely to be the 2027 baseline:

- **Drones** — Whisper-class speech recognition + a 3B-class SLM handles autonomous mission parsing and replanning without GPS — moving from academic demos to production payloads.
- **Raspberry Pi 5 + AI HAT+ 2** — The Hailo-10H accelerator (**40 TOPS INT4**) and 8 GB LPDDR4X turn an SBC into a real LLM host. ([Raspberry Pi AI HAT+ 2 release — The Register, Jan 2026](https://www.theregister.com/2026/01/15/pi_5_ai_hat_2/))
- **x86/ARM SBCs (Latte Panda, Khadas Edge, Orange Pi, …)** — Sitting next to PLCs on the factory floor, local SLMs handle log triage, anomaly detection, and natural-language operator UIs.
- **Laptops and tablets** — NPU-equipped SoCs (Apple Silicon, Snapdragon X, AMD Strix Halo) make 4B-class on-device inference an **OS-level default**. Samsung, Google, and Motorola's 2026 flagships already ship support for 4B models at Q4 quantization. ([2026 SLM comparison: Phi-4 vs Gemma 3 vs Qwen](https://aegisai.in/best-small-language-models-for-edge-devices-2026-slm-comparison-phi-4-gemma-3-qw/))

All of these targets share the same **structural constraints**:

| Constraint | Implication |
|------------|-------------|
| **No runtime present** — drone firmware and SBC minimal images rarely carry a .NET / Java / Python runtime, and adding one is expensive | A single self-contained binary is effectively a requirement |
| **Memory and storage pressure** — the model already owns most of the RAM; surrounding tools must be small | AOT trimming and single-file compression matter |
| **Cold-start cost** — battery-powered and event-triggered workloads must respond immediately | "No JIT warmup" is a decisive advantage |
| **Supply-chain trust** — edge updates are infrequent, so the integrity of the artifact you ship matters more | Single file + SHA256 + SBOM is a natural fit |

### Where Native AOT single-binary fits

CodeScan's build shape lines up with each of those constraints:

- **Instant startup (no JIT)** — Decisive when an edge agent must respond within ~50 ms of a voice trigger. ([Native AOT deployment overview — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/))
- **Runtime-free single file** — Copy a single `~/.codescan/bin/codescan` and it runs on a Raspberry Pi with no .NET installed.
- **Smaller memory footprint** — AOT drops the JIT, its metadata, and unreachable runtime services, leaving more RAM for the model.
- **Reduced attack surface** — Dynamic code generation and most reflection paths are stripped; pairing a single file with an SBOM is friendly to supply-chain audit.
- **First-class multi-arch** — From v1 the same pipeline publishes `linux-x64`, `linux-arm64`, `osx-arm64`, and `win-x64` as peer artifacts. SBC deployment needs no separate build procedure.

### CodeScan's role in an edge-AI workflow

CodeScan does not host an LLM itself. It is the **indexing and retrieval layer** an agent needs whenever it has to interact with a codebase:

- An FTS5 + graph backend that lets a code-aware agent (e.g., Gemma 3 4B with tool-use) on an SBC sweep a local repository quickly.
- A Cypher-like graph-query surface that an autonomous build/deploy bot can use to reason about change impact.
- A RAG-lite component for drone or robot SDK repos — analyze offline, then feed code context into the SLM.

#### Companion project: AgentZeroLite

The "agent" half of the picture is being built in parallel as a sibling research project — [**psmon/AgentZeroLite**](https://github.com/psmon/AgentZeroLite). AgentZeroLite focuses on **running and evaluating on-device SLMs** (the Gemma 3 / Nemotron Nano-class models discussed above) on real consumer-grade hardware, while CodeScan acts as the code-aware retrieval layer those agents call into. The two are designed to compose:

- **AgentZeroLite** — hosts the on-device model, manages prompting, tool-use, and evaluation loops for edge inference scenarios.
- **CodeScan** — answers "what's in this codebase?" with FTS5 keyword hits, the source graph, and Cypher-like queries — the kind of structured context an SLM needs to do useful code work without a frontier model.

If you want to see how this plays out end-to-end (on-device SLM ↔ structured code retrieval), AgentZeroLite is the natural next stop.

In short — **as small models start doing real work on the edge, the *tooling* around those models has to be small, instant, and runtime-free too**. A Native AOT single binary is the most direct answer to that requirement, and CodeScan is built along that line.

> For the full build/distribution spec, see [`Docs/install-distribution-strategy.md`](Docs/install-distribution-strategy.md).

## Blumn Ai Studio

<p align="left">
  <img src="Home/img/blumn-ai.png" alt="blumn ai studio" width="300" />
</p>

**Blumn AI Studio**

An R&D Studio researching next-generation AI agents and real-time edge AI infrastructure technologies at Blumn AI.
This repository is an open-source project that publishes part of those research and development activities.

🌐 https://blumn.ai/
📝 Tech Blog · https://blumnai-studio.github.io/tech-writing-harness/

