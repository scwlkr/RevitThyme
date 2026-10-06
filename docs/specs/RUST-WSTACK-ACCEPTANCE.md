# Rust replacement acceptance and qualification

Companion to [the replacement specification](RUST-WSTACK-REPLACEMENT.md). These are verification requirements, not permission to install software or change Revit models.

## Evidence rules

Every result records source/head SHA, baseline, command or UI driver, actual tool/runtime versions, fixture identity, outcome and retained evidence. Keep private model data/settings out of GitHub. Mark each check as source/offline, packaged application, actual Revit, or external/physical work. Skipped, pending and unexecuted cases remain gaps. A synthetic screenshot or successful .NET compile cannot establish native transaction correctness.

Drive the real feature's inputs and observe effects independently. Use existing CLI/UI drivers first; add small parameterized project adapters where needed so sessions can seed, reach, inspect and reset meaningful states. Record gaps in existing task/verification notes rather than building a parallel test platform. Retain screenshots for visual interactions and native readback for mutations.

## Approved bounded M3 qualification scope

On 2026-10-06 the user accepted the following priorities for Visual View Range. Checks 1–9 are the remaining practical M3 qualification work, alongside the completed evidence in [the M3 assessment](../../verification/rust-view-range-m3.md). This scopes the catalog below; it does not mark unexecuted cases passed or qualify every R1–R12 scenario. Initial qualification covers one Revit instance on the current Windows host. Existing automated tests, authentication, transaction guards and recorded evidence remain required and preserved.

| Check | Qualification | Accepted disposition / catalog coverage |
| --- | --- | --- |
| 1 | Change document, native range, level or geometry after review; reject stale or wrong-target Apply | Keep: R5. Underlay and active-view cases already passed; remaining cases still required. |
| 2 | Lose the Apply reply or repeat the request; inspect the outcome without a second transaction | Keep: R9 and O9. Actual lost-response and duplicate-request cases remain required. |
| 3 | Close the window/document or cancel queued work; prevent a later queued write | Keep: R3/R10 and O9. Preview close passed; queued cancellation/shutdown still required. |
| 4 | Disconnect/reconnect and try the old review; require a fresh review | Keep: R10 and O12. Actual reconnect invalidation remains required. |
| 5 | Template-controlled, dependent/primary-with-dependents, read-only, family, active edit/transaction and unsupported views | Keep: R6. Explain exclusions without mutation; elevation passed, remaining restrictions still required. |
| 6 | Remaining supported range options: Bottom/Depth Unlimited, alternate references, mm/feet Apply and upward-looking structural plans | Keep: R2 and O2/O3. Verify supported combinations; any proposed exclusion needs an explicit scope decision. |
| 7 | Drag planes and change the slice in the actual connected UI without changing native range or Undo history | Keep: R3. Numeric preview, Reset and Escape passed; actual drag/slice observations remain required. |
| 8 | Compare a known opening and rotated/mirrored family with the displayed section | Keep: R11. Independent actual geometry accuracy remains required; partial-capture warnings passed. |
| 9 | Measure practical capture and drag speed on the test house | Keep: R11. Record machine/load and useful interaction measurements; no elaborate benchmark suite. |
| 10 | Start and use the feature with pyRevit disabled | Required before a Python-free operation claim: R1. May wait during preview testing; currently unexecuted. |
| 11 | Run two Revit instances and demonstrate isolation | Defer actual multi-instance qualification: R10. Initial scope is one instance; deterministic targeting and automated isolation coverage remain required. |
| 12 | Install/update/uninstall/restore on a second computer | Defer until distribution: P6–P8. Preserve current-host physical installation/ownership checks; no second-computer claim. |
| 13 | Manufacture every rare internal Revit failure, including inaccessible Pending/finalizer states | Remove exhaustive manufactured native-state testing as a near-term blocker: R7/R8. Preserve offline failure guards, inducible native results and truthful uncertain outcomes; inaccessible states remain unqualified. |
| 14 | Additional cross-user/network attack trials | Defer actual additional attack trials: O11/P4/R10 boundaries. Existing authentication, local-only transport and renderer-security checks remain required. |
| 15 | Migrate every other suite tool and remove the legacy host | Move to replacement M4 parity/distribution and M5 approved cutover. Outside this feature milestone; no legacy removal is authorized. |

Required checks 1–9 need recorded actual results before this bounded qualification can pass; see the [current numbered assessment](../../verification/rust-view-range-m3-remaining.md), including the explicit unresolved-reference restriction proposed after a native mismatch. Check 10 gates the separate Python-free claim. Deferred checks 11–14 remain visible gaps for their broader claims and do not block the accepted one-instance preview scope. Check 15 belongs to later milestones. Merge, release and cutover retain separate authorization; this agreement grants none of them.

## Offline and source acceptance

| ID | Scenario | Required observation |
| --- | --- | --- |
| O1 | Synthetic wall/floor/opening/transformed instance | Rust section intersects the expected coordinates and preserves holes/transforms; analytical or independently constructed expected results |
| O2 | Unit/reference precision | mm/m/decimal ft round trips, negative offsets, level-relative values and untouched exact values preserve the proposed native meaning |
| O3 | Floor/ceiling/Unlimited validation | Valid cases pass; nonfinite/unsupported values and native-invalid proposals are rejected; native validity remains the final adapter check |
| O4 | Interactive feature | Drag/numeric edits, slice locator, Reset, Cancel/Escape/close and before/after Apply preview work through the actual Expo screen and typed backend |
| O5 | Preview races | Slow old response cannot overwrite a newer input revision; dragging uses cached geometry and opens no native transaction |
| O6 | Snapshot limits | Partial capture is visible with reasons; oversized/expired frames/snapshots fail safely; memory/request retention is bounded |
| O7 | Contract conformance | Rust/OpenAPI and derived or checked TS/Zod agree on valid/invalid fixtures, enums, units, error/result fields and protocol mismatch |
| O8 | Native queue fake | Wrong target/session, stale originals, template/dependent restrictions and document close reject before writes; background I/O never calls API |
| O9 | Mutation outcomes | Identical values return verified unchanged with empty changed IDs and no transaction; duplicate same ID returns previous result; changed payload rejected; lost ack never triggers retry; queued cancellation differs from an executing transaction |
| O10 | Failure injection | Each capture/validation/transaction/readback/commit/group/rollback error maps to a truthful result; post-commit readback occurs before assimilation and can trigger group rollback; uncertainty never reports no change |
| O11 | Local access | Unauthorized HTTP/pipe requests and IPC senders fail; renderer cannot access credential, shell, arbitrary file/URL/pipe methods |
| O12 | Lifetime | Disconnect/reconnect/restart cannot reuse proposals; multiple Revit identities cannot cross-target; only owned children are stopped |

Fakes prove orchestration, not Revit behavior. Avoid tests that merely restate implementation branches; preserve independent fixtures and failures with practical consequences. Choose a measured interactive-preview budget on representative bounded captures and document the machine/load before declaring performance passed.

## Packaged application acceptance

| ID | Scenario | Required observation |
| --- | --- | --- |
| P1 | Clean Windows x64 package | Exported Expo routes/assets/fonts/icons load through the shipped app protocol with no development server |
| P2 | Real desktop interaction | Keyboard/pointer, focus, scaling, light/dark, validation messages, preview and reconnect states work in packaged Electron |
| P3 | Child launch | Packaged Rust sidecar launches outside ASAR, authenticated API connects, version handshake matches; missing/crashed child has useful recovery |
| P4 | Security boundary | Packaged CSP/navigation/IPC checks remain active; no Node integration or credentials leak into renderer/logs |
| P5 | File ownership | Manifest/allowlist/checksums verified; package excludes secrets/models/TimberFold and Autodesk API assemblies |
| P6 | Lifecycle fixture | Approved install/update/uninstall/rollback against disposable owned paths preserves unrelated/modified files and settings; interrupted update recovers |
| P7 | Native destination | Actual per-user Revit 2027 discovery path and physical destination checked; redirected/unexpected paths refused |
| P8 | Compatibility recovery | Incompatible bridge/core/desktop or settings schema fails before writes; previous compatible component set restored together |

P6/P7 against fake filesystem paths are package tests. Installing registration/code into Revit's discovery path is a separate approved action, even if the installer passed offline checks. A second-computer claim needs actual second-computer results.

## Proposed disposable Revit qualification

Before executing, report to the approving user/coordinator: exact package SHA/component versions, destination/registration paths, observed Revit build, active document, proposed fixture location, elements/views to create or edit, expected transactions/side effects and cleanup/recovery. Obtain approval for installing the add-in and creating/changing this fixture. Do not use a live production project or TimberFold baseline by default.

Recommended fixture: a small local non-workshared project in an approved temporary location, with explicit metric/feet reference cases, two levels, a wall with an opening, floor and placed/transformed family instance; separate floor, engineering and ceiling plans. Add only the minimum template/dependent-view cases needed below. Do not save, sync, close or replace any model unless separately requested. If the fixture requires an initial save, state that exact action in the approval request.

Reinspect active document/session before each scenario. Run mutations through explicit tool Apply and keep independent native API readback of the four plane references/offsets and supported document/view state. Use only adapter-owned test hooks for failure injection; production paths must not accept arbitrary code. Keep the prior compatible installation available and never force-kill Revit for cleanup.

| ID | Actual Revit scenario | Required observation |
| --- | --- | --- |
| R1 | Startup/ribbon/capture | Exact approved .NET adapter loads without Python/pyRevit; ribbon opens UI; correct process/document/view and model section captured in valid API context |
| R2 | Supported views/rules | Floor/engineering/ceiling plans display native references correctly; valid finite/Unlimited proposals apply; native-invalid proposals do not change range |
| R3 | Preview-only interactions | Drag, type, slice, Reset, Cancel/Escape/window close leave independently read native range unchanged and create no undo item |
| R4 | Changed and unchanged Apply | Displayed target/proposal explicitly confirmed; pre/post-commit readback equals requested range/references before assimilation; one undo restores a change and redo restores result; identical settings/repeated current-value Apply creates no transaction/undo item and returns verified unchanged |
| R5 | Stale state | Change range/level/geometry/active view or document after capture; old Apply rejected and refresh required; no unintended target changed |
| R6 | Restrictions | Template-controlled, dependent/primary-with-dependents, family/read-only/modifiable and unsupported views produce useful exclusions without mutation |
| R7 | Rollback | Controlled failures before/after SetViewRange, regeneration, commit and group completion restore original where rollback is possible; inconclusive states identified honestly |
| R8 | Native failure handling | Error/warning/failure-processing path cannot report Pending as committed; UI remains responsive and final status/readback is inspectable |
| R9 | Lost response | Drop response during/after apply; outcome query/readback identifies changed/unchanged/uncertain truth; no automatic second transaction |
| R10 | Session lifetime | Close fixture/switch session/stop sidecar with queued work; no orphan writes; reconnect invalidates old proposal; second Revit process remains isolated |
| R11 | Geometry/limits | Actual openings/transforms agree with reference coordinates; truncated/unsupported geometry warnings visible; exclusions explain preview limitations |
| R12 | Source preservation | No source-model geometry altered by range editing; TimberFold repository/baselines/install unchanged; no unintended saves/syncs |

Do not manufacture otherwise inaccessible failure states in a production model. Document which native failures can be induced on the fixture, the injection mechanism and any remaining unqualified statuses. Apply the approved scope above when deciding which gaps block bounded qualification or a broader claim; deferred or inaccessible scenarios never count as passed.

## Parity and cutover gate

Maintain a capability inventory covering shipped Suite Status, Settings, read-only TimberFold inspection, ribbon entry points, read-only external queries, installation/recovery, and every adopted draft tool. Map each to source, packaged and live evidence where applicable. Preserve no-document/wrong-target/disconnected diagnostics and configurable defaults. List deferred draft tools explicitly; source existence does not mean adoption.

Before removing an owned legacy path, demonstrate that its callers use the replacement, required behavior is qualified and rollback remains available. Python retirement covers RevitThyme-owned product and repository automation; external TimberFold remains separate. No mass deletion of unrelated pyRevit extensions or global Python installations.

Merge/release/cutover require their own authorization and exact current required checks. A milestone may have source-complete code with live qualification blocked; report both states clearly. Keep Linear In Progress until accepted work lands and is verified on the default branch.
