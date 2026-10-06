# RevitThyme project

## Purpose

Build a reusable Revit tool suite with a controlled execution host and an MCP interface for Codex. People and AI clients should invoke the same tool operations and receive verified results. TimberFold is the first tool.

Project root: **C:\Revit\RevitThyme**. Product ID: `revitthyme`. Foundation created: **2026-10-02**.

## Accepted direction

- Product name: **RevitThyme**.
- Visual direction: an agricultural, eco and plant feel, using the supplied teal building/thyme wordmark, green leaves and warm timber accents. Ship distinct light/dark ribbon icons with editable vector sources.
- Prepare a separate project with general documentation and TimberFold as its starting tool.
- Keep the host's responsibilities separate from each tool's implementation.
- Preserve TimberFold at **C:\Revit\TimberFold**, including its repository, working installation, baselines and generated artifacts.
- Support future use through a Revit interface and MCP, with a release that can be installed on a second computer.
- Use wstack project instructions and a Rust CLI for repository automation; retain the Revit-specific host/worker languages. Track substantive work in the [RevitThyme Linear project](https://linear.app/wlkr-labs/project/revitthyme-fea9af3040c9) under WLKR LABS. The setup follow-up authorizes needed dependencies and local CI, with no product migration.
- Use `master` as the local default branch, matching the existing Git default. Land verified local work there; publishing/pushing remains an explicit separate request.

## Replacement target agreed on 2026-10-06

The user chose a full replacement of RevitThyme's owned Python/pyRevit runtime: Rust/Axum domain logic, the wstack Expo Web/Electron Forge UI, typed OpenAPI/TypeScript/Zod boundaries and a minimal .NET 10 Revit adapter. The initial native target is the observed Windows Revit 2027.2 build. See the [replacement specification](docs/specs/RUST-WSTACK-REPLACEMENT.md), [acceptance gates](docs/specs/RUST-WSTACK-ACCEPTANCE.md) and [implementation-session prompt](docs/specs/RUST-WSTACK-IMPLEMENTATION-PROMPT.md).

This future target supersedes the host comparison below and the earlier Rust-native UI suggestion. No replacement is implemented or installed by the specification PR. Existing functionality, drafts and installation remain preserved during migration; external TimberFold remains independently owned. Implementation, installations, live Revit fixture work, merge and release require applicable separate authorization.

## Existing foundation implementation plan

Replacement M2 source on [WLK-111](https://linear.app/wlkr-labs/issue/WLK-111/m2-native-net-visual-view-range-capture-and-validated-apply) continues M1 PR #6. The minimal .NET 10 adapter implements API-context capture, session-bound local pipes, serialized ExternalEvent execution and validated transaction/group Apply with rollback and pre/post-commit readback. Rust and the existing UI orchestrate confirmation, outcomes and queued cancellation. See [M2 decisions/evidence boundaries](docs/RUST-VIEW-RANGE-M2.md). Windows offline/packaged orchestration is verified; actual Revit qualification remains M3. No adapter is installed; the legacy host and external TimberFold remain preserved.

Replacement M1 is implemented on WLK-109 as a synthetic offline Visual View Range feature: Rust/Axum, generated OpenAPI/Zod/TS and an Expo Web/Electron Forge package. See [feature and evidence boundaries](docs/RUST-VIEW-RANGE-M1.md). Native .NET capture/apply remains M2; approved Revit qualification remains M3. The installed 0.2.0 host, existing drafts, website/logo work and external TimberFold remain preserved.

The accepted first shipping host is a custom pyRevit extension, using upstream pyRevit without a fork. Publish approved code and docs to the user personal GitHub account scwlkr; private models and settings stay excluded. License original RevitThyme code GPL-3.0-or-later. The existing bridge remains the development adapter. A focused C#/.NET Revit host is the intended independent-host pilot. Its runtime target, project structure and install method will be confirmed against the installed Revit SDK before implementation. The two adapters should be compared through the same operations before choosing the production host.

This plan does not create a fork of Autodesk Revit or pyRevit. A pyRevit fork would be a separate decision if a concrete platform limitation requires it.

The operation names, job states and proposed source layout in [ARCHITECTURE.md](docs/ARCHITECTURE.md) are drafts. Implement only the next bounded milestone, and update the docs when the interface becomes accepted behavior.

## Current state and evidence

| Item | State |
| --- | --- |
| Project docs and local tool registry | Created in this foundation |
| Read-only project diagnostic | Implemented in `scripts/check-project.ps1` |
| Project automation CLI | Implemented with Rust 1.99.0; Windows help/doctor, project diagnostic and CLI boundary checks verified |
| Local CI | Implemented through `project ci`: scope routing, format/lint/build, real CLI checks, setup readiness/idempotence and Cargo cache checks; exact-SHA results in ignored artifacts/local-ci |
| TimberFold implementation | External checkout; read-only inspection integrated, generation not integrated |
| RevitThyme pyRevit adapter | Implemented read-only operations in v0.2.0; live evidence recorded separately |
| Native C# host | Planned |
| RevitThyme ribbon and MCP server | Ribbon and named Routes implemented; dedicated MCP server planned |
| Installer / boss's computer pilot | Versioned ZIP and per-user installer implemented; second-computer pilot unverified |
| Live RevitThyme validation | See verification/release-0.2.0.md for exact scope and remaining gates |

The first-host cold-start smoke test passed on 2026-10-05: the user confirmed icons and Suite Status, native ribbon read-back found images on all three buttons, and eight installed read-only Routes checks passed. Earlier repairs and images had landed in Codex's MSIX AppData cache instead of native Revit's folders. They were reapplied to verified native paths; the installer and developer repair now refuse redirected destinations. The loaded Routes repair was read back in Revit. Repeated Reload stability and another-machine behavior remain unverified; use normal Revit startup on this host. This local maintenance patch does not establish a general pyRevit fork or wider version support.

TimberFold source was inspected on 2026-10-05 at commit `45d49b4969d7cd67bb1bde821e20add0adf0a9b9`, on its active setup branch with a clean working tree. It is maintained independently and is not bundled. Its metadata declares version **1.1.0**. Its active runtime is `C:\Revit\TimberFold`; `C:\Revit\Laser-Model` is a preserved legacy copy.

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
