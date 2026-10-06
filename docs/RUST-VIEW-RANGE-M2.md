# Visual View Range M2 native source

Work: [WLK-111](https://linear.app/wlkr-labs/issue/WLK-111/m2-native-net-visual-view-range-capture-and-validated-apply). Base: [M1 PR #6](https://github.com/scwlkr/RevitThyme/pull/6), `dfbf9632c3abe82364bce0d0a09026c17623c91b`. Contract: [replacement spec](specs/RUST-WSTACK-REPLACEMENT.md) and [acceptance](specs/RUST-WSTACK-ACCEPTANCE.md). M1 is preserved in this separate stacked milestone.

## Outcome and fixed decisions

The minimal .NET adapter source connects the existing Rust range/section engine and Expo Web/Electron screen to native capture, validation and Apply. Standalone launch retains the offline fixture. A ribbon launch binds one explicit Revit process/start/session; the screen shows document/view identity and before/after references, then requires a fresh target/range confirmation. The portable bundle includes owned adapter files but never installs or registers them automatically.

The user's Rust/Axum, generated OpenAPI/TypeScript/Zod, Expo Web/NativeWind and Electron Forge choices remain fixed. The .NET 10 adapter uses `IExternalApplication`, a launch command and `IExternalEventHandler`; no Python/pyRevit runtime participates. Existing legacy automation/installation and external TimberFold stay independent.

Installed executable/API metadata: **27.2.0.39**, `.NETCoreApp,Version=v10.0`; SDK **10.0.400**. These files were reinspected before native project creation. The adapter refuses other executable builds. `RevitApiPath` overrides the local licensed SDK path; references are externally resolved by Revit and CopyLocal=false. No Autodesk binaries or .NET runtime are bundled. Approved Cargo/pnpm dependencies were reused offline, with no new software/NuGet dependencies. Tokio's existing time feature is enabled.

**Actual Revit R1–R12 remain unexecuted.** Windows compilation, real pipe/fake and packaged UI tests prove source/orchestration only. No add-in installation, bridge/API request, active-document inspection, RVT fixture, live project edit/save/sync, merge, release or legacy retirement occurred.

## Selected M2 design

- Concrete native protocol 1 replaces M1's unused provisional frame model; conformance tests now exercise production DTOs/reader. Frames: 4-byte little-endian length plus strict JSON, maximum 64 KiB. Operations: capture, geometry chunk, validate, apply, outcome, cancel. No arbitrary-code/save/sync/close operation.
- Pipes use an explicit current **user SID** DACL, first-instance reservation and PIPE_REJECT_REMOTE_CLIENTS. Handshake checks a random credential and process/start/session; Rust independently checks the OS server PID. Each ribbon window selects its own Revit instance, avoiding title-based routing.
- Windows GUI stdin failed to deliver the credential in the packaged test. A one-shot pipe is reserved before launching Electron, with the same ACL/remote restrictions and exact child-PID verification. The secret stays out of argv/environment/renderer/logs. Electron delivers the Rust credential/native binding secret over owned Rust stdin and stops only its Rust child.
- One connection and immutable native snapshot are retained. Capture limits: 1,500 candidates, 50,000 triangles, 10-minute expiry; transport uses 128-triangle chunks. Native geometry is local-document model geometry in project feet, with native instance transforms applied once. Rust alone slices it. Hidden/phase/design-option geometry may be included. Links, annotation, crop, plan regions, underlays and final plan visibility are excluded; omissions are visible.
- Queue: maximum 16 entries, 30-second queued expiry. Native outcomes: maximum 128 entries, terminal retention at least 10 minutes; active outcomes are never evicted to admit work. Rust keeps up to 128 accepted Apply IDs until restart. This is bounded deduplication, not crash-proof exactly-once execution.
- Capture/validate/apply run only in ExternalEvent.Execute; background readers, geometry-chunk/outcome/cancel reads call no Revit APIs. Idling reschedules remaining work. Model change/close and view activation invalidate snapshots conservatively. Disconnect removes queued work and facts; executing work keeps its final outcome.
- Apply resolves adapter-owned facts and rechecks exact process/document/view/revision, originals, writable project state and captured levels, then calls CheckPlanViewRangeValidity. Family/read-only/modifiable, unsupported/template-controlled and dependent/primary-with-dependents states return named exclusions.
- Native level references are decimal strings, including Current/above/below/Unlimited tokens. Relative references resolve using project elevation. Untouched Unlimited preserves its stored values. Switching an originally Unlimited plane to finite explicitly uses Current, disclosed in its label; native validity remains final.
- Identical values return verified unchanged without a transaction. A change uses one transaction inside a transaction group: Set, regenerate, readback, commit, post-commit readback **before assimilation**, then check assimilation status. Warnings/errors force rollback instead of being silently deleted. Rollback also requires original readback; Pending/inconclusive outcomes never claim success or no change. Changed IDs are stable view unique identities.
- Cached previews fetch no Revit geometry or transaction. New input revisions invalidate earlier proposals, including edits during native validation. One explicit Apply gets a new UUID; polling inspects read-only outcomes and never retries mutation. Queued cancellation differs from executing work. Unconfirmed outcomes block new review until resolved, with outcome/reconnect/refresh controls available.

## Commands and artifacts

```powershell
.\project.cmd check-m1
.\project.cmd check-m2
.\project.cmd package-m1
.\project.cmd check-m1-ui
.\project.cmd m2 ui
.\project.cmd ci --base dfbf9632c3abe82364bce0d0a09026c17623c91b --full --require-clean --report artifacts/local-ci/m2-final.json
```

The existing package-m1 route packages the extended feature and adapter, preserving its callers. `m2 build` compiles native source. Final CI/report and manifest record the checked SHA. Reports: ignored artifacts/m1 and artifacts/m2. Screenshot: artifacts/m2/native-packaged.png. Portable executable: desktop/out/RevitThyme-win32-x64/RevitThyme.exe. Manifest labels 27.2.0.39 as a **build reference**, with qualified_revit_builds empty. See [verification/review](../verification/rust-view-range-m2.md).

## Remaining runtime and distribution gates

Win32 pipe flags and client PID semantics were checked against [Microsoft CreateNamedPipe](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea) and [GetNamedPipeClientProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid). Revit calls/signatures were checked against the installed 2027 API XML and SDK build; runtime results still require approved execution.

M3 must qualify discovery/loading, real ExternalEvent/modal scheduling, native geometry/openings/transforms, level/Unlimited/native rules, restrictions, target invalidation, native failures/Pending/finalizers, rollback/readback and undo/redo. Maximum native capture latency and Revit responsiveness are unmeasured. Cross-user/remote attack trials and simultaneous actual Revit processes are unexecuted; source flags/same-user pipe tests do not establish those scenarios.

P6–P8 installation/update/uninstall/rollback, physical destinations, component compatibility/recovery, complete notices/signing and another-machine trials remain separate. Hosted CI still checks the legacy portable boundary. No installer/cutover is selected. Native restart loses bounded outcomes; unknown results require fresh independent readback and user-directed recovery, never automatic Apply.

## Exact proposed M3 procedure: approval required

This procedure is **unexecuted**. Before requesting approval, refresh the exact source SHA, manifest and adapter/core hashes, Revit build, physical paths and active document through a read-only bridge check. Diagnose an unavailable bridge; never assume document state or use a live job.

1. Ask the user to close Revit normally; never force-kill it or close a model. Proposed persistent bundle: `%LOCALAPPDATA%\RevitThyme\native-preview\<source-SHA>\RevitThyme-win32-x64`. Stage/check the complete portable folder and retain its original for recovery.
2. Verify the physical per-user Revit 2027 discovery directory before writes. Proposed registration: `%APPDATA%\Autodesk\Revit\Addins\2027\RevitThyme.NativePreview.addin`, pointing to that bundle's `resources\native\RevitThyme.RevitAdapter.dll`. Refuse existing/modified/unrelated files or redirected paths. Materialize the registration template only after installation approval; preserve legacy.
3. With separate fixture approval, have the user start Revit with no live job open and create a new non-workshared project. Proposed fixture: `%TEMP%\RevitThyme-M3-<source-SHA>\ViewRangeQualification.rvt`. Creation/changes need approval; initial saving at this exact path requires explicit save permission.
4. Create only two levels, one wall/opening, one floor and one transformed family instance, floor/engineering/ceiling plans and minimum template/dependent cases. Report exact counts/IDs after inspection. Reinspect active document/process/session before every case. Apply changes only four plane references/offsets on one approved plan, with one undo item and no source-geometry change/save/sync.
5. Run R1–R12 with independent native readback, undo/redo and approved controlled failures. Keep private evidence local. On failure, stop writes and report actual completion/rollback truth without retrying. Recovery removes only owned registration while Revit is user-closed, retains bundle/evidence and preserves legacy/TimberFold. Closing/deleting fixtures or removing files needs its stated approval.
