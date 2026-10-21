# Changelog

## Unreleased

## 0.11.0 — release candidate (unreleased)

- Prepare product 0.11.0 / package `0.11.0-mcp.1` for the reviewed MCP image, discovery and lifecycle operations, retaining protocol 13 and 32 tools. Require the complete toolkit payload and matching product identity, preserve earlier cohorts, and pin the optional draft workflow to the new source tag. Rebuilt-package, installer and native qualification precede publication; earlier native results retain their original provenance. Physical fabrication remains outstanding.

- Extend electrical-lab qualification to a single 418-assertion external SDK invocation. Two real native-warning transport timeouts recover after continuous Pending polling of their original GUIDs, without restarting the client or replaying operations. Keep explicit warning intervention, historical acceptance, image-proof limits and last-active-close constraints visible in [verification](verification/electrical-lab.md).

- Qualify the current electrical-lab edit/inspect/image/save/reopen loop through a real external MCP client against the unchanged installed host. Add fixture-specific original-request recovery and direct postcommit companion inspection, distinguishing 12 native additions from 11 collector-visible elements. Preserve the later source revision separately from outstanding historical Fine-detail acceptance; native warning intervention and image-evidence limits are recorded in [electrical-lab verification](verification/electrical-lab.md). No release or protocol change.

- Refuse explicit save before calling Revit when the document was opened from a read-only file. Return retained `read_only_file` rejection instead of discovering this known restriction through an uncertain native save failure. In-memory editing and read-only-file open/activation remain unchanged; no SaveAs or implicit save/discard. Candidate `0.10.0-lifecycle.5` retains protocol 13 and 32 tools.

- Add shared read-only document discovery through MCP `list_documents` and CLI `list-documents`, including at Home. Return exact session-native IDs and document flags with bounded pages; native-session inventory fingerprints reject continuation after background opens/closes or metadata changes without changing lifecycle semantics. Fixture-bound sessions expose only their fixture. Candidate `0.10.0-lifecycle.4` uses protocol 13 and 32 tools; install matching components together. Native qualification is recorded separately in [document discovery](verification/document-discovery.md).

- Correct `list_operations` guidance to describe the already-supported explicit clean-document handoff for active close, including Unconfirmed recovery and the remaining last-document-to-Home limit. Align architecture guidance with the independent outcome pipe. No native operation or protocol change.

- Qualify the existing MCP outcome channel while native dialogs block document opening: real transport timeout and lost-server responses recover the original result after explicit test-dialog intervention. Document Pending response IDs and retain the original caller GUID. Production protocol, tool inventory and installed host remain unchanged.

- Extend `close_document` with an explicit already-open clean document to activate before closing the captured active project. Revalidate both native identities and saved hashes after the synchronous handoff; partial activation or cancelled close remains retained and Unconfirmed. Fix cancelled native closes changing still-open document identities. Candidate `0.10.0-lifecycle.3` uses protocol 12 with the same 31 MCP tools; last-document close to Home remains unsupported.

- Extend MCP lifecycle to Home/no active document, explicit clean background-document close and exact native view/sheet switching. Reject active/wrong/dirty/changed-file closes before side effects, and poll retained outcomes through an independent process-verified pipe. The unreleased `0.10.0-lifecycle.2` candidate has protocol 11 and 31 tools. No implicit save/discard or changed-ID budget expansion.

- Add shared explicit document open, activation and active-document save with three MCP tools. Require exact captured context, canonical local nonworkshared RVT paths, document IDs and expected disk hashes; verify native completion and retain original request recovery outside model transactions/Undo. The unreleased `0.10.0-lifecycle.1` candidate uses protocol 10 and 29 tools; published v0.10.0 is unchanged.

- Fix unfiltered native element queries with `includeTypes:true`, and align the pinned development launcher with protocol 9. Installed-host qualification checks complete element/type pagination as well as native edit, image and readback.

- Recognize `0.10.0-image-export.1` in the installer/setup gates while retaining the complete existing toolkit payload requirements. Add external SDK qualification against the immutable installed MCP executable and normally started native host, without test host wiring or export callbacks.

- Add the shared `ExportViewImage` operation and MCP `export_view_image` tool for one explicit native view/sheet PNG. Reuse the existing ExternalEvent queue, captured-target checks, request-ID replay and `get_operation` recovery. Decode/hash the PNG, verify unchanged native state, and return structured file metadata plus an inline MCP image without a model transaction, activation or save.
- Bound images to 128–4096 pixels and 8 MiB in host-owned private request folders. Reject unsupported/missing/stale targets, preserve partial output on unconfirmed completion and refuse to regenerate changed retained artifacts. The candidate has 26 tools, protocol 9 and build identity `0.10.0-image-export.1`; it requires matching binaries and is not part of published v0.10.0. See [contract](docs/MCP.md#exact-view-and-sheet-images-unreleased) and [verification](verification/view-images.md).
- Add a resumable website publication workflow: prepare a pinned candidate, require owner approval for its source/digest/Site, publish through native Sites tools, verify all live files and managed GitHub settings, then merge a baseline-only PR after CI. Preserve operation receipts to reconcile interrupted deployments; read-only drift monitoring remains separate.

## 0.10.0 — TimberFold — 2026-10-19

- Add native C# TimberFold inspection, retained previews, fitting, stock layout, inch-unit DWG/DXF cutting files, calibration coupons, source-mapped guides and annotation-only review sheets. Generation preserves source geometry and saved files, and groups native documentation into one Undo item.
- Share seven TimberFold controls through the native window, CLI and official-SDK MCP server: inspect, preview, generate, status, cancel, verify and read-only reconcile. The MCP inventory is now 25 tools; protocol 8 requires matching add-in, CLI and MCP binaries. The qualified package is `0.10.0-timberfold.4`.
- Qualify four original editable buildings, independent native documentation/DWG readback, canonical repeatability, source restoration, export/publication/cancellation recovery and a complete 100-panel generation in 23.35 seconds. Preserve exact candidate provenance and historical evidence in [TimberFold qualification](docs/timberfold/qualification.md).
- Include the Windows setup and a small physical-test ZIP with calibration, an original six-panel gable example, cutting files and guides. Measured material/kerf, physical cutting, joint/web strength and assembly remain outstanding; `ManufacturingReady` remains false. The C# replacement is accepted. Retire the old active source checkouts and installed launcher after verifying recoverable private source/model/history archives; other add-ins and models remain untouched.

- Add daily read-only checks of deployed website files and GitHub presentation against reviewed baselines. Report unpublished source separately; deployment and baseline approval remain maintainer actions.

- Rebuild the project website with Docusaurus, native-tool screenshots, local search, accessible navigation and user/developer/agent entry points.
- Refresh project and contribution guidance; add first steps, CLI, troubleshooting, agent integration, community and governance documentation.
- Generate website content and agent indexes from canonical repository Markdown. Derive website release metadata and MCP inventory from source; add strict website CI and scheduled drift checks.
- Prepare updated GitHub presentation settings and a local review workflow; public deployment remains a separate approval step.

## 0.9.3 — Parameter identity — 2026-10-15

- Add native shared status, nullable shared GUID, nullable built-in parameter identity and native `HasValue` to parameter inspection and readback through the existing CLI and MCP contract. Guard shared GUID access with `IsShared`.
- Add CLI `inspect-elements` with parameter-page continuation. Preserve exact document-local parameter IDs, raw values, read-only behavior, pagination and expected-value edit safeguards.
- Qualify same-name built-in/shared fields, distinct shared GUIDs, nonshared fields, assigned/unassigned/zero/empty values, pagination, inspection without model changes and exact-ID edits in a disposable Revit 2027 fixture. See [parameter identity verification](verification/parameter-identity.md).

## 0.9.2 — Native ribbon and SVG icons — 2026-10-15

- Organize the ribbon into Views, Annotation and RevitThyme panels. Keep View Range, Inspect View, Room Dimensions and Open Agent directly accessible; place Check for Updates in the toolkit slide-out.
- Add original SVG optical icon masters with light/dark and display-density exports, native theme refresh, clearer labels, extended tooltips, command help and appropriate context availability.
- Establish reusable ribbon organization and SVG design standards, with a one-line skill pointer for future ribbon work. Preserve existing modeling workflows and installer/update behavior.
- Panel moves change Revit-generated command paths; existing custom keyboard shortcuts or Quick Access entries may need reassignment.

## 0.9.1 — Installed version reporting and global CLI — 2026-10-15

- Show executing CLI, development checkout and registered RevitThyme versions separately. Verify registration, immutable bundle identity, relevant hashes and receipt agreement. Compare updates against the installed copy, so an up-to-date development CLI still upgrades an older Revit installation.
- Automatically expose native `rthyme` through an owned Windows user PATH entry during setup, without an SDK or administrator. Upgrades switch to the new immutable bundle; uninstall and failed installs preserve unrelated PATH entries and developer tools. Keep the optional source snapshot through `project.cmd cli-install`.
- Preserve Revit-open guards, no-downgrade behavior, native argument handling and exit codes. Record explicit product versions in package manifests; older bundles fall back to assembly product metadata rather than mistaking package suffixes for release versions.

## 0.9.0 — Built-in Agent and update checks — 2026-10-14

- Add the dockable Codex Agent, ChatGPT sign-in, streamed conversations, native approvals, interruption, saved session resume and ordinary skills. Bundle the pinned official Codex CLI 0.162.1 with the add-in, CLI and MCP. See [actual Revit Agent qualification](verification/agent.md).
- Check stable releases quietly once per day after Revit becomes idle. Add a manual ribbon check and verified installer download. Install only after Revit is closed; preserve Agent state, registrations and earlier bundles.
- Report the actual version and source commit through CLI `--version` and `version --json`. Add `update check`, `update download` and `update install`; development builds do not automatically downgrade. See [commands](docs/UPDATES.md) and [verification](verification/updates.md).
- Keep Exploration deferred. The Windows installer supports full Revit 2027 and remains unsigned.

## 0.8.1 — Windows PowerShell bootstrap fix — 2026-10-14

- Handle Windows PowerShell 5.1's nonterminating maximum-redirect error while retaining and checking the HTTPS redirect response. Keep strict host, checksum and asset-digest verification.
- Verify real public installer downloads in PowerShell 7 and Windows PowerShell 5.1. Add a regression for the observed redirect behavior. Installer patch only; the bundled native toolkit remains 0.8.0 with protocol 4.
## 0.8.0 — Windows installation — 2026-10-14

- Implement only the install experience on plan lines 81–85: one per-user Windows setup executable and a checksum-verified GitHub Release bootstrap, bundling the existing add-in, CLI and MCP.
- Detect full Revit 2027 through Autodesk registration/default paths and verify the .NET 10 x64 runtime. Preserve immutable bundles, owned registration backups and foreign add-ins; refuse installation while Revit is open.
- Add executable install/uninstall and bootstrap checks, stable GitHub Release asset names and a manually dispatched review/draft workflow. The installer is unsigned; Built-in Agent and Exploration remain deferred.

## Unreleased — C# Dynamic execution — 2026-10-14

> Historical milestone note. This and the dated development entries below preserve their original status at the time; they are not the current release backlog. See the first **Unreleased** section for pending changes and the versioned entries above for shipped releases.

- Add opt-in trusted Roslyn C# query/modify through the existing Core, native ExternalEvent, CLI and official-SDK MCP path (18 tools, protocol 4). Queries always roll back; modifications share the native transaction wrapper and one Undo item. Return bounded JSON, mapped compiler diagnostics, source hashes, elapsed time and native change IDs with retained request recovery.
- Add disposable process/model qualification and an independent 100-point native Cabin Eval with a temporary 14-to-18-foot parameter probe, negative controls and separate execution metrics. Keep Built-in Agent and Exploration deferred.
- Package pinned Roslyn dependencies and document process trust, cooperative budgets and recovery. See [Dynamic execution evidence](verification/dynamic-execution.md).

## Unreleased — C# Native utilities — 2026-10-14

- Add native WPF View Range and rectangular Room Dimensions ribbon tools with shared C# operations exposed through CLI and four additional official-SDK MCP tools. Preserve the completed Foundation/MCP implementation.
- Validate exact prior ranges and reviewed room plans, use native finish-face references and tag bounds, avoid duplicate dimension pairs, verify native readback/rollback and group each edit into one Revit Undo item. Upgrade clients/add-in together to bridge protocol 3.
- Share an existing View Tools ribbon panel using distinct command IDs, preserve legacy installations and provide isolated native ribbon/WPF/MCP/CLI qualification plus an allowlisted local utilities package. See [Native utilities evidence](verification/native-utilities.md). Dynamic execution, Cabin Eval, built-in Agent and Exploration remain deferred.

## Unreleased — C# MCP Development — 2026-10-14

- Add official C# SDK stdio MCP with 12 generic tools and external Codex support, sharing the Foundation operation layer and native pipe. Add paged native inspection, selection, atomic typed parameter edits, retained outcomes, value-based replay, cancellation and protocol-2 guards.
- Package the add-in, CLI and MCP together with pinned dependencies and license notices; preserve Foundation upgrade/rollback and existing installations.
- Pass 50 Foundation and 33 MCP offline checks, 58 live MCP checks, 50 independent native checks, three external Codex rounds (78 calls, zero failures) and 11 isolated installer/upgrade checks. See [MCP evidence](verification/mcp.md). All subsequent stages remain deferred.

## Unreleased — C# Foundation — 2026-10-14

- Adopt the full new plan and complete only Foundation: shared C# operations, native Revit add-in, WPF Inspect View and a basic C# CLI. Rename View uses captured identity, verified readback, rollback and one Revit Undo item.
- Replace active Rust/Electron/pyRevit build, developer tooling and CI with .NET 10. Preserve superseded source/evidence under `legacy/pre-foundation` with a verified 216-file migration inventory.
- Add an allowlisted local package, immutable per-user installation and recoverable unregister. Keep Autodesk SDKs, private models, historical builds and test loaders out of distribution.
- Pass 50 offline checks, 17 native Revit 27.3.0.28 checks, 12 real CLI checks and 7 isolated installer checks. See [Foundation evidence](verification/foundation.md). Later stages remain deferred.


## Unreleased - Wstack 0.2 setup refresh - 2026-10-13

- Move general project workflow/verification notes to `w.md` and retain a short AGENTS.md pointer with the existing Revit-specific rules and authorization. Add current feature/glossary pointers without replacing the CLI or product architecture.
- Add project-owned `check-wstack` verification for notes/pointers and actual CLI help/doctor. Replace local CI's retired separate setup-skill dependency; preserve the remaining checks. Record the global worker's Windows timer failure and local fallback separately from native product qualification.

## Unreleased - Native MCP inspection milestone - 2026-10-13

- Add `revit_context`, `revit_elements` and `revit_project_health` to the native test MCP. Read exact identity without geometry, inspect category pages/64-bit IDs and evaluate six review checks in shared Rust logic. Enforce target/revision guards in native callbacks, exact inputs and bounded results. Incomplete scans retain lower-bound counts; no automatic fixes or production-model enablement.
- Bound pages by serialized bytes with valid continuation, health reads by row/page/byte/time budgets and MCP output by bytes. Evict terminal read receipts under pressure while retaining Apply outcomes. Add Windows regressions for known findings, missing IDs, partial scans, Unicode pagination, oversized responses and sustained inspection traffic.
- Install the read-only native inspection bundle in an isolated Revit 27.3.0.28 process. Ten actual-Revit inspection checks and eleven View Range read-only checks pass with the saved fixture unchanged and the working process preserved. Populated door/room/family mapping and larger models remain unqualified; see [evidence](legacy/pre-foundation/verification/native-mcp.md).
- Rotate the connection from an exact stopped run in another checkout, verifying its target/manifest and preserving unrelated TOML fields and enabled settings. Add a guarded external MCP-only update with separate native/client revisions, immutable artifacts and backups; keep the active Revit process and document intact.
- Isolate the lab core's CLR assembly identity while sharing its source with the ribbon adapter, so an older installed preview cannot supply incompatible core types to the new test host.
- Compare duplicate view names within each Revit view kind after actual Revit demonstrated valid repeated Level 1 floor/ceiling/structural plan names. Retain the initial failed qualification and a fail-before/pass-after regression.

## Unreleased - Imperial View Range units - 2026-10-12

- Default Visual View Range to feet/inches with reduced fractional inches rounded to 1/8 inch. Accept architectural entry, negative offsets and decimal feet; use eighth-inch imperial slider/plane-drag increments and consistent section/review/outcome labels. Retain metric options and exact untouched Revit values behind rounded display.
- Install and locally verify candidate 19d0806: clean full CI, packaged UI/native-fixture checks and actual Revit 27.3 imperial preview/review/reset. Independent native readback confirms unchanged range, captured geometry and saved fixture hash; working Revit remains open on its loaded bundle until normal restart. See [verification and limits](legacy/pre-foundation/verification/imperial-units.md).

## Unreleased - Native Revit 27.3 cutover candidate - 2026-10-12

- Target the installed Revit 27.3.0.28 in native preview adapter 0.2.6; retain the previous 27.2 bundle separately. Add native startup/runtime evidence and a guarded CLI cutover with immutable package checks, registration/config backups, reversible pyRevit disable and exact native test MCP selection. Preserve running documents and source files; broader native suite operations remain separate implementation work.
- Locally qualify source 225bdf2 on a disposable Revit 27.3 process with no pyRevit assemblies loaded: native ribbon capture, preview without mutation, reviewed Cut 4 ft to 5 ft Apply, independent readback and one Undo restoring the baseline with the saved fixture hash unchanged. Disable the pyRevit startup registration and legacy Python/Jev MCP entries reversibly; existing working windows remain open until the user chooses a normal restart. See [verification](legacy/pre-foundation/verification/native-cutover.md).

## Unreleased - Compact dialogs and owned UI components - 2026-10-12

- Restyle the Visual View Range desktop candidate as a compact Autodesk-style utility dialog: neutral light/dark surfaces, aligned plane/level/offset rows, section beside controls, concise labels and fixed bottom actions. Move detailed help and diagnostics into Help/disclosures, retain conditional warnings and reveal the Apply comparison when requested. Display offsets without floating-point noise while preserving untouched native values.
- Establish an owned shadcn-style component library for dialog layout, group boxes, buttons, forms, tables, notices and disclosures. Visual View Range, Apply review and Help compose the shared components; future tools follow the component guide. Retain React Native/NativeWind containers and explicit desktop form/table adapters.
- Verify the packaged Windows UI at normal and 125% scaling, editing/review/recovery and offline native-fixture confirmation/transaction behavior. This UI candidate is local; its new source/package evidence does not extend the installed preview's actual-Revit qualification.

## Unreleased - Isolated native test MCP - 2026-10-12

- Install the separately approved native test connection and qualify 11 read-only checks inside Revit 27.3.0.28 while the working instance remains running. Verify exact test process/document/plan, native geometry and feet/mm readback, shared preview/proposal and wrong-target/stale rejection. Preserve the first failed fixture and keep Apply disabled; live model-writing and foreground UI checks remain separate.

- Correct actual-Revit startup timing: wait for document opening, request the test plan asynchronously on a later idle cycle, and publish the connection after activation. Retain failed fixtures, support exact prior-run connection rotation with backups, and redirect the test process output so the launcher can exit. Remove the blanket installation/fixture approval sentence from AGENTS.md at the user's request; preserve the live-job qualification prohibition.

- Add a separate opt-in native lab adapter and Rust stdio MCP server for Visual View Range. Share the existing native model and Rust feature logic; pin the process/session and exact disposable document, reject wrong/reopened targets and keep credentials in Windows Credential Manager. The first prepared run is read-only. Add CLI preparation, guarded installation/startup and offline Windows MCP/transaction/target tests. Revit 27.3.0.28 SDK compilation is separate from pending installation and actual-Revit qualification. Preserve the working Revit session, legacy suite and external TimberFold.

## Unreleased - Local ribbon cleanup - 2026-10-06

- Place native Visual View Range under RevitThyme > View Tools, sharing the existing suite tab and creating it when pyRevit is absent. Keep the preview support restriction visible in the tooltip and guide. Adapter 0.2.5 retains protocol 2 and existing model operations; preserve the original 0.3.0-preview.1 candidate separately. This cleanup change remains local and unmerged.

## 0.3.0-preview.1 candidate — Visual View Range — 2026-10-06

- Complete the accepted bounded M3 trials 1–10, including a normal pyRevit-disabled native ribbon/capture/Apply/Undo trial on Revit 27.2.0.39. Keep exact source/package and actual-host revisions separate in the assessment.
- Accept the named-level restriction for unresolved Above/Below placeholders; retain Current, resolved level IDs and permitted Unlimited. Keep models unsaved and the existing suite installed.
- Add the user guide, release notes and exact-clean CI-gated local candidate preparation with binary/source ZIPs, checksums and archive round-trip verification. The candidate remains unpublished; deferred distribution and broader suite work retain their own gates.

## Unreleased - Native qualification repairs - 2026-10-06

- Complete approved practical M3 checks 1–9 with an explicit proposed unresolved-reference restriction. Requalify the exact 0.2.4 bundle in Revit 27.2.0.39 for rejection/Apply/Undo, Current/explicit references, mm, paired Unlimited, reply recovery, drag/slice, reconnect, section coordinates and responsiveness. Preserve original evidence and separate installed source/full CI from later documentation revisions.
- Reject unresolved native Level Above/Below sentinels rather than guessing a nearby level's elevation. Resolved native choices store actual level IDs and remain supported. Adapter 0.2.4 keeps protocol 2; the preview never converts model references automatically. Record this proposed restriction and the actual native mismatch in the M3 assessment.
- Add actual packaged transport and native BRep section-comparison drivers to the existing project CLI. Retain numbered stale/restriction/range/drag/queue/reconnect and responsiveness evidence separately from source/package CI.
- Reject native edit modes between transactions using the SDK's edit-scope readiness check; `IsModifiable` alone misses this state. Adapter 0.2.3 retains protocol 2. Keep the no-write actual-native regression driver outside the package.
- Identify open Revit documents through native document equality instead of managed wrapper references, retaining process/session/view/revision checks and close invalidation.
- Keep authenticated idle connections available; retain bounded handshake, incomplete-frame and reply timeouts. Add an actual Windows pipe regression that fails on the prior 30-second idle disconnect.
- Package adapter 0.2.2 with the existing protocol-2 Rust/UI components. Add a separately built, excluded-from-package qualification harness for controlled native rollback/failure observations on explicitly approved disposable targets.
- Verify bounded actual Revit 2027.2 floor/ceiling/engineering Apply, floor unchanged/Unlimited/Undo/Redo, finite and Unbounded underlay Look Up/Look Down, stale review rejection, native-invalid ranges and controlled rollback/error/warning cases. Retain private screenshots/readbacks separately from Windows source/package results; full R1–R12 and legacy-free runtime qualification remain pending.

## Unreleased - Visual View Range readability and orientation - 2026-10-06

- Separate coincident/nearby plane callouts with leader lines, wrap desktop controls and show out-of-section/Unlimited planes on the correct side. Start native sections at the middle of the captured model.
- Capture structural plan type direction and independent underlay metadata in the minimal .NET adapter; use captured up/down direction for Rust depth validation and display it in the existing Expo/Electron feature.
- Version the strict native/API contract to protocol 2, regenerate OpenAPI/TypeScript/Zod and package compatible Rust/desktop 0.1.1 and adapter 0.2.1 components.
- Add failing-before/passing-after range and rendered-label regressions plus Windows pipe and packaged UI checks for floor/ceiling/structural up/down, opposite underlay and desktop scaling. Source/package verification is separate from pending updated-adapter qualification in actual Revit.

## Unreleased - Rust replacement M2 - 2026-10-06

- Add minimal .NET 10/Revit 2027.2 source for current-plan capture, bounded local model triangles, session-bound local pipes and serialized ExternalEvent execution.
- Add native validity/restriction checks, explicit Apply, one transaction/group with rollback and pre/post-commit readback, bounded outcome/deduplication and queued cancellation.
- Connect the existing Rust/Axum and Expo/Electron feature to native capture/review/Apply/outcomes; preserve M1 and generated contracts.
- Package owned adapter files without Autodesk assemblies or automatic registration. Windows production-frame/pipe/failure and packaged native-orchestration tests pass; actual Revit qualification remains pending. No install, live write, merge or release.

## Unreleased - Rust replacement M1 - 2026-10-06

- Add the interactive offline Visual View Range feature: Rust sections/units/range rules, authenticated Axum and derived strict OpenAPI/Zod/TypeScript contracts.
- Add the branded Expo Web/NativeWind screen in a sandboxed Electron Forge package, with plane drag/keyboard/numeric controls, slice locator, units, Reset/Cancel, explicit Apply review, stale-response rejection and reconnect recovery.
- Add independent geometry/API and .NET 10 frame-contract checks, package allowlist/hashes, and actual packaged UI/security drivers behind the project CLI and full local CI.
- Preserve existing runtime, drafts and external TimberFold. Native adapter source and approved Revit qualification remain M2/M3; no native Apply or release claim.

## 0.2.0 - 2026-10-05 - First pyRevit extension

- Add a RevitThyme ribbon with Suite Status, Tool Settings and read-only TimberFold inspection.
- Add the supplied plant/building wordmark and matching transparent light/dark ribbon icons, with editable SVG sources and a guarded image-only refresh for an already loaded extension.
- Capture the development-host reload crash and add an optional backed-up Routes lifecycle repair with an isolated failing-before/passing-after HTTP regression.
- Correct the native installation after identifying MSIX AppData redirection. Refuse redirected installer/repair destinations, verify loaded images and repaired code in Revit, and pass the first-host cold-start/read-only smoke test. Repeated Reload stability remains unverified.
- Share validated operations with loopback Routes in Revit API context; refuse stale/missing targets and invalid inputs.
- Add deterministic allowlisted ZIP packaging, checksums and a per-user installer with recoverable backups and ownership checks.
- Prepare GPL-3.0-or-later licensing, contribution/security docs, GitHub issue/PR templates and manual release automation.
- Preserve external TimberFold and all model geometry. No generation, standalone MCP server or second-computer claim in this release.

## Unreleased — 2026-10-05 — Development setup

- Add wstack development standards and WLKR LABS Linear routing while preserving Revit-specific constraints and external TimberFold ownership.
- Scaffold a dependency-free Rust project CLI, connect the existing structural diagnostic with argument forwarding, and add a Windows command launcher.
- Record stack exceptions, missing Rust/Cargo, the setup checker's Windows launcher limitation, and pending CLI/runtime and local CI evidence in SETUP-TODO.md.

- Complete the authorized dependency bootstrap with Rust 1.99.0, rustfmt and clippy; use the existing Visual Studio C++ build tools.
- Repair the owning wstack-setup skill's Windows launcher and permission handling; retain its patch and verify the prior regression now passes.
- Add real CLI boundary checks and local CI with scope routing, aggregate results at a Git SHA, and Cargo cache reuse/invalidation checks.
- Establish `master` as the local default for verified checkpoints.

This setup verifies offline repository automation. Revit host behavior remains planned.

## 0.1.0 — 2026-10-02 — Project foundation

- Establish RevitThyme as a separate local Revit suite/host project.
- Document purpose, host/tool separation, a draft operation interface, build milestones and a second-computer deployment plan.
- Register TimberFold 1.1.0 as an external implementation, referencing its inspected source revision without copying its repository or models.
- Add portable local-source configuration and a read-only project diagnostic.
- Preserve the existing TimberFold installation and Revit model state; this foundation performs no live Revit work.

Host execution, a ribbon, MCP operations and an installer remain planned capabilities.
