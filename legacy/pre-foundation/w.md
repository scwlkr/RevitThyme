# RevitThyme project notes

<!-- wstack:begin setup -->
Wstack: 0.2.0
Features: [FEATURES.md](FEATURES.md)
Terms: [GLOSSARY.md](GLOSSARY.md)
CLI: `.\project.cmd --help`
<!-- wstack:end setup -->

## Scope and architecture

Work in `C:\Revit\RevitThyme`. Current behavior and accepted decisions live in [PROJECT.md](PROJECT.md); feature work updates the relevant [feature](FEATURES.md). Preserve the installed host, working models, active checkouts and unrelated edits.

The accepted [replacement specification](docs/specs/RUST-WSTACK-REPLACEMENT.md) owns the architecture: Rust domain logic/automation/MCP, a minimal .NET Revit adapter and the Expo Web/Electron UI. Preserve existing Cargo/pnpm lockfiles and the pinned toolchains. Legacy IronPython sources and external TimberFold retain their own boundaries. Add layers only for accepted behavior; setup does not migrate the product.

## Work and delivery

- Investigate first; begin with a bounded milestone and finish authorized work. Delegate useful independent work with explicit ownership when needed; main integrates.
- Track substantive work in **WLKR LABS / [RevitThyme](https://linear.app/wlkr-labs/project/revitthyme-fea9af3040c9)**. Read/reuse/create the issue before implementation; use its ID in branches/commits and post verification/blockers. Done requires verified acceptance and default-branch landing. [SETUP-TODO.md](SETUP-TODO.md) records setup alignment only.
- Make focused local commits on `codex/<issue>-<purpose>` branches. Preserve unrelated work. Publishing/pushing requires the owner's explicit request; private models stay outside release packages. Default branch: `master`.
- Verify a clean checkout before a local fast-forward; retain checked SHA/base/results and recheck the landed SHA. Keep useful unmerged work. Archive managed worktrees through the host; stop only processes started for the task. Cleanup first inventories useful/untracked/ignored files and preserves unpublished work.
- Keep files around 300 lines; split by responsibility when that improves the design. Generated/vendor files are exempt. Explain a needed exception in the handoff.
- Tests cover meaningful behavior, independent boundaries or regressions. Keep credible existing coverage; verify before removing redundant tests. Bug regressions fail before the fix and pass after it. Build/package checks do not prove app behavior.

## CLI and verification

Use `.\project.cmd` on Windows and `./project` on Unix. Extend `tools/project-cli/src/routes.rs`; raw tooling is for CLI bootstrap/repair. `--help` lists current commands; `doctor` checks prerequisites without starting Revit.

- `check-wstack` verifies these notes, agent/vocabulary pointers and real CLI help/doctor. It replaces the retired separate setup-skill CI dependency and preserves project documents.
- `check-project` runs the structural diagnostic and forwards `-LocalConfigPath`/`-ReportPath`. Use it after foundation/registry changes. If the launcher itself needs bootstrap diagnosis, run `& .\scripts\check-project.ps1` directly.
- `check-cli` verifies routing, report/path forwarding and failure exits. Run relevant interface/geometry/live checks for behavior changes; distinguish source/offline, actual Revit and physical evidence.
- `ci --base <sha> --require-clean` selects proportional checks. Use `--full` for shared code, dependencies, build/CI changes or uncertain scope. Static non-executable docs may use light checks; executable documentation needs behavior checks. `--explain <paths>` shows routing.
- CI writes ignored `artifacts/local-ci` reports with SHA/base, commands, aggregate result and cleanliness. Pending/missing/failed results do not pass a gate. Reuse dependency/tool/build caches keyed by platform/toolchain/lockfile and verify warm reuse/invalidation. No hosted check substitutes for native proof.

Before push/merge, applicable local checks must pass on the exact clean SHA. Edits/new SHA require the affected gates again. Remote/hosted checks serve documented requirements and storage/review; current required results must pass before merge/Done. Unverified CI alignment belongs in [SETUP-TODO.md](SETUP-TODO.md).

## Native work

Follow AGENTS.md's standing installation/disposable-fixture authorization and model safeguards. Read [native MCP](docs/NATIVE-MCP.md) before MCP work, and use its exact process/document target. Shared named native operations serve the ribbon and MCP; implement adapters only when behavior varies.

Keep credentials in the secret manager and native IPC session-bound; network listeners stay on loopback. Read back native changes and report changed/skipped IDs. Working-model save/sync/close, suite retirement and publication retain their operation-specific boundaries.

## Setup history

The 2026-10-05 setup introduced the Windows CLI and local CI. The older skill's Windows repair remains in [verification/wstack-setup-windows.patch](verification/wstack-setup-windows.patch). The 2026-10-13 refresh uses wstack 0.2 project notes and project-owned verification; see [current setup evidence](verification/wstack-setup.md). The global 0.2 worker's Unix timer is unavailable on Windows, so this setup used the skill's local fallback without changing global skills.
