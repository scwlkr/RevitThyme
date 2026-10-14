# C# Foundation — 2026-10-14

Scope: Foundation only from the complete `2026-10-14_NEW_PLAN.md`. The active product is four C# projects: Core, Addin, UI and Cli. Inspect and captured-target RenameView prove native inspection/modification through shared operations. All later roadmap stages remain deferred.

## Source and offline checks

- .NET SDK **10.0.400**, Windows; full Revit solution builds with zero warnings/errors, warnings treated as errors.
- **50 unit checks passed**: inspection/no-op behavior, complete target checks, validation, verified mutation, rollback after set/commit/readback failures, unconfirmed failures, request replay/conflict, shutdown/cancellation, framed JSON, stale process binding and CLI boundaries. Runner: `project.cmd test`.
- Portable solution builds Core, UI, CLI and Unit without Autodesk references. Active workflow and issue/contribution docs now use C#; hosted CI is configured but was not submitted/run during this request.
- `project.cmd check` additionally validates PowerShell syntax, the four-project Foundation boundary, Autodesk CopyLocal exclusion and migration preservation.
- Migration verified **216 tracked files** byte-for-byte after moving superseded source into `legacy/pre-foundation`. Original `AGENTS.md` edits and untracked logo are preserved outside this implementation's commit. Private artifacts, installed legacy bundles and external TimberFold were retained.

## Actual Revit

The complete successful run is `artifacts/foundation/live/6e5e19c9-7c5d-4bfa-8a51-84e3c02a7970`. It used a new Revit process **2260**, process start `639275762018279565`, session `f1e01392-1001-431e-886d-346bdd1cb93d`, Revit **27.3.0.28**, and only its new `Foundation-Test.rvt`. Existing working Revit remained separate and open.

**17 native checks passed**: empty-session/no-document behavior; loaded WPF capture; ExternalEvent rename; native refresh/restoration; CLI final name; unchanged **2,425 elements** and saved-file hash; native Undo, Redo and final Undo; wrong-document rejection and closed/reopened fixture rejection. **12 CLI checks passed** through the real OS-verified named pipe: inspection, unchanged rename, apply, stale capture, native duplicate-name rollback, wrong view/document and restoration, with inspection between mutations.

Saved fixture SHA-256 remained `45ab9900299c6749bb5c3f670774a67ad38855fb890669e5fa672c55bece123f`. Test edits were restored or undone and discarded without saving over that fixture. A second owned file was used for document-switch/reopen guard tests. The temporary qualification registration was removed; its owned process stopped; pre-existing registration hashes passed cleanup verification. All fixtures and failure evidence remain retained.

The successful bundle identifies the actual compiled product independently of the then-uncommitted worktree:

| Assembly | SHA-256 |
| --- | --- |
| RevitThyme.Core.dll | `6beae9c4b65ef5e56e85d8e930766b7c3529c4b2589c9831b942e62e42a7565a` |
| RevitThyme.Addin.dll | `45828d782e515aab4b73f86fbe70fc318d64921035404a12827372601b83be07` |
| RevitThyme.UI.dll | `db058225b222c89f4d5741b552004a30f878404885962dd206374961dd5eba53` |
| revitthyme.dll | `5084f18f19047c0add17c11a9fe20c6ce2f3889697592a5bf82ecc5a484b5454` |

Reports: `plan.json` (immutable bundle hashes), `ready.json`, `cli-complete.json`, `native-result.json` and `native-wpf.png`. The [README image](../docs/images/foundation-wpf.png) is rendered from the actual loaded Revit WPF window, not a design mockup.

## Packaging and installation

The Foundation package contains only owned Addin/Core/UI, framework-dependent CLI, verified scripts and license/docs. No Autodesk DLL, test loader, model, Electron or Rust runtime is included. An isolated installation test at `artifacts/foundation/install-test/2f59cf95-d9d1-4a99-b8d1-d08a8e71be07` passed **7 checks**: exact native registration, immutable reinstall, registration backup, owned unregister, recoverable registration, modified bundle rejection and preservation of unknown registrations. It used folders outside Revit's native Addins directory and did not touch the running working session.

## Repairs and limits

Retained earlier attempts exposed two test-harness restrictions (shared file handles and document switching from Idling) and one fixture-guard defect (the initial save/close occurring before pinning). The final run includes all corresponding repaired paths and passes the full suite. Failed/aborted runs were preserved and are not counted as passing.

This proves Foundation on the current Revit 2027.3 host. Other Revit releases, another computer, large/workshared production models and a signed `.exe` distribution remain unqualified. A normal production registration was not swapped into the user's running working session. MCP, full native utilities, arbitrary C#, Cabin Eval, Codex Agent and planning experiments are not part of this completion.
