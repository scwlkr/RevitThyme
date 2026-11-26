# Foundation acceptance

The Foundation milestone is **native inspection and modification through a C# operation layer, WPF and a basic CLI**. Full View Range/Room Dimensions migration and all AI interfaces belong to later stages.

| Requirement | Implementation | Verification |
| --- | --- | --- |
| C# solution | Core, Addin, UI and Cli projects | Full solution builds with warnings as errors |
| Revit add-in | Native ribbon and serialized ExternalEvent host | Separate Revit 2027 process |
| Shared operations | Inspect and RenameView | Same executor used by UI and CLI |
| WPF | Modeless Inspect View, Refresh and Apply | Native loaded window and readback |
| Basic CLI | sessions, inspect, rename-view; text/JSON and meaningful exits | CLI through native named pipe |
| Reversibility | Transaction group, verified rollback, one Undo item | Failure checks and native Undo/Redo |
| Contributor foundation | CI, docs, issue templates, install/package scripts | Portable CI path and local checks |

Run `project.cmd check` for the full build and source checks. `project.cmd check -Portable` builds the portable toolkit without Autodesk SDKs, matching hosted Windows CI.

Use a disposable fixture in the [isolated development workflow](DEVELOPMENT.md) for native Revit proof.

See [verification/foundation.md](../verification/foundation.md) for the observed result. Build/test/package evidence never substitutes for actual Revit evidence. Another machine and other Revit releases are unqualified.

**Foundation is complete on Revit 27.3.0.28:** 50 offline unit checks, 17 native checks, 12 actual-Revit CLI checks and 7 isolated installer checks passed. The active product binaries match the successful native run's recorded hashes.
