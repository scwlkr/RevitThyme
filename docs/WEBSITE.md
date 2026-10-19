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
| Installer endpoint | `installer/bootstrap.ps1`, copied byte-for-byte to `/install` and `/install.ps1` |

`prepare-content.mjs` creates `.generated/docs`, product metadata, native screenshots, `/llms.txt` and `/llms-full.txt`. Generated content is ignored by Git. Edit-source links return to the original Markdown, never the generated copy. Repository-relative links remain valid on GitHub; links to source outside the published guide set lead to GitHub.

## Keeping things aligned

Website CI runs on pull requests and default-branch pushes, with a scheduled weekly check. It uses `npm ci`, a production build and `npm run check`. Broken links/anchors, missing assets, stale generated documents, incomplete navigation, mismatched release links and mismatched MCP inventory fail validation. Maintained documentation checks cover active source, not historical `legacy` snapshots. The current installer endpoint is checked against its canonical source.

`npm run check:release` performs a bounded, read-only GitHub check that the advertised source version is the latest public stable release and all seven required assets exist. Scheduled and manual website reviews run it; pull requests can prepare an unreleased version without requiring it to be public already. A built content manifest hashes every canonical document so checks detect source changes made after the last build.

Temporary npm overrides keep the build graph on patched `tinypool`, `serialize-javascript`, `postcss-selector-parser` and the older `uuid` dependency line. Review them with Docusaurus updates and remove them when upstream ranges include the fixes. The remaining `braces` advisory has no patched registry release at this review; it concerns deeply nested build-time glob patterns. Keep source/build inputs trusted, keep preview bound to loopback and recheck the advisory during dependency reviews. Static deployment serves the built output rather than the Node development toolchain.

Run `npm run format` after editing website code/configuration; CI uses `npm run format:check`. The current `braces` issue is tracked by [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm). Check `npm audit` during dependency review; multiple affected parent packages can refer to this same underlying advisory.

Dependabot proposes weekly website dependency and GitHub Actions updates. Review updates and run checks before merging. A dependency update is not a deployment. The locked dependency graph is committed; use `npm ci` for reproducibility.

No checker can establish that prose describes an implementation correctly. The PR template requires relevant docs and evidence updates. When adding an operation, update its guide and verification; when changing a command, update the CLI reference and help together. Do not rewrite historical verification records as though older checks covered new code.

## Live publication checks

`npm run check:live` reads production and GitHub without changing either. The **Live publication drift** workflow runs on pull requests, daily, after pushes to `master`, and on manual dispatch. Failures appear as failed Actions runs with the affected URLs/settings in the job summary. GitHub notification delivery follows each maintainer's Actions notification settings.

The reviewed baseline is [`.github/website-publication.json`](../.github/website-publication.json): approved source commit, hosting source commit/version, review PR, and SHA-256 hashes for every deployed file. Checks cover pages, scripts, styles, images, search data, agent indexes, installer endpoints and source provenance. HTML comparison excludes only the exact Cloudflare security-script template observed on this host, allowing its changing ray/time tokens. Changed page content or other scripts still fail; an unfamiliar host transformation requires investigation. HTTP failures, redirects and access challenges fail verification rather than being treated as a match. The host's `/404.html` redirect is represented by its canonical `/404` URL.

GitHub expectations live in [`.github/repository-presentation.json`](../.github/repository-presentation.json) and [`.github/labels.json`](../.github/labels.json). Checks compare the description, homepage, exact topic set, Issues/Discussions settings, private vulnerability reporting, social-preview URL and managed label descriptions/colors. Additional labels are allowed. These checks require no write token; CI uses read-only repository permissions. Public API access works locally without a token; `GITHUB_TOKEN` avoids GitHub API rate limits and is sent only to `api.github.com`.

New source can merge while its deployment awaits approval. The live check lists changed publication inputs since the approved commit as pending review; it does **not** silently advance the baseline or fail merely because source is newer. A green result means production matches the last approved deployment, not that every merged change is deployed. Review the pending-source list during release approval.

## Publish a release

Ask Codex to **publish website**, or invoke `$publish-website`. The repository [publication skill](../.agents/skills/publish-website/SKILL.md) coordinates preparation, owner approval, native Sites publication, live verification and the baseline PR. One approval covers the frozen candidate and the baseline-only PR/merge after successful verification. CI remains read-only.

Preparation pins a merged `origin/master` source commit, exports it into a new run directory outside the repository, installs locked dependencies, builds and checks the site, and checks the advertised release and current live/GitHub state. It freezes the static output and displays its digest, source, existing Site and domain for approval. Nothing publishes during preparation. Changed candidate bytes require a fresh candidate and approval.

After approval, `publication stage` copies the frozen candidate into the separate hosting checkout and validates its manifest. The skill packages those exact bytes through the installed Sites helper, saves and deploys to the existing Site, and records the returned version/deployment IDs. Verification compares every candidate file and all managed GitHub settings. Only a successful readback can produce the baseline-only PR; the workflow waits for CI and merges that PR under the same approval. It never changes settings to conceal drift or refreshes hashes from production to make a failed check green.

The CLI keeps the candidate, operation journal and delivery receipts in the run directory. Resume an interruption with `npm run publication -- status --run <absolute-directory>` from `website/`, then follow the skill's next step. Save/deploy intents are recorded before native mutations. When a tool result is uncertain, reconcile the existing version/deployment through native Sites APIs before continuing; never deploy again merely because observation timed out. Native Sites credentials stay in session memory and the official helper's hidden stdin. Use an ordinary source credential without `publish_on_push`; resolve any active automatic-publish window before pushing source.

The CLI does not call native Sites tools or independently authenticate human approval. The skill supplies those tool calls and records direct owner approval bound to the candidate digest. See `npm run publication -- --help` for the command contract. GitHub authentication for the baseline PR uses the existing `gh` login; the workflow needs permission to push its baseline branch, create the PR and merge after checks.

If preparation fails before `state.json` exists, fix the reported prerequisite and prepare in a new run directory. Once the candidate journal exists, resume that run. The deployment lock serializes worktrees sharing one Git directory; use one publisher at a time across independent clones or machines. Baseline and live-state checks reject detected concurrent publication changes.

### Manual recovery

Use the recorder only when recovering a separately approved publication outside the normal workflow. Retain the exact deployed static directory, including `deployment-source.json` with its repository and source commit, plus the hosting source/version receipt:

```powershell
cd website
npm run record:publication -- --build C:\path\to\deployed\dist --source <full-GitHub-commit> --site-source <full-hosting-commit> --site-version <number> --approval https://github.com/scwlkr/RevitThyme/pull/<number>
npm run check:live
```

The recorder changes only the local baseline. Review and merge it after successful public readback; it does not deploy or repair settings. Intentional GitHub presentation changes require updating the presentation JSON alongside separately approved settings changes. `npm run test:publication` exercises drift detection; `npm run test:publish` exercises approval, interruption and publication workflow failures. Both run in PR CI.

## Release checklist

1. Update product version and release-specific installation details with the source change. The site derives its release download paths from `Directory.Build.props`.
2. Update `CHANGELOG.md` and relevant features, guides and limits. Keep historical versions inside the changelog/evidence.
3. Verify the tagged release assets and adjacent checksums exist before advertising a public download. Public release publication is a separate owner action.
4. Run toolkit checks appropriate to the change, then website build/check. Test the production preview's download links, search and narrow-screen navigation.
5. Review `/install` and its `/install.ps1` alias as PowerShell, not HTML. Preserve `/install-0.8.1` as an explicitly pinned historical endpoint. Deployment must serve extensionless installer files as text and avoid an SPA fallback for them.
6. Invoke `$publish-website` for a pinned merged source. Review its frozen candidate and approve that exact source, digest, Site/domain and baseline PR/merge. The workflow then deploys, verifies live files/settings and merges the baseline after CI.
7. Retain the completed run receipt with its source revision, host/version identity, verification results and baseline PR. A failed readback leaves the baseline unchanged; investigate before approving a replacement.

Both website workflows have read-only repository permissions and do not deploy, update baselines or repair GitHub settings. The [GitHub presentation file](../.github/PROJECT-PRESENTATION.md) explains the settings. [Audit checklist](WEBSITE-AUDIT.md) and [validation record](../verification/website.md) track the initial refresh.
