# Website maintenance

The website is a Docusaurus application in `website/`. It builds static files and has no separate editorial database. Root Markdown, `docs/` and `verification/` are canonical sources for GitHub, the website and agent indexes.

## Presentation

Use the supplied `assets/branding/revitthyme-logo.svg` wordmark. Keep the homepage under 100 words of main text, excluding the install command. State what the software does, provide installation and source links, and show native UI where useful. Put explanations in the docs. Use normal headings, links and compact tables; avoid slogans, promotional sections and decorative product drawings. Dark artwork is a palette variant of the original, not a separate logo.

## Run locally

Use Node.js 22+ (24 in CI) and npm:

```powershell
cd website
npm ci
npm start
```

This runs a loopback-only development server at `http://127.0.0.1:3100`. Markdown changes regenerate into the site; React and CSS use Docusaurus hot reload. Search indexes are created during production builds. To review the deployable result including search:

```powershell
npm run build
npm run check
npm run serve
```

Stop the editing server before serving the production build on the same port. For a different port, use Docusaurus directly after content preparation. No command above publishes anything.

## Source map

| Content | Edit here |
| --- | --- |
| Guides and references | `docs/*.md` |
| Community, license, feature and release records | Root Markdown |
| Qualification evidence | `verification/*.md` |
| Homepage and download layout | `website/src/pages/` |
| Styling | `website/src/css/` |
| Navigation | `website/sidebars.js` and `website/docusaurus.config.js` |
| Canonical-document selection and routes | `website/scripts/content.mjs` |
| Product version | `Directory.Build.props`; website metadata is generated from it |
| MCP inventory | Tool attributes in `src/RevitThyme.Mcp`; docs are checked against them |
| Installer endpoint | `installer/bootstrap.ps1`, copied byte-for-byte to `/install` |

`prepare-content.mjs` creates `.generated/docs`, product metadata, native screenshots, `/llms.txt` and `/llms-full.txt`. Generated content is ignored by Git. Edit-source links return to the original Markdown, never the generated copy. Repository-relative links remain valid on GitHub; links to source outside the published guide set lead to GitHub.

## Keeping things aligned

Website CI runs on pull requests and default-branch pushes, with a scheduled weekly check. It uses `npm ci`, a production build and `npm run check`. Broken links/anchors, missing assets, stale generated documents, incomplete navigation, mismatched release links and mismatched MCP inventory fail validation. Maintained documentation checks cover active source, not historical `legacy` snapshots. The current installer endpoint is checked against its canonical source.

`npm run check:release` performs a bounded, read-only GitHub check that the advertised source version is the latest public stable release and all seven required assets exist. Scheduled and manual website reviews run it; pull requests can prepare an unreleased version without requiring it to be public already. A built content manifest hashes every canonical document so checks detect source changes made after the last build.

Temporary npm overrides keep the build graph on patched `tinypool`, `serialize-javascript`, `postcss-selector-parser` and the older `uuid` dependency line. Review them with Docusaurus updates and remove them when upstream ranges include the fixes. The remaining `braces` advisory has no patched registry release at this review; it concerns deeply nested build-time glob patterns. Keep source/build inputs trusted, keep preview bound to loopback and recheck the advisory during dependency reviews. Static deployment serves the built output rather than the Node development toolchain.

Run `npm run format` after editing website code/configuration; CI uses `npm run format:check`. The current `braces` issue is tracked by [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm). Check `npm audit` during dependency review; multiple affected parent packages can refer to this same underlying advisory.

Dependabot proposes weekly website dependency and GitHub Actions updates. Review updates and run checks before merging. A dependency update is not a deployment. The locked dependency graph is committed; use `npm ci` for reproducibility.

No checker can establish that prose describes an implementation correctly. The PR template requires relevant docs and evidence updates. When adding an operation, update its guide and verification; when changing a command, update the CLI reference and help together. Do not rewrite historical verification records as though older checks covered new code.

## Release checklist

1. Update product version and release-specific installation details with the source change. The site derives its release download paths from `Directory.Build.props`.
2. Update `CHANGELOG.md` and relevant features, guides and limits. Keep historical versions inside the changelog/evidence.
3. Verify the tagged release assets and adjacent checksums exist before advertising a public download. Public release publication is a separate owner action.
4. Run toolkit checks appropriate to the change, then website build/check. Test the production preview's download links, search and narrow-screen navigation.
5. Review `/install` as PowerShell, not HTML. Preserve `/install-0.8.1` as an explicitly pinned historical endpoint. Deployment must serve extensionless installer files as text and avoid an SPA fallback for them.
6. Obtain owner acceptance before deploying. Preserve the existing production domain and hosting identity. Upload the reviewed `website/build` static output through the current host's supported release workflow; do not create a replacement hosting project implicitly.
7. Read back the public homepage, docs, versioned links and installer endpoints after deployment. Record the source revision, host/version identity and observed checks.

The website CI workflow builds review artifacts only. It has read-only repository permissions and does not deploy. The [GitHub presentation file](../.github/PROJECT-PRESENTATION.md) records proposed settings for owner review. [Audit checklist](WEBSITE-AUDIT.md) and [validation record](../verification/website.md) track this refresh.
