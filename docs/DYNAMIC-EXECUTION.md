# Trusted C# execution

Trusted C# extends the shared native operation layer with explicitly reviewed query and modify method bodies. CLI, external MCP clients and the optional [built-in Agent](AGENT.md) can use the same execution contracts. This guide describes permissions, transactions, bounds and qualification; [isolated development](DEVELOPMENT.md) provides the repeatable fixture-copy workflow.

## Enable and execute

Dynamic execution is disabled by default. For a trusted, disposable Revit session, launch Revit with `REVITTHYME_TRUSTED_CODE=1` in its process environment. This setting is captured at add-in startup. Changing an MCP client's environment cannot enable it in an already running host. Ordinary utilities work without this setting.

Connect explicitly to the intended process/session, call `get_context`, then call `run_query` or `run_modify` with that `captureId`, a new GUID `requestId`, a C# `source` method body and `trusted: true`.

```csharp
return new {
    view = doc.ActiveView.Name,
    walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount()
};
```

Available variables are `doc` (captured `Document`), `uiapp` (`UIApplication`) and `cancellationToken`. Imports include System, LINQ, generic collections, threading and the Revit DB, Architecture and UI namespaces. Use fully qualified names for other namespaces. Return JSON data, not live Revit objects, deferred enumerables or tasks. The host runs the code synchronously in its ExternalEvent API callback and owns its transaction. Do not open or commit a top-level transaction in submitted code. No packages, files, API lookup/reflection endpoint or agent runtime are automatically loaded.

`run_query` serializes the result then **always rolls back** the current-document transaction/group. It can therefore query temporary geometry without retaining it. It requires an editable document because rollback needs a transaction. `run_modify` regenerates, commits and captures native added/modified/deleted IDs from `DocumentChanged`, then assimilates the group into one Undo item. Compiler errors open no transaction. Runtime, serialization, result-size and returned-budget failures roll back. Rollback checks native completion status and the full element-ID set; uncertain outcomes report `unconfirmed`.

## Trust, bounds and recovery

Arbitrary C# has the Revit process's privileges. **It is not sandboxed.** Transaction rollback covers the current Revit document; it cannot undo file/network activity, UI changes or changes deliberately made to other documents. Both MCP tools are therefore annotated as potentially destructive, including query. Use reviewed code and disposable models first. Do not use these tools for untrusted submissions.

Source is bounded to 24 KiB UTF-8; returned JSON to 8 KiB; native change reports to 512 IDs; diagnostics to 12 bounded entries with source line/column. Each result records the source SHA-256 and elapsed milliseconds. Raw source is retained in session request memory for exact replay, not written automatically to a log. Test reports do contain submitted source and local paths and belong in private artifacts.

`budgetMilliseconds` defaults to 10000, range 100–30000. The token supports cooperative cancellation; the host also checks elapsed execution/serialization time after return. There is **no hard timeout or interruption of an infinite loop** in Revit's process. Compile time is included in reported elapsed time but excluded from the execution budget. A disconnected caller cannot stop running code. Queued cancellation prevents execution; running code finishes and retains its outcome. After a transport timeout, poll `get_operation` with the original ID. Replay only identical arguments/ID. A new ID could run the code twice. Refresh context after execution or rollback because native revisions can change.

CLI uses exactly the same operation and caller-supplied request ID:

```powershell
.\project.cmd cli inspect --session C:\test\session.json --json > C:\test\capture.json
.\project.cmd cli run-query --session C:\test\session.json --capture C:\test\capture.json --source C:\test\query.csx --trusted true --request-id YOUR_GUID --json
.\project.cmd cli get-operation --session C:\test\session.json --request-id YOUR_GUID --json
```

Use `run-modify` for a committed edit. CLI returns JSON, exit 0 for success, 1 for a native rejection/rollback/uncertain result, and 2 for invalid CLI input/connection failure. Protocol **4** requires add-in/Core/CLI/MCP to be upgraded together. Roslyn 5.9.0 is pinned and bundled next to the add-in; Autodesk assemblies remain locally supplied.

## Disposable qualification and Cabin Eval

```powershell
.\project.cmd check
.\project.cmd dynamic-prepare
.\project.cmd dynamic-start C:\Revit\RevitThyme\artifacts\dynamic\live\RUN_GUID
.\project.cmd dynamic-check C:\Revit\RevitThyme\artifacts\dynamic\live\RUN_GUID
.\project.cmd dynamic-cleanup C:\Revit\RevitThyme\artifacts\dynamic\live\RUN_GUID
```

Preparation creates a new immutable, hashed bundle; start records existing process/start and registration hashes and pins a fresh process to one new nonworkshared fixture. It never connects to a working model. Check first runs direct native fault probes, then official-SDK stdio/CLI tests and native Undo/Redo. A pre-existing legacy startup failure dialog may need dismissing in the owned process. Cleanup addresses only that run's PID/start and unchanged qualification registration, verifies prior sessions/registrations and preserves all models/evidence. An unfinished run requires explicit `-Abort` with the cleanup command.

The [Cabin Eval](../tests/CabinEval/README.md) runs a reference C# construction body through the real MCP `run_modify` tool. A separately compiled evaluator reads actual native geometry, BIM objects and parameters, changes only Cabin Length from 14 to 18 in a temporary transaction group, measures the response and rolls the probe back. The result model is saved as a new `Cabin-Result.rvt` inside the owned run, never over the original fixture. Scores describe the tested model; a perfect score is not a condition for the execution stage to work. Execution time, call count and errors remain separate from the 100-point rubric. See [verification](../verification/dynamic-execution.md) for actual results and limits.

No model-copy merge or automatic promotion to a working model is implemented. Apply a reviewed operation separately against a fresh capture if later authorized. Production worksharing, other Revit releases/machines, untrusted code isolation and hard interruption remain unqualified.
