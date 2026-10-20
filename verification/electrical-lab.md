# Electrical-lab MCP loop

The current-source electrical-lab workflow passed in actual Revit through the external official MCP SDK, including bounded edits, native image export, explicit save, close/reopen, stale-document refusal and original-request recovery. It required explicit agent intervention in Revit's missing-reference warning and staged continuation after harness failures. This is not an unattended, uninterrupted run or historical fixture acceptance.

The installed `0.10.0-lifecycle.5` host remained unchanged: protocol 13 / 32 tools, native source `9e3db84e3c29e31230b217a7bad151d15538e187`, manifest `332c26eff5341ea26b66651bbd8f492dc3953bc82db7e1df46e0366dc8d43809`. Prior [startup/image](view-images.md), [lifecycle](document-lifecycle.md), [modal recovery](modal-recovery.md) and [restricted-document](restricted-documents.md) evidence retains its original provenance. No installation qualification was repeated.

## Harness and acceptance

The opt-in [ElectricalLab runner](../tests/ElectricalLab/README.md) starts an owned trusted process at Home, copies the current electrical project and a distinct clean handoff project, and pins their hashes and process/start/session. The official SDK client launches the immutable installed MCP executable. Trusted C# only inspects or performs bounded transactions; open, activation, view switching, image export, save and close use typed operations. No test-only native callback or dialog handler was installed.

The edit creates an independent Power study without detailing, then copies exactly two inspected Generic Annotation instances in a second transaction. Every native Added ID requires a bounded role; Modified IDs require explicit bookkeeping roles. Each edit retains the 512 changed-ID budget. Canonical views/templates, raw settings/ranges, annotations, model counts, schedule footers, sheet placements and original collector membership must remain unchanged. Only copied A may be explicitly saved.

The later source differs from the historical reference: all four working views are Medium rather than Fine, two schedule footer arrays have one fewer empty cell, and non-type membership is 10,871 rather than 10,876. Current source size is 195,670,016 bytes, SHA-256 `8b9dd4c93e3e18c1f4833dcf014a5d67686c865a7315072bd3b1071f1b78c77a`; older receipts describe different files. The cause of that transition is unestablished. Current identities, names, scales, annotation counts, model quantities, wire counts and nonempty schedule totals still match the historical hard guards. Current raw fields and complete collector membership are frozen independently before edits and compared through save/reopen.

Consequently `currentSourceWorkflow=passed`, while `historicalAcceptance=false` and `historicalFineAcceptance=false` remain explicit. Historical raw-footer conformity and older derived display/range booleans are not requalified by preserving the later source. No source restoration or settings changes were made.

## Actual Revit results — 2026-10-20

Accepted staged run `bc512072-f0c8-4487-bb8e-cb575d00b3da` used PID 29668/start 639281066629826971/session `3cc890dc-9c6b-4621-b851-3b9e69e8980b`. Native duplicate source was `994f77d`, committed companion classification/continuation source `2d7a023`, and final saved-reopen recovery source `c027bd6008eeba36c9b247457bb2dceaa4630543`. The final SDK invocation passed **187 assertions**. Original failed checkpoints remain retained; their assertion counts are not added into a purported single passing run.

| Native action or check | Observed result |
| --- | --- |
| Home and background open | Exact copied A opened in 6.224 s; distinct clean B in 0.497 s. Both retained original GUID/path/hash and different native document IDs. |
| Duplicate Power without detailing | Original GUID `43357777-5344-465e-875a-e4b6fb491f61` Applied in 0.704 s. Ten native Added IDs: study plus nine classified structural companions; no Modified/Deleted IDs. |
| Inspect postcommit additions | All ten directly accessible by ID, nine collector-visible. ID 2915374 is an unplaced Viewport for exact study 2915372, invalid sheet ID, valid type, and a study dependency. Its UID/role/associations persisted after actual save/reopen. |
| Copy exact two annotations | GUID `84eef81d-24e6-4b23-94e3-135d4e3c94ef` Applied in 0.269 s. Added IDs 2915391/2915392 only; no Modified/Deleted IDs. Independent queries verified owner, types, exact points and all nine native transform-basis components. |
| Image export and switching | Typed exact-view activation and six native PNG exports: baseline Power/E101, empty study, copied study, subsequent E101 and reopened study. Plan 1600 x 1082; sheet 1600 x 1066. PNG bytes, dimensions, SHA and inline MCP image agreed; each export preserved native state. |
| Explicit A save | GUID `0adfcc02-ebea-4750-8107-87a5d712c2cf` completed in 2.161 s. Saved SHA `273802c2694cba931938e5a6b6d7c58ea5d5c4c3f17d3f28c6a8d07157c7db57` matched disk; A clean. |
| Active close and reopen | Typed close handed off to exact clean B in 1.749 s. Saved A reopened in 5.853 s with a new document identity. Retired A identity was refused with `wrong_document`, preserving B. |
| Interrupted reopened activation | Original GUID `93de2571-eea0-4449-b9d7-de5a544f4bde` returned Unconfirmed/transport_lost after 30.008 s while the native warning blocked. Following explicit UI intervention, a fresh SDK observer recovered Completed/document_activated for that same GUID in 106.1 ms, without replay. |
| Persisted state and recovery | Canonical inventory unchanged; study UID, all ten direct duplicate identities/roles, and both annotation identities/types/positions/orientations persisted. Original duplicate/copy/save and lifecycle/query/refusal outcomes recovered byte-exact through SDK clients. |
| Finish and cleanup | Typed close removed clean background B; inventory contained only clean A on the persisted QA view. All prior run files retained their hashes before completion. Runner then normally closed the owned process; source, handoff and installation preservation passed. |

Total additions are **12 native IDs and 11 collector-visible IDs**. Native accounting counts the internal Viewport and remains the basis for the unchanged budget. No original native IDs were modified or deleted by either transaction. A temporary rollback-only clone supplied metadata, but postcommit direct-ID reads supplied the authoritative companion classification.

All six PNGs were inspected as images: native plans show devices/fans and halftone architecture; E101 shows its titleblock, lighting views, legend and schedules, without app chrome or warning overlays. Empty, copied and reopened study PNGs share SHA `1bebe1bab3d82f449e3c0dd78045f51ded020f63e3ce450d988b56db6a323c2f`. They do **not** visually prove the annotation copy; independent native identities/type/point/basis checks do. E101's titleblock timestamp changes between exports, so byte equality is not a preservation oracle for that sheet.

## Failures and recovery retained

Initial run `e8e53655-7f3c-494e-a040-2ce07336ca80` encountered Revit's actual missing-three-point-cloud warning during activation. The caller timed out at 30.015 s. Two independent SDK clients recovered Pending in 101.0 / 93.8 ms; after explicit agent Computer Use intervention, they recovered the original Completed activation in 107.9 / 90.6 ms. The agent inspected the exact owned warning and selected Ignore and continue. References and source files were not edited or sanitized. The initial process later closed normally with source/copy hashes checked; it has no accepted completion marker.

The first inventory query rolled back because Revit could not extract `LocationPoint.Rotation`. Harness commit `147172c` uses `GetTransform` basis instead. Subsequent source/reference mismatch led to the explicit current-source profile in `ec819ab`, not silently restoring historical settings. A first duplicate diagnostic rolled back on an unknown companion; `994f77d` classified observed structures, then `2d7a023` added direct postcommit inspection when the native Added set exposed a Viewport absent from the collector. The already-Applied duplicate was recovered and continued, never resubmitted.

On saved-copy activation the same real warning recurred. The agent resolved it directly in the owned process, recovered the original GUID, and used a narrowly guarded saved-reopen tail rather than repeating duplicate/copy/save/open. Source `c027bd6` also adds bounded original-GUID polling to future calls. Its actual terminal recovery branch passed here; the new continuous three-minute Pending wait and recovered-image branch were not separately exercised in this run. Earlier modal qualification remains separate evidence. See [native dialog recovery](../docs/MCP.md#native-dialogs-and-recovery).

Private evidence under ignored `artifacts/electrical-lifecycle` includes exact intents/bodies, native results, all transcripts, frozen inventory, classifications, image bytes, completion/cleanup and before/after preservation receipts. Models and private reference/captures are not repository artifacts.

Source checks passed: focused Release compilation with zero warnings/errors, five submitted script bodies compiled against the installed Revit 2027 API, 76 portable MCP checks, launcher PowerShell parsing and `git diff --check`. The website build passed, followed by 7,371 content/link/contract checks across 59 HTML pages, 50 canonical documents and 32 MCP tools. These checks supplement the native evidence; they do not replace it. Separate Standards and Spec reviews found no blockers.

## Preservation and remaining gaps

Final hashes preserved the electrical source, handoff source/copy, user TimberFold test model, unrelated logo, installation receipt and both add-in registrations. Raw user PATH value/type and the primary worktree HEAD/status also matched their initial records. Installed payload hashes passed before completion. No existing working Revit process was used; both owned test processes exited normally.

Historical Fine/raw-footer/display acceptance remains outstanding. This fixture requires explicit native-warning intervention; there is no general automatic warning handler. A visibly different annotation-image example remains useful because this copy's pixels did not change. Last-active close to Home, active unsupported contexts and broader warning workflows remain gaps. Work above 512 changed IDs stays separate. Physical TimberFold calibration, cutting and assembly remain outstanding. No release, website deployment or promotional posts.

Next: retain these current-source results in the existing queue, then address the smallest remaining typed lifecycle gap, last-active close to Home, with explicit semantics and disposable-model recovery tests before promotion.
