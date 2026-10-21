# MCP interface

Thirty-eight tools expose native operations, saved tools and result checks: thirteen generic tools, five document/view-lifecycle tools, one image-export tool, four utility tools, two trusted C# tools, three saved-tool tools, three result-check tools and seven TimberFold tools. **Version 0.13.0 uses package `0.13.0-checks.1` and protocol 14.** Version 0.10.0 used 25 tools/protocol 8 (`0.10.0-timberfold.4`), without typed image export or document lifecycle/discovery. Both external clients and the optional [built-in Agent](AGENT.md) use this interface. TimberFold physical cutting and assembly remain outstanding. See [release qualification](../verification/release-0.11.0.md), [Native utilities](NATIVE-UTILITIES.md), [Dynamic execution](DYNAMIC-EXECUTION.md) and [TimberFold](timberfold/README.md) for their scope.

The server uses official `ModelContextProtocol` **2.2.0**, pinned with a NuGet lockfile, and local stdio. It calls the same Autodesk-independent Core and OS-verified local pipe as the CLI; the add-in drains operations through ExternalEvent. No legacy bridge or arbitrary-code fallback exists.

| Tool | Behavior |
| --- | --- |
| `list_sessions` | Discover live process/start/session bindings in the configured directory |
| `connect_session` | Explicitly connect to a chosen absolute binding file; handshake and reset captures |
| `get_context` | Native document/view inspection and connection-local `captureId` |
| `list_operations` | Typed operations, units, limits and recovery semantics, including explicit active-close handoff and its limits |
| `list_documents` | Read visible session-native document identities and flags at Home or in a model, with stable paged inventory and retained read recovery |
| `list_views` | Paged native view IDs, names, classes and template flags |
| `query_elements` | Paged elements, optional negative category ID/name filter, optional types |
| `inspect_elements` | Properties and paged typed parameters for 1–5 native UniqueIds |
| `get_selection` | Read up to 100 selected UniqueIds |
| `set_selection` | Verify selected IDs, restore UI selection on failure; empty array clears |
| `rename_view` | Shared Foundation operation, captured name, verified readback and one Undo |
| `set_parameters` | Atomic batch of 1–20 expected-value parameter edits and one Undo |
| `get_operation` | Retrieve the retained native outcome without submitting the edit again |
| `export_view_image` | Exact native view/sheet PNG, verified inline image and file metadata, unchanged model and retained request recovery |
| `open_document` | Explicit local RVT and expected saved hash; open in the background and return native document identity |
| `activate_document` | Activate an already-open exact document ID/path, with native active-document readback |
| `save_document` | Save only the exact captured active document with its expected disk hash; verify clean native state and resulting file hash |
| `close_document` | Close one exact clean document; active close requires an explicit already-open document to activate first; no save/discard |
| `activate_view` | Switch to one exact native printable view/sheet in the captured document without transaction or save |
| `get_view_range` | Four native range planes, level choices and template edit control |
| `set_view_range` | Exact expected planes, native validity/readback/rollback, one Undo |
| `preview_room_dimensions` | Read-only rectangular room plan against native wall finish faces and tag bounds |
| `create_room_dimensions` | Recomputed exact preview, verified native associative pair and one Undo |
| `run_query` | Explicitly trusted Roslyn C# method body, serialized JSON result, always rolls back current-document changes |
| `run_modify` | Explicitly trusted Roslyn C# method body, native change IDs, rollback on failure and one Undo |
| `save_tool` | Save an individual trusted C# definition, typed inputs, expected result and executable tests outside the installation |
| `get_tool` | Retrieve the saved definition, exact digest and unverified test status by tool ID |
| `run_tool` | Validate inputs and the exact definition digest, then execute the saved method body or a named test through trusted native execution |
| `prepare_result_checks` | Resolve a declared plan's allowed changes against the pre-action native target and original request ID |
| `verify_result` | Recover the retained native operation, read declared observations and compare expected values and allowed changes without resubmitting the edit |
| `verify_tool` | Recover/check a saved tool's original operation using its exact definition digest, original inputs and configured result checks |
| `timberfold_inspect` | Captured exterior-shell accounting and private evidence, without model changes |
| `timberfold_preview` | Accept a retained request/run ID; capture, fit, nest and stage digital DXF preview files |
| `timberfold_generate` | Generate from an unchanged captured target and verified preview ID; native kit documentation and exports in one Undo group, without model save |
| `timberfold_status` | Inspect retained progress or terminal evidence without a native callback/capture |
| `timberfold_cancel` | Request cooperative cancellation by run ID, preserving previous outputs |
| `timberfold_verify` | Rehash and validate retained preview or kit evidence by run ID, without native edits or a capture |
| `timberfold_reconcile` | Check an existing committed native kit using a fresh capture; retain separate recovery evidence without recreating documentation |

MCP tool annotations identify reads/writes; results include JSON text and structured content. Rejected, rolled back and uncertain results set MCP `isError=true`. SDK schema/argument errors also fail the tool call. Query limits are 1–100; element/parameter pages shrink further to fit the 64 KiB operation budget inside the 256 KiB bridge frame. Follow `nextOffset`/`nextParameterOffset` until null. Parameter strings over 512 characters are marked truncated and cannot be edited.

## Saved Revit tools

The [Agent creation skill](AGENT.md#create-a-reusable-tool) turns a plain-language request into an individual reusable trusted C# tool. These MCP calls also work for external clients:

| Call | Contract |
| --- | --- |
| `save_tool(toolId, definition)` | Create a definition under a caller-owned GUID. Repeating the same ID and definition is safe; conflicting replacement is refused. Does not connect to Revit or execute code. |
| `get_tool(toolId)` | Return `toolId`, definition `digest`, the complete definition and `testStatus: "not_run"`. |
| `run_tool(captureId, requestId, toolId, digest, inputs, trusted=false, budgetMilliseconds=10000, testName=null)` | Verify the exact saved definition and typed input object, then submit its query or modify body. A named test runs only its assertion as a rollback query. |

A definition contains `name`, the original `request`, `description`, `mode` (`query` or `modify`), C# `source`, `inputs`, `expectedResult`, `tests` and optional `resultChecks`. Input definitions contain `name`, `type`, `description` and `required`; supported types are `string`, `number`, `integer`, `boolean` and `string[]`. Tests contain `name`, an example `inputs` object, model `assumptions`, `expectedResult` and an `assertionSource` method body returning a Boolean. The body receives the same `inputs` JSON object as the main tool. Review the full definition before execution.

Definitions persist in `%LOCALAPPDATA%\RevitThyme\tools` across server and Revit restarts, independently of installed package versions. `REVITTHYME_TOOLS_DIRECTORY` selects an absolute alternate folder for isolated qualification. The save call writes local files and is declared as a mutation; get is read-only. Neither implies compilation or successful testing. Saved code and test assertions retain full-process trust requirements; `run_tool` is potentially destructive even for rollback queries.

Retain the definition digest returned by save/get and provide it at execution. Changed definitions, invalid IDs, unknown/duplicate input properties, missing required inputs and type mismatches are rejected before native execution. A fresh capture and caller-owned GUID are still required. The tool's expected-result description expresses intent. Optional `resultChecks` defines structured checks that run automatically after a definite successful main operation; definitions without a plan retain their original behavior. See [input binding, test use and recovery](DYNAMIC-EXECUTION.md#saved-tool-inputs-and-tests).

## Workflow result checks

Saved definitions may include `resultChecks`. `run_tool` prepares that plan before its main operation and verifies it after definite native success. An unchanged tool without a plan behaves as before. A named test remains a separate explicitly requested assertion, rather than an automatically managed fixture test.

A `ResultCheckPlan` contains `checks`, `allowedChangesSource`, `maxAdded` (default zero) and optional `addedCategoryIds`. Each check has a `name`, C# `source`, `comparison` (default `equals`), optional JSON `expected`, optional `expectedInput` name, and optional numeric `minimum`/`maximum`. Equality requires exactly one fixed `expected` value or `expectedInput` referencing the invocation's input object. Observation source returns an object with `exists` and `value`; Core evaluates `equals`, `exists`, `absent` or `range`. Missing data, source errors and uncertain native state are reported rather than treated as matching values.

`allowedChangesSource` runs before the action and returns `modified` and `deleted` arrays of native element IDs. Added elements must match both the declared maximum and allowed native category IDs. The report compares the original operation's actual native added/modified/deleted IDs with these permitted changes and flags unexpected IDs. Declare the intended policy before running the action; do not derive permission from the changes after they occur.

Plans contain 1–16 uniquely named checks. Added-element limits are 0–512; permitting additions requires up to 64 declared negative native category IDs. Plans are bounded to 64 KiB, input objects to 8 KiB and observation data to 32 KiB of serialized JSON. The protocol-14 bridge frame is bounded to 256 KiB so independently bounded source and data fit after JSON escaping. The generated observation method body must fit the trusted 24 KiB source limit, including its adapter, and its complete returned JSON must fit 8 KiB. A plan that fits storage can still exceed an execution bound; such failures are not verification passes.

For a caller-managed sequence, prepare the plan against a fresh `captureId` with `prepare_result_checks`, using the original action `requestId`, input object and intended `sourceSha256`. After executing or recovering that action under the same request ID, call `verify_result` with the same plan, inputs and source identity. Both calls require `trusted=true`; their cooperative `budgetMilliseconds` uses the trusted execution limits. Verification retrieves authoritative retained native results rather than accepting an arbitrary caller-supplied success record. It does not execute the action again.

| Call | Arguments |
| --- | --- |
| `prepare_result_checks(captureId, requestId, plan, inputs, sourceSha256, trusted=false, budgetMilliseconds=10000)` | Fresh captured target, original action request ID and declared plan/inputs/source identity |
| `verify_result(requestId, plan, inputs, sourceSha256, trusted=false, budgetMilliseconds=10000)` | Original request ID and unchanged prepared plan/inputs/source identity |
| `verify_tool(requestId, toolId, digest, inputs, trusted=false, budgetMilliseconds=10000)` | Original request ID, exact saved definition digest and original inputs |

Preparation returns `operationId`, deterministic `preparationId`/`verificationId`, `planDigest`, `sourceSha256` and a retained native `receipt`. Stop before the action if the receipt failed. `verify_tool` resolves the saved definition and expanded action source itself; it requires the original preparation and does not execute the saved tool. Missing preparation cannot produce a passing check. A first observation rejects an intervening native revision; recovery of an already retained observation describes the original checked state and does not certify later model changes.

The report keeps its check status (`passed`, `failed`, `error` or `unconfirmed`) distinct from the original native outcome. A committed Applied operation remains Applied even when a comparison fails, an unexpected change is found or verification is uncertain. Observations include expected and observed values/errors; change coverage includes actual, permitted and unexpected IDs. Attribution binds the report to the original operation and exact captured target, definition where available, plan and inputs. Inspect these records before another edit; there is no automatic Undo or retry.

Verification is returned on the original operation result's `verification` field, with status, operation/preparation/verification IDs, plan/source identity, message, per-check comparisons, `actualChanges`, `allowedChanges`, `unexpectedChanges` and policy `violations`. The MCP call sets `isError=true` when the original operation failed or its check status is not `passed`; inspect both the original `outcome` and `verification.status` to understand whether model changes were committed.

Readback uses a native observation path without a writable model transaction; comparison occurs in Core. Trusted C# still has full process permissions and no sandbox. The result covers only the declared observations and allowed-change policy, rather than every possible model defect. See [execution boundaries](DYNAMIC-EXECUTION.md#workflow-result-checks) and [CLI recovery](CLI.md#workflow-result-checks).

[Qualification](../verification/workflow-result-checks.md) records the exact installed package, actual-Revit comparison/guard/recovery matrix, real Agent integration and preservation evidence. Offline checks and release/publication checks remain separate from native proof.

<a id="open-document-discovery-unreleased"></a>

## Open document discovery

`list_documents(captureId, requestId, offset=0, limit=50, inventoryToken?)` reads the selected native session, including at Home. Retain a caller GUID before submitting. Each item contains the same native document ID used by lifecycle operations, full title/path, and active, modified, family, linked, workshared, read-only and saved-path flags. These flags describe state; they do not authorize activation, saving or closing unsupported documents. Unsaved paths remain empty. No file is hashed, opened, activated or saved.

The returned `isReadOnly` is Revit's current dynamic document state. It is distinct from `IsReadOnlyFile`: a model opened from a read-only disk file can remain editable in memory. `save_document` independently checks that native file constraint and returns `read_only_file` before attempting a save, for both clean and dirty documents. Opening/activating such a nonworkshared project remains allowed. SaveAs is outside this operation; no fallback path or automatic discard is selected.

Use `get_context`, then start at offset zero without a token. Follow each `nextOffset` with the returned `inventoryToken` and the same valid capture. Pages are sorted by session-native document ID. The token fingerprints the native session and complete visible inventory, including every returned flag; membership or metadata changes return `stale_inventory` even if the active document/revision stayed unchanged. Refresh context and start at zero without the old token. Wrong/stale captured targets return `stale_target`. Fixture-bound development sessions expose only their permitted fixture.

Limits are 1–100 records per requested page, 4096 visible documents, 16 KiB per metadata record and 1 MiB total metadata. Pages shrink to fit the bridge bound; identity/path fields are never truncated and no unsupported documents are silently omitted. An oversized inventory returns an explicit error. All reads run through the shared queue inside a native API callback without a transaction. `get_operation` can recover the caller's original read; replay/recovery returns that point-in-time page, so use a fresh capture/request to inspect current state. See [qualification status](../verification/document-discovery.md) and [CLI](CLI.md#discover-open-documents-unreleased).

<a id="exact-view-and-sheet-images-unreleased"></a>

## Exact view and sheet images

After an edit, call `get_context` again, use `list_views` to identify a native view UniqueId, then call:

```json
{
  "captureId": "CAPTURE_FROM_GET_CONTEXT",
  "requestId": "CALLER_GENERATED_GUID",
  "viewId": "NATIVE_VIEW_UNIQUE_ID",
  "pixelSize": 1600
}
```

`export_view_image` uses the existing serialized ExternalEvent queue and runs outside a model transaction. It exports only that view or sheet, including an inactive view, using native `SetOfViews`; it does not activate a view, change selection, save a model, create an image element or add an Undo item. The captured process/start/session/document/active-view/revision must still match at execution. There is no active-view fallback. An export can inspect an unsaved edit; its returned snapshot must retain the original modified state.

The bounded output is one PNG, 128–4096 pixels on its longest edge and at most 8 MiB. Supported targets are printable plans, sections, elevations, details, drafting views, 3D views and non-placeholder sheets. Templates, schedules, missing IDs and unsupported view classes are rejected. Revit graphics/export restrictions can still refuse an otherwise supported view. There is no batch export, crop override, arbitrary output path, model save or document lifecycle operation in this slice.

`code: image_exported`, `outcome: Unchanged` means native export returned, the PNG decoded, its dimensions/length/SHA-256 were recorded, and the native snapshot remained unchanged. The response contains JSON/structured `imageExport` metadata (exact view identity, private absolute path, SHA-256, bytes, width, height) **and an MCP `image/png` content block**, so an image-capable agent can see the native result directly. Compare native parameter readback as well as pixels; an image alone does not establish model correctness. Native implementation uses Autodesk's [image-export options](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/c2e823a1-6eb0-2bf3-f07b-ed46d8f7b70a.htm) and [Document.ExportImage](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/b98a3e71-39fa-bf7a-cb3d-591d8b1fcd93.htm), checked against the installed 2027 SDK.

Keep the request GUID before submitting. On timeout, cancellation or disconnect, reconnect to the **same native session** and call `get_operation(requestId)`. It returns `pending`, `not_found`, or the retained terminal result; a recovered success includes the verified inline PNG. Poll rather than replacing an uncertain request ID. Identical ID/arguments return the original result without rendering again; conflicting arguments return `id_conflict`. A fresh MCP connection needs a new capture for new operations, but outcome recovery needs no capture. Captures do not survive reconnects. Retained outcomes survive MCP server restarts, not native session shutdown. Native export cannot be interrupted once started; the current 30-second transport deadline can expire before a native callback returns. Protocol 12 uses an independent process-verified outcome pipe; retained pending results remain available while the native callback runs.

Files live under `%LOCALAPPDATA%\RevitThyme\view-images\SESSION_GUID\REQUEST_GUID`; a create-new ownership marker prevents overwriting a previous attempt. They contain private model imagery and remain until explicitly removed. Output files are not part of Revit Undo. `export_incomplete`/`export_unconfirmed` mean partial files or unverifiable completion, with no automatic regeneration. `image_artifact_unavailable` means the retained PNG is missing, unreadable or hash-changed: metadata is retained and no replacement export occurs. Exact replay is a completion receipt, not a new render or a new check of today's model. Capture and use a new GUID only when intentionally requesting a new image after resolving prior uncertainty.

Native qualification and limits are recorded in [image-export verification](../verification/view-images.md). The 0.11.0 runtime passed 59 external SDK checks against normal Revit startup with trusted code disabled, including edit/readback, real Pending recovery and verified inline PNG bytes. Earlier results retain their original source/package identities; [release qualification](../verification/release-0.11.0.md) identifies the frozen payload.

## TimberFold kit runs

Call `get_context`, then `timberfold_preview(captureId, requestId, profile?, scope?)` with a caller-generated GUID. Poll `timberfold_status(runId)` until `PreviewReady` or a failure state. Preview writes private nominal-inch cutting files but does not edit the model.

For generation, capture fresh context from the same unchanged source and call `timberfold_generate(captureId, requestId, previewRunId)`. Use a new generation GUID distinct from the preview ID. The preview supplies every fabrication setting and scope; generation accepts no overrides. Changed settings require a new preview. The captured target must still match the preview's document, view and revision. The returned generation run retains `previewRunId`; poll its new ID through `Guides` and `Finalizing` to `KitReady` or a failure state.

`KitReady` means digital files and native readback passed. The model contains native review geometry, views/sheets and imported drawings under one Undo group; it is never automatically saved. `manufacturingReady` remains false and `physicalChecksOutstanding` remains true. `timberfold_verify(runId)` checks the retained file inventory and hashes again without model edits; changed or missing evidence downgrades verified readiness.

Cancellation remains cooperative. Once a native callback starts, wait for its definitive commit or rollback receipt; cancellation cannot turn a confirmed commit into a cancelled run. On uncertain submission or publication, retain the original request/run ID, poll status and `get_operation`, and inspect the retained evidence. Never submit a replacement generation ID to retry an uncertain native edit. Closing the native settings window does not cancel the run.

The pinned actual-SDK harness is `dotnet run --project tests/Mcp/RevitThyme.McpTests.csproj -- --timberfold-live ABSOLUTE_DEV_RUN ABSOLUTE_FIXTURE_RECEIPT`. It checks preview, generation, verification, saved output hashes and source RVT preservation through the immutable installed MCP executable. Run it only against its isolated disposable fixture; it leaves generated native documentation unsaved for inspection. A successful build alone does not establish a native pass.

<a id="explicit-document-lifecycle-unreleased"></a>

## Explicit document lifecycle

Start with `get_context`, including at Home. `snapshot.hasDocument:false` explicitly identifies no active document: native document/view IDs and path are empty, `canModify:false`, while process/start/session/revision still pin a valid lifecycle capture. Document reads/edits refuse Home; no fallback model is selected. Every lifecycle call uses its `captureId` and a new caller-generated `requestId`, and executes on the existing ExternalEvent queue outside a model transaction. The current process/start/session/document/view/revision must still match. The scope is canonical absolute fixed-local-drive `.rvt` paths, Revit 2027 and nonworkshared project documents. Reparse aliases, family/linked/workshared projects and unsupported edit modes are refused. Fixture-bound development sessions retain their original single-path/reopen guards.

- `open_document(captureId, requestId, path, expectedSha256)` verifies the saved file before opening it in the background. It returns `documentLifecycle.documentId`, path, title, active/modified flags and SHA-256; the current active snapshot and saved file remain unchanged. Already-open clean paths return the existing identity; dirty paths are refused.
- `activate_document(captureId, requestId, documentId, path)` requires that exact ID/path already be open in the selected session. It does not implicitly open, save or close a file. Success verifies the active native document and returns its fresh snapshot. Refresh context before another action.
- `save_document(captureId, requestId, documentId, path, expectedSha256)` accepts only the captured active document, exact existing path and expected current disk hash. SaveAs, synchronization and implicit saves of another document are excluded. Native `Save()` runs only if modified; completion verifies `IsModified=false` and hashes the resulting file. The saved disk bytes are outside model Undo.

`outcome: Completed` with `document_opened`, `document_activated` or `document_saved` is verified native lifecycle completion, distinct from a committed model transaction. `performed:false` means the requested native state was already satisfied. A file hash mismatch is `file_changed`; wrong ID/path or inactive save is `wrong_document`; changed captured context is `stale_target`. A native call or post-call verification failure can return `lifecycle_unconfirmed` after opening, activation or saving occurred. Inspect the native state and recover that original ID before continuing.

On interruption reconnect to the same native session and call `get_operation(requestId)` without a capture. Pending/completed outcomes use the same retained history as image export; recovery never repeats the lifecycle action. Identical request/target replay returns its receipt; conflicting arguments return `id_conflict`. History ends at native shutdown. Native file calls or dialogs can outlast the 30-second transport deadline; cancellation cannot interrupt an in-flight save/open. Protocol 12 routes recovery through a separate process-verified outcome pipe, accepting only `GetOperation`, so it does not wait behind a pending native lifecycle call. No cross-session recovery store, arbitrary model promotion or changed-ID budget increase is included.

### Native dialogs and recovery

A dialog can keep the native operation Pending after the original MCP call returns `transport_lost` / `Unconfirmed` or its server disconnects. Keep polling `get_operation` with the **original saved request GUID**; do not create a replacement operation. Pending is currently returned with `isError:true`, `code: pending` and the polling query's response ID. Retain the requested GUID; do not replace it with that response ID. Terminal recovery returns the original operation ID and result.

Explicitly inspect and resolve the dialog in the pinned Revit process. The toolkit does not dismiss project warnings. After intervention, recover the original terminal outcome and inspect exact native document/path/state before continuing. `get_context` and ordinary model operations require Revit's API callback to become available again; outcome polling uses the independent pipe. An uncertain native terminal result still requires inspection rather than automatic retry.

The [native-modal qualification](../verification/modal-recovery.md) records actual transport timeout, lost-server response, Pending responsiveness and original completion after controlled test-dialog intervention. It does not establish handling of every project warning or recovery after native-session shutdown.

### Home, clean close and exact view switching

Home supports background `open_document` and explicit activation by the returned document ID/path. Opening alone does not activate a project. Closing/reopening creates a new document identity; old captures and closed IDs cannot retarget the new document.

`close_document(captureId, requestId, documentId, path, expectedSha256, activateBeforeClose?)` accepts one exact **clean** local project. Background close preserves the captured active document/view/modified state, including Home. Dirty documents return `document_dirty`: explicitly save first if desired.

To close the captured active document, explicitly supply `activateBeforeClose: { documentId, path, expectedSha256 }` for a different **already-open, clean, editable local project**. Wrong, reopened, dirty or changed-file handoff targets are refused before activation. The existing queue synchronously activates that exact document, rechecks both native identities/clean state/hashes after activation, then directly calls `Document.Close(false)` on the original target. Completion verifies the original document is absent, its saved hash unchanged and the exact selected document/view remains active, clean and unchanged on disk. No posted Close command, deferred command ownership, implicit open, save, discard or fallback is involved. The handoff is outside Undo. If activation or close fails/cancels, `lifecycle_unconfirmed` may mean the explicitly selected document is already active and the original remains open; inspect native state and recover the original ID. There is no automatic restoration or second close.

Without an explicit handoff, active close returns `close_blocked` before side effects. Last-active-document close to Home remains unsupported: both [Document.Close](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/5948b03d-5537-33d4-6e38-a8f16d5d6779.htm) and [UIDocument.SaveAndClose](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/b7a3b928-bca9-d060-72b6-d7feaa2e8439.htm) prohibit closing the active document. At Home, closing the last background document keeps Home in the same process.

A posted native Close has no request-specific invocation identity. A generic pre-close event cannot safely establish that a particular queued command was consumed. No native Close command is posted. Cancellation before execution has no close side effect; in-flight native calls are not forcibly cancelled. A third-party cancelled close keeps the still-open native document's retained identity; only completed closure retires it. Recover the original request ID and inspect rather than submitting a replacement after uncertainty.

The [active-close qualification](../verification/active-close.md) records 58 installed-host external MCP checks, actual third-party cancellation and original-response loss/recovery. These are separate from the earlier Home/background-close/view results below.

`activate_view(captureId, requestId, viewId)` selects exactly one printable non-template native view/sheet UniqueId in the captured active document. It opens no model transaction and does not save. `view_activated` / `Completed` requires exact native active-view readback, the same document/path and unchanged modified state. Templates, missing IDs and stale/wrong document captures are refused. Refresh context afterwards. Native API/graphics restrictions may still return `view_unconfirmed`; recover the original ID and inspect before continuing.

See [Home/close/view verification](../verification/ui-lifecycle.md) for this slice's separate native results. Revit's [active-view property](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/b6adb74b-39af-9213-c37b-f54db76b75a3.htm), [close restrictions](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/5948b03d-5537-33d4-6e38-a8f16d5d6779.htm) and Autodesk's [posted-close example](https://blog.autodesk.io/close-doc-and-zero-doc-rvtsamples/) define the native integration boundaries.

The native adapter uses Autodesk's [OpenDocumentFile](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/11420983-6d0d-0022-e24c-2361c4e42fba.htm) and [OpenAndActivateDocument](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/3b3d671d-47ec-2ed8-1818-a7c19d01884b.htm) APIs, compiled against the installed Revit 2027 SDK. Source checks and actual-Revit evidence are recorded separately in [lifecycle verification](../verification/document-lifecycle.md).

## Parameter identity

`inspect_elements` returns each field's exact document-local `id`, existing name/data type/raw value and read-only/truncation flags, plus:

| Field | Meaning |
| --- | --- |
| `isShared` | Native Revit shared-parameter status |
| `sharedGuid` | Native shared GUID, or explicit null for nonshared fields; GUID access is guarded by `IsShared` |
| `builtInParameter` | Native `BuiltInParameter` enum name, or explicit null when not applicable (`INVALID`) |
| `hasValue` | Native Revit `HasValue`, independent of the raw value; assigned does not mean nonempty, useful or safe to delete |

The CLI exposes the same typed operation and JSON with no trusted-code opt-in:

```powershell
rthyme inspect-elements --session C:\absolute\session.json --element ELEMENT_UNIQUE_ID --json > C:\absolute\page1.json
rthyme inspect-elements --session C:\absolute\session.json --element ELEMENT_UNIQUE_ID --capture C:\absolute\page1.json --parameter-offset 20 --json
```

Use the returned `nextParameterOffset` for the next call (it may be less than 20). The CLI captures context on the first page and requires `--capture` for continuation pages; MCP uses the supplied `captureId`. Both paths reject stale captured revisions. Capture files contain model paths; keep them private. Inspection opens no model transaction and neither path saves the model.

For example, two fields named **Comments** can have `id: "-1010106"`, `builtInParameter: "ALL_MODEL_INSTANCE_COMMENTS"`, `isShared: false`, `sharedGuid: null` and `id: "123456"`, `builtInParameter: null`, `isShared: true`, `sharedGuid: "87a96cdc-7f6a-44b6-b0a7-fd5faecfcb22"`. IDs and GUIDs here are illustrative. Compare the coordinator's GUID with the returned shared field, then use that field's exact local `id` in the existing `set_parameters` edit with its exact raw `expected` value. Two shared fields with the same name and different GUIDs remain separate, even across pages. Ordinary nonshared project fields have neither identity. Inspection does not automatically enumerate absent type fields or choose a field for you. IDs stay document-local; GUIDs do not authorize model-wide edits.

## External Codex setup

Install the local package with Revit closed normally. The installer prints `mcp`, the absolute framework-dependent server executable. Configure that executable directly, keeping build logs off MCP stdout:

```powershell
codex mcp add revitthyme -- 'C:\absolute\installed\mcp\revitthyme-mcp.exe'
codex mcp list
```

For an isolated or otherwise pinned target:

```powershell
codex mcp add revitthyme_test -- 'C:\absolute\mcp\revitthyme-mcp.exe' --session 'C:\absolute\sessions\session-id.json'
```

Normal sessions are in `%LOCALAPPDATA%\RevitThyme\sessions`. Optional `--sessions-directory ABSOLUTE_FOLDER` limits discovery. Startup `--session ABSOLUTE_FILE` prevents switching to another binding. Dead/reused processes and mismatched protocols are rejected. Version 0.13.0 uses protocol 14; versions 0.11.0 and 0.12.0 used protocol 13; version 0.10.0 used protocol 8. Upgrade the add-in, CLI and MCP together. Every server starts disconnected unless pinned; it never chooses the first running Revit process. These configuration examples are supported by [official Codex MCP documentation](https://developers.openai.com/codex/mcp).

For source development, `project.cmd mcp` builds to stderr and then starts stdio. Installed/published executables avoid startup compilation. No account configuration is edited automatically by packaging or qualification.

## Scripted operation flow

1. Discover sessions and explicitly connect to the intended process. Confirm document path/title and active view through `get_context`.
2. Query elements and inspect native UniqueIds, numeric parameter IDs and typed raw values. Use the resulting capture only while document, view and revision remain unchanged.
3. For each new write, supply a fresh GUID `requestId`. Parameter edits contain `elementId`, `parameterId`, `expected` and `value`. A typed value has `storage` equal to `String`, `Integer` or `Double`, and exactly one `text`, `integer` or `number` field. Double values use Revit internal units, including feet for length and radians for angles; `dataType` is the native Forge type ID. ElementId/None parameters are inspection-only. Duplicate parameter targets and read-only/truncated fields are rejected before mutation.
4. Inspect fresh context and parameters to verify the native result. A successful parameter batch or rename is a single native Undo item. Selection is UI state and can already be empty after a native rollback.
5. On connection loss, use `get_operation(requestId)` and inspect native values. Retry only the same ID and identical arguments, never a new ID for an uncertain edit. Outcomes remain in the add-in until session shutdown; pending requests can be polled. Connection-local captures retain at most 512 entries and reset on explicit connection changes; use outcome recovery if an old capture has expired.

Saving and clean document closing occur only through their explicit lifecycle tools. Other typed operations never save or close models; synchronization is unsupported. Room Dimensions creates only its verified native dimension pair. Trusted C# is disabled by default, has full process permissions, and can affect files or other state outside Revit rollback; both tools are annotated as potentially destructive. Host opt-in, caller trust and the standard caller approval policy apply. The generic Codex qualification script preauthorizes only its three generic write tools on its pinned disposable server. Dynamic qualification uses a separate disposable fixture and official SDK client; see [guide](DYNAMIC-EXECUTION.md). This is a local trusted-user interface, not a sandbox for untrusted code.

## Repeatable qualification

```powershell
.\project.cmd check
.\project.cmd check -Portable
.\project.cmd mcp-prepare
.\project.cmd mcp-start 'C:\absolute\artifacts\mcp\live\run-uuid'
.\project.cmd mcp-check 'C:\absolute\artifacts\mcp\live\run-uuid'
.\project.cmd mcp-codex 'C:\absolute\artifacts\mcp\live\run-uuid'
.\project.cmd mcp-check 'C:\absolute\artifacts\mcp\live\run-uuid'
.\project.cmd mcp-cleanup 'C:\absolute\artifacts\mcp\live\run-uuid'
```

Prepare creates a unique immutable bundle and inventories hashes. Start registers a test-only loader, launches a separate process and creates a populated fixture. Check exercises the generic MCP tools using the official SDK client; its independent Revit evaluator checks actual values. Native utility tools use the separate [utilities qualification](NATIVE-UTILITIES.md) with room fixtures and actual ribbon-command/WPF tests. Codex qualification runs three external `codex exec --json` tasks with saved local authentication, ignored user configuration and only the pinned MCP. Shell, plugins, apps and agent delegation are disabled. It validates recorded tool calls and requires direct native checkpoints after every round, then native Undo/Redo and document-switch/reopen rejection. It uses the user's configured authentication without reading, copying or printing secrets. Codex noninteractive output is documented [here](https://developers.openai.com/codex/noninteractive).

Check can be repeated to observe startup/final native status. Cleanup stops only the owned PID/start identity and removes only its hash-verified registration; unfinished runs require explicit `-Abort`. All fixtures, failures, transcripts and saved file hashes remain private under ignored `artifacts/mcp`. Failed check attempts get unique reports. See [verification](../verification/mcp.md) for observed results and qualification limits.

SDK references: [official C# SDK](https://github.com/modelcontextprotocol/csharp-sdk), [stdio hosting and client guide](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/getting-started.md).
