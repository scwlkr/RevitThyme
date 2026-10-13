# RevitThyme development instructions

M2 native source and Windows orchestration are described in [RUST-VIEW-RANGE-M2.md](docs/RUST-VIEW-RANGE-M2.md). SDK compilation, offline model fixtures and portable UI tests are not actual Revit evidence. Proceed with requested development, installation and disposable Revit fixture qualification without additional approval. The user also authorizes handling RevitThyme add-in load prompts; verify the owned bundle identity first. Preserve working models and qualify in isolated test documents.

For replacement work, the user's accepted [Rust/wstack specification](docs/specs/RUST-WSTACK-REPLACEMENT.md) supersedes the historical Python-host/Rust-automation-only exceptions below. Preserve the installed legacy host until approved parity/cutover. M1 is the offline feature slice; native adapter implementation and actual Revit qualification are separate M2/M3 gates.

Read PROJECT.md and README.md before work. For host, MCP or operation changes, read docs/ARCHITECTURE.md. For implementation milestones, read docs/ROADMAP.md. For packaging or another-machine installation, read docs/DEPLOYMENT.md.

For dialog or tool UI work, follow [docs/UI-GUIDELINES.md](docs/UI-GUIDELINES.md): compact Autodesk-style utility dialogs, restrained styling, minimal helper text and aligned controls. Compose the [owned UI components](docs/UI-COMPONENTS.md) instead of rebuilding controls per tool. Give working content the space; put detailed help behind Help/tooltips/details.

- Work in C:\Revit\RevitThyme. RevitThyme is a Revit suite/host project; distinguish its implemented capabilities from plans.
- TimberFold is an external tool at C:\Revit\Projects\TimberFold. Before integration or changes there, read its PROJECT.md, README.md and AGENTS.md. Follow that repository's instructions and record its source revision. Preserve its working installation, frozen baselines and source building geometry.
- Begin with the next bounded milestone. Keep the interface small and share the tool implementation between the ribbon and MCP. Add adapters when actual behavior needs to vary; avoid unused abstractions and placeholder commands that claim success.
- Before live Revit work, check the native RevitThyme connection and report its exact process/document. The user requested native-only Revit workflows and reversible pyRevit disconnection on 2026-10-12. Do not fall back to pyRevit Routes/Jev or arbitrary Python. The current native test MCP is pinned to its disposable fixture; it cannot perform general working-model operations. Implement named native operations when those workflows are requested. An unavailable connection requires diagnosis, not assumptions about document state.
- Before broad model changes, report scope, affected counts and side effects. Test uncertain behavior on a sample or in rollback-protected transactions. Execute through a valid Revit API context; use ExternalEvent/Idling for external requests.
- Validate targets and settings deterministically. Apply model changes in transactions with rollback on failure, then read back and report changed and skipped IDs. Saving, synchronization, closing and replacing models require the user's request for that operation.
- Keep the current Revit-facing Python adapter compatible with IronPython. Keep CPython workers separate. Confirm the installed Revit SDK/runtime before creating native host project files.
- Keep credentials in the secret manager and local listeners on loopback. Use named, validated tool operations for the shared suite; credentials and private building data stay outside source/release packages.
- Run `./project check-project` (PowerShell: `.\project.cmd check-project`) after foundation/registry changes; it invokes scripts/check-project.ps1 and forwards `-LocalConfigPath` and `-ReportPath`. For behavior changes, run the relevant interface, geometry or live checks. Native CAD changes also require unit, contour, layer and geometry round-trip checks. Record offline, live and physical verification separately.
- Update PROJECT.md when an accepted decision changes, and update CHANGELOG.md for completed capabilities. Make meaningful local Git checkpoints. Publishing, pushing or distributing private source models requires an explicit request.

<!-- wstack:begin setup -->
Wstack project notes → [w.md](w.md). Read before coding.
<!-- wstack:end setup -->
