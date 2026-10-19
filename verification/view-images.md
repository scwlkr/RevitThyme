# Native view/sheet image export qualification

This is the unreleased `0.10.0-image-export.1` / protocol-9 follow-up to WLK-247. It adds one typed native operation and one MCP tool, using the existing queue and retained outcomes. It does not authorize electrical-model promotion, installation over the working host, release publication or website deployment. The [operation contract](../docs/MCP.md#exact-view-and-sheet-images-unreleased) defines completion, targeting and recovery.

## Confirmed published baseline

An external official C# SDK client started the actual installed v0.10.0 MCP executable, initialized stdio, listed its 25 tools and read `list_operations` protocol 8. `run_query`/`run_modify` and `get_operation` exist; `export_view_image` does not. WLK-247's trusted-C# export workaround schedules callbacks outside the transaction owned by query/modify. Source/package metadata alone was not used to claim that the released MCP had typed image export.

## Test seams and isolation

Checks cross the public shared `IOperationClient`/dispatcher interface, MCP tool responses and an actual external official-SDK stdio client. Native checks use Revit 2027.3, a new empty process, an original saved two-wall fixture, printable plans, an isometric 3D view, a sheet with a viewport and a view template. Separate disposable documents test wrong-document and reopen rejection. Trusted C# is disabled in the qualification process; test edits use existing typed tools. No caller-written export callbacks or UI export fallback are used.

The startup-only test loader loads candidate assemblies in a separate assembly context. A test adapter wires the unchanged production `PipeServer`, `OperationDispatcher` and `RevitModel`; reflection supplies the test binding and fixture identity and forwards the normal revision/closing events. Production ribbon/dock startup is deliberately omitted to avoid replacing or duplicating the installed host. This qualifies the native operation and MCP path, **not** a newly installed candidate or its complete application startup.

The new test manifest disallows loading into existing sessions and is removed after exact-hash verification. All existing registrations and the user's already-open Revit process/start identity are recorded and checked. Candidate binaries are staged, signed using the existing persistent local development identity and inventoried before launch. Updated external MCP binaries can be separately inventoried with `-ClientDirectory`; no loaded native assembly or immutable bundle is replaced.

## Evidence and results

Private baseline, source-check logs, fixtures, bundle inventories, SDK transcripts and receipts are under ignored `artifacts/view-image*`. Successful native receipts are retained in UUID run `52d7beed-bede-4054-b36b-832f300bad45`; failed attempts are retained independently rather than overwritten. The exact terminal native and source counts are recorded in those receipts.

On 2026-10-19 the final external SDK run passed **58 checks** in owned Revit PID 29388/start identity `639280375625179211`, session `8a640cb6-aa45-4690-9230-073405fc75e7`. Native element count remained 2,444 and the saved fixture SHA-256 remained `3AE0941F3B5043C430A8DF4E3E2E902189A899B4B2EF81B85336E6FB39BFD173`. The export-only audit retained `IsModified=false`, original active view and empty selection. Deliberate typed edits left `IsModified=true` after value restoration; reopening discarded only those unsaved test edits and then correctly invalidated the fixture identity. Cleanup closed the owned process normally and verified all prior registrations/process identities.

Local validation passed **480 unit, 75 MCP, 13 Agent, 6 WPF and 46 development-safety checks**, with zero build warnings/errors. Full and portable checks passed, followed by a focused unit/MCP rerun for the final shared-dispatch argument guard (the extra unit regression rejects image arguments on unrelated run controls). The native exporter and MCP image/recovery path were unchanged by that final guard. The test-only isolated loader also built with warnings as errors. The published-baseline external client passed two inventory checks separately. Standards and scope reviews found no actionable issues. None of these source/transport checks substitutes for the actual native run above.

The qualification exercises:

- Active plan, inactive plan, inactive sheet and isometric 3D PNG exports. Exact native identities, file SHA-256/length, PNG signature/dimensions and inline MCP image bytes are checked independently by the external client.
- A native wall-height change through `set_parameters`, stale-capture rejection, a fresh capture and a visibly changed 3D PNG, followed by restoring the height. A separate rename/readback cycle confirms refreshed context and returned view metadata.
- No export-side change to native element count, active view, selection, modified flag or saved-RVT SHA-256. Unsaved typed test edits are explicitly distinguished from read-only export; the file is not saved by the operation.
- Template, missing view and out-of-bound image-size rejection. Stale revision/view, wrong fixture, reopened fixture and stale process-start binding are rejected without fallback. Connection recovery requires explicit selection of the valid session.
- Same request ID replay without rendering/file-timestamp change, conflicting argument rejection, and a changed retained PNG returning structured `image_artifact_unavailable` without regeneration.
- Deliberate termination of only the owned external MCP server after the native queue accepts an export; a new external client recovers the original successful request and inline PNG using `get_operation`, without a capture or resubmission.
- Shared-interface checks for pending recovery, pre-execution cancellation, all six target-identity components, invalid arguments, native completion failure and post-export state drift. MCP regression checks cover valid inline delivery, missing/changed/same-size-tampered files, structured stale-session errors and discovery retaining valid sessions.

Two early harness issues were corrected: a capture cannot be reused after reconnecting, and document activation/reopen must run in the test ExternalEvent rather than an Idling event handler. Negative tests also found and fixed two real MCP error paths: retained-file validation and invalid session data previously escaped as generic SDK errors because `InvalidDataException` was not caught. These failures are not represented as successful qualification.

## Repeat

```powershell
.\scripts\live-view-image.ps1 prepare
.\scripts\live-view-image.ps1 start -RunFolder ABSOLUTE_PREPARED_RUN
.\scripts\live-view-image.ps1 check -RunFolder ABSOLUTE_PREPARED_RUN
.\scripts\live-view-image.ps1 cleanup -RunFolder ABSOLUTE_PREPARED_RUN
```

Prepare requires the existing development signing certificate and local Revit SDK, and never installs a package. Start never swaps the installed registration. Wait for `ready.json`; startup or export failures are retained. Cleanup verifies the exact process/start and performs only a normal close, then verifies other registrations/processes. It refuses unfinished runs unless `-Abort` is explicit and an unmodified owned fixture is verified; it never force-kills Revit. A failed run cannot be restarted; prepare a fresh UUID run.

## Remaining limits and next step

The ordinary installed application remains published v0.10.0/protocol 8. The candidate needs coordinated installed-host/startup qualification during an authorized restart window before use in working models. This machine's Revit 2027.3 results do not qualify other machines, releases, every supported view subtype, graphics modes or modal/export failure conditions. Cancellation cannot interrupt an in-flight native export; retained outcomes end at native shutdown. No new persistent cross-session recovery store, image resource server or file cleanup policy is introduced.

Explicit open/activate/save lifecycle operations, changed-ID budgets above 512 and bounded reviewed promotion remain separate WLK-247 work. Image export does not solve those issues. First qualify the installed candidate; then scope explicit lifecycle completion/recovery before any promotion. Physical TimberFold calibration, cutting and assembly remain outstanding.
