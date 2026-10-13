# RevitThyme project

## Purpose

Build a reusable Revit tool suite with a controlled execution host and an MCP interface for Codex. People and AI clients should invoke the same tool operations and receive verified results. TimberFold is the first tool.

Project root: **C:\Revit\RevitThyme**. Product ID: `revitthyme`. Foundation created: **2026-10-02**.

## Accepted direction

- Product name: **RevitThyme**.
- On 2026-10-13 the user approved the WLK-238 isolated MCP installation/fixture plan and removed the additional approval requirement for requested development, installations and disposable Revit qualification. Proceed within that work without asking again; preserve working models, isolate qualification and retain operation-specific save/sync/close and publication boundaries.
- On 2026-10-12 the user set Visual View Range's default to imperial feet/inches with nearest 1/8-inch display and imperial editing increments (WLK-234). Metric choices remain available. Formatting, focusing and unit changes preserve exact untouched native values; only edited imperial offsets snap to the requested increment. Candidate 19d0806 is installed and locally verified with packaged checks and native UI preview/review/reset in a separate Revit 27.3 test session; see [verification and limits](verification/imperial-units.md).
- On 2026-10-12 the user requested native Visual View Range verification and reversible pyRevit disconnection for future Revit work. WLK-233 updates the candidate to installed Revit 27.3.0.28 and owns the loader/MCP cutover. Preserve old bundles, Python sources, TimberFold and rollback records; do not use legacy Routes/Jev as a fallback. General working-model MCP operations and the pyRevit-based TimberFold ribbon still need native implementations. The working session has unsaved changes and must remain open unless a specific save/close decision is supplied. See [native cutover](docs/NATIVE-CUTOVER.md).
- WLK-233 locally qualified and installed source 225bdf2 in a separate Revit 27.3 session without pyRevit, including reviewed native Apply and Undo. Future startup registration and Codex Python/Jev connections are disabled with rollback copies; already running working sessions keep their loaded runtime until normal restart. Native test MCP remains fixture-bound. See [actual-Revit evidence and limitations](verification/native-cutover.md).
- Visual identity: an agricultural, eco and plant feel, using the supplied teal building/thyme wordmark, green leaves and warm timber accents. Ship distinct light/dark ribbon icons with editable vector sources. Tool dialogs follow the compact Autodesk-style [UI guidance](docs/UI-GUIDELINES.md) accepted on 2026-10-12: restrained styling, aligned controls, minimal helper text and space devoted to working content. WLK-232 implements this in the desktop candidate using [owned, reusable dialog components](docs/UI-COMPONENTS.md). The installed preview and its actual-Revit qualification remain separate.
- Prepare a separate project with general documentation and TimberFold as its starting tool.
- Keep the host's responsibilities separate from each tool's implementation.
- Preserve TimberFold at **C:\Revit\Projects\TimberFold**, including its repository, working installation, baselines and generated artifacts.
- Support future use through a Revit interface and MCP, with a release that can be installed on a second computer.
- Use wstack project instructions and a Rust CLI for repository automation; retain the Revit-specific host/worker languages. Track substantive work in the [RevitThyme Linear project](https://linear.app/wlkr-labs/project/revitthyme-fea9af3040c9) under WLKR LABS. The setup follow-up authorizes needed dependencies and local CI, with no product migration.
- Use `master` as the local default branch, matching the existing Git default. Land verified local work there; publishing/pushing remains an explicit separate request.
- On 2026-10-06 the user granted standing approval for installations needed on this project and normal Revit close/open in its approved test session. Do not ask again for an already authorized project installation. Preserve unsaved changes unless their discard is authorized; fixture/model writes and save/sync retain their separate request boundaries. This does not authorize live-job edits, legacy retirement, merge or release.
- The same day's subsequent full approval includes temporary test-view changes, native Apply/rollback/Undo/Redo tests and needed repairs/install/restarts on the named disposable `Revit-Plugin-Test-House.rvt`. Restore original settings and discard test changes when restarting; keep the file unsaved. Do not request the same test/install approval again. Live jobs, save/sync, legacy retirement, merge and release remain outside this qualification work.
- The user then accepted the [bounded M3 qualification list](docs/specs/RUST-WSTACK-ACCEPTANCE.md#approved-bounded-m3-qualification-scope): keep practical checks 1–9, require check 10 before claiming Python-free operation, defer broader checks 11–14 as described, and move suite migration/legacy cutover (15) to replacement M4/M5. Initial qualification is one Revit instance on the current host. Preserve automated coverage and completed evidence; deferred/unexecuted cases are not passed.
- WLK-114 rejects unresolved native Level Above/Below placeholders with named-level guidance, rather than guessing their elevation. Actual resolved native choices store positive level IDs and stay supported, as do Current and allowed Unlimited. Preserve model references; never convert them automatically. The user explicitly accepted this restriction and authorized completing verification, merging the reviewed changes and preparing an unpublished release on 2026-10-06. This supersedes earlier no-merge statements. Models stay unsaved; existing tools stay installed. Publishing, second-computer distribution and suite retirement remain separate steps.

## Replacement target agreed on 2026-10-06

The user chose a full replacement of RevitThyme's owned Python/pyRevit runtime: Rust/Axum domain logic, the wstack Expo Web/Electron Forge UI, typed OpenAPI/TypeScript/Zod boundaries and a minimal .NET 10 Revit adapter. The initial native target is the observed Windows Revit 2027.2 build. See the [replacement specification](docs/specs/RUST-WSTACK-REPLACEMENT.md), [acceptance gates](docs/specs/RUST-WSTACK-ACCEPTANCE.md) and [implementation-session prompt](docs/specs/RUST-WSTACK-IMPLEMENTATION-PROMPT.md).

This future target supersedes the host comparison below and the earlier Rust-native UI suggestion. No replacement is implemented or installed by the specification PR. Existing functionality, drafts and installation remain preserved during migration; external TimberFold remains independently owned. Implementation, installations, live Revit fixture work, merge and release require applicable separate authorization.

## Visual View Range readability and direction follow-up

The 2026-10-06 bounded workspace cleanup (WLK-122) places native Visual View Range on the shared RevitThyme tab under View Tools. Adapter 0.2.5 creates that tab when pyRevit is absent and reuses it when present; commands, protocol, model behavior and preview support scope stay the same. This local, unmerged cleanup build is separate from the preserved unpublished 0.3.0-preview.1 candidate and completed M3 record. No publishing, merging, distribution or legacy retirement is authorized by cleanup.

[WLK-117](https://linear.app/wlkr-labs/issue/WLK-117/visual-view-range-readable-labels-and-look-uplook-down-plan-support) continues the exact M2 source from draft PR #7. Plane callouts are separated without moving physical elevations. Native captures carry main plan direction (floor down, ceiling up, structural type up/down) separately from underlay orientation, resolved level elevations, names/references and Unbounded top. Rust range/depth rules use the captured main direction; Revit validity remains authoritative. The UI displays both settings, keeps the section elevation axis upright and annotates the underlay level band and look-up/look-down sight direction separately; halftone and final projected visibility remain controlled by Revit. This required contract extension uses protocol 2 and a compatible component bundle (Rust/desktop 0.1.1, adapter 0.2.1). See [decisions and verification](docs/RUST-VIEW-RANGE-ORIENTATION.md).

The earlier WLK-117 and WLK-114 bundles remain preserved. WLK-114 continues PR #8 at `db0b43a388d87f3b9cbf400f1d13fbdc6adf7f2d`. The remaining bounded trials ran on `080e13dbf421434daa779f0ba62e1f53c4f5fd26` (adapter 0.2.2); native edit-mode and unresolved-relative defects led to checked, installed and selectively requalified source `645af30b13528169ca6282321bdc63e133ac91e3` (adapter 0.2.4). Native edit readiness is checked independently of IsModifiable; bare Above/Below placeholders reject instead of guessing their elevations. Trials 1–10 are complete within the accepted scope, including a normal native startup/capture/Apply/Undo trial with pyRevit disabled. See the [current numbered M3 assessment](verification/rust-view-range-m3-remaining.md) and [user guide](docs/VISUAL-VIEW-RANGE.md). Source/package checks, historical actual results and final-bundle results retain separate revisions. Temporary fixtures and settings stay inside the approved test house and are rolled back/undone; normal restart discards unsaved state. Multi-instance and second-machine qualification remain deferred; no aggregate R1–R12 qualification is claimed. The release candidate is prepared locally and remains unpublished.

## Existing foundation implementation plan

Replacement M2 source on [WLK-111](https://linear.app/wlkr-labs/issue/WLK-111/m2-native-net-visual-view-range-capture-and-validated-apply) continues M1 PR #6. The minimal .NET 10 adapter implements API-context capture, session-bound local pipes, serialized ExternalEvent execution and validated transaction/group Apply with rollback and pre/post-commit readback. Rust and the existing UI orchestrate confirmation, outcomes and queued cancellation. See [M2 decisions/evidence boundaries](docs/RUST-VIEW-RANGE-M2.md). Windows offline/packaged orchestration is verified; actual Revit qualification remains M3. That M2 source report predates the separately approved preview installation described above; the legacy host and external TimberFold remain preserved.

Replacement M1 is implemented on WLK-109 as a synthetic offline Visual View Range feature: Rust/Axum, generated OpenAPI/Zod/TS and an Expo Web/Electron Forge package. See [feature and evidence boundaries](docs/RUST-VIEW-RANGE-M1.md). Native .NET capture/apply remains M2; approved Revit qualification remains M3. The installed 0.2.0 host, existing drafts, website/logo work and external TimberFold remain preserved.

The accepted first shipping host is a custom pyRevit extension, using upstream pyRevit without a fork. Publish approved code and docs to the user personal GitHub account scwlkr; private models and settings stay excluded. License original RevitThyme code GPL-3.0-or-later. The existing bridge remains the development adapter. A focused C#/.NET Revit host is the intended independent-host pilot. Its runtime target, project structure and install method will be confirmed against the installed Revit SDK before implementation. The two adapters should be compared through the same operations before choosing the production host.

This plan does not create a fork of Autodesk Revit or pyRevit. A pyRevit fork would be a separate decision if a concrete platform limitation requires it.

The operation names, job states and proposed source layout in [ARCHITECTURE.md](docs/ARCHITECTURE.md) are drafts. Implement only the next bounded milestone, and update the docs when the interface becomes accepted behavior.

## Current state and evidence

The 2026-10-13 custom MCP request is tracked as WLK-238. Build on the recent native lab/UI source at `6104656081738e8ff81d51f065d23e4f75a34252`, preserving installed bundles and the default branch. The first slice adds native context, typed paged inspection and shared Rust project-health rules; see [native MCP](docs/NATIVE-MCP.md). Source/offline checks do not extend actual-Revit qualification. Retain the native-only path and fixture/process/document guards; wider editing/export/family tools need their own named operations and acceptance.

On 2026-10-12 the user approved installing the native test connection and creating/saving/reopening its new disposable fixture. The user also removed the blanket exact-step installation/fixture approval sentence from `AGENTS.md`. This does not authorize changes to working models, legacy retirement, publication or distribution. Test-run repair/rotation preserves prior fixtures and replaces only the identified test connection with backups.

WLK-229 implements a separate native test adapter and Rust stdio MCP connection, sharing the existing native Visual View Range code. The accepted test design has no pyRevit dependency, a unique process/session pipe and an exact fixture/document guard in Revit API callbacks. The first prepared run is read-only. SDK compilation and Windows MCP/guard tests are offline evidence; approved installation and actual simultaneous-instance qualification follow [the native lab guide](docs/NATIVE-REVIT-LAB.md). The installed executable and SDK are now 27.3.0.28; historical 27.2 results remain historical. Preserve the working session and installed tools.

| Item | State |
| --- | --- |
| Project docs and local tool registry | Created in this foundation |
| Read-only project diagnostic | Implemented in `scripts/check-project.ps1` |
| Project automation CLI | Implemented with Rust 1.99.0; Windows help/doctor, project diagnostic and CLI boundary checks verified |
| Local CI | Implemented through `project ci`: scope routing, format/lint/build, real CLI checks, setup readiness/idempotence and Cargo cache checks; exact-SHA results in ignored artifacts/local-ci |
| TimberFold implementation | External checkout; read-only inspection integrated, generation not integrated |
| RevitThyme pyRevit adapter | Implemented read-only operations in v0.2.0; live evidence recorded separately |
| Native C# host | Minimal .NET 10 replacement preview implemented; bounded actual Visual View Range evidence in verification/rust-view-range-m3.md; full suite parity unimplemented |
| RevitThyme ribbon and MCP server | Earlier WLK-229 native test MCP installed/qualified; WLK-238 context/inspection/health source added, new installation and actual-Revit qualification pending; broader suite parity remains separate |
| Installer / boss's computer pilot | Versioned ZIP and per-user installer implemented; second-computer pilot unverified |
| Live RevitThyme validation | See verification/release-0.2.0.md for exact scope and remaining gates |

The first-host cold-start smoke test passed on 2026-10-05: the user confirmed icons and Suite Status, native ribbon read-back found images on all three buttons, and eight installed read-only Routes checks passed. Earlier repairs and images had landed in Codex's MSIX AppData cache instead of native Revit's folders. They were reapplied to verified native paths; the installer and developer repair now refuse redirected destinations. The loaded Routes repair was read back in Revit. Repeated Reload stability and another-machine behavior remain unverified; use normal Revit startup on this host. This local maintenance patch does not establish a general pyRevit fork or wider version support.

TimberFold source was inspected on 2026-10-05 at commit `45d49b4969d7cd67bb1bde821e20add0adf0a9b9`, on its active setup branch with a clean working tree. It is maintained independently and is not bundled. Its metadata declares version **1.1.0**. Its active runtime is `C:\Revit\Projects\TimberFold`; `C:\Revit\Projects\Legacy\Laser-Model` is a preserved legacy copy.

The TimberFold docs record completed digital workflows, including Cedar Cottage, and remaining physical-fit work. Those are source-project records, not new live validation by RevitThyme. Consult the tool's current PROJECT.md and verification artifacts before relying on them.

## First work

1. Validate installed v0.2.0 read-only operations and packaged ribbon. Preserve no-document, wrong-target and disconnected diagnostics. The public package has no TimberFold generation implementation.
2. For replacement work, follow the [specification milestones](docs/specs/RUST-WSTACK-REPLACEMENT.md#migration-milestones), beginning with a working Visual View Range feature. Confirm current SDK/runtime before native implementation.
3. Keep the [foundation roadmap](docs/ROADMAP.md) as historical scope/evidence; replacement acceptance is defined in the new spec.

## Open decisions

- Company display name and final ribbon layout; the first three branded button icons are implemented.
- Exact replacement dependency versions, contract generation and packaging/update format; host direction is fixed by the replacement decision.
- Supported Revit builds beyond the initial replacement target; legacy pyRevit compatibility remains separately tracked.
- Portable TimberFold bundling and second-computer verification; per-user extension ZIP is the first installer format.
- TimberFold redistribution license and third-party dependencies before bundling; RevitThyme original code is GPL-3.0-or-later.
- Final approval UX for model-changing operations and long-running job cancellation.

## Project continuity

Keep accepted decisions here, architecture in docs/ARCHITECTURE.md and completed work in CHANGELOG.md. Record future runs with source revision, host/tool versions, settings and verification scope. Use local Git checkpoints; the requested public source repository is https://github.com/scwlkr/RevitThyme.

The setup handoff and explicit stack exceptions are in [SETUP-TODO.md](SETUP-TODO.md). Setup is tracked as [WLK-95](https://linear.app/wlkr-labs/issue/WLK-95/apply-wstack-project-setup-to-revitthyme). Local CI validates repository automation; live Revit verification remains a future milestone. The setup skill's CI inventory intentionally reports unverified text hints; use actual CI reports for this project's alignment evidence.

Implementation and public release foundation are tracked as [WLK-97](https://linear.app/wlkr-labs/issue/WLK-97/build-pyrevit-extension-and-versioned-open-source-release-foundation). Public issues are contribution intake; maintainer execution remains in Linear.
