# Website and documentation audit

This checklist tracks the October 19, 2026 presentation and onboarding refresh. Completion means the change is implemented and checked locally. Publishing the website, landing repository changes and changing production GitHub settings require the owner's final review.

## Findings before the refresh

- The live website advertised 0.8.1 while the latest public release was 0.9.3.
- The GitHub description still described a pyRevit extension, with no homepage or topics.
- Current guides contradicted shipped Agent functionality and linked contributors to internal stage tracking.
- The README read like a qualification log, making the product and first successful task hard to find.
- Installation details existed, but there was no current website source, coherent documentation home or first-session/CLI/troubleshooting path in the active tree.
- Agent setup assumed prior knowledge of receipts, session bindings, captures and recovery.
- Community expectations, governance and docs-only issue/reporting paths were incomplete.
- Website/source content, release links and tool inventories had no shared generation or drift gate.

## Product credibility

- [x] Replace the missing current website source with a reproducible Docusaurus site.
- [x] Introduce the product in plain language, with a clear audience and useful feature examples.
- [x] Show actual native UI screenshots with informative captions and accessible text.
- [x] Make installation, requirements, release downloads and checksums easy to find.
- [x] State supported Revit versions, pre-1.0 status and unsigned installer limits honestly.
- [x] Replace stage-history-heavy README content with a focused project front page.
- [x] Correct conflicting current-scope claims in project, roadmap and interface guides.
- [x] Separate supported features, future direction and historical implementations.

## User and agent onboarding

- [x] Add a documentation home with distinct user, developer and agent entry points.
- [x] Provide a first-session guide with an observable success condition.
- [x] Add a CLI reference based on the implemented command surface.
- [x] Explain external MCP setup, targeting, inspection, edits and outcome recovery.
- [x] Document common installation, command discovery, connection and native-tool failures.
- [x] Publish machine-readable documentation indexes and full text from the same sources.
- [x] Keep repository and website docs in sync without maintaining parallel copies.

## Open-source participation

- [x] Give contributors a working route without licensed Revit or private issue-tracker access.
- [x] Explain architecture boundaries, source layout and appropriate verification.
- [x] Add community conduct, support and public security-reporting guidance.
- [x] Improve issue forms and PR guidance, including documentation-only contributions.
- [x] Document governance, scope decisions, review expectations and release boundaries.
- [x] Provide dependency maintenance and documentation CI with locked dependencies.
- [x] Prepare GitHub description, topics, homepage and social-preview guidance for final review.

## Site quality and delivery

- [x] Add useful navigation, local full-text search, edit-source links and code copying.
- [x] Support narrow screens, keyboard navigation, readable contrast and light/dark themes.
- [x] Add page metadata, canonical URLs, sitemap, social artwork and a favicon.
- [x] Preserve legacy website routes and installer endpoints without serving stale software.
- [x] Fail checks for broken local links, anchors, missing assets and stale generated content.
- [x] Build and inspect representative pages and interactive behavior in a browser.
- [x] Record validation, remaining limits and the explicit deployment approval boundary.
- [x] Open a local preview for owner acceptance.

## Acceptance evidence

All 30 source/local-review items in this checklist are implemented. The website production build, strict content/link checks, source-contract checks and full toolkit build/offline checks passed. Browser review covered light/dark themes, native screenshots, local search, install-command copying, keyboard focus and 390px/320px layouts. See [validation](../verification/website.md).

GitHub description/topics/homepage/social-preview changes are prepared in [the presentation file](../.github/PROJECT-PRESENTATION.md). Applying them, landing the branch and publishing the site are final owner-approved delivery actions, not already completed production changes.

## Boundaries

This refresh does not claim new native Revit qualification, additional supported Revit years, a signed public installer, a stable 1.0 API or a security sandbox. Those require implementation and evidence beyond a documentation refresh. Public settings and deployment are staged, not silently applied.
