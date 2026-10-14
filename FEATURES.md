# Features

| Feature | Current behavior |
| --- | --- |
| Inspect View | Native WPF inspection of active document, view identity, type and edit readiness |
| Rename View | Shared C# operation with captured-target checks, verified native readback, rollback and one Undo item |
| View Range | Native four-plane editor with WPF section preview, exact expected values, template guards and one Undo |
| Room Dimensions | Two associative finish-face dimensions for rectangular rooms, tag clearance, duplicate guards and one Undo |
| CLI | Session/context, rename, View Range get/set and Room Dimensions preview/create with JSON/text and meaningful exits |
| MCP | Official C# SDK stdio server, 16 tools, explicit connection and typed native results/errors |
| Generic inspection | Views, filtered/paged elements, paged typed parameters and native selection |
| Parameter editing | Expected-value checks, verified atomic batches, whole-batch rollback and one Undo item |
| Outcome recovery | Session-retained request IDs, value-based replay, conflict rejection and queued cancellation |
| Local package | Allowlisted add-in, CLI, MCP dependencies and notices; reversible per-user registration |
| Verification | Portable unit/stdio checks, pinned actual-Revit and three external Codex rounds |

Current scope is Foundation, [MCP](docs/MCP.md) and [Native utilities](docs/NATIVE-UTILITIES.md). All subsequent stages are deferred as described in the [roadmap](docs/ROADMAP.md). Earlier functionality is preserved in `legacy/pre-foundation` for migration.
