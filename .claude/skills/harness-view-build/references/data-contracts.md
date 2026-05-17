# data-contracts — what `build-indexes.js` expects

Other skills (and the operator) need to know what file shape the indexer
accepts. This file is the single source of truth for those expectations.

## Markdown frontmatter

Most resources use YAML frontmatter at the top of the `.md`. The indexer
reads three optional fields:

```yaml
---
name: my-agent                # slug, falls back to filename
description: one-line summary # used in the card subtitle
triggers:                     # bullet list of natural-language utterances
  - "..."
  - "..."
---
```

Bodies are not parsed — the viewer fetches them at runtime via marked.

## Per-resource expectations

### `harness/docs/*.md` — Build Log

- Filename `v<major>.<minor>.<patch>.md` enables semver-desc sort.
  Other filenames still show up but in lexicographic order.
- First H1 becomes the card title (a leading `v<version> — ` prefix is
  stripped).

### `harness/agents/*.md` — Roles

- Frontmatter `name` / `description` / `triggers` used for the spec card.
- Body rendered with marked + mermaid.

### `.claude/skills/<skill>/SKILL.md` — Skills

- Frontmatter `name`, `description`, `allowed-tools` (string or array)
  used for the spec card.
- Body embedded inline in `claude-skills.json` — a body edit requires
  rerunning the build.

### `harness/knowledge/**/*.md` — Expert Knowledge

- Recursive walk. Frontmatter optional.
- Directory becomes the tag.

### `harness/logs/<subfolder>/*.md` — Activity Log

- **Subfolder name = category tag.** No hardcoded list; whatever
  subfolders exist become available tags.
- Filename convention `yyyy-MM-dd-HH-mm-<title>.md` is parsed for the
  date / time / kebab title.

### `Docs/**/*.md` — Tech / Domain (TECH-DOC)

- Recursive tree, used by Knowledge → TECH-DOC tab.

### `harness/engine/*.md` — engine reference

- Indexed for reference. The Workflow surface itself reads
  `data/workflow-graph.json`, not this manifest.

## Static (hand-curated) JSON

Never built — hand-edited:

- `Home/harness-view/data/news.json` — Dashboard Recent Updates.
  Schema: `headline / narrative / highlights[].label,text /
  prompts[].tag,text` all carry `{ en, ko }`.
- `Home/harness-view/data/pdsa-insight.json` — Dashboard PDSA Learning.
  Schema: `sources[]` (with `title: { en, ko }`), `tried[]`, `solved[]`,
  `remaining[]`, `learned.{lead, body}` all carry `{ en, ko }`.
- `Home/harness-view/data/workflow-graph.json` — Workflow menu.
  Schema: `workflows[].{id, label, description, mermaid, sourceMermaid}`.

When editing these by hand, **fill both `en` and `ko`** if the surface
shows up in the bilingual toggle. The renderer falls back to `en` if `ko`
is missing, but a missing `en` means an empty card.
