---
name: harness-view-build
description: |
  CodeScan Dev harness viewer (Home/harness-view/) partial-update build skill.
  When content is added or modified, only the index manifests are regenerated
  so the UI reflects the change immediately.

  Three modes:
   1) BUILDER mode — regenerate index manifests (content updates)
   2) CREATOR mode — author or improve the viewer code itself (HTML/CSS/JS)
   3) DEV SERVER mode — run the local dev server (127.0.0.1:8765, no auth)

  Dispatch by user utterance.

  BUILDER mode triggers (ops — content refresh):
  - "harness viewer 갱신", "harness-view 갱신", "harness-view 빌드"
  - "viewer index 재생성", "harness viewer refresh"
  - After adding/removing/modifying anything under
    harness/{docs,agents,knowledge,logs,engine} or Docs/, when the user
    mentions "viewer 반영" / "reflect in viewer".

  CREATOR mode triggers (dev — touch the viewer itself):
  - "harness-view에 OOO 추가", "harness-view OOO 화면 만들어줘"
  - "harness-view OOO 개선", "harness-view 디자인 변경"
  - Any request that requires editing Home/harness-view/{css,js,index.html}.

  DEV SERVER mode triggers (verify — boot local server):
  - "harness-view dev server 띄워", "harness-view 로컬 서버"
  - "harness-view serve", "harness-view 로컬 테스트 준비"
  - Whenever Playwright verification needs a local server up first.

allowed-tools: Bash, Read, Write, Edit, Glob, Grep
---

# harness-view-build — partial-update build skill (CodeScan variant)

This skill has three responsibilities. Read the matched reference file with
the Read tool and follow its procedure, except for DEV SERVER which runs
the inline procedure below.

> **Identity of this variant**
> Adapted from the sibling AgentZeroLite skill. Key differences:
> - **Site title**: **`CodeScan Dev`** (sidebar logo + page title)
> - **Language**: English-default UI and content (CodeScan README is
>   English-default with a Korean mirror; the viewer follows the same
>   convention). User conversation can be Korean.
> - **Menu scope**: trimmed — no on-device-model views (those live in the
>   sibling AgentZeroLite project), no missions, no design (until the
>   matching content exists in this repo).
> - **GitHub Pages publish**: not yet wired (no `pages.yml`). Add when
>   the team is ready to publish the viewer. The publish gate convention
>   to follow when wired: `doc-v*` tag push only (so harness/logs noise
>   does not trigger a redeploy).
> - **`.nojekyll`** required at repo root **and** `Home/` once Pages is on.
> - **No external absolute-path references** — every data source is in-repo.

## Data-source mapping (canonical for this project)

| Surface | Source path | Notes |
|---|---|---|
| Dashboard "Build Log" | `harness/docs/*.md` | semver-desc sort (vN.N.N cards). UI labels in `dashboard.js` `T` dict. |
| Dashboard "Recent Updates" | `Home/harness-view/data/news.json` | **Static, bilingual.** `headline / narrative / highlights[].label,text / prompts[].tag,text` are `{ en, ko }` objects. Hand-edited. |
| Dashboard "PDSA Learning" | `Home/harness-view/data/pdsa-insight.json` | **Static, bilingual.** Same `{ en, ko }` shape. Regenerated **only when the operator explicitly asks** (after a release gate / milestone). Never auto-refreshed by routine work. |
| Dashboard "Contributors" | git log on `harness/`, `Docs/`, `Home/harness-view/` (union, dedup by SHA) | **Auto.** Recomputed every build. Language-neutral data; labels in `dashboard.js` `T` dict. |
| Workflow | `Home/harness-view/data/workflow-graph.json` | **Static.** Pre-built mermaid graphs of CodeScan workflows (scan / search / graph query / TUI / GUI / release / install channels). |
| Roles | `harness/agents/*.md` | Dynamic. Detail view renders a spec card from frontmatter. |
| Skills | `.claude/skills/<skill>/SKILL.md` | In-repo snapshot of installed skills. SKILL.md body embedded inline in the manifest. |
| Knowledge → Expert | `harness/knowledge/**/*.md` (recursive) | Dynamic. |
| Knowledge → Tech / Domain | `Docs/**/*.md` | Dynamic tree. |
| Activity Log | `harness/logs/<subfolder>/*.md` | Dynamic. **Subfolder name = category tag** — no hardcoded list. |
| Product Intro | `Home/index.html` | iframe-embedded inside the harness viewer. |

## Mode dispatch

| User intent | Mode | Reference |
|---|---|---|
| New `.md` added → reflect in viewer | **BUILDER** | `references/builder.md` |
| Routine ops refresh / manifest rebuild | **BUILDER** | `references/builder.md` |
| New menu / new view | **CREATOR** | `references/creator.md` |
| Viewer component / design improvement | **CREATOR** | `references/creator.md` |
| Bug fix that needs code changes | **CREATOR** | `references/creator.md` |
| Boot local dev server (Playwright prep) | **DEV SERVER** | inline below |
| Both content and code change required | **CREATOR → BUILDER** in sequence |

If the dispatch is ambiguous, ask the user in one line.

## DEV SERVER mode — boot the local dev server (inline)

When the user says "harness-view dev server 띄워" or similar, do not load a
reference file — **execute the procedure below directly**.

### Procedure

1. **Run via Bash with `run_in_background: true`** (the server is long-lived):
   ```
   node Home/harness-view/scripts/serve.js
   ```
   - Custom port: `node Home/harness-view/scripts/serve.js 9000`
   - Skip preflight auto-build: `node Home/harness-view/scripts/serve.js --no-build`

2. **Preflight log** — `serve.js` decides whether to rebuild manifests:
   - `[preflight] rebuild — ...` : sources newer → auto-runs `build-indexes.js` first
   - `[preflight] up-to-date — ...` : already fresh, skips
   - `[preflight] skipped — --no-build flag`

3. **Boot OK** when the Bash output shows:
   ```
   CodeScan Dev Harness — local server (no auth)
   URL : http://127.0.0.1:8765/Home/harness-view/
   ```

4. **Already running** (`EADDRINUSE`) — do not boot again, just print the URL.

5. **One-line report** to the user: URL + preflight outcome + how to stop.

### Playwright verification handshake

After the server is up, Playwright steps follow:
- `mcp__playwright__browser_navigate` → `http://127.0.0.1:8765/Home/harness-view/#<menu>`
- `mcp__playwright__browser_take_screenshot filename="tmp/playwright/<name>.png"` (gitignored path)
- Production URL (once GitHub Pages is on) is verified by the user post-push.

## Execution pattern

1. **Decide mode** from the table above.
2. **Load reference** — `Read .claude/skills/harness-view-build/references/<mode>.md`
3. **Follow the procedure** in that reference exactly.
4. **Report what changed** — file paths + URL + build state.
5. **Do not git commit / push unless the user asks.** Pages publishing
   (when it lands) will be `doc-v*` tag gated, so routine pushes will not
   redeploy.

## Context — CodeScan Dev harness viewer

`Home/harness-view/` is a dev-facing static site inside the CodeScan repo.
- Vanilla HTML + ES Modules + CDN libraries (marked, mermaid, lucide). No bundler.
- Two data modes: **resource-reference** (dynamic manifests under `indexes/`)
  and **pre-baked** (static JSON under `data/`).
- Default language English; Korean visible via the bilingual toggle on
  surfaces that support it (Dashboard, PDSA, news). Toggle lives in
  `Home/harness-view/js/components/bilingual.js` — import its
  `getLang / makeLangToggle / t` rather than re-implementing.

Detailed directory structure / routing / component patterns: `references/creator.md`.
Build commands: `references/builder.md`.

## Constraints

- Output text is **EN-default**; bilingual surfaces carry both languages
  in `{ en, ko }` objects. Hardcoded UI labels live in each view's `T` dict.
- User conversation can be Korean.
- **No build/bundle tooling** added — vanilla HTML + ES Modules + CDN only.
- **No external absolute-path references** — every data source is in-repo.
- This skill does **not** push or run any GitHub API calls (user-driven).
