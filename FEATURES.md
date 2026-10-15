# Features

| Feature | Current behavior |
| --- | --- |
| Ribbon | Task panels, SVG-authored light/dark icons, context availability, extended tooltips and F1 help; updates in the toolkit slide-out. See [ribbon standards](docs/RIBBON.md). |
| Inspect View | Native WPF inspection of active document, view identity, type and edit readiness |
| Rename View | Shared C# operation with captured-target checks, verified native readback, rollback and one Undo item |
| View Range | Native four-plane editor with WPF section preview, exact expected values, template guards and one Undo |
| Room Dimensions | Two associative finish-face dimensions for rectangular rooms, tag clearance, duplicate guards and one Undo |
| CLI | Native operations, separate CLI/development/registered versions, installed-copy updates and optional global `rthyme` command with JSON/text and meaningful exits |
| MCP | Official C# SDK stdio server, 18 tools, explicit connection and structured native results/errors |
| Trusted C# | Opt-in Roslyn method bodies through MCP/CLI; rollback queries, atomic modifications, bounded JSON/diagnostics, native change IDs and retained recovery |
| Cabin Eval | Independent 100-point native evaluator with temporary length probe and negative controls in a disposable fixture |
| Generic inspection | Views, filtered/paged elements, paged typed parameters and native selection |
| Parameter editing | Expected-value checks, verified atomic batches, whole-batch rollback and one Undo item |
| Outcome recovery | Session-retained request IDs, value-based replay, conflict rejection and queued cancellation |
| Installation | Single per-user Windows setup and verified GitHub bootstrap; Revit 2027/runtime detection, bundled add-in/CLI/MCP/Agent/Codex, immutable upgrades and recoverable uninstall |
| Built-in Agent | Dockable Codex chat, ChatGPT sign-in, streaming, approvals, interruption, saved sessions and ordinary skills, pinned to this Revit process |
| Updates | Quiet daily native check, manual ribbon/CLI check, registered-installation comparison, verified installer download and close-Revit installation |
| Verification | Portable unit/stdio checks, pinned actual-Revit and three external Codex rounds |

Version 0.9.0 includes the completed Built-in Agent stage and the requested [release checker](docs/UPDATES.md), using the existing installer. The installer is unsigned and supports full Revit 2027. Exploration remains deferred as described in the [roadmap](docs/ROADMAP.md). Earlier functionality is preserved in `legacy/pre-foundation` for migration.
