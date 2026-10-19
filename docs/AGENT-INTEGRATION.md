# Connect an agent

The local stdio MCP server gives external clients structured access to shared native operations. It uses the official C# MCP SDK and explicitly connects to a matching RevitThyme add-in. No cloud Revit bridge or HTTP listener is required.

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

## Trust and private data

The bridge is scoped to the local Windows user. Model paths, parameters and context can reach the agent provider you configure; review its account/workspace policies. Keep captures and conversations private.

Trusted C# requires startup opt-in and per-call acknowledgement. It has full process permissions and cannot undo external file/network effects. Revit transaction rollback is not a sandbox. Read [execution](DYNAMIC-EXECUTION.md) and [security](../SECURITY.md).
