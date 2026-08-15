# CodeScan — AGENT.md

> ⚠️ **This file is no longer maintained.** The canonical, up-to-date
> documentation for agents and developers lives in **[CLAUDE.md](CLAUDE.md)** —
> start there. This stub only redirects so older links keep working.

CodeScan is a CLI / TUI / GUI code scanner (C# / .NET 10, Native AOT single
binary) that analyzes source at the class:method level with git blame, indexes
into a local SQLite DB (FTS5 full-text + an **incrementally reconciled** source
knowledge graph), and exposes command-line, terminal, and local web interfaces.

For everything — architecture, build & test, CLI commands, the graph model
(reconciliation, weights, variable-hop queries, code↔doc `mentions`), and design
decisions — see:

- **[CLAUDE.md](CLAUDE.md)** — project guidance (canonical source of truth)
- **[README.md](README.md)** / **[README-KO.md](README-KO.md)** — features & usage
- **[harness/knowledge/graph-reconciliation.md](harness/knowledge/graph-reconciliation.md)** — graph reconcile & query model
- **[.claude/skills/testsample-build/SKILL.md](.claude/skills/testsample-build/SKILL.md)** — per-language build-artifact harvest
