# CodeScan — AGENTS.md

> **This file is a pointer, not the source of truth.**
> Canonical project guidance lives in **[CLAUDE.md](CLAUDE.md)** — read that first.
> This stub exists so agents that look for `AGENTS.md` by convention land in the
> right place, and to describe the **harness layer** that sits alongside the code.

CodeScan is a CLI / TUI / GUI code scanner (C# / .NET 10, Native AOT single
binary) that analyzes source at the class:method level with git blame, indexes
into a local SQLite DB (FTS5 full-text + an **incrementally reconciled** source
knowledge graph), and exposes command-line, terminal, and local web interfaces.

## Start here

| Topic | Document |
| --- | --- |
| Architecture, build & test, CLI commands, design decisions | **[CLAUDE.md](CLAUDE.md)** — canonical |
| Features & usage (EN / KO) | [README.md](README.md) / [README-KO.md](README-KO.md) |
| Graph reconcile, weights, variable-hop queries, code↔doc `mentions` | [harness/knowledge/graph-reconciliation.md](harness/knowledge/graph-reconciliation.md) |
| Per-language build-artifact harvest | [.claude/skills/testsample-build/SKILL.md](.claude/skills/testsample-build/SKILL.md) |
| Legacy redirect stub | [AGENT.md](AGENT.md) (singular — same purpose as this file) |

---

## Harness layer

This repo carries a **harness** (하네스, "정원"/garden): a layered set of
domain knowledge, specialist agent definitions, and workflow engines that drive
quality and structure review. The harness is **data in this repo**; the runtime
that executes it is an **external Claude Code plugin**.

```
harness/
├── harness.config.json   # manifest — name, version, agent/engine roster
├── knowledge/            # Layer 1 — domain knowledge (read by agents)
├── agents/               # Layer 2 — specialist agent definitions
├── engine/               # Layer 3 — multi-agent workflow definitions
├── docs/                 # harness changelog (v1.1.0 … v1.6.0)
└── logs/                 # per-agent / per-engine run records
```

### Manifest

`harness/harness.config.json` is the single source of truth for what the harness
contains. Current: **CodeScan Harness v1.6.1** (`$schema: kakashi-harness`,
`$schemaVersion: 1.0.0`).

### Runtime — plugin, not repo code

The harness is executed by the **`harness-kakashi` plugin**, installed at the
user/marketplace level (it is *not* vendored under `.claude/` here). It exposes:

| Command | Role |
| --- | --- |
| `/harness-kakashi-creator` | Unified entry — 개선부 (author/improve the harness) + 수행부 (run agents & engines) |
| `/harness-build` | Harness builder — design agents, build knowledge, define engines, validate structure |
| `/harness-chakra-kakashi` | Token/prompt efficiency audit (차크라 감사), typically after an engine run |

These do **not** auto-trigger; invoke them explicitly. Calling it "카카시 하네스"
means running `/harness-kakashi-creator`.

### Agents (Layer 2)

Markdown files under `harness/agents/`, YAML frontmatter: `name`, `persona`,
`triggers[]`, `description`.

| Agent | Focus |
| --- | --- |
| `tamer` | 정원지기 — meta-agent that tends the harness itself (built-in) |
| `regex-safety-guard` | `[GeneratedRegex]` correctness & AOT-safe regex patterns |
| `aot-compatibility-scout` | Native AOT trim/reflection compatibility |
| `test-sentinel` | Language × case test-matrix coverage |
| `language-analyzer-keeper` | `SourceAnalyzer` / `CommentExtractor` parser rules per language |
| `actor-toolkit-scout` | Cross-toolkit actor-model analysis |
| `semantic-bridge-architect` | Docker-based semantic analysis bridge |
| `graph-curator` | Knowledge-graph curation (`curated=1` rows, edge hygiene) |
| `web-gui-design-reviewer` | CodeScan View (web GUI) design review |

### Engines (Layer 3)

Markdown workflows under `harness/engine/`, frontmatter adds `type: engine` and
`participants[]` (the agents the workflow orchestrates).

| Engine | Participants |
| --- | --- |
| `test-audit` | `test-sentinel`, `regex-safety-guard`, `aot-compatibility-scout` |
| `actor-graph-extension` | actor-model graph extension workflow |
| `gui-design-review` | `web-gui-design-reviewer` |

### Knowledge (Layer 1)

`harness/knowledge/` holds the domain documents agents read before acting —
`graph-reconciliation.md`, `aot-rules.md`, `regex-patterns.md`,
`language-analyzer-patterns.md`, `doc-code-linkage.md`,
`graph-curation-guide.md`, `actor-model-cross-toolkit.md`,
`semantic-analyzer-docker.md`, `web-gui-design-craft.md`, and
`design/codescan-gui-view.pen` (Pencil design file — open via the `pencil` MCP
tools only, never `Read`/`grep`).

### Logs

`harness/logs/<agent-or-engine>/` — dated run records, plus
`harness/logs/observations/` for standalone findings. Append new records; do not
rewrite history.

---

## Project skills (`.claude/skills/`)

Repo-local skills any Claude Code agent in this workspace can invoke:

| Skill | Use |
| --- | --- |
| `codescan-analysis` | Drive the CodeScan CLI + git for search / graph / history analysis — **prefer this over blind file reads** |
| `harness-view-build` | Build / extend the harness viewer under `Home/harness-view/` |
| `testsample-build` | Pre-build `TestSample/<lang>` projects for static-analysis harvesting (explicit request only) |
| `playwright-e2e` | Browser e2e / UI automation (default channel: Microsoft Edge) |

## Ground rules for agents

- **`CLAUDE.md` wins.** If this file and `CLAUDE.md` disagree, follow `CLAUDE.md`.
- **Keep this file a stub.** Add substantive guidance to `CLAUDE.md` or to
  `harness/knowledge/`, and link it here — do not duplicate content.
- **Harness edits go through the plugin.** Add or change agents/engines/knowledge
  via `/harness-build` or `/harness-kakashi-creator` so the manifest, docs, and
  logs stay in sync — don't hand-edit `harness.config.json` in isolation.
- **All regexes use `[GeneratedRegex]`** — required for Native AOT.
- **Runtime data lives in `~/.codescan/`**, never in the working directory
  (see `AppPaths.cs`).
