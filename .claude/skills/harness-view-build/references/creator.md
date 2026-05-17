# CREATOR — author / improve the harness view itself

> Dev mode. Edit the viewer code (HTML/CSS/JS).
> Index refresh is BUILDER mode — not handled here.

## Design philosophy

### Core principles

1. **No bundler** — vanilla HTML + ES Modules + CDN libraries only
   (marked, mermaid, lucide). No build step.
2. **One file per menu** — `js/views/<menu>.js`. Keep view files focused;
   shared logic moves into `js/components/`.
3. **Two data modes**
   - **Resource-reference**: dynamic from `indexes/*.json` manifests (built
     by `scripts/build-indexes.js`). Content changes are reflected by
     rerunning the build, not by code edits.
   - **Pre-baked**: hand-written JSON under `data/*.json` for content that
     requires curation (news, PDSA, workflow graphs).
4. **Bilingual surfaces share `bilingual.js`** — never reimplement EN/KO
   toggling; import `getLang / makeLangToggle / t` from
   `js/components/bilingual.js`.
5. **Markdown bodies are fetched at runtime** — the viewer reads
   `../../<rel>` directly from `harness/` / `Docs/`. Body-only edits
   never need a rebuild.

## Directory layout

```
Home/harness-view/
├── index.html                       # shell: sidebar + topbar + view + modal-root
├── css/
│   ├── main.css                     # layout (sidebar, topbar, subbar, modal)
│   ├── components.css               # cards, tabs, pills, boards, trees, spec-*
│   └── md.css                       # markdown render + mermaid blocks
├── js/
│   ├── app.js                       # hash router + marked/mermaid boot + sidebar
│   ├── config/menu.js               # MENU array + inline SVG ICONS
│   ├── utils/
│   │   ├── loader.js                # fetchText / fetchJson / loadIndex / loadData / loadMd
│   │   └── dom.js                   # h(tag, props, children) / mount / clear / humanize
│   ├── components/
│   │   ├── md-viewer.js             # marked + mermaid + show/edit toggle
│   │   ├── pen-renderer.js          # .pen JSON → DOM (no Docs/design yet)
│   │   ├── pen-viewer.js            # .pen modal
│   │   ├── spec-card.js             # Roles/Skills frontmatter → spec card
│   │   └── bilingual.js             # EN/KO toggle (getLang / makeLangToggle / t)
│   └── views/
│       ├── _common.js               # renderTopBar / renderSubBar / emptyState
│       ├── dashboard.js             # Recent Updates / PDSA / Build Log / Contributors
│       ├── workflow.js              # mermaid graphs from data/workflow-graph.json
│       ├── role.js                  # harness/agents/ → cards + spec card detail
│       ├── skill.js                 # .claude/skills/ → cards + body
│       ├── knowledge.js             # harness/knowledge + Docs tree (2 tabs)
│       ├── usage-log.js             # harness/logs/<subfolder>/ → board
│       ├── design.js                # Docs/design/*.pen (none yet — empty state OK)
│       ├── pen-viewer.js
│       └── product-intro.js         # iframes Home/index.html
├── scripts/
│   ├── build-indexes.js             # BUILDER entry — emits indexes/*.json
│   └── serve.js                     # DEV SERVER entry — preflight + 127.0.0.1:8765
├── data/                            # hand-curated JSON (news, pdsa-insight, workflow-graph)
└── indexes/                         # build output (gitignored is fine; CI regenerates)
```

## Look & feel

- Default to the existing CSS (`main.css` / `components.css` / `md.css`).
  Component CSS reuses card / pill / badge primitives — extend those
  rather than introducing new visual patterns.
- Font stack: system + Caveat + Gaegu (decorative). Don't add new font
  imports.
- Color tokens defined as CSS variables at the top of `main.css`.
  Reach for the existing tokens before defining new ones.

## Routing

- Hash-based — `#dashboard`, `#workflow`, `#role`, `#role/<agent>`, etc.
- `app.js` `route()` extracts `id` + remainder, dynamic-imports
  `views/<id>.js`, calls `mod.render({ viewEl, topbarEl, subbarEl, menu, params })`.
- Mermaid re-runs after every render.

## Adding a new menu

1. Add an entry to `MENU` in `js/config/menu.js` (and a corresponding icon
   in `ICONS` if it's a new glyph).
2. Create `js/views/<id>.js` exporting `async function render(ctx)`.
3. Reuse `renderTopBar` from `js/views/_common.js` for the header.
4. If the data comes from a manifest, run `node Home/harness-view/scripts/build-indexes.js`
   once to seed it and confirm `indexes/<your-manifest>.json` has content.
5. Smoke-test locally via DEV SERVER mode.

## Adding a new pre-baked surface

1. Add a JSON file under `data/`. Use `{ en, ko }` for any human-written
   string so the bilingual toggle works.
2. Render in a view via `loadData('data/<file>.json')` (from `utils/loader.js`).
3. Hand-edit the JSON when content needs to change — no build step.

## Common pitfalls

- **Don't introduce a bundler / npm dependency** — the dev story is "open
  index.html in a browser; no install".
- **Don't hardcode absolute paths** — every viewer fetch is repo-relative
  (`../../harness/...` from `Home/harness-view/`).
- **Don't reimplement bilingual toggle** — use `bilingual.js`.
- **Don't add Korean by accident in EN-default surfaces** — fallback to
  EN is the default; KO is opt-in per surface.
- **Don't put state in module scope across renders** — `route()` may call
  `render()` many times; treat each render as a fresh mount.
