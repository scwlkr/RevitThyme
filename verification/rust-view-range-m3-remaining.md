# WLK-114 remaining bounded M3 assessment

Date: 2026-10-06. Draft PR #9, stacked on PR #8. The approved practical trials 1–9 have been performed. Check 6 exposed an unresolved-reference defect; the proposed restriction is implemented in adapter 0.2.4 and its exact package/native requalification is pending at this checkpoint. No merge, release, save/sync, live-job edit, legacy retirement or TimberFold change.

## Separate evidence revisions

| Evidence | Revision and result |
| --- | --- |
| Comparison base | PR #8 db0b43a388d87f3b9cbf400f1d13fbdc6adf7f2d |
| Earlier installed runtime | 080e13dbf421434daa779f0ba62e1f53c4f5fd26; adapter 0.2.2.0; remaining matrix and edit-mode fail-before |
| Runtime repair | 6275144c80f338b33ecf1f90864b52b7f7d3a603; native edit readiness guard and adapter 0.2.3 |
| Interim checked source/installed package | 1cacd9536442f71fe7e1fe7c0025faf4e299a89d; adapter 0.2.3; exact-clean full Windows CI passed against the comparison base |
| Package | All 105 files checked and installed to a new immutable directory; registration backed up; previous bundles preserved; SDK, qualification DLL, private models and credentials excluded |
| Actual replacement bundle | Revit 27.2.0.39, adapter 0.2.3.0, Core 1.0.0.0, Rust/desktop 0.1.1, protocol 2; edit regression plus floor/ceiling/engineering Apply/Undo, mm, paired Unlimited, drag/slice, reconnect, BRep coordinates and responsiveness rechecked |

Later static documentation commits do not replace the installed bundle. Their scope-routed exact-clean CI is separate. Full CI includes existing Rust geometry/rules/units/cache and strict typed boundaries, native offline orchestration, real Windows Rust/.NET pipes, packaged interaction/security and native-mode fixture UI. These are source/package results, not additional actual Revit cases.

## Recorded checks 1–9

| Check | Actual assessment |
| --- | --- |
| 1. Stale/wrong target | Pass for native range, level elevation, geometry and document switch; old review rejects. Earlier underlay/active-view results retained. Temporary changes independently restored. |
| 2. Lost reply/repeats | Pass through the installed typed preload: submit once while deliberately discarding the caller reply, inspect retained outcome, repeat the identical request twice, reject a conflicting payload. Independent native value and DocumentChanged show one Apply transaction; one Undo restores the original. This is caller reply loss, not a byte-level pipe corruption test. |
| 3. Queued cancellation/close | Pass: actual native modal dialog holds Apply queued; Cancel removes it; closing the actual desktop cancels it as disconnected. Document close/reopen rejects the old queued request as stale_snapshot and leaves native values unchanged. The document-close race uses a temporary qualification-only scheduling hold, released by native DocumentClosing. No production scheduling switch was added. |
| 4. Reconnect | Pass: actual Reconnect removes the old review/confirmation/Apply control; a fresh review succeeds. No native mutation. Rechecked on 0.2.3. |
| 5. Restrictions | Pass for template-controlled, dependent and primary-with-dependents, family, active transaction, actual read-only DocumentChanged context and unsupported elevation. Native edit mode failed before: IsModifiable=false allowed CurrentTarget despite another edit scope. Fixed with IsPermitted; actual guarded scope now returns active_edit, cancels and preserves the document. A read-only event is the tested read-only state, not every file/worksharing restriction. |
| 6. Range/references/units/direction | Paired Bottom/Depth Unlimited, explicit alternate levels, mm/feet Apply and temporary structural up passed. Native sentinel IDs/offsets round-trip, but their guessed elevations failed a calibrated native comparison. Resolved native Above choices store actual positive level IDs. Adapter 0.2.4 rejects unresolved bare Above/Below sentinels; exact-package pass-after pending. Bottom Unlimited with finite Depth correctly rejects an inconsistent combination. |
| 7. Drag/slice | Pass: actual pointer drag changes preview; slice controls change cached section. Native range and DocumentChanged remain unchanged. An independent native Undo marker is the next Undo item after preview, proving preview added no Undo transaction. Replacement-bundle drag/slice also passes. |
| 8. Section accuracy | Pass for existing wall opening, rotated door and mirrored garage family: 170 independent native BRep cut-face samples compared with actual rendered SVG world coordinates. Straight geometry maximum error below 0.000002 ft; curved hardware below the stated 1/16-inch faceting budget. Rechecked on 0.2.3. This tests boundary coordinates, not final Revit visibility or an exhaustive curved-surface guarantee. |
| 9. Responsiveness | Pass within capture-to-editable 3,000 ms and pointer-drag-to-display 350 ms budgets. Five original-bundle captures: 914–971 ms; drags: 206–213 ms. Three replacement-bundle captures: 926–1,947 ms; drags: 205–212 ms. Machine/load/individual samples retained locally. Capture still discloses 1,500-candidate and 50,000-triangle limits. |

Native UI inspection distinguishes resolved Level Above (name), stored as an actual positive level ID, from bare negative Above/Below placeholders with no resolved name. A calibrated floor-Below/ceiling-Above visibility fixture disproved the adapter's nearest-level guess. Earlier probes could not distinguish the cases because generic geometry above a floor cut is not visible; their inconclusive records remain. The successful direction-aware probe commits native child transactions, refreshes the active view, compares explicit references, and rolls back the entire group inside one valid callback.

Proposed preview restriction: reject capture/Apply for unresolved native Above/Below placeholders with unresolved_relative_level and actionable guidance to choose a named level in native View Range. Keep resolved positive IDs, Current, allowed Unlimited and explicitly named alternate levels supported. Never silently choose a substitute level or alter a model reference. This deliberately narrows the supported subset for plans such as the test house's default engineering view until their unresolved references are set explicitly by the user; it is not a change to all engineering-plan support. Exact new-package/native validation remains required before delivery.

The opening comparison uses native solid cut-face edges, not the Rust triangle algorithm as its reference. Straight edges use 0.00001-ft tolerance; differing native arc/face tessellation uses 1/16 inch. A strict arc-tolerance failure is retained. A proposed empty-opening point check was confounded by legitimate co-located door/window geometry; it is not counted as a pass or as a product defect. Capture already discloses hidden/phase/design-option geometry and exclusion of final projected visibility.

## Preservation and remaining gates

All original plan ranges, underlays, levels and 2,089 model-category IDs match after cleanup; IsModified=false. Temporary template/dependent/type fixtures, moved geometry and native edit-scope internals produced real changes or rollback events, all restored. Do not reuse the earlier session's no-add/delete statement for these fixture trials. The disk file remains unchanged; final normal restart unloads the excluded harness. Original screenshots and failed setup attempts remain private evidence.

The replacement registration points to the checked bundle; previous compatible bundles, other add-ins and the original test-house disk contents are preserved. Chromium created an extra debug.log beside the earlier installed app: all 105 original files still match; the extra log was inventoried/copied and preserved rather than deleted. Root logo, website worktree/unpushed work, M1 draft and external TimberFold source remain preserved.

Check 10 is deferred during preview and still required for a Python-free claim. Actual multiple instances (11), second-computer distribution (12) and additional cross-user/network attack trials (14) remain deferred/unqualified. Rare inaccessible Pending/finalizer states (13) are nonblocking, not passed. Existing inducible rollback/warning/error and offline guards remain. Suite migration and legacy cutover (15) belong to M4/M5. No aggregate R1–R12 claim; qualified_revit_builds stays empty. Linear stays In Progress until accepted work lands and is verified on the default branch.

## Separate review passes

Standards: smallest native guard; no new dependency, unused adapter layer or arbitrary-code production IPC. Drivers extend the existing CLI and typed packaged preload. Qualification code remains separately built and excluded. Changed owned files stay below 300 lines. Full exact-clean CI passed before source push; static-only delivery changes receive routed checks.

Spec: preserve process/session/document/view/revision/original validation, transactional Apply and independent readback. Trace the agreed numbered trials separately from historical and offline evidence. The native edit defect has actual fail-before/pass-after evidence. Disclose caller fault and artificial scheduling-hold limits, relative-height uncertainty and every deferred broader gate. PR stays draft; no merge/release.
