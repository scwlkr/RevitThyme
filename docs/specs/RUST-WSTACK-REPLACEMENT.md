# RevitThyme Rust and wstack replacement

Status: implementation specification for review; no replacement is implemented by this document.
Owner: [WLK-107](https://linear.app/wlkr-labs/issue/WLK-107/specify-rust-and-wstack-replacement-of-the-revitthyme-python-host). Decision recorded: 2026-10-06.
Companion documents: [acceptance and qualification](RUST-WSTACK-ACCEPTANCE.md) and [implementation-session prompt](RUST-WSTACK-IMPLEMENTATION-PROMPT.md).

## Outcome and authority

Replace RevitThyme's Python/pyRevit product host with a Windows application that runs domain logic in Rust and presents useful tools through the user's wstack UI. Keep only the managed adapter required to call Autodesk's Revit API. Deliver Visual View Range as the first complete feature, then migrate existing suite capabilities before retiring the owned legacy runtime.

The user chose the replacement and [wstack technical stack](https://github.com/scwlkr/wstack/blob/f75857872302a6a7c58d8c79572d7073271160fd/shared/technical-stack.md). This specification supersedes the earlier Rust-UI/egui suggestion and future pyRevit-versus-C# pilot comparison. Current shipped behavior and existing installation remain the migration baseline.

Publication of this spec authorizes documentation only. Implementation, dependency installation, installing an add-in, creating/changing a disposable Revit model, production model work, merge and release each need the applicable separate authorization. Never treat an acceptance scenario as permission to execute it.

### Fixed user decisions

| Decision | Required target |
| --- | --- |
| Product runtime | Rust domain engine; eliminate owned Python/pyRevit execution after qualified parity |
| Backend | Rust + Axum |
| UI | TypeScript, React/React Native primitives, Expo Web and Expo Router |
| Styling | Tailwind/NativeWind, owned shadcn-style components and existing RevitThyme branding |
| Windows desktop | Electron + Electron Forge; narrow preload IPC |
| Contract | Rust/Axum OpenAPI source; derived or alignment-verified TypeScript clients/types and Zod boundary schemas |
| Tooling | Cargo + pnpm; commit lockfiles |
| Revit integration | Minimal managed .NET adapter; no Python bridge dependency |
| Preservation | Existing useful behavior, existing drafts, website/logo work and external TimberFold source/geometry/baselines |

### Recommended design and validation decisions

The dataflow, source boundaries, initial snapshot limits and milestone ordering below are the proposed implementation design. Validate them through the acceptance cases before production adoption. Exact JS dependencies, contract generator, installer format and performance budgets are unresolved; choose them using current official compatibility documentation and recorded measurements. Changing a fixed decision requires returning to the user.

Initial qualification is Windows x64, Autodesk Revit **2027.2**, observed executable version **27.2.0.39**, with **.NET 10**. The connected host has .NET SDK 10.0.400, CPython 3.12.14, pyRevit 6.5.5.26237+2044 and IronPython 2.7.12; those Python versions describe the existing environment, not replacement dependencies. Reinspect before implementation: installation presence is not API execution evidence. Other Revit builds remain unqualified.

## Baseline and parity boundary

Use `master` commit `518d673511c8392daf212b559a672c9dd8000154` as the historical shipped baseline. Confirm current remote state and unpushed work at implementation start rather than resetting an active checkout.

| Source | Evidence or behavior to preserve |
| --- | --- |
| v0.2.0 | Suite Status, Settings, read-only TimberFold inspection, ribbon identity and installation recovery; scope in [release verification](../../verification/release-0.2.0.md) |
| [Draft #1](https://github.com/scwlkr/RevitThyme/pull/1), `e6b26def279d9326565eae254e78053dfb7bb7c7` | Developer validation work; preserve branch and evidence |
| [Draft #2](https://github.com/scwlkr/RevitThyme/pull/2), `5c3b6db5e312db0fc945b2badd1bce7ab2553410` | Configurable read-only Pre-Issue Check; parity if adopted, not a shipped claim |
| [Draft #3](https://github.com/scwlkr/RevitThyme/pull/3), `af05ee9d7cb2f45ead0fb0e09d86b6402b49f68c` | Serialized named queries and timeout recovery semantics; transport/client compatibility requires an explicit new contract |
| [Draft #4](https://github.com/scwlkr/RevitThyme/pull/4), `f3d1b23be2290d86c25e2f069716e0997297012f` | Visual View Range source behavior reference; has offline/source/package evidence, no actual Revit qualification |
| WLK-101 / WLK-102 | Sheet-set creation and bulk view templates remain future feature requests, not completed tools |

TimberFold remains independently maintained at `C:\Revit\TimberFold`; recorded reference is `45d49b4969d7cd67bb1bde821e20add0adf0a9b9`. RevitThyme may inspect its readiness through a defined adapter. Do not copy, port, bundle, delete or run its generation code as part of this replacement. Preserve its original geometry, frozen baselines and installations. Its Python runtime is outside the owned-runtime retirement boundary; integrating or porting it requires separate scope and license review.

## Components and dataflow

    Expo Web renderer -> typed preload methods -> Electron main
        -> authenticated loopback Axum API / Rust engine
        -> session-bound Windows named pipe -> .NET Revit adapter -> Revit API

| Component | Owns | Boundary |
| --- | --- | --- |
| Rust core | Unit conversion, range/proposal rules, geometry cache, section slicing, domain errors and result composition | Independent of Electron and Revit API types; test against synthetic fixtures |
| Rust Axum application | Application API, sessions, snapshot/proposal lifecycle and access control | Sole application HTTP backend; delegates native facts/actions to adapter |
| Electron main/preload | Child lifecycle, permitted desktop integration and typed transport | No second backend or geometry implementation; renderer cannot invoke arbitrary URLs, shell, filesystem or pipe calls |
| Expo UI | Screens, keyboard/pointer interaction, presentation, local input state and drawing computed section segments | Zod validates boundaries; Rust remains authoritative for untrusted input and domain rules |
| .NET adapter | Revit registration/ribbon launch, API-context capture, native validation, transactions and readback | Thin API translator; no duplicated section algorithm, general workflow engine or desktop UI |
| Revit | Document data, native validity and final committed state | Never accessed from a background I/O thread |

Start with a Rust core crate, an Axum application crate, a desktop package and a managed Revit adapter project. Keep Expo Router files in `src/app` and screen/tool implementations outside that route directory. Separate these responsibilities without inventing a generic plugin/RPC framework. Add shared packages only when real callers require them. Native API constraints justify small adapter-specific checks even when Rust performs earlier validation.

The OpenAPI document is emitted/maintained from the Rust API contract and checked for drift in CI. Generate TypeScript and Zod artifacts, or verify them against the source schema with independent conformance fixtures. Do not hand-maintain three competing models. Define required fields, closed enums, units, finite-number rules, bounded payloads, structured errors and protocol versions. Internal pipe DTOs form a separate small versioned execution contract; .NET does not expose a duplicate REST API.

The adapter exports native coordinates/offsets explicitly labelled as Revit internal feet and element IDs without 32-bit truncation, alongside stable unique identities. Rust owns display conversion and a documented computation-unit convention; no caller infers units or coordinate origin from a bare number. Derive geometry in project coordinates with native transforms applied once.

Initial application operations are inspect session/view, capture snapshot, compute preview, propose range, explicitly apply a proposal, and inspect request outcome. Exact route/method spelling belongs in the implementation contract. Dedicated future MCP/agent callers reuse named Rust operations. They initially have read/proposal capabilities only; an agent request must not silently acquire model-write authority from the UI's capabilities. No save, sync, close or arbitrary-code endpoint.

### Local access and process lifetime

- Bind Axum to `127.0.0.1` on an allocated port. A random startup credential is held by Electron main and Rust, delivered over an owned startup channel, excluded from arguments/logs/renderer. Loopback alone is not authentication. Authenticate every API call and bind it to the intended session.
- Use a local Windows pipe restricted to the current user with a session handshake. Reject remote clients, mismatched protocol/session identities and oversized frames. Choose framing and cancellation semantics explicitly; do not accept arbitrary method names or executable code.
- Each native connection identifies Revit process/start, session, document and view. Enumerate/disambiguate multiple Revit processes; never route by document title alone. Rust may manage multiple connections, but each operation has one explicit target.
- Ship local Expo Web assets through a tested application protocol. Enable context isolation and renderer sandboxing; disable renderer Node integration. Validate IPC senders/arguments, apply CSP and forbid untrusted navigation. Generated HTTP clients run behind Electron main/preload, not with credentials exposed to the renderer.
- Electron owns and stops only its Rust child. Revit shutdown invalidates sessions/snapshots, rejects queued requests and disposes managed registrations safely. Restart/reconnect cannot resurrect a proposal or silently execute a pending change. Do not force-kill Revit to recover a sidecar.
- Keep model geometry/snapshots in bounded memory by default. Exclude model names, private paths and geometry from public logs/releases. Any persisted diagnostic capture needs explicit scope and private storage.

## Revit adapter execution

Compile against the installed Revit 2027 API assemblies with the confirmed .NET 10 Windows target and `CopyLocal=false`; do not redistribute Autodesk assemblies. Register an `IExternalApplication` and a small launch `IExternalCommand`. External requests enter through an `IExternalEventHandler` with one bounded serialized queue. A background pipe reader may validate framing/enqueue but cannot call Revit. Avoid blocking the Revit thread waiting for IPC or recursively invoking API work.

Capture current document/view identity and canonical original values in valid API context. Revalidate when the queued operation executes, because the active document can change after enqueue. Close/document/view change invalidates the relevant snapshot; conservatively invalidate on model changes that could affect view state or captured geometry. A snapshot ID resolves to adapter-owned facts, not a client-provided copy of original state.

For apply, recheck writable/project document state, supported view, template control, dependency restrictions, referenced levels, original range and revision. Invoke native `CheckPlanViewRangeValidity` before changing anything. Reject stale or incompatible proposals with a refresh path. Report named reasons that the UI can explain; do not turn native exceptions into success.

After revalidation, an identical range returns verified unchanged values and empty changed IDs without opening a transaction or adding an undo item. For a change, use a bounded transaction inside a transaction group to produce one undo item. Configure native failure handling to force modal completion or rollback rather than treating `Pending` as committed. Set/regenerate/read back every plane/reference/offset, successfully commit the transaction, then read back again while the group remains open. Only assimilate the group after this post-commit verification passes. Check commit, rollback and group statuses; do not defer a required fallible success check until after assimilation removes rollback ability. On failure, roll back the group where possible and read back the original values; if rollback/readback or group completion is inconclusive, report an unconfirmed outcome. Never save or synchronize as part of Apply.

## Snapshot, preview and mutation contract

| Record | Required semantics |
| --- | --- |
| Target | Revit process/start and session ID; document instance identity; view unique identity; revision |
| Snapshot | Immutable target/original range, level references, units, geometry, bounds and completeness diagnostics; finite expiry/size limits |
| Preview | Snapshot ID plus monotonic input revision; Rust-derived section/proposed range; ignore stale responses in UI |
| Proposal | Snapshot-bound editable values, canonical original state retained by adapter, affected view and any known side effects |
| Apply | Unique request ID, proposal identity and explicit user confirmation bound to the displayed target/revision |
| Result | Rejected, queued, executing, applied-and-verified, unchanged-and-verified, rollback-confirmed, or outcome-unconfirmed; changed/skipped IDs and diagnostics where meaningful |

Dragging a plane uses cached snapshot geometry; it must not fetch Revit geometry or open a transaction per pointer event. Coalesce previews and discard old revisions. Rust owns section intersection and unit conversion; canvas/SVG renders its output. Changing the model/view or range makes Apply unavailable until refresh and a new confirmation. Do not transfer confirmations to another document, view, session or edited proposal.

Store bounded per-session request outcomes in the native adapter. A repeated ID with identical payload returns known status/results without rerunning; the same ID with another payload is rejected. This is bounded deduplication, not crash-proof exactly-once execution. Timeout, disconnect or restart may leave an unknown result: query the original outcome and independently refresh/read back before any user-directed new apply. Never automatically retry a mutation or reinterpret a missing response as no change.

Cancellation may remove a queued request or stop pure preview computation. Once a native transaction is executing, the caller cannot claim cancellation succeeded; wait for its native outcome. A request whose client disconnected before execution should be removed/rejected. Define limits and expiry so abandoned snapshots/jobs do not grow indefinitely, without evicting active outcomes needed for recovery.

## First feature: Visual View Range

Launch from a useful RevitThyme ribbon button for the current view. The desktop screen shows document/view identity, supported-state diagnostics and an architectural model section with labelled **Top, Cut, Bottom and View Depth** planes. Users drag planes or edit precise numeric offsets, choose supported display units, move the X/Y slice locator, Reset, Cancel or explicitly Apply. Keyboard operation and readable light/dark presentation are required.

Preserve the behavior captured in draft #4:

- Support Floor Plan, Engineering Plan and Ceiling Plan in the qualified Revit build. Refuse family/read-only/modifiable documents, template-controlled ranges, dependent views and primary views with dependents until propagation is independently qualified.
- Preserve existing level references and exact stored values when untouched. Display offsets relative to their native levels using project coordinates; support mm, m and decimal ft without rounding an untouched value into a change. Unlimited is permitted only where Revit allows it; the cut plane remains finite. Use native floor/ceiling range rules rather than a universal plane ordering.
- Extract local-document transformed model triangles in API context. Preserve openings and instance transforms in slice computation. Initial limits are configurable, bounded defaults of 1,500 candidate elements and 50,000 triangles; record omissions and partial capture visibly. Never silently label a truncated capture complete.
- Include supported architectural, structural, furniture and services geometry. The reference snapshot may include hidden geometry and geometry across phases/design options; disclose this in the preview rather than implying active-view visibility. Linked models, annotation, crop, plan regions, underlays and special visibility rules are excluded initially. This section aids range editing; it does not reproduce Revit's final plan visibility/rendering.
- Show before/after values, level references, completeness warnings, target and affected view before Apply. Reset restores the captured original; Cancel, Escape and window close cause no model changes. Changed Apply returns verified native values and one undo item; identical values return unchanged without a transaction/undo item. Stale/invalid/failed Apply yields a useful reason and refresh/recovery action.

Do not copy the Python implementation as a runtime dependency. Reuse it as a behavioral reference and translate independent test fixtures. Do not invent firm units, names, standards or title-block rules. Keep defaults configurable and schema-versioned.

## Migration milestones

| Milestone | Useful deliverable | Exit gate |
| --- | --- | --- |
| M1: Offline feature slice | Rust range/slice core + real Expo Web screen in Electron with synthetic architecture fixture, typed Axum/preload contract and adapter contract tests | Drag/numeric/reset/cancel/apply-preview behavior works; malformed/stale inputs rejected; measured preview behavior; actual packaged navigation/assets exercised |
| M2: Complete native source | .NET capture/apply adapter and complete Visual View Range orchestration, Windows build and package | Offline acceptance and API/build checks pass; missing Revit remains a visible unqualified gate; no installation implicit |
| M3: Approved Revit qualification | Installed disposable-fixture trial of the complete tool on the exact host | Approval obtained first; all live acceptance/rollback/readback/undo scenarios recorded at exact SHAs; actual Revit execution distinguished from mocks |
| M4: Suite parity and distribution | Status/settings/read-only TimberFold inspection, adopted QA/query capabilities, qualified install/update/uninstall/rollback | Inventory mapped to behavior evidence; second-machine scope explicit; no Python needed by migrated product workflows |
| M5: Owned legacy retirement | Retire owned Python host, pyRevit extension and Python-dependent repository automation after parity | Qualified replacement and approved cutover/rollback; legacy-only files removed in reviewed changes; external TimberFold and unrelated pyRevit installations preserved |

M1 must end with an interactive feature, not only host scaffolding or system checks. Keep new tools such as sheet sets/templates as separately scoped follow-ups; reuse the proven capture/propose/confirm/apply pattern when they are implemented. Do not force old drafts to merge as a prerequisite or rewrite their branches blindly. Record adopt/defer decisions per capability.

The end state includes migrating RevitThyme-owned Python automation to the Cargo/pnpm tooling where needed. During transition, label legacy source/package checks honestly; their passing does not qualify the Rust replacement. Retire dependencies only after their actual callers are replaced and verified.

## Packaging and recovery

Package Windows x64 Expo Web assets/Electron, the Rust MSVC sidecar and the managed adapter with a versioned manifest. Keep native executables outside ASAR, verify launch paths in the built installer, and bundle only redistributable dependencies. Manifest records source revision, hashes, component/protocol versions and supported Revit build(s). Verify the pnpm layout required by Forge (`node-linker=hoisted`) and mutually compatible Expo/RN/NativeWind/Tailwind versions; no invented version pins.

Choose an installer that can manage only owned files and an appropriate per-user Revit registration. Confirm actual Revit 2027 add-in discovery/native paths; do not infer them from older years or Codex's redirected AppData view. Refuse unexpected destinations and changed owned files rather than overwriting unrelated content. Stage/check hashes before replacement, require Revit to be closed for adapter replacement, retain a known compatible previous bundle, and roll back the component set together. Do not update a loaded DLL or force-close the user's Revit process.

Settings migrations preserve a backup and schema version; rollback handles compatibility explicitly. Initial updates are explicit user actions, with inspectable version/scope; no silent remote executable loading. Uninstall removes owned registration/files and preserves user data according to explicit choice. Preserve v0.2.0 separately until approved cutover; do not uninstall pyRevit globally. Check licenses/notices for new dependencies and retained code; a language rewrite is not evidence that obligations disappeared.

## Non-goals and unresolved choices

No PostgreSQL, Docker, cloud backend/account, mobile/macOS/Linux deliverable, general plugin marketplace, universal RPC engine, Rust-native UI, WPF product UI, multi-version Revit claim or Revit/pyRevit fork is required. Add a layer/target only for an accepted behavior that needs it.

Before implementation records a buildable lockfile, select the OpenAPI/Zod generation approach and compatible dependency versions. Before live qualification, settle the adapter install/discovery path and provide the exact disposable-fixture procedure. Before release, measure performance/limits, choose packaging/update format, resolve redistribution obligations, approve the parity inventory and define the supported build matrix. These decisions must not displace the feature work or be reported as completed capabilities.

## Sources and implementation handoff

- [Pinned user stack](https://github.com/scwlkr/wstack/blob/f75857872302a6a7c58d8c79572d7073271160fd/shared/technical-stack.md).
- [Expo Web production export](https://docs.expo.dev/workflow/web/) and [Tailwind/NativeWind compatibility guidance](https://docs.expo.dev/guides/tailwind/).
- [Electron security](https://www.electronjs.org/docs/latest/tutorial/security) and [Electron Forge](https://www.electronforge.io/).
- [.NET named pipes](https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-use-named-pipes-for-network-interprocess-communication) and [current-user pipe option](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0).
- Revit API assertions must be checked against the installed Revit 2027 SDK/XML and actual approved execution; source documentation alone cannot establish transaction/readback behavior.

Start an implementation session with the [short prompt](RUST-WSTACK-IMPLEMENTATION-PROMPT.md). Read [acceptance and qualification](RUST-WSTACK-ACCEPTANCE.md) before claiming any milestone complete.
