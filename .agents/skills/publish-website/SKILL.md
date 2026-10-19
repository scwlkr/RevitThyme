---
name: publish-website
description: Publish RevitThyme's website, verify its live files and GitHub settings, and merge the approved baseline. Use for publication or resuming an interrupted publication.
---

# Publish website

Use the repository's publication CLI to prepare one frozen candidate, obtain owner approval, publish through native Sites tools, then verify and merge its baseline. Read [website maintenance](../../../docs/WEBSITE.md). Discover and read the installed Sites skill and use its current official source helper; resolve the plugin root from that skill's installed location rather than a developer-specific path.

Run CLI commands from `website/`. Native Sites calls belong to this conversation; the CLI records their results but cannot make them. The approval must cover the candidate source commit, digest, existing Site/domain, live publication, and subsequent baseline-only PR creation/merge after verification. A request to implement or prepare this workflow is not publication approval.

## Prepare and approve

1. Fetch `origin/master`, select its full commit SHA and a merged source-review PR. Choose a new absolute run directory outside the repository, such as `~/.revitthyme/publications/<unique-name>`. Run:

   ```text
   npm run publication -- prepare --run <absolute-new-directory> --source <full-SHA> --approval <source-review-PR-URL>
   npm run publication -- status --run <directory>
   ```

   Preparation exports and builds the pinned source under `run/checkout`, checks the release and existing live/GitHub state, and freezes `run/dist`. It must succeed before asking for approval. Keep candidate files unchanged afterward.
2. Present the candidate commit, full `candidateKey`, domain and Site identity from status, with the review PR and check results. Obtain direct human approval for this candidate and the baseline PR/merge. Existing approval counts only when it unambiguously covers these exact values. Record its human-readable evidence:

   ```text
   npm run publication -- approve --run <directory> --key <full-candidateKey> --evidence <direct-human-approval-description>
   ```

   The CLI records trusted conversational approval; it does not independently authenticate the owner. Source changes require a new candidate and approval.

## Publish the frozen candidate

3. Read `status` and use its `paths` and native arguments for the next action. Call native Sites `get_site` for `projectId`; confirm the existing audience and domain match the candidate. Preserve hosting identity and audience. Obtain an ordinary source write credential, omitting `publish_on_push`, and open the existing Site into the separate `paths.site` repository with the official `site-workflow.mjs`, omitting `archivePath` for opening. If the credential response or a known active publish window indicates automatic publication on source push, stop and reconcile that state before any source push. Keep the complete opening result in session memory.
4. After approval and source opening, stage the frozen candidate automatically:

   ```text
   npm run publication -- stage --run <directory>
   ```

   This replaces the backend static directory, including `deployment-source.json`, and validates the preserved hosting manifest against the candidate Site/static-directory identity. Use the official helper again with the retained opening result, `commands: []` and `archivePath: paths.archive`. These bytes are already built and checked. Pass credentials through the helper's hidden stdin only, retaining them in session memory; never put credentials in files, arguments or logs. Store the helper's successful non-secret result as a JSON receipt and register it:

   ```text
   npm run publication -- package --run <directory> --receipt <helper-result-JSON-file>
   ```

   A changed manifest or archive fails validation; correct staging from the frozen candidate rather than rebuilding it.
5. Before each native save or deploy call, journal its intent:

   ```text
   npm run publication -- intent --run <directory> --operation save
   ```

   Use the emitted native arguments and Sites skill to save the exact archive/source commit to the existing Site. Immediately persist the returned version ID and non-secret result to a JSON receipt, then run:

   ```text
   npm run publication -- accept --run <directory> --operation save --receipt <native-result-JSON-file>
   ```

   Repeat intent/accept for `deploy`, using the recorded saved version. Poll only deployments that native status identifies as pending, building or publishing; register each result with `accept --operation status`. Keep raw credentials and access/bypass tokens out of receipts. Persist IDs immediately, before further tool calls.

## Verify and close

6. Once native deployment succeeds, run:

   ```text
   npm run publication -- finish --run <directory>
   ```

   This verifies every frozen candidate file and the managed GitHub settings, creates a baseline-only PR, waits for required checks, merges it, and records delivery. Finish must pass before reporting success. It never repairs GitHub settings to hide drift. Open the deployed URL and report the source, saved version, baseline PR and receipt path.

## Resume or recover

Run `status --run <directory>` first; retain and resume the same candidate. For an uncertain native save/deploy outcome, use native list/get APIs to find the version/deployment matching the journaled archive/source/version, then register the reconciled result. An observation timeout does not authorize another deployment. If the existing operation cannot be identified, stop and report the uncertainty; preserve the run for reconciliation.

If preparation failed before creating `state.json`, fix its reported prerequisite and prepare in a new run directory. Coordinate one publisher at a time across independent clones/machines; the local deployment lock covers only worktrees sharing the same Git directory.

A terminal failure or live/settings mismatch leaves the baseline unchanged. Investigate the failed evidence; obtain separate approval for a replacement deployment or settings repair. Keep the run's candidate, journal, helper result and receipts until delivery is verified. The manual recorder in the maintenance guide is a recovery path, not a replacement for this workflow.
