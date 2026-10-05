# Setup handoff

Setup scope: repository instructions, CLI scaffold and handoff. Product version remains 0.1.0 at the foundation milestone. Track implementation in [RevitThyme / WLKR LABS](https://linear.app/wlkr-labs/project/revitthyme-fea9af3040c9); this setup is [WLK-95](https://linear.app/wlkr-labs/issue/WLK-95/apply-wstack-project-setup-to-revitthyme). This file records setup gaps, not a product backlog.

## Tooling and evidence

- [x] Wire the existing structural diagnostic into `tools/project-cli/src/routes.rs` as `check-project`; preserve `-LocalConfigPath`, `-ReportPath` and process failure codes in the route/launcher source.
- [ ] Exercise that route through Rust and capture the result. No Rust/Cargo toolchain was found in PATH, the standard user toolchain directory or the bundled runtime dependencies. Dependency installation is outside setup scope. `project.cmd` is the Windows launcher; `project` is the Unix-shell launcher.
- [ ] Pass the upstream setup checker. Its direct launch of the extensionless Unix `project` file fails on Windows with WinError 193. The skill source is external and has not been modified. Once Rust/Cargo is available, check Windows help/doctor and the real route independently; repair the checker in its owning skill rather than claiming it passed.
- [ ] Resolve the setup script's Windows permission reporting. Repeated apply exits 0 and preserves the custom instructions/CLI sources; SHA-256 comparisons show no file-content changes. It still reports `changed: ["project"]` because Windows does not retain Unix executable permission bits. The requested literal `changed: []` gate remains pending.
- [ ] Record a clean checked SHA and land on the repository's chosen default branch before marking the Linear issue Done. The repository has no remote/default branch configured; the foundation is on `codex/project-foundation`. Setup uses a local `codex/wlk-95-wstack-setup` checkpoint. Publishing/pushing requires an explicit request.

The direct foundation diagnostic passed 42 checks before setup and 44 after the added documentation links. Windows help and doctor each return 127 with an explicit missing-Cargo diagnostic. The Rust route has not run. Re-run the foundation diagnostic under the documented CLI bootstrap exception while Rust is unavailable. This is offline file/source-reference evidence; no live Revit, native CAD or physical-fit checks are part of setup.

## Current stack and bounded next work

| Current | Preferred target or exception | Next bounded step |
| --- | --- | --- |
| Offline PowerShell diagnostic | Rust CLI orchestrates the existing script; keep its implementation and flags | Supply the existing-toolchain prerequisite, then check help/doctor, successful report generation and failure exit propagation |
| Planned pyRevit adapter with IronPython; separate CPython workers | Retain these Revit-specific languages; Rust CLI does not replace a Revit execution context | M1 in docs/ROADMAP.md, through the existing loopback bridge |
| Planned C#/.NET native host pilot | Retain C# because the host must match the installed Revit SDK/runtime | Confirm the SDK/runtime before M2 project files |
| Planned ribbon/MCP interface | Keep the small shared operation seam and Revit interface | Implement only named operations proved by the next roadmap milestone |
| No database, web/mobile/desktop shell or container service | PostgreSQL, Axum, OpenAPI/Zod, React/Expo/Electron, Docker and pnpm remain defaults only for future accepted layers | No migration or provisioning is currently needed |
| External TimberFold | Preserve its independent repository, runtime, baselines and toolchain | Read its instructions and record its revision before future integration |

## CI alignment

- [ ] Locate/plan local CI behind `./project`: proportional checks, useful caching, exact clean SHA/base/commands/results. Applicable checks pass before push/merge; edits/new SHA → recheck. No hosted config ≠ gap; hosted → documented requirement/owner direction. Setup does not provision CI. <!-- setup:ci-discovery -->

Alignment remains pending. Setup has not audited app tests, configured CI, dispatched hosted jobs, installed dependencies or proved live Revit behavior. The only current app diagnostic is wired above; planned status/inspection/generation operations are not CLI commands.
