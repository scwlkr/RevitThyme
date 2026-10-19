# Native view/sheet image export qualification

This is the unreleased `0.10.0-image-export.1` / protocol-9 follow-up to WLK-247. It adds one typed native operation and one MCP tool, using the existing queue and retained outcomes. After the user closed Revit and authorized the restart window, the signed candidate was installed locally and qualified through normal startup. Electrical-model promotion, release publication and website deployment remain outside this work. The [operation contract](../docs/MCP.md#exact-view-and-sheet-images-unreleased) defines completion, targeting and recovery.

## Confirmed published baseline

An external official C# SDK client started the actual installed v0.10.0 MCP executable, initialized stdio, listed its 25 tools and read `list_operations` protocol 8. `run_query`/`run_modify` and `get_operation` exist; `export_view_image` does not. WLK-247's trusted-C# export workaround schedules callbacks outside the transaction owned by query/modify. Source/package metadata alone was not used to claim that the released MCP had typed image export.

## Test seams and isolation

Checks cross the public shared `IOperationClient`/dispatcher interface, MCP tool responses and an actual external official-SDK stdio client. Native checks use Revit 2027.3, a new empty process, an original saved two-wall fixture, printable plans, an isometric 3D view, a sheet with a viewport and a view template. Separate disposable documents test wrong-document and reopen rejection. Trusted C# is disabled in the qualification process; test edits use existing typed tools. No caller-written export callbacks or UI export fallback are used.

The initial startup-only test loader loads candidate assemblies in a separate assembly context. A test adapter wires the unchanged production `PipeServer`, `OperationDispatcher` and `RevitModel`; reflection supplies the test binding and fixture identity and forwards the normal revision/closing events. Production ribbon/dock startup is deliberately omitted to avoid replacing or duplicating the then-open installed host. This first pass qualifies the native operation and MCP path. The subsequent installed-host pass below separately qualifies normal application startup without that adapter.

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

## Normal installed-host follow-up

After all Revit processes were closed normally, the local production setup executable installed the signed candidate successfully. Isolated validation passed **20 installer upgrade/recovery, 25 global-command, 21 compiled setup and 7 native CLI checks**; package validation passed **37 checks**. These tests retained the complete existing toolkit payload requirements. The compiled setup's payload was source `9912d9a`; its local setup/installer source also included the candidate compatibility changes subsequently committed as `8527038`. The setup executable itself is unsigned; first-party payload binaries use the persistent local development certificate. Neither setup nor package was uploaded.

The first normal-host SDK attempt correctly exposed an existing `query_elements(includeTypes:true)` failure before any model edits: Revit forbids enumerating an unfiltered collector. A separate pinned trusted development query reproduced that exact error; an explicit type-or-instance filter read all 2,444 elements with no metadata failures or model changes. The development wrapper also still required protocol 8. Source `2329fb0db23e50b15344b49604084a25d233bcc3` fixes both issues, preserves protocol mismatch rejection, and makes unexpected SDK errors fail the harness explicitly. Build and **75 MCP / 47 development-safety checks** passed; standards and spec reviews found no actionable issues.

With Revit closed again, the documented package installer updated the standard receipt to a fresh signed immutable bundle, manifest SHA-256 `caa0ee85a94d199e74fee5c31a9d8878aa6a90c4cea0b05d3e727662ad42a378`. This package was built from clean source `2329fb0`; later evidence/documentation commits do not change its binaries. The earlier stable and candidate bundles, registration backups, raw user PATH registry type and unrelated entries, other add-in registrations and the user's TimberFold test RVT were verified intact.

On 2026-10-19 a fresh **normal** Revit 2027.3 process, PID 24960/start `639280400793403484`, session `2427f086-dfb1-46f4-bcf7-96f0736a07dd`, passed **57 external official-SDK checks** against the installed `revitthyme-mcp.exe`. No test loader, reflection host wiring, submitted callbacks or UI export were used. Normal ribbon startup was observed without a RevitThyme publisher prompt. `run_query(trusted:true)` was rejected as `trusted_code_disabled`.

This run covered exact plans, sheet and 3D exports; inline/file PNG bytes and dimensions; 128-pixel boundary and unsupported/missing/oversized target rejection; complete native element/type pagination; unchanged snapshot/selection/count/saved files across exports; and native wall height 9 → 18 → 9 with stale-capture rejection, changed 3D pixels and independent parameter readback. The client was terminated only after the native ownership marker appeared while its reply was still outstanding; the original call actually failed. A fresh installed client recovered the original PNG and outcome without a capture or resubmission, and repeated recovery preserved its timestamp. Conflicting IDs and stale process-start connections were rejected without fallback.

Two native Undo actions then removed the deliberate test transactions. Native readback confirmed height 9 and `IsModified=false`; the owned process closed normally. Saved source and copied RVT hashes remained `3AE0941F3B5043C430A8DF4E3E2E902189A899B4B2EF81B85336E6FB39BFD173`. This cleanup is separate from export's read-only guarantees. Private setup, failure, diagnostic, preservation and successful SDK receipts remain under ignored `artifacts/image-installed-20261019`; the successful normal run is `normal-1b426801dd854c5ea0f015170323e967`. Failed attempts were retained independently.

## Repeat

### Normal installed host

Build the SDK harness and run it against a freshly launched disposable normal fixture using `dotnet tests/Mcp/bin/Release/net10.0/RevitThyme.McpTests.dll --image-installed ABSOLUTE_NORMAL_RUN`. Its `installed-input.json` pins the absolute session, installed bundle, PID/start, fixture/original paths, saved SHA-256, immutable manifest SHA-256 and the fixture oracle. It intentionally refuses trusted startup and performs/restores two native height edits without saving. Retain the transcript and complete/cleanup receipts. Never point this harness at a working model.

### Initial isolated native adapter

```powershell
.\scripts\live-view-image.ps1 prepare
.\scripts\live-view-image.ps1 start -RunFolder ABSOLUTE_PREPARED_RUN
.\scripts\live-view-image.ps1 check -RunFolder ABSOLUTE_PREPARED_RUN
.\scripts\live-view-image.ps1 cleanup -RunFolder ABSOLUTE_PREPARED_RUN
```

Prepare requires the existing development signing certificate and local Revit SDK, and never installs a package. Start never swaps the installed registration. Wait for `ready.json`; startup or export failures are retained. Cleanup verifies the exact process/start and performs only a normal close, then verifies other registrations/processes. It refuses unfinished runs unless `-Abort` is explicit and an unmodified owned fixture is verified; it never force-kills Revit. A failed run cannot be restarted; prepare a fresh UUID run.

## Remaining limits and next step

The local installation now uses the signed protocol-9 candidate; published v0.10.0 remains protocol 8. This machine's Revit 2027.3 results do not qualify other machines, releases, every supported view subtype, graphics modes or modal/export failure conditions. Wrong-document/reopen cases were covered by the initial isolated native adapter; normal installed startup was qualified separately above. Cancellation cannot interrupt an in-flight native export; retained outcomes end at native shutdown. No new persistent cross-session recovery store, image resource server or file cleanup policy is introduced.

Explicit open/activate/save lifecycle operations, changed-ID budgets above 512 and bounded reviewed promotion remain separate WLK-247 work. Image export does not solve those issues. Next scope explicit lifecycle completion/recovery and its native wrong-document tests before any promotion. Physical TimberFold calibration, cutting and assembly remain outstanding. No release, push, merge or website deployment was performed in this follow-up.
