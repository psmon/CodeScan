# BUILDER — regenerate index manifests

> Ops mode. Refresh the data the viewer reads. Do not touch the viewer code.

## Purpose

`Home/harness-view/` reads two kinds of data:
- **Manifests** under `Home/harness-view/indexes/*.json` — build output, a snapshot of `harness/` + `Docs/` resources.
- **Upstream sources** under `harness/**.md` / `Docs/**.md` — fetched directly by the viewer via `../../<rel>`. No mirror, single source of truth.

When a `.md` file under `harness/{docs,agents,knowledge,logs,engine}` or `Docs/` is added/removed, the manifest must be regenerated so the menu cards show the change.

## Procedure

### 1. Run the build script

```bash
node Home/harness-view/scripts/build-indexes.js
```

The script scans the resource directories and emits these manifests:

| Manifest | Scans | Used by |
|----------|-------|---------|
| `harness-docs.json` | `harness/docs/*.md` | Dashboard "Build Log" (semver-desc cards) |
| `harness-agents.json` | `harness/agents/*.md` | Roles menu (card + spec card detail) |
| `claude-skills.json` | `.claude/skills/<skill>/SKILL.md` | Skills menu — body embedded inline |
| `harness-knowledge.json` | `harness/knowledge/**/*.md` (recursive) | Knowledge → Expert Knowledge tab |
| `document-tree.json` | `Docs/**/*.md` (tree) | Knowledge → Tech / Domain (TECH-DOC) tab |
| `harness-logs.json` | `harness/logs/<subfolder>/*.md` | Activity Log — subfolder = dynamic category |
| `design-index.json` | `Docs/design/*.md`, `*.pen` | Product Design menu (none yet in CodeScan; empty manifest is OK) |
| `harness-engine.json` | `harness/engine/*.md` | (reference — Workflow graph is in `data/workflow-graph.json`) |
| `claude-tips.json` | `CLI-TIPS.md` (placeholder if absent) | (currently unused) |

### 1-A. Static data is not part of the build

These are hand-edited and **not** rebuilt:

- `Home/harness-view/data/news.json` — Dashboard "Recent Updates"
- `Home/harness-view/data/pdsa-insight.json` — Dashboard "PDSA Learning". **No auto trigger** — the operator regenerates it explicitly when meaningful activity has accumulated (release gate / milestone).
- `Home/harness-view/data/workflow-graph.json` — Workflow menu mermaid graphs

These belong to areas where the content is hand-curated, so they stay out of the indexer.

### 1-B. Contributors + commit-stats + top-files (auto)

`harness-docs.json` computes the following from git log at build time (scope = `harness/`, `Docs/`, `Home/harness-view/` union, dedup by SHA):

- `contributorsAll` / `contributorsRecent` — per-author breakdown
- `commitStats.{totalAllTime, last7d, last30d}` — velocity cards
- `topChangedFiles` — top 5 most-changed files in the last 30 days

So the Dashboard activity numbers stay fresh on every build with no manual step.

### 2. Verify the counts

The script prints per-manifest counts:

```
✓ harness-docs.json       (N)
✓ harness-agents.json     (N)
✓ claude-skills.json      (N)
✓ harness-knowledge.json  (N)
✓ document-tree.json      (N)
✓ harness-logs.json       (N)
✓ harness-engine.json     (N)
✓ design-index.json       (N)
✓ claude-tips.json        (N)
✓ _meta.json  (trigger=manual, ~XXXms)
done.
```

Confirm any newly added file is reflected in the matching count.

### 3. (Optional) Local dev server

To preview the change locally:

```bash
node Home/harness-view/scripts/serve.js
```

URL: `http://127.0.0.1:8765/Home/harness-view/`

`serve.js` runs a preflight that auto-builds the manifest if a source file is newer than the last build — so just running `serve.js` is usually enough.

### 4. GitHub Pages publish

Pages is not wired yet in CodeScan. When the team wires `pages.yml`, the canonical convention to mirror from AgentZeroLite is:
- Trigger on `doc-v*` tag push only — keeps routine `harness/logs` additions from redeploying.
- CI runs `node Home/harness-view/scripts/build-indexes.js` and uploads the whole repo as the Pages artifact (so the viewer can fetch `../../harness/**` and `../../Docs/**` at runtime).

Until Pages is wired, the viewer is local-only — `node Home/harness-view/scripts/serve.js` and a browser at `127.0.0.1:8765`.

## Reporting to the user

A short summary line with the changed counts:

> harness-view 인덱스 9개 재생성 완료. Build Log 2 → 3개로 증가 (v1.2.0.md 반영).
> 로컬 미리보기: http://127.0.0.1:8765/Home/harness-view/

## Related files

- Builder script: `Home/harness-view/scripts/build-indexes.js`
- Manifest output: `Home/harness-view/indexes/*.json`
- Scanned resource directories: `harness/{docs,agents,knowledge,logs,engine}`, `Docs/`, `.claude/skills/`, `Docs/design/`

## Cautions

- **The viewer fetches `.md` bodies at runtime**, so a body-only edit needs no rebuild — just save the `.md` and reload the page.
- **Skills (`.claude/skills/<skill>/SKILL.md`) are embedded inline** in `claude-skills.json`, so a body edit there does require a rebuild.
- The build itself takes <1 second; Pages publish (when wired) is ~30–60 seconds.
- Commit-message convention for content changes: `docs(<scope>): ...` or `feat(harness): ...`.
