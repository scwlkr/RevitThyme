# Explicit document lifecycle qualification

This unreleased `0.10.0-lifecycle.1` / protocol-10 follow-up uses the existing shared queue, exact captured targets and retained request outcomes for background open, explicit activation and active-document save. [MCP contract](../docs/MCP.md#explicit-document-lifecycle-unreleased) defines supported local nonworkshared RVTs and completion/recovery. The installed image-export qualification at `ac2bab9` is preserved in [view-images.md](view-images.md); it is not repeated or attributed to these new lifecycle operations.

## Qualification boundaries

Source checks cover native queue dispatch, all six captured identity components, save document/path guards, argument rejection, cancellation before execution, pending/result recovery, conflicts and native completion/readback failure. Actual-Revit results will be recorded separately after the external SDK harness runs; a build is not native qualification.

`tests/Mcp/DocumentLifecycleLive.cs` starts the immutable installed MCP executable through the official SDK. It uses three private copies of an original nonworkshared RVT, with exact PID/start/session pins, trusted execution disabled, and native typed operations only. A dedicated normal process intentionally has no single-fixture path environment variable because this test must exercise multiple explicit documents; it is owned by this run and must contain no working model. Existing single-fixture development guards are retained.

The harness refuses original/copy hash mismatches, verifies full installed payload hashes, checks wrong document/stale captures and expected disk hashes, changes a view name, explicitly saves only the selected copy and independently compares native modified/name/path readback with disk hashes. It terminates only its owned MCP server after native file activity while the caller response is still outstanding, requires that response actually be lost, and recovers the original outcome without submitting another write. A separate fresh native process checks saved persistence.

Private fixtures, failed/successful transcripts and preservation receipts stay under ignored `artifacts/document-lifecycle`. Run `dotnet tests/Mcp/bin/Release/net10.0/RevitThyme.McpTests.dll --lifecycle-live ABSOLUTE_RUN`, then `--lifecycle-reopened ABSOLUTE_RUN` with that run's saved-copy session. `lifecycle-input.json` pins session, bundle/manifest hash, PID/start, initial/other/recovery copy paths and source/hash. Keep every failed attempt independently; never target a working model or replace an uncertain request ID.

## Remaining limits

No Home-screen/no-document open, explicit close, view switching, SaveAs, synchronization, cloud/workshared documents, modal dismissal or persistent cross-session recovery. Native open/save calls can block past transport deadlines and cannot be forcibly cancelled. File saves are outside model Undo. More than 512 changed IDs, model promotion, physical TimberFold fabrication, release publication and promotional posts remain separate.
