# Verification records

`scripts/check-project.ps1` performs read-only structural checks. To save a record:

```powershell
.\project.cmd check-project -ReportPath verification/foundation-2026-10-02.json
```

The report records check results, configuration mode, source revisions and a UTC timestamp. It explicitly declares that no live Revit call or generation occurred. Record local paths only as diagnostics; use portable configuration in source.

The CLI requires the pinned Rust/Cargo toolchain. Before that prerequisite is available on another machine, use the direct diagnostic documented under CLI bootstrap in AGENTS.md and distinguish its results from CLI execution.

`project ci` writes aggregate local results to ignored artifacts/local-ci, including checked/base SHAs, commands, scope selection, cleanliness and cache evidence. `project check-cli` writes the real CLI boundary report there. Exact-revision gates require a clean checkout; an unrelated user file must stay outside the checkpoint. The Windows skill repair is retained in wstack-setup-windows.patch; apply it in the owning skill only when that repair is absent. Its normalized patch context needs Git's `--ignore-space-change` for existing Windows line endings; a reverse dry run verifies that the repair is already present.

Future behavior reports must identify the operation, tool/host revisions, intended document identity, resolved settings, affected IDs, artifacts and verification scope. Distinguish offline, live-Revit, native-CAD and physical-fit evidence. Saved records describe the checked state at that time.
