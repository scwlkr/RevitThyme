# Website refresh verification

This record covers the Docusaurus website and repository presentation refresh. The [audit checklist](../docs/WEBSITE-AUDIT.md) records the acceptance items. Checks and browser observations below cover local source and preview; they do not imply new actual-Revit qualification.

## Scope

Website, documentation, contributor/agent onboarding, community files, CI and drift prevention. No Revit process, add-in installation or model is modified by website preview or checks.

## Delivery boundary

Production hosting, GitHub settings and default-branch landing await owner approval. The local preview is the acceptance surface. A built static artifact is deployable only after review and release-link verification. See [website maintenance](../docs/WEBSITE.md).

## Local validation — October 19, 2026

Base revision: `bd4d5c6d452c4a1fcf9e80887293a3333bc85c34`, with the reviewed website/documentation changes on top. This checkout was clean before the refresh.

- Clean locked installation with `npm ci --ignore-scripts` succeeded. Docusaurus 3.10.2 production build passed strict broken-link/anchor checks.
- `npm run check` passed more than 3,000 checks across 51 HTML pages, 42 canonical documents and 18 MCP tools. It checks source links, assets, anchors, source edit links, metadata, navigation, built document hashes, release paths, installer bytes, agent indexes, MCP attributes and CLI help inventory. The exact final counts are retained in ignored `artifacts/website/checks.json`.
- A guide edit after a build caused the stale agent-document check to fail; rebuilding restored a passing result. This confirms the freshness check rejects an out-of-date artifact.
- `npm run check:release` verified v0.9.3 as the latest public stable release with all seven required assets through GitHub's public API.
- `npm run format:check` passed. Dependencies are locked; Docusaurus/React updates are grouped for review through Dependabot.
- `project.cmd check` passed: 165 unit checks, 51 MCP checks, 13 Agent checks, 46 development-safety checks and development-signing/script/architecture checks. No live Revit test was launched.
- Browser review checked the homepage, native-tool showcase, documentation, download page, local search results for parameter identity, exact command-copy text, theme switching, mobile navigation and keyboard focus. The site fit at 390px and the MCP reference at 320px without document-wide horizontal overflow.
- Production preview runs on loopback port 3100; the live editing server was also verified on port 3101. Production search is tested on the built preview because its index is generated at build time.
- Existing public sitemap routes were checked against the new pages/redirects. `/install` is the canonical bootstrap bytes; `/install-0.8.1` preserves its explicit version pin.

## Dependency review

Patched transitive overrides remove the reported `tinypool`, serialization, selector-parser and old UUID advisories. One underlying `braces` build-time glob-parser advisory remains, reported through multiple dependent packages, with no patched registry release at review. Its scope and follow-up are documented in [website maintenance](../docs/WEBSITE.md). The static site does not expose the Node toolchain as a production service. The raw local audit is retained under ignored `artifacts/website-npm-audit-final.json`.

## Preservation and remaining gates

The owner rejected the first visual pass as too wordy and promotional. The revision restores the supplied wordmark, removes the slogan/showcase layout and uses a short tool index. A homepage word budget and original-logo check now enforce those constraints. Documentation generation, search and source-contract checks remain in place.

No model, Revit process, installed add-in, global agent configuration or production hosting was changed. No credentials were added. Website CI is prepared locally; hosted CI has not run before publication/branch landing. Additional Revit versions, publisher signing and another-machine native compatibility remain separate product qualification work.

The source/local audit is complete. Owner review still controls production deployment, repository landing and the staged GitHub presentation settings.
