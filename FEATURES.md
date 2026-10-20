# Features

| Feature | Current behavior |
| --- | --- |
| Ribbon | Task panels, SVG-authored light/dark icons, context availability, extended tooltips and F1 help; updates in the toolkit slide-out. See [ribbon standards](docs/RIBBON.md). |
| Inspect View | Native WPF inspection of active document, view identity, type and edit readiness |
| Rename View | Shared C# operation with captured-target checks, verified native readback, rollback and one Undo item |
| View Range | Native four-plane editor with WPF section preview, exact expected values, template guards and one Undo |
| Room Dimensions | Two associative finish-face dimensions for rectangular rooms, tag clearance, duplicate guards and one Undo |
| CLI | Native operations, separate CLI/development/registered versions, installed-copy updates and automatic global `rthyme` command with JSON/text and meaningful exits |
| MCP | Official C# SDK stdio server, 32 source tools (25 in published v0.10.0), explicit connection and structured native results/errors, including seven shared TimberFold controls |
| Document discovery (unreleased) | Read visible session-native document identities and native flags, including at Home; fingerprinted bounded pages, fixture filtering and retained read recovery through shared MCP/CLI operations |
| View/sheet image export (unreleased) | Exact native PNG with verified inline MCP image, retained request outcome and no model transaction, save or view activation; [contract](docs/MCP.md#exact-view-and-sheet-images-unreleased) |
| Document/view lifecycle (unreleased) | Home-aware background open, exact document/view/sheet activation, explicit active-document save with native read-only-file preflight, clean close with expected file hashes, explicit active-document handoff and retained recovery; last-active close to Home is refused; [contract](docs/MCP.md#explicit-document-lifecycle-unreleased) |
| Trusted C# | Opt-in Roslyn method bodies through MCP/CLI; rollback queries, atomic modifications, bounded JSON/diagnostics, native change IDs and retained recovery |
| Development sessions | Local signing and fixture-copy launches with process/session/document pinning; prompt-free startup, revised C# in one process, native modification and Undo qualified on Revit 2027.3. See [development setup](docs/DEVELOPMENT.md) and [native evidence](verification/development-signing.md). |
| Cabin Eval | Independent 100-point native evaluator with temporary length probe and negative controls in a disposable fixture |
| Generic inspection | Views, filtered/paged elements, native selection and paged typed parameters with shared GUID, built-in identity and native assigned-value state through MCP and CLI `inspect-elements` |
| Parameter editing | Expected-value checks, verified atomic batches, whole-batch rollback and one Undo item |
| Outcome recovery | Session-retained request IDs, value-based replay, conflict rejection and queued cancellation |
| Installation | Single per-user Windows setup and verified GitHub bootstrap; Revit 2027/runtime detection, bundled add-in/CLI/MCP/Agent/Codex, owned user PATH entry for `rthyme`, immutable upgrades and recoverable uninstall |
| Built-in Agent | Dockable Codex chat, ChatGPT sign-in, streaming, approvals, interruption, saved sessions and ordinary skills, pinned to this Revit process |
| Updates | Quiet daily native check, manual ribbon/CLI check, registered-installation comparison, verified installer download and close-Revit installation |
| Verification | Portable unit/stdio checks, pinned actual-Revit and three external Codex rounds |
| TimberFold | Native controls and shared CLI/MCP inspection, previews, DWG/DXF kits, source-mapped guides and annotation-only review sheets. Four original buildings pass native readback, repeatability and single Undo; explicit read-only recovery preserves original files/timings. Native export/publication/cancellation fault cases pass; the final candidate meets the 100-panel target in 23.35 s. Digital qualification, peer review and the hash-verified private delivery bundle are complete. Physical fabrication remains outstanding. See [TimberFold](docs/timberfold/README.md). |

| Documentation | Shared Markdown sources for GitHub, Docusaurus and agent indexes; source-contract checks and read-only live website/GitHub drift checks against a reviewed publication baseline. A resumable publication workflow freezes the candidate, requires owner approval, then deploys, verifies live content/settings and merges the baseline after CI. See [website maintenance](docs/WEBSITE.md). |

Version 0.10.0 adds TimberFold digital kits and seven shared controls, bringing MCP to 25 tools with protocol 8. The qualified package version is `0.10.0-timberfold.4`. Physical calibration, cutting and assembly remain outstanding; `ManufacturingReady` is false. Parameter identity, exact IDs, pagination and expected-value edit safeguards from 0.9.3 remain available. The release includes the native ribbon, Built-in Agent and [release checker](docs/UPDATES.md), using the existing installer. The installer is unsigned and supports full Revit 2027. Exploration remains deferred as described in the [roadmap](docs/ROADMAP.md). Earlier functionality is preserved in `legacy/pre-foundation` for migration.
