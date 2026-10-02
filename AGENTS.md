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
- Run scripts/check-project.ps1 after foundation/registry changes. For behavior changes, run the relevant interface, geometry or live checks. Native CAD changes also require unit, contour, layer and geometry round-trip checks. Record offline, live and physical verification separately.
- Update PROJECT.md when an accepted decision changes, and update CHANGELOG.md for completed capabilities. Make meaningful local Git checkpoints. Publishing, pushing or distributing private source models requires an explicit request.
