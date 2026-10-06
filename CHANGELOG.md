# Changelog

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
