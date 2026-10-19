# TimberFold

The C# feature is being implemented in seven verified slices. The current source supports exterior-shell inspection and retained cutting-layout previews through **RevitThyme → Fabrication → TimberFold**, CLI and MCP. Final DWG kits, native review documentation and four-building delivery qualification remain open; the preserved external implementation is still available.

Open a project containing vertical basic exterior walls and planar roofs. Set the model scale and physical stock dimensions in inches, then choose **Inspect**. Review included/excluded source counts, phase/options, unsupported geometry and profile diagnostics. **Open files** gives the complete private source snapshot and diagnostic report when the window samples a larger inventory. Nominal settings do not establish physical fit. Inspection does not create model elements or cutting files and does not save the source.

```powershell
rthyme timberfold inspect --session ABSOLUTE_SESSION_JSON --json
rthyme timberfold inspect --session ABSOLUTE_SESSION_JSON --profile ABSOLUTE_PROFILE_JSON --scope ABSOLUTE_SCOPE_JSON --json
rthyme timberfold preview --session ABSOLUTE_SESSION_JSON --run-id NEW_GUID --json
rthyme timberfold status --session ABSOLUTE_SESSION_JSON --run-id SAME_GUID --json
rthyme timberfold cancel --session ABSOLUTE_SESSION_JSON --run-id SAME_GUID --json
```

The MCP `timberfold_inspect` tool uses a fresh `get_context` capture ID and optional typed profile/scope. Settings use physical inches; captured Revit geometry uses feet. Scope omitted means the active view phase (or a reported fallback) and main/primary options. Explicit scope records a phase ID and compatible design-option IDs. Full support and numeric requirements are in [support/acceptance](support.md).

Choose **Preview** to capture the current source, fit nominal-thickness panels, plan stock and stage inch-unit DXF files plus calibration coupons. The run ID acknowledges acceptance; poll until `PreviewReady`, `Failed`, `Cancelled` or `Unconfirmed`. Open files for canonical panels, joint removals, source/opening maps, layout, stage timings and complete diagnostics. Preview creates no model elements and never saves the source. Its manifest explicitly leaves native import/final-kit qualification and physical checks outstanding.

Keep the original run ID after a lost response. Poll status or recover the original write with `get_operation`; never issue a new ID for an uncertain request. Same-ID retries require identical source target and settings. Completed evidence survives host restart; interrupted runs remain unconfirmed and native work is never resumed automatically. Cancellation is cooperative; individual Autodesk calls cannot be forcibly interrupted. Closing the native window stops its polling without cancelling a run.

MCP also exposes `timberfold_preview(captureId, requestId, profile?, scope?)`, `timberfold_status(runId)` and `timberfold_cancel(runId)`. Status/cancel need no fresh capture and use separate process-bound control channels. Native work remains on Revit's callback; geometry, layout and file processing run in the background. Previous run folders are never overwritten.

This source uses protocol 6; install matching host, CLI and MCP together after qualification. Older installed clients/hosts cannot acquire new operations by reconnecting.

Development references: [spec](spec.md), [preserved baseline](baseline.md), [library evaluation](geometry-libraries.md), [isolated native workflow](../DEVELOPMENT.md), and the original fixture builders under the test project. Actual new C# Revit qualification and physical fabrication must be reported separately from source/build tests.
