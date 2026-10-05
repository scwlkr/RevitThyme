# TimberFold in RevitThyme

## Registration

Tool ID: `timberfold`. Category: **Fabrication**. Integration state: **read-only inspection**. [tool.json](tool.json) records the external source and implementation states. Version 0.2.0 exposes inspection through the RevitThyme ribbon and named Routes. Generation remains in the existing independent installation.

The local source root is resolved from config/local.json, falling back to config/local.example.json. The foundation example points to **C:\Revit\TimberFold** through the portable sibling path `../TimberFold`.

Source inspected on 2026-10-05: commit `45d49b4969d7cd67bb1bde821e20add0adf0a9b9`, declared version **1.1.0**, clean working tree on its setup branch. This snapshot is recorded in registration; installed inspection reports that it has not independently verified the selected folder's Git revision.

## Existing implementation

- `scripts/launcher.py`: IronPython orchestration with `run(doc, uidoc, config=None, notify=True)`.
- `scripts/extract_model.py`: Revit source extraction.
- `scripts/run_pipeline.py`: separate CPython geometry/validation entry point.
- `scripts/revit_outputs.py`: Revit documentation and native CAD outputs.
- `prototype-settings.json`: authoritative current defaults.
- `install.ps1` and `LaserModel.extension/`: existing standalone installation.

The launcher accepts supplied settings and can suppress its final notification. With no supplied config it opens a modal settings dialog. It synchronously waits for the CPython stages and runs generation/verification together. Those behaviors need deliberate adaptation for preview jobs, progress, cancellation and MCP use.

Generation writes a new run folder and adds review views/sheets. It preserves source building geometry and leaves saving to the user. Native CAD verification uses Revit, so an offline geometry pass does not prove the full workflow.

## Integration work

1. Read-only candidate/source-file inspection is implemented. Geometry extraction and fabrication settings validation remain separate work.
2. Separate explicit settings validation and preview computation from generation through the operation interface. Return diagnostics instead of opening unattended dialogs.
3. Queue Revit extraction/export in the host and run geometry externally. Bind each stage to its intended document and run identity.
4. Preserve source/part IDs, resolved settings, inverse transforms and verified artifacts in results. Report created review IDs and all skipped elements.
5. Keep TimberFold independently usable while validating RevitThyme integration on a small sample.

Read the external checkout's PROJECT.md, README.md and AGENTS.md before changing it. Do not duplicate its fabrication defaults here: the profile and its measured/provisional flags must be read from the selected settings at run time. Physical fit remains a separate validation stage.
