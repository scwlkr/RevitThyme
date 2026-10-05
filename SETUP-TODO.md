# Setup handoff

The setup follow-up authorizes needed dependencies, Windows repairs and local CI. Product version remains 0.1.0 at the foundation milestone. Track implementation in [RevitThyme / WLKR LABS](https://linear.app/wlkr-labs/project/revitthyme-fea9af3040c9); this setup is [WLK-95](https://linear.app/wlkr-labs/issue/WLK-95/apply-wstack-project-setup-to-revitthyme). This file records setup evidence, not a product backlog.

## Tooling and evidence

- [x] Wire the existing structural diagnostic into `tools/project-cli/src/routes.rs` as `check-project`; preserve `-LocalConfigPath`, `-ReportPath` and process failure codes in the route/launcher source.
- [x] Exercise that route through Rust and capture the result. Rust 1.99.0, rustfmt and clippy are installed through checksum-verified rustup, using the existing Visual Studio C++ build tools. The toolchain is pinned in rust-toolchain.toml. `project.cmd` handles immediate Cargo discovery without restarting Codex.
- [x] Pass the upstream setup checker. The owning skill now generates/uses project.cmd on Windows. The previous WinError 193 regression was reproduced before repair and passes afterward, including a temporary project path with spaces. The patch is retained in verification/wstack-setup-windows.patch.
- [x] Resolve the setup script's Windows permission reporting. It now applies Unix executable permissions only on Unix. Repeated apply returns `changed: []`, preserving the custom instructions and routes. Seven relevant owning-skill tests pass.
- [x] Record a clean checked SHA and land on the local default branch. `master` follows the existing Git default; full CI passed in a temporary clean checkout before fast-forward landing. Recheck the final landed SHA after this checklist update; its aggregate record is artifacts/local-ci/landed.json. The unrelated logo is preserved outside commits. No remote publication is part of this follow-up.

The real Rust route passes 44 structural checks. The CLI boundary driver passes 13 checks: help/doctor, invalid commands/arguments, explicit configuration/report paths with spaces, offline read-back, missing-source failure and malformed-JSON failure. Expected exits 0, 1 and 2 are preserved through the Windows launcher. Evidence is in ignored artifacts/local-ci; no live Revit, native CAD or physical-fit checks are part of setup.

## Current stack and bounded next work

| Current | Preferred target or exception | Next bounded step |
| --- | --- | --- |
| Offline PowerShell diagnostic | Rust CLI now orchestrates the existing script, retaining implementation and flags | Complete: real route and failure/report read-back verified |
| Planned pyRevit adapter with IronPython; separate CPython workers | Retain these Revit-specific languages; Rust CLI does not replace a Revit execution context | M1 in docs/ROADMAP.md, through the existing loopback bridge |
| Planned C#/.NET native host pilot | Retain C# because the host must match the installed Revit SDK/runtime | Confirm the SDK/runtime before M2 project files |
| Planned ribbon/MCP interface | Keep the small shared operation seam and Revit interface | Implement only named operations proved by the next roadmap milestone |
| No database, web/mobile/desktop shell or container service | PostgreSQL, Axum, OpenAPI/Zod, React/Expo/Electron, Docker and pnpm remain defaults only for future accepted layers | No migration or provisioning is currently needed |
| External TimberFold | Preserve its independent repository, runtime, baselines and toolchain | Read its instructions and record its revision before future integration |

## CI alignment

- [x] Locate/plan local CI behind `./project`: proportional checks, useful caching, exact clean SHA/base/commands/results. Implemented and verified in this authorized follow-up; failed clean-checkout gates correctly produce aggregate failure and exit 1. Before push/merge, applicable checks must pass on the current clean SHA; recheck after edits/landing. No hosted config is required. <!-- setup:ci-discovery -->

Local CI is implemented through `.\project.cmd ci`, with scope routing, Rust format/lint/build, the real CLI contract, setup readiness/idempotence and Cargo warm-cache/invalidation checks. It records aggregate success/failure, commands, SHA/base and checkout cleanliness. Static Markdown takes the light route; code, dependencies, CI and executable documentation take full checks. Full clean-checkout evidence is in artifacts/local-ci/prelanding.json and artifacts/local-ci/landed.json; the latter identifies the final landed SHA.

The setup skill's CI inventory remains a conservative text-hint report; actual local CI results provide alignment evidence. No hosted service is needed for this foundation, and no hosted jobs or source publication are part of this work. Planned Revit operations remain future roadmap milestones.

## Public release CI - WLK-97

- [x] Public workflows always run the full portable release boundary; there are no path filters. Local project ci retains docs/full scope routing. Hosted Windows checks are required for the requested public project and package releases. <!-- setup:ci-scope -->
- [x] Portable checks retain aggregate evidence at the checked SHA. Full local exact-SHA checks remain a separate maintainer gate; public checks cannot prove Revit or TimberFold behavior. Pending/failed hosted results are not release evidence. <!-- setup:ci-verify -->
