# MCP interface

The current [2026-10-14 plan](../2026-10-14_NEW_PLAN.md) boundary is Dynamic execution. Twelve generic tools, four utility tools and two trusted C# tools share the native execution host. Built-in Agent and Exploration remain deferred. See [Native utilities](NATIVE-UTILITIES.md) and [Dynamic execution](DYNAMIC-EXECUTION.md) for acceptance and limits.

The server uses official `ModelContextProtocol` **2.2.0**, pinned with a NuGet lockfile, and local stdio. It calls the same Autodesk-independent Core and OS-verified local pipe as the CLI; the add-in drains operations through ExternalEvent. No legacy bridge or arbitrary-code fallback exists.

| Tool | Behavior |
| --- | --- |
| `list_sessions` | Discover live process/start/session bindings in the configured directory |
| `connect_session` | Explicitly connect to a chosen absolute binding file; handshake and reset captures |
| `get_context` | Native document/view inspection and connection-local `captureId` |
| `list_operations` | Typed operations, units, limits and recovery semantics |
| `list_views` | Paged native view IDs, names, classes and template flags |
| `query_elements` | Paged elements, optional negative category ID/name filter, optional types |
| `inspect_elements` | Properties and paged typed parameters for 1–5 native UniqueIds |
| `get_selection` | Read up to 100 selected UniqueIds |
| `set_selection` | Verify selected IDs, restore UI selection on failure; empty array clears |
| `rename_view` | Shared Foundation operation, captured name, verified readback and one Undo |
| `set_parameters` | Atomic batch of 1–20 expected-value parameter edits and one Undo |
| `get_operation` | Retrieve the retained native outcome without submitting the edit again |
| `get_view_range` | Four native range planes, level choices and template edit control |
| `set_view_range` | Exact expected planes, native validity/readback/rollback, one Undo |
| `preview_room_dimensions` | Read-only rectangular room plan against native wall finish faces and tag bounds |
| `create_room_dimensions` | Recomputed exact preview, verified native associative pair and one Undo |
| `run_query` | Explicitly trusted Roslyn C# method body, serialized JSON result, always rolls back current-document changes |
| `run_modify` | Explicitly trusted Roslyn C# method body, native change IDs, rollback on failure and one Undo |

MCP tool annotations identify reads/writes; results include JSON text and structured content. Rejected, rolled back and uncertain results set MCP `isError=true`. SDK schema/argument errors also fail the tool call. Query limits are 1–100; element/parameter pages shrink further to fit the 64 KiB bridge bound. Follow `nextOffset`/`nextParameterOffset` until null. Parameter strings over 512 characters are marked truncated and cannot be edited.

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

Normal sessions are in `%LOCALAPPDATA%\RevitThyme\sessions`. Optional `--sessions-directory ABSOLUTE_FOLDER` limits discovery. Startup `--session ABSOLUTE_FILE` prevents switching to another binding. Dead/reused processes and bindings other than protocol 4 are rejected. Upgrade the add-in, CLI and MCP together. Every server starts disconnected unless pinned; it never chooses the first running Revit process. These configuration examples are supported by [official Codex MCP documentation](https://developers.openai.com/codex/mcp).

For source development, `project.cmd mcp` builds to stderr and then starts stdio. Installed/published executables avoid startup compilation. No account configuration is edited automatically by packaging or qualification.

## Scripted operation flow

1. Discover sessions and explicitly connect to the intended process. Confirm document path/title and active view through `get_context`.
2. Query elements and inspect native UniqueIds, numeric parameter IDs and typed raw values. Use the resulting capture only while document, view and revision remain unchanged.
3. For each new write, supply a fresh GUID `requestId`. Parameter edits contain `elementId`, `parameterId`, `expected` and `value`. A typed value has `storage` equal to `String`, `Integer` or `Double`, and exactly one `text`, `integer` or `number` field. Double values use Revit internal units, including feet for length and radians for angles; `dataType` is the native Forge type ID. ElementId/None parameters are inspection-only. Duplicate parameter targets and read-only/truncated fields are rejected before mutation.
4. Inspect fresh context and parameters to verify the native result. A successful parameter batch or rename is a single native Undo item. Selection is UI state and can already be empty after a native rollback.
5. On connection loss, use `get_operation(requestId)` and inspect native values. Retry only the same ID and identical arguments, never a new ID for an uncertain edit. Outcomes remain in the add-in until session shutdown; pending requests can be polled. Connection-local captures retain at most 512 entries and reset on explicit connection changes; use outcome recovery if an old capture has expired.

Typed operations never save, synchronize or close models. Room Dimensions creates only its verified native dimension pair. Trusted C# is disabled by default, has full process permissions, and can affect files or other state outside Revit rollback; both tools are annotated as potentially destructive. Host opt-in, caller trust and the standard caller approval policy apply. The generic Codex qualification script preauthorizes only its three generic write tools on its pinned disposable server. Dynamic qualification uses a separate disposable fixture and official SDK client; see [guide](DYNAMIC-EXECUTION.md). This is a local trusted-user interface, not a sandbox for untrusted code.

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
