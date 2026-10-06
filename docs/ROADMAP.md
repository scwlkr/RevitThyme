# Build roadmap

Each milestone ends with working behavior and evidence. A scaffold or successful compilation alone does not satisfy a live milestone.

The table below records the existing foundation plan and evidence. The user's 2026-10-06 Rust/wstack replacement decision supersedes its future host comparison: use the [replacement milestones](specs/RUST-WSTACK-REPLACEMENT.md#migration-milestones) and [acceptance gates](specs/RUST-WSTACK-ACCEPTANCE.md) for that implementation. The specification does not change the installed product or complete any live milestone.

| Milestone | State | Deliverable | Completion criterion |
| --- | --- | --- | --- |
| M0: Project foundation | Complete | Docs, TimberFold registration, local check and Git checkpoint | Project check passes; external source reference is valid; no host-installation claim |
| M1: Read-only operations | Implemented; evidence in verification/release-0.2.0.md | `revitthyme_status` and `timberfold_inspect` through the existing development bridge | Live results identify the intended session/document and tool readiness; disconnected and wrong-document cases give useful diagnostics; no document changes |
| M2: Native host pilot | Planned | Focused C# add-in with the same status operation | Installed SDK/runtime confirmed; add-in loads in the intended Revit build; external request executes through valid API context; read-only behavior matches M1 |
| M3: TimberFold preview | Planned | Extract/compute pipeline producing a preview job and report | Sample produces scope/settings/part counts and unsupported-geometry diagnostics; no persistent source-document changes; worker progress/failure reported |
| M4: Verified generation | Planned | Preview-bound generation with native CAD and Revit documentation | On a small sample, changed/skipped IDs, units, closed CUT contours, layers, CAD round trip and source-geometry preservation verified; rollback/failure evidence recorded |
| M5: Suite interface and MCP | Read-only ribbon/Routes implemented; MCP/jobs planned | RevitThyme UI and MCP exposing implemented operations | Both callers invoke the same implementation; duplicate requests and stale previews handled; UI remains usable during external computation; job state is inspectable |
| M6: Second-computer pilot | ZIP and installer implemented; pilot unverified | Versioned installation package and onboarding guide | Install and smoke test on a second computer; ordinary buttons work without AI configuration; upgrade/uninstall and previous-version recovery documented |

## Pilot comparison

Compare the pyRevit adapter and native host using the same status/inspection tasks before expanding host scope. Record operation completion, round-trip time, UI responsiveness, failure diagnostics and recovery. Decide the production host from evidence, including maintenance and deployment cost.

## Initial sample

Use a dedicated small supported project for read/write milestones. TimberFold's Cedar Cottage example is a candidate after inspecting its current files and source diagnostics. Use a separate copy for experiments, preserve the original and check the active document before live work. The S1 private source house is not the default distribution sample.

## Deferred work

Additional suite tools, a general plugin marketplace, multi-version Revit support, cloud-hosted model access, voice UI, a full pyRevit fork and advanced laser joinery are separate milestones. Add them when requested or when working tools establish a concrete need.

The first release host is upstream pyRevit plus a custom RevitThyme extension. A fork and native-host decision are deferred until a demonstrated limitation needs them.
