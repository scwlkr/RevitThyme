# Changelog

## Unreleased - Native Revit 27.3 cutover candidate - 2026-10-12

- Target the installed Revit 27.3.0.28 in native preview adapter 0.2.6; retain the previous 27.2 bundle separately. Add native startup/runtime evidence and a guarded CLI cutover with immutable package checks, registration/config backups, reversible pyRevit disable and exact native test MCP selection. Preserve running documents and source files; broader native suite operations remain separate implementation work.

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
