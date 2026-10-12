# Native Revit test session

WLK-229 adds a local test MCP connection using the native RevitThyme adapter. It has no Python or pyRevit dependency. The first prepared run allows capture, preview and proposals. Apply is hidden and blocked until a separately approved write-enabled run is configured.

This is implemented source with offline Windows checks. The user approved the separate test installation on 2026-10-12; actual-Revit qualification is in progress. The installed SDK and Revit executable are **27.3.0.28**. Earlier Visual View Range results on 27.2.0.39 do not qualify this build.

## How the connection stays separate

The new `RevitThyme.LabAdapter` add-in does nothing unless its Revit process inherits `REVITTHYME_LAB_CONFIG` from the lab launcher. The existing working process does not receive that setting. The launcher starts a new Revit process and never restarts or closes the working process.

Each run has a new native pipe, process ID, process start time and session ID. Its Rust MCP client verifies the pipe server's Windows process ID and the native handshake. It has no fallback to Routes, Jev or any working connection. Credentials live in Windows Credential Manager for the Windows logon session and are deleted at normal lab shutdown. They are never placed in configuration, command arguments or logs.

The native model adapter checks the exact test file path and open-document identity inside the same Revit API callback as capture, validation, Apply and readback. A different file, a workshared model, a redirected path or a reopened test document is refused. Closing the fixture ends its eligibility. Cached geometry and retained outcomes describe only prior approved captures; they do not access another model.

The ribbon adapter and lab adapter compile the same `Model`, `Geometry`, `Orientation` and `Change` source. Both use the existing native `Session` and transaction/readback implementation. MCP preview and proposals use the same Rust range and section code as Visual View Range. The lab build has a separate assembly and registration; it does not replace the installed preview or legacy suite.

## Commands

Use the project CLI from the repository:

```powershell
.\project.cmd check-revit-lab
.\project.cmd revit-lab prepare
.\project.cmd revit-lab status <absolute-prepared-run-folder>
```

`prepare` builds a local bundle, hashes it and writes a reviewable plan beneath `artifacts/revit-lab/<run-id>`. It creates no Revit model, registration or global MCP setting. Its immutable binding file will be written by the new Revit process after fixture creation. A stale run never selects a newer process; prepare a new run for a new session.

The preparation contains:

| File | Purpose |
| --- | --- |
| `plan.json` | Source revision, bundle hashes and proposed host destinations |
| `config.json` | Exact test file, binding path and read-only setting |
| `RevitThyme.Lab.addin` | Separate native registration to review |
| `mcp.toml` | New `revitthyme_test` MCP setting to review |
| `bundle/` | Native lab adapter, shared native core and Rust MCP executable |

## Installation and first-run steps

The user approved installation and the first disposable fixture on 2026-10-12, and removed the blanket exact-step approval requirement from `AGENTS.md`. These steps describe the bounded setup. Keep the working instance and models preserved; never qualify against a live job.

1. Back up the user's Codex `config.toml` into that ignored run folder. Add the `revitthyme_test` entry from `mcp.toml`, leaving existing MCP entries in place. Refuse an existing entry instead of silently replacing it.
2. Install only the new per-user `RevitThyme.Lab.addin` manifest under `C:\Users\scwlk\AppData\Roaming\Autodesk\Revit\Addins\2027`. It references the reviewed immutable local bundle. Verify the physical destination and copied manifest; preserve all existing add-ins.
3. Start one new `C:\Program Files\Autodesk\Revit 2027\Revit.exe` process with that run's configuration. Keep the working process and its open model running. Record the new process identity. An unsigned-add-in prompt must be handled by the user; automation does not approve it.
4. In that new, empty process, create an imperial project with two named levels, one floor plan and a simple solid. Save it once as that run's new `RevitThyme-Test.rvt`, refusing overwrite. Close only that new document and reopen it to activate the floor plan. These startup steps require the explicit fixture creation/save/close/reopen approval.
5. Verify the native binding, MCP initialization, tool list, capture, preview and proposal against the test model. Record actual Revit results separately. The first run opens no test transactions after fixture setup, and provides no save, sync, close, delete or arbitrary-code MCP tools.

Installation and startup use an explicit prepared folder and the approved-operation flag:

```powershell
.\project.cmd revit-lab install <absolute-prepared-run-folder> --approved
.\project.cmd revit-lab start <absolute-prepared-run-folder> --approved
.\project.cmd revit-lab verify <absolute-prepared-run-folder>
```

Adding a new MCP entry can require Codex to reconnect/reload the server configuration. Until that happens, the same stdio executable can be checked directly. The server exits if its exact binding or credential is unavailable. Check the run's `binding.error.json` after a startup failure; keep any partial fixture for review.

To rotate a stopped test session, prepare a new run and install it with `--approved --replace-run <exact-previous-run-folder>`. The installer refuses a running prior test process, requires its exact saved MCP entry and manifest, retains both backups and changes only that test connection. The old binding never points to a new process. Startup waits for document opening to finish, requests the floor plan on a later idle cycle, and publishes the binding only after that plan becomes active.

## Current limits

This first connection tests Visual View Range. Other suite features need named operations as they are implemented. A write-enabled run needs explicit test-model permission, plus a confirmed fresh proposal for each Apply. Native Apply changes one view range, uses the existing transaction/readback checks and never saves or synchronizes.

Two Revit processes separate API requests and documents. They still share this Windows desktop. Foreground mouse/keyboard UI tests can interrupt the user; schedule those tests when the user is idle, or use a separate Windows session or machine for full UI separation. Native API tests do not require bringing the test window to the front.

Do not use this source check as actual-Revit evidence. Live fixture creation, 2027.3 behavior and simultaneous working/test isolation remain acceptance gates. Normal shutdown removes the lab credential; a crash can leave it until Windows logoff. Uninstalling or changing the saved MCP entry/manifest requires a reviewed bounded change and its retained backup.

MCP follows the official [stdio transport](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports) and [tools interface](https://modelcontextprotocol.io/specification/2025-11-25/server/tools). Stdout contains JSON-RPC messages only. The test adapter uses Autodesk ExternalEvent for queued API requests.
