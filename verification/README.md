# Verification records

`scripts/check-project.ps1` performs read-only structural checks. To save a record:

```powershell
& .\scripts\check-project.ps1 -ReportPath verification/foundation-2026-10-02.json
```

The report records check results, configuration mode, source revisions and a UTC timestamp. It explicitly declares that no live Revit call or generation occurred. Record local paths only as diagnostics; use portable configuration in source.

Future behavior reports must identify the operation, tool/host revisions, intended document identity, resolved settings, affected IDs, artifacts and verification scope. Distinguish offline, live-Revit, native-CAD and physical-fit evidence. Saved records describe the checked state at that time.
