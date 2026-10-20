# Connect an agent

The local stdio MCP server gives external clients structured access to shared native operations. It uses the official C# MCP SDK and explicitly connects to a matching RevitThyme add-in. No cloud Revit bridge or HTTP listener is required.

Version 0.10.0 exposes 25 tools, including seven TimberFold controls, through protocol 8. Install matching add-in, CLI and MCP binaries together. The qualified package is `0.10.0-timberfold.4`.

The unreleased source candidate `0.10.0-lifecycle.3` provides 31 tools, including Home-aware document open/activate, explicit save, clean close with explicit active-document handoff, exact view/sheet activation and [`export_view_image`](MCP.md#exact-view-and-sheet-images-unreleased), and uses protocol 12. Active close requires a different already-open clean document selected explicitly; last-document close to Home is refused. It returns a verified inline native view/sheet PNG and retained completion metadata. Published v0.10.0 does not contain these operations. Keep candidate testing separate from the installed working host; do not mix protocol versions.

## Choose a route

| Need | Setup |
| --- | --- |
| Chat inside Revit | [Built-in Agent](AGENT.md), with bundled Codex runtime |
| External MCP client | Installed `revitthyme-mcp.exe` as a local stdio command |
| Development | [Isolated run](DEVELOPMENT.md), then `project.cmd dev-mcp -RunFolder $run` |
| Shell scripts | [CLI](CLI.md) |

## Locate the installed server

Install with Revit closed, then open Revit normally. Read your own setup receipt:

```powershell
$receipt = Get-Content "$env:LOCALAPPDATA\RevitThyme\setup\receipt.json" -Raw | ConvertFrom-Json
$receipt.mcp
```

Configure your client's stdio command with that absolute executable. The receipt points into an immutable package; after an upgrade, check the path and update the client. Adapt this illustrative fragment to your client's configuration schema:

```json
{
  "mcpServers": {
    "revitthyme": {
      "command": "C:\\absolute\\installed\\mcp\\revitthyme-mcp.exe",
      "args": []
    }
  }
}
```

For Codex, see [client setup](MCP.md#external-codex-setup). Optional `--sessions-directory ABSOLUTE_FOLDER` bounds discovery; `--session ABSOLUTE_FILE` pins a single binding. Ordinary servers start disconnected and do not choose a process automatically.

## First read-only task

List sessions, review their process/document identities and explicitly connect to the intended binding. Then call `get_context` and `list_operations`. Confirm the document/view before querying elements.

Suggested prompt:

> Use RevitThyme to list sessions. Show me their process and document identities before connecting. After I select the intended session, inspect the active view and available operations. Keep this task read-only.

## Before a write

1. Capture fresh context and exact element/parameter identities.
2. Review the intended edit and expected prior values. Do not identify parameters by display name alone.
3. Assign a fresh request GUID to a new edit. Retain its ID and exact arguments.
4. Inspect the structured result and native values.
5. On connection loss, call `get_operation` with the original ID before retrying. Identical replay recovers the retained outcome; conflicting reuse is rejected.

The [MCP reference](MCP.md) covers schemas, paging, internal units and recovery. Typed operations do not save or synchronize. Tests stay pinned to a separate disposable process/session/document; never use a working model as fallback.

## TimberFold through MCP

Use `timberfold_inspect`, `timberfold_preview`, `timberfold_generate`, `timberfold_status`, `timberfold_cancel`, `timberfold_verify` and `timberfold_reconcile`. Capture fresh context before inspection, preview or reconciliation; generation uses its retained preview. Starts return acceptance and a run ID, so poll status until terminal and retain exact request identities after uncertainty. Reconcile produces a separate read-only recovery run rather than recreating documentation. Status/cancel require no fresh capture.

Generated native review sheets belong to one Undo group; cutting files remain retained after Undo. The feature never saves the source. Digital acceptance does not certify physical stock, kerf, joints or assembly; `ManufacturingReady` remains false. See [TimberFold](timberfold/README.md) for supported scope and physical-test instructions.

## Trust and private data

The bridge is scoped to the local Windows user. Model paths, parameters and context can reach the agent provider you configure; review its account/workspace policies. Keep captures and conversations private.

Trusted C# requires startup opt-in and per-call acknowledgement. It has full process permissions and cannot undo external file/network effects. Revit transaction rollback is not a sandbox. Read [execution](DYNAMIC-EXECUTION.md) and [security](../SECURITY.md).
