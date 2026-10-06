# RevitThyme website

Static download and documentation site for **revitthyme.wlkrlabs.com**. Use pyRevit-inspired guide navigation, global search and concise task-focused copy. Preserve canonical technical instructions. The site uses the project's wordmark and ribbon artwork; its small house is a project-owned illustration, not a Revit screenshot or fabrication result.

The simple static site deliberately needs no React, server, database, package manager or JavaScript build dependency. This follows the repository's bounded-layer exception and the Sites static-page workflow. Search runs in the visitor's browser.

## Source and output

- layout.html: shared navigation, metadata, footer and search dialog.
- home.html and download.html: visitor-facing pages.
- pages.json: ordered guide registry; most guides render canonical repository Markdown.
- content: short getting-started and troubleshooting guides.
- styles.css and docs.css: responsive presentation and guide layouts.
- app.js: mobile guide navigation, keyboard search, full-text results and copy buttons.
- assets: house illustration; the builder reuses assets/branding for other artwork.
- .openai/hosting.json: persistent Sites identity; no credentials.

Build and verification go through the repository CLI:

```powershell
.\project.cmd site-build
.\project.cmd site-check
.\project.cmd site-serve
```

The build writes only ignored artifacts/site/dist. Preview binds to 127.0.0.1:4173; use site-serve --port to override it. Stop the owned preview with Ctrl+C. site-check builds all pages and checks local links, assets and heading anchors, with a report under artifacts/site/check.json. Full local and public CI include this check. Browser interactions and live deployment are separate verification scopes.

The escaped Markdown renderer supports this guide set's headings, lists, tables, fenced code, links, inline code and emphasis. It does not execute raw HTML or Mermaid diagrams; Mermaid remains readable source. Changes needing a broader Markdown syntax must extend the renderer and check real output, or introduce a maintained renderer deliberately.

## Publish

See [website deployment](../docs/WEBSITE.md). Preserve the existing Sites ID and only publish sanitized generated static output. The Revit extension package allowlist stays separate from website artifacts.
