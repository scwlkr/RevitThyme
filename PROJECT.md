# RevitThyme project

## Purpose

Build a reusable Revit tool suite with a controlled execution host and an MCP interface for Codex. People and AI clients should invoke the same tool operations and receive verified results. TimberFold is the first tool.

Project root: **C:\Revit\RevitThyme**. Product ID: `revitthyme`. Foundation created: **2026-10-02**.

## Accepted direction

- Product name: **RevitThyme**.
- Prepare a separate project with general documentation and TimberFold as its starting tool.
- Keep the host's responsibilities separate from each tool's implementation.
- Preserve TimberFold at **C:\Revit\TimberFold**, including its repository, working installation, baselines and generated artifacts.
- Support future use through a Revit interface and MCP, with a release that can be installed on a second computer.
- Use wstack project instructions and a Rust CLI for repository automation; retain the Revit-specific host/worker languages. Track substantive work in the [RevitThyme Linear project](https://linear.app/wlkr-labs/project/revitthyme-fea9af3040c9) under WLKR LABS. Setup does not install toolchains or migrate the product.

## Working implementation plan

The existing pyRevit bridge is the first development adapter. A focused C#/.NET Revit host is the intended independent-host pilot. Its runtime target, project structure and install method will be confirmed against the installed Revit SDK before implementation. The two adapters should be compared through the same operations before choosing the production host.

This plan does not create a fork of Autodesk Revit or pyRevit. A pyRevit fork would be a separate decision if a concrete platform limitation requires it.

The operation names, job states and proposed source layout in [ARCHITECTURE.md](docs/ARCHITECTURE.md) are drafts. Implement only the next bounded milestone, and update the docs when the interface becomes accepted behavior.

## Current state and evidence

| Item | State |
| --- | --- |
| Project docs and local tool registry | Created in this foundation |
| Read-only project diagnostic | Implemented in `scripts/check-project.ps1` |
| Project automation CLI | Rust scaffold and `check-project` route created; Windows launcher provided; compilation/runtime pending an installed Rust/Cargo toolchain |
| TimberFold implementation | Existing external checkout; registered, not integrated into RevitThyme |
| RevitThyme pyRevit adapter | Planned |
| Native C# host | Planned |
| RevitThyme ribbon and MCP server | Planned |
| Installer / boss's computer pilot | Planned |
| Live RevitThyme validation | Not performed |

TimberFold's source was inspected at commit `4d8873fb7be5c100dbe3f3a41899b7c5e4a41f0e`, with a clean working tree. Its metadata declares version **1.1.0**. Its active runtime is `C:\Revit\TimberFold`; `C:\Revit\Laser-Model` is a preserved legacy copy.

The TimberFold docs record completed digital workflows, including Cedar Cottage, and remaining physical-fit work. Those are source-project records, not new live validation by RevitThyme. Consult the tool's current PROJECT.md and verification artifacts before relying on them.

## First work

1. Build `revitthyme_status` and `timberfold_inspect` through the existing bridge, with no model edits. Return explicit session/document identity, host/tool versions, readiness and useful diagnostics.
2. Define a small C# host pilot implementing the same read-only status operation. Verify the installed SDK/runtime and supported Revit build first.
3. Continue the [roadmap](docs/ROADMAP.md) only after those checks produce inspectable evidence.

## Open decisions

- Company display name, icons and final ribbon layout.
- Production host choice after the pilot comparison.
- Supported Revit and pyRevit versions beyond the current Revit 2027 target.
- Installer technology, update source and company repository location.
- Redistribution license and ownership arrangements before distribution.
- Final approval UX for model-changing operations and long-running job cancellation.

## Project continuity

Keep accepted decisions here, architecture in docs/ARCHITECTURE.md and completed work in CHANGELOG.md. Record future runs with source revision, host/tool versions, settings and verification scope. Use local Git checkpoints; no remote is configured by this foundation.

The setup handoff and explicit stack exceptions are in [SETUP-TODO.md](SETUP-TODO.md). Setup is tracked as [WLK-95](https://linear.app/wlkr-labs/issue/WLK-95/apply-wstack-project-setup-to-revitthyme). Local CI alignment and live Revit verification remain separate, pending work.
