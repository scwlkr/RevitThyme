# Native-modal request recovery

This qualifies the existing unreleased `0.10.0-lifecycle.3` host, protocol 12 / 31 MCP tools, when a native dialog blocks document opening. Production code and the installed package are unchanged. It extends the separate [active-close](active-close.md), [Home/view](ui-lifecycle.md) and [document lifecycle](document-lifecycle.md) evidence rather than attributing these checks to those earlier runs.

## Test boundary

`tests/Mcp/ModalRecoveryLive.cs` uses the real official-SDK external MCP client and immutable installed executable. The separately signed test-only `tests/LifecycleProbe` observer shows one native Revit `TaskDialog` during `DocumentOpened` for each explicitly armed copied A/B path. It does not perform lifecycle operations or modify a model. The intent is consumed once; no project warning is automatically dismissed. Test-dialog Close buttons were explicitly observed and selected with Computer Use only after the external client recorded dismissal readiness. All document operations and recovery queries used MCP.

Two distinct cases are required: allow the real transport deadline to expire while the dialog remains open, then terminate only an exact owned MCP server in a second blocked open. Independent and newly connected clients must recover Pending promptly for each original request GUID while the native callback remains blocked. After explicit test-dialog intervention, the original request must recover verified native completion without a replacement open or replay.

Pending replies use the polling query's response ID. Evidence pairs them with the submitted `get_operation(requestId)` argument; only a terminal retained outcome returns the original operation ID. The caller must keep the original GUID instead of replacing it with a Pending response's ID.

## Observed qualification — 2026-10-20

**36 external MCP/native checks passed** in normal installed Revit 2027.3, PID 28372/start 639280961821741770/session f311edaf-961d-4081-818e-9f9a3972c8fd. Native source was `a6b720c3c997cf77a268b78db0b04b112d87925d`, immutable manifest SHA-256 `8e5cfb42e8a0eb52927ca97fee0bc37426676b37917602074b43f856001fc902`. The native observer/client source is `0f92ea17d0e68fd9c801fe8e8611c006898581a4`; later documentation does not alter the qualified host. Trusted C# was disabled. No submitted C#, host loader, callback-based lifecycle implementation or UI lifecycle fallback was used.

- The A open exceeded its actual transport deadline: after **30.020 seconds** the original call reported `transport_lost` / `Unconfirmed`, while native history still reported Pending. Newly connected and independent clients both recovered Pending before intervention. Closing only the identified test dialog allowed the original request to recover `document_opened` / `Completed` with its exact document identity/path/hash.
- During B's native dialog, retained acceptance was observed while its original response was outstanding. Only the exact owned installed MCP server was terminated. Its original SDK call actually failed. A new client and the independent observer recovered Pending while the dialog remained open, then the original verified Completed outcome after explicit test-dialog Close.
- Six Pending polls took **2.5–98.9 ms**, each below the two-second qualification gate. Repeated terminal recovery returned identical retained outcomes. Subsequent already-open verification returned performed:false and the same native document IDs; neither interrupted request required a native reopen.
- Both background opens preserved Home and unchanged file bytes. After activating exact A, stale capture close, inactive B save, mismatched document ID/path and wrong expected hash were refused with the captured active state preserved. Explicit background B close left exact A active and unmodified.
- Source, A and B all retained SHA-256 `3ae0941f3b5043c430a8df4e3e2e902189a899b4b2ef81b85336e6fb39bfd173`. The owned process closed normally. The exact temporary probe registration was hash-checked and removed after exit. Installed package integrity, setup receipt, existing registrations, primary checkout/unrelated logo and working gable model were preserved. The toolkit installation and PATH were unchanged.

Private bindings, original intents, native modal enter/close receipts, two explicit UI intervention records, timed polling/timeout responses, recovered outcomes, SDK transcript and cleanup/preservation evidence stay under ignored `artifacts/modal-recovery`. The installed host and earlier native results retain their original provenance.

## Remaining limits

These are controlled native test dialogs, not qualification of every missing-reference, failure-processing, licensing or third-party warning. Real project dialogs still require explicit inspection and user intervention. This verifies recovery while a native callback blocks; it does not add automatic dialog dismissal or in-flight cancellation. Outcomes remain local to the native session and end when Revit exits. Last-active-document close to Home and bounded model promotion remain unresolved; keep the >512 changed-ID work separate. No release, website deployment or promotional posts. Physical TimberFold fabrication remains outstanding.
