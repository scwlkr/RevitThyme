# Website deployment

Intended public hostname: **revitthyme.wlkrlabs.com**. Website implementation is tracked as [WLK-105](https://linear.app/wlkr-labs/issue/WLK-105/build-revitthyme-download-and-documentation-website). The user authorized publication and public access on 2026-10-06.

The download targets are the actual v0.2.0 prerelease ZIP and sidecar on GitHub. The website introduces only released capabilities. Unreleased tools under active development do not appear as downloads or completed features.

## Build and source separation

Read [site source](../site/README.md). RevitThyme's source worktree owns the templates, canonical docs, CLI and persistent Sites identity. Generated publishing source lives separately under ignored artifacts/site-publish, with its own Sites-managed Git history. It contains only dist and .openai/hosting.json; it never copies the Revit checkout, local configuration, private models, TimberFold or credentials.

```powershell
.\project.cmd site-check
.\project.cmd ci --base 518d673511c8392daf212b559a672c9dd8000154 --full --require-clean
.\project.cmd site-stage
```

Run from an exact clean source commit before staging. dist/build.json retains that RevitThyme source revision. The Sites helper commits/pushes the sanitized source, then packages the exact pushed Sites SHA. Save that archive and deploy the saved version. Keep source revision and Sites revision separate in evidence.

Preserve site/.openai/hosting.json's project_id. Do not create another Site when updating this one. Acquire a fresh short-lived Sites write credential if needed; pass it to the plugin's site-workflow.mjs through hidden stdin only. Never write it to source, logs or shell arguments.

When restaging an existing Sites checkout, preserve its .git metadata and history. Reconcile remote changes rather than force-pushing. The generated dist tree must exactly match the current clean source build; remove stale task-owned output before copying when pages are removed.

## Domain

After a successful published version, use Sites add_custom_domain for the intended hostname. Apply the exact returned CNAME and validation records at the authoritative DNS provider, then refresh domain status until active. A configured hostname or generated URL alone does not prove DNS or TLS readiness.

The site emits canonical URLs, sitemap.xml and robots.txt for the intended hostname. Until DNS is active, use the Sites production URL and record the custom-domain gap. Do not replace existing unrelated DNS records.

## Verification scope

Offline: generated page/asset/anchor checks, release link identity, public package preservation and exact-clean local CI. Browser: desktop/mobile navigation, guide rendering, search results/empty state, keyboard dismissal and copy commands. Hosted: saved-version source/archive identity, terminal deployment status, public page and direct guide access. Domain: DNS and TLS status. Revit and physical fabrication are outside this website change.
