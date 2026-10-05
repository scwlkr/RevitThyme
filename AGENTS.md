# RevitThyme development instructions

Read PROJECT.md and README.md before work. For host, MCP or operation changes, read docs/ARCHITECTURE.md. For implementation milestones, read docs/ROADMAP.md. For packaging or another-machine installation, read docs/DEPLOYMENT.md.

- Work in C:\Revit\RevitThyme. RevitThyme is a Revit suite/host project; distinguish its implemented capabilities from plans.
- TimberFold is an external tool at C:\Revit\TimberFold. Before integration or changes there, read its PROJECT.md, README.md and AGENTS.md. Follow that repository's instructions and record its source revision. Preserve its working installation, frozen baselines and source building geometry.
- Begin with the next bounded milestone. Keep the interface small and share the tool implementation between the ribbon and MCP. Add adapters when actual behavior needs to vary; avoid unused abstractions and placeholder commands that claim success.
- Before live Revit work, check the bridge and report the active document. The existing development bridge uses pyRevit Routes at 127.0.0.1:48884. An unavailable bridge requires diagnosis, not assumptions about document state.
- Before broad model changes, report scope, affected counts and side effects. Test uncertain behavior on a sample or in rollback-protected transactions. Execute through a valid Revit API context; use ExternalEvent/Idling for external requests.
- Validate targets and settings deterministically. Apply model changes in transactions with rollback on failure, then read back and report changed and skipped IDs. Saving, synchronization, closing and replacing models require the user's request for that operation.
- Keep the current Revit-facing Python adapter compatible with IronPython. Keep CPython workers separate. Confirm the installed Revit SDK/runtime before creating native host project files.
- Keep credentials in the secret manager and local listeners on loopback. Use named, validated tool operations for the shared suite; credentials and private building data stay outside source/release packages.
- Run `./project check-project` (PowerShell: `.\project.cmd check-project`) after foundation/registry changes; it invokes scripts/check-project.ps1 and forwards `-LocalConfigPath` and `-ReportPath`. For behavior changes, run the relevant interface, geometry or live checks. Native CAD changes also require unit, contour, layer and geometry round-trip checks. Record offline, live and physical verification separately.
- Update PROJECT.md when an accepted decision changes, and update CHANGELOG.md for completed capabilities. Make meaningful local Git checkpoints. Publishing, pushing or distributing private source models requires an explicit request.

<!-- wstack-setup:start -->
## Project standards

- Project: RevitThyme. Rust `./project` → tests/automation; on Windows use `.\project.cmd`. `--help` → usage; `doctor` → prerequisites. Extend `tools/project-cli/src/routes.rs`; raw tools only CLI bootstrap/repair.
- Verify real app outcomes/side effects through CLI/UI drivers; scaffold/build ≠ app proof. Retain evidence; stop only processes you started.
- Smallest correct change; preserve unrelated work. Files ≈300 lines, split by responsibility; generated/vendor exempt; justify exceptions in handoff.
- Tests → behavior/credible regression/uncovered independent contract; prefer one owner-boundary check. Verify before deleting redundant/implementation-coupled tests/test-only seams; preserve regression coverage. Regression: fail before → pass after. No quotas/trivial-change tests.
- Linear only: team **WLKR LABS**, project https://linear.app/wlkr-labs/project/revitthyme-fea9af3040c9. Before substantive work: read issue/discussion, reuse/create issue; ID → branches/PRs; post verification/blockers. Done → acceptance verified + landed on default branch. `SETUP-TODO.md` = setup handoff, never backlog.
- Frequent focused local commits; short-lived branches; merge verified work within repo rules/authorization. Publishing and pushing require an explicit request. Isolate active work; never commit unrelated edits for a clean status.
- Local CI: applicable `./project` checks pass before push/merge on exact clean SHA; retain SHA/base/commands/results. Edits/new SHA → recheck, including landed SHA before Done. Remote → storage/review; hosted → documented requirement/owner direction; required current results pass before merge/Done. Pending/missing/failed ≠ pass.
- CI changes: non-executable docs → light; reliable scope → focused; shared code/dependencies/build/CI/uncertain scope → full; generated/executable docs → behavior checks. Cache costly dependencies/tools/builds keyed by platform/toolchain/lockfile; verify routing/invalidation. Aggregate pass/fail@SHA covers applicable checks; filtering cannot hide failures. Unverified alignment → `SETUP-TODO.md`.
- Task boundaries/after landing → remove completed inactive worktrees/obsolete branches/disposable builds. First verify useful commits on GitHub, inspect uncommitted/untracked/ignored files, preserve useful local data; never upload secrets/caches. Keep active/shared worktrees; managed → host archive tool; creation → using-git-worktrees skill.
- Investigate first; state material assumptions; finish authorized work; ask only consequential blockers. Proportional checks; report verified outcomes/gaps/landing.

## Technical stack

Defaults → new work; migration target → existing. Needed layers/targets only; user/repo choices prevail; explain deviations. Existing current→target gaps + bounded migration → `SETUP-TODO.md`; implementation → Linear. Setup → instructions/tooling only.

RevitThyme exceptions: Rust is the project automation CLI, not a replacement Revit host. Preserve the planned IronPython-compatible pyRevit adapter, separate CPython workers, and C#/.NET host pilot after SDK/runtime confirmation. The Revit ribbon remains the intended in-product interface. No database, Axum service, OpenAPI/Zod contract, React/Expo/Electron UI, Docker environment or pnpm workspace is needed by the current foundation; add those layers only for accepted behavior that requires them. TimberFold stays external and keeps its own toolchain. No product migration or toolchain installation is included in setup.

| Layer/target | Tools |
| --- | --- |
| Database | PostgreSQL |
| Backend | Rust + Axum |
| Contract/validation | OpenAPI + Zod |
| UI | TypeScript + React/React Native |
| App/routing | Expo + Expo Router |
| Styling | Tailwind CSS + NativeWind |
| Components | Owned shadcn-style UI |
| Desktop | Electron + Electron Forge |
| Containers | Docker + Compose |
| Packages | pnpm + Cargo |
| Web | Expo Web / React Native Web |
| iOS + Android | Expo |
| macOS + Windows + Linux | Electron |

- Contracts: OpenAPI = Rust/Axum API source; derive TypeScript clients/types + boundary Zod schemas or verify alignment. Zod → TypeScript inputs/responses; Rust → untrusted inputs/domain rules; clients → backend → PostgreSQL.
- UI: own source/tokens/variants/accessibility; React Native primitives + NativeWind; platform adapters as needed. DOM-only shadcn components ≠ native. Share screens/Expo Router routes across requested web/mobile where practical.
- Desktop: Expo Web → Electron shell; OS integration → narrow preload/IPC bridge; context isolation on, renderer Node integration off. Forge packaging → verify navigation/assets on each claimed OS; pnpm → [Forge dependency layout](https://www.electronforge.io/) (`node-linker=hoisted`).
- Tooling: pnpm → JS/TS, Cargo → Rust; commit lockfiles; preserve declared manager/lockfile until migrated. Docker/Compose → backend/database; native/desktop → target toolchains; automation → `./project`.
- Compatibility: check Expo SDK/React Native/NativeWind/Tailwind + Forge/pnpm versions together against official docs; no single Tailwind major across incompatible packages. Prove behavior per claimed target; web build ≠ mobile/desktop proof.
<!-- wstack-setup:end -->

## CLI bootstrap

The CLI needs an existing Rust/Cargo toolchain; setup does not install dependencies. Until it is available, the foundation diagnostic can be checked directly:

```powershell
& .\scripts\check-project.ps1
```

Run the setup skill from its directory with the installed Windows Python in UTF-8 mode:

```powershell
python -X utf8 scripts/setup.py check 'C:\Revit\RevitThyme'
```

The skill checker currently executes the extensionless Unix launcher directly and fails with WinError 193 on Windows. Check `.\project.cmd --help` and `.\project.cmd doctor` separately once Cargo is available; report the skill checker limitation independently from CLI runtime evidence.
