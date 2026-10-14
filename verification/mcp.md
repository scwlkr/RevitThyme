# C# MCP Development — 2026-10-14

**MCP stage complete on Revit 27.3.0.28.** The entire `2026-10-14_NEW_PLAN.md` was read. This stage builds on Foundation and exposes 12 generic tools through official C# `ModelContextProtocol` 2.2.0 stdio. No native utilities, dynamic C#/Cabin Eval, built-in Agent or exploration work was implemented. Maintainer tracking: [WLK-245](https://linear.app/wlkr-labs/issue/WLK-245/complete-the-c-mcp-development-stage-with-external-codex-qualification).

## Source and offline evidence

`.NET SDK 10.0.400`, Windows. Full solution and portable WPF/Core/CLI/MCP solution build with zero warnings/errors, warnings treated as errors. Locked SDK dependency restore passes. **50 Foundation checks and 33 MCP checks pass**. MCP checks use the official SDK client against the actual stdio server, covering initialization, 12-tool schemas/annotations, structured errors, offline discovery, required arguments and missing connections. Shared-operation tests cover typed atomic batches, stale target/prior values, invalid/duplicate inputs, oversized Unicode batches, no-op behavior, rollback after setter/commit/readback failures, uncertain rollback, value-based request replay, conflicting IDs, retained/pending/missing outcomes and cancellation before native execution.

Commands: `project.cmd check`, `project.cmd check -Portable`. The check route also verifies PowerShell syntax, the five-project C# stage boundary, no copied Autodesk DLLs and preserved legacy migration hashes. Hosted CI is configured; no hosted run or public publication was requested.

## Actual Revit MCP

Successful immutable run: `artifacts/mcp/live/e1911abb-4ed4-4240-a1da-c8ed41428c4f`.

- Owned process **25272**, start `639275785414317571`, session `7b81e9f3-d8d5-4718-9bcc-e0fe23682a8b`, bridge protocol **2**, Revit **27.3.0.28**.
- New `Foundation-Test.rvt` contained two native walls and **2,427 elements**. All calls were pinned to this process/session/document; no working-model or legacy-bridge fallback occurred.
- **58 actual-Revit MCP checks passed**, using every tool through official SDK stdio and the OS-verified native pipe. Coverage included explicit discovery/connection, pinned-switch refusal, view/element/parameter paging, missing element/invalid query, selection/readback, rename/readback/replay/conflict, stale capture, native duplicate-name rollback, six String/Double/Integer parameter changes in one batch, stale expected values, no-op, outcome recovery and whole-batch rollback when a later view-name parameter setter failed.
- Reports: `plan.json`, `ready.json`, `mcp-checks.txt`, `mcp-complete.json`, `mcp-native.json`. Independent checks read actual Revit values directly, rather than trusting MCP response text.

## External Codex

The same fixture passed **three separate external Codex CLI tasks** (`codex-cli 0.162.0-alpha.17.2`). Each made **26 MCP calls**, **78 total**, with **zero failed calls** in the successful traces. Each discovered both walls through pagination, inspected parameter IDs/values, selected and read both elements, renamed/restored the native view, retrieved retained outcomes, edited/restored all six String/Double/Integer fields atomically, inspected native results and cleared/verified selection.

The harness validates completed `mcp_tool_call` events for the required tools and rejects another server, failed calls, shell execution or file changes. Each round required an independent Revit API checkpoint verifying the original document/view, both comments, 9-foot wall heights, Integer values, empty selection, element count and saved file hash. Final model state passed **50 native evaluator checks**, including one Undo/Redo for the entire parameter batch, final restoration, wrong-document rejection and closed/reopened-fixture rejection.

Codex reused saved local authentication without reading/copying secrets. User configuration was ignored for each ephemeral invocation; only the pinned MCP was configured. Shell, plugins, apps and agent delegation were disabled. Per-tool `approval_mode="approve"` was scoped to the three authorized write tools on that disposable server. Global Codex configuration and existing MCP connections were not changed.

Reports: `codex-round-{1,2,3}.prompt.txt`, `.jsonl`, `.final.json`, `.native.json`, plus `codex-complete.json` and `native-result.json`. Saved fixture SHA-256 stayed **`366f8979883ca08a69cdb24a8c84caec110b67ae7e31700acbc7b5ac78e35a39`**. No test edits were saved over the fixture.

## Packaging and preservation

Package: `packages/RevitThyme-0.5.0-mcp-e274aa955c4e4bb69629f92ee6638af1`, adjacent ZIP and SHA-256. Addin/Core/UI and MCP product binary hashes match the successful native bundle. The package includes allowlisted framework-dependent CLI/MCP files, SDK dependencies and full license notices. It excludes Autodesk DLLs, models, test loaders and legacy runtimes.

**11 isolated installation checks passed** at `artifacts/foundation/install-test/b9fc6ab8-11ae-48ed-8e5a-e35acc9bd061`: exact registration, launchable installed MCP, modified SDK rejection, immutable reinstall, backups/unregister/recovery, modified native bundle/unknown-registration rejection, and upgrade from the prior Foundation package preserving its bundle and registration. These used isolated folders and never replaced a live production registration.

Cleanup removed only process 25272 and its exact hash-verified temporary registration. `cleanup.json` confirms every pre-existing registration hash and working process/start identity was preserved. All fixtures, prior failed/aborted runs and private transcripts remain retained.

## Repairs and limits

Retained earlier attempts exposed test assumptions about parameter paging and selection clearing after Revit rollback, and a noninteractive Codex policy that refused write tools before submission. The final harness pages parameters, accepts verified unchanged selection, scopes explicit write approvals to the pinned disposable server and schedules its own API callbacks for unattended native evaluation. The successful run includes those repaired paths. Earlier failures are not counted as passing.

This verifies scripted MCP tasks on this populated disposable fixture and current Revit 2027.3 machine. Production workshared/large models, multiple simultaneous MCP clients, other Revit versions and a second machine remain unqualified. The production signed `.exe`/bootstrap remain distribution work. The working model and installed legacy tools were preserved. Later roadmap stages remain deferred.
