# Features

| Feature | Current behavior |
| --- | --- |
| Inspect View | Native WPF inspection of active document, view identity, type and edit readiness |
| Rename View | Shared C# operation with captured-target checks, verified native readback, rollback and one Undo item |
| CLI | Session listing, inspection and captured-view rename with JSON/text and meaningful exits |
| Local package | Allowlisted add-in and CLI; reversible per-user registration |
| Verification | Portable unit checks and separately pinned actual-Revit qualification |

Current scope is [Foundation](docs/FOUNDATION.md). All later-stage tools/interfaces are deferred as described in the [roadmap](docs/ROADMAP.md). Earlier functionality is preserved in `legacy/pre-foundation` for migration.
