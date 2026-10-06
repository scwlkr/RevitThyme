# WLK-114 native qualification assessment

This is the preserved earlier assessment. The [remaining numbered qualification report](rust-view-range-m3-remaining.md) supersedes its pending rows and installation status, while retaining these historical observations and their exact revisions.

Date: 2026-10-06. Work: [WLK-114](https://linear.app/wlkr-labs/issue/WLK-114/m3-visual-view-range-installed-revit-qualification). Scope: bounded actual Visual View Range qualification and two runtime repairs, stacked on draft PR #8. No merge, release, save/sync, legacy retirement or external TimberFold modification.

## Revisions and evidence boundaries

| Evidence | Exact revision / scope |
| --- | --- |
| Comparison base | PR #8 `db0b43a388d87f3b9cbf400f1d13fbdc6adf7f2d` |
| Installed production source/package | `080e13dbf421434daa779f0ba62e1f53c4f5fd26`; adapter 0.2.2.0, Core 1.0.0.0, Rust/desktop 0.1.1, protocol 2 |
| Updated excluded failure harness | `c0d8e4575a70eae3c0ff383b8f2c72ed80a19eb9`; guarded native Error severity and detached invalid-range probe |
| Updated actual UI driver | `dce15dde42f7665fa5d02f2f51139fa10e572e00`; preserves disabled underlay direction and checks capture exclusions |
| Source/package CI | Exact clean full Windows CI at installed SHA and source/tooling checkpoint `9df334e5e85a61aa2db9e687e52c9e4c89212b59` passed against the PR #8 base. Later static documentation changes get separate scope-routed exact-clean checks. Retained JSON reports include head/base/commands/results. |
| Actual Revit | Build 27.2.0.39, one user-approved disposable non-workshared house, existing floor/ceiling/engineering plans and levels; private identity records retained locally |

The later commits change qualification tooling/docs only. Production RevitAdapter/Adapter.Core, Rust and renderer source are unchanged from the installed SHA. The installed bundle's 105 files were individually verified against the checked package, with registration backup and previous bundles preserved. Autodesk API assemblies, qualification DLLs, private models, credentials and TimberFold are excluded. New packages built by final CI are source/package evidence, not the installed artifact tested in Revit.

## Source and packaged checks

The real Windows pipe regression failed before the idle-reader fix, then passed after it: an authenticated client can remain idle 31 seconds and submit capture; a one-byte incomplete header still times out. Native managed wrappers were independently observed as ReferenceEquals=false/Equals=true for the same open document; production capture/current target now retain the same session document ID across callbacks.

Full Windows CI covers the existing independent Rust geometry/units/rules/cache, strict OpenAPI/TS/Zod boundaries, production .NET frame contracts, native offline scheduling/transaction/failure checks, real Rust/.NET pipes, allowlisted Forge package, actual packaged interaction/security/recovery and offline native-mode UI. Source/offline failure observations and native fixture packages do not establish actual Revit transactions. The new native orchestration check count is 63; real Rust/.NET pipe checks remain 36; packaged UI and offline native-mode checks remain 60 and 19.

## Actual Revit results

The packaged production app uses its PID-bound bootstrap, session-bound named pipe, Rust API and ExternalEvent scheduler. The screenshot driver enables loopback CDP only for the explicitly launched diagnostic window, uses real renderer controls and confirms the displayed target/proposal. Independent native API readbacks and a temporary DocumentChanged observer verify effects; the observer is removed at cleanup. pyRevit remains co-installed and is used for development inspection/setup and invoking the excluded failure harness. This does not establish startup with every legacy component disabled.

- Floor changed Apply: Cut 4→5 ft, all other offsets/references exact; one Undo restores 4, Redo restores 5, another Undo restores the baseline. Identical Apply returns unchanged_verified with no DocumentChanged transaction. Outcome inspection retains terminal truth without resubmission.
- Ceiling changed Apply: Cut 7.5→8 ft; main direction up, distinct Top/Depth upper-level references preserved; one Undo restores every original value.
- Engineering changed Apply: Cut 4→5 ft; main direction down, negative Bottom/Depth offsets and native relative-reference IDs preserved; one Undo restores every original value. Structural-up type behavior and precise relative-level rendering semantics are not qualified here.
- Floor Top Unlimited applies the native Unlimited reference while preserving its stored 7.5-ft offset and all other values; one Undo restores the finite original. Other Unlimited/native unit combinations remain gaps.
- Finite underlays at 0/10 ft and Unbounded tops each pass Look Up and Look Down capture/review, actual band bounds/arrows, readable coincident labels and independence from the four main planes. Each setup is undone. A disabled ceiling underlay legitimately retains Look Up; the UI preserves that fact rather than assuming Look Down. Projected underlay visibility/halftone remain controlled by Revit and outside the section visualization.
- Old reviews are rejected after independently changing underlay state or active view, without changing either target. Existing elevation capture explains the unsupported-view exclusion and disables review. Actual native validity rejects a detached impossible Cut with invalid_native_range before a transaction; all original values remain exact.
- Actual idle native review succeeds after 31 seconds. Numeric preview/Reset/Escape and diagnostic window close leave the native range unchanged.

Controlled native rollback runs wrap production Model/Change/Apply inside a valid API callback; they do **not** inject failures through product IPC or the renderer. Before/after Set, after regeneration, before/after Commit, post-commit read failure and before Assimilate restore every original reference/offset and return rollback_confirmed. Actual posted Warning and Error severities force native rollback. After completed Assimilate, the injected exception correctly returns outcome_unconfirmed and the changed native value remains; independent readback and one Undo restore it. No uncertain result reports unchanged.

The earlier GenericNonFatalError definition unexpectedly had DocumentCorruption severity. Its controlled posted failure rolled back and restored the model; the private historical record is retained separately. The harness now selects GenericError and verifies its metadata severity before posting. A subsequent actual Error trial passed. This is an induced failure-processing observation, not evidence of physical RVT corruption.

DocumentChanged affected the tested views and their internal range-control elements only; no elements were added/deleted. All 2,089 baseline model-category element IDs match at the end. All three ranges and original underlays are restored and IsModified=false. The disk SHA-256 stays unchanged. Revit is normally restarted without saving, and the installed bundle remains available for further user testing. Private model IDs/paths/hashes/screenshots stay in ignored artifacts/m3.

## Acceptance assessment and remaining runtime gaps

| Gate | Actual assessment |
| --- | --- |
| R1 startup/ribbon/capture | Partial: native DLL/build/target capture and production launcher observed; startup without legacy pyRevit remains unexecuted |
| R2 supported views/rules | Partial: floor/ceiling/engineering finite Apply, floor Top Unlimited and native-invalid rejection passed; structural up and other reference/Unlimited combinations pending |
| R3 preview-only | Partial: numeric/Reset/Escape/review/close preserve native values; full actual drag/slice/focus and undo-stack matrix pending |
| R4 changed/unchanged Apply | Tested subset passes: independent four-plane readback, floor Undo/Redo, per-view Undo, unchanged no-transaction and outcome inspection; broader repeated-request matrix remains R9 |
| R5 stale state | Partial: underlay and active-view invalidation passed; native range/level/geometry/document-switch matrix pending |
| R6 restrictions | Partial: actual elevation exclusion passed; template/dependent/family/read-only/modifiable fixtures pending |
| R7 rollback | Inducible points pass; completed assimilation truthfully uncertain and independently undone; broader group-start/rollback failure states unexecuted |
| R8 native failures | Warning/Error rollback passed via excluded harness; inaccessible Pending/finalizer/UI recovery states remain unqualified |
| R9 lost response | Pending actual native response-drop/deduplication/cancellation scenarios; offline evidence only |
| R10 session lifetime | Partial idle review/restart/owned-child cleanup; actual queued shutdown, reconnect invalidation and second Revit process isolation pending |
| R11 geometry/limits | Actual partial-capture warnings/section visible at 1,500 candidates/50,000 triangles; independent opening/transform coordinate and representative performance qualification pending |
| R12 preservation | Tested session passes: no geometry events/add/delete/save/sync, model IDs/settings/disk preserved; external source/install retained |

No aggregate R1–R12 or second-machine qualification. The manifest deliberately keeps qualified_revit_builds empty. On 2026-10-06 the user accepted the [numbered M3 scope](../docs/specs/RUST-WSTACK-ACCEPTANCE.md#approved-bounded-m3-qualification-scope). Remaining practical checks 1–9 belong to WLK-114 and are required for bounded qualification: stale target/settings, lost response/duplicates, queued cancellation/shutdown, reconnect, restrictions, supported range/reference/unit combinations, actual drag/slice, independent opening/transform accuracy and practical speed. Existing completed results above remain unchanged.

Check 10 (pyRevit-disabled startup/use) is required before a Python-free claim, but may wait during preview testing. Actual multiple-instance testing (11), second-computer distribution testing (12) and additional cross-user/network attack trials (14) are deferred. Exhaustive manufacturing of inaccessible Pending/finalizer and other rare native failure states (13) is removed as a near-term blocker; existing offline guards and actual inducible rollback/warning/error evidence remain required. These unexecuted scenarios remain unqualified. Initial qualification covers one Revit instance on the current host. Full suite migration and legacy removal (15) belong to replacement M4/M5 and remain outside this feature milestone.

The agreement changes qualification priorities, not test outcomes, automated coverage or merge/release/cutover authorization. Required checks 1–9 still have gaps, so bounded M3 qualification is incomplete. Linear remains In Progress until accepted work lands and is verified on the default branch.

## Separate review passes

Standards pass: compare only the pinned PR #8 base; retain the smallest native identity/reader repairs, no new dependency, strict existing boundaries and no arbitrary-code production endpoint. Qualification tooling is separately built and excluded from package allowlists. Preserve useful regressions, private artifacts and unrelated worktrees. Changed owned source files remain below 300 lines.

Spec pass: trace the existing Rust/UI behavior through actual native Apply, underlay independence, rollback and truthful outcomes; assess each R gate individually and apply the accepted numbered priorities. Existing caption/layout work is inherited from PR #8. Source/package and actual evidence are separate; incomplete geometry, legacy-free startup and deferred native scenarios remain explicit gaps. Current-SHA applicable CI must pass before pushing the separate draft PR.
