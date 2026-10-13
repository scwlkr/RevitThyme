# Native RevitThyme MCP

WLK-238 builds on WLK-229's native session. Rust owns MCP, input validation and health rules. The minimal .NET adapter reads Revit through the existing serialized ExternalEvent queue. There is no Python/Routes fallback or arbitrary-code tool.

The lab compiles the same native queue/protocol source into `RevitThyme.LabCore.dll`. Its distinct CLR identity prevents an older installed preview's `Adapter.Core.dll` from supplying incompatible types to the new lab. Existing bundles remain intact; no working host DLL is replaced.

This milestone is **source and Windows offline verified**. The new inspection tools have not been installed or qualified inside Revit. Existing immutable bundles retain their own earlier evidence. Preparation does not change registrations, MCP settings or models.

## Implemented tools

| Tool | Behavior |
| --- | --- |
| `revit_context` | Read the approved document, active view, exact process/start/session/document/revision, modified/read-only state and internal units without geometry capture |
| `revit_elements` | Inspect exact numeric IDs or search a category; return name, type/family, level, mark, view-template/sheet/room facts and continuation |
| `revit_project_health` | Evaluate six review checks in shared Rust logic; report affected IDs, names, levels, counts, sample truncation and coverage |
| Existing View Range tools | Capture, preview, native proposal, retained outcome and queued cancellation; Apply remains absent in a read-only run |

Read `revit_context` first, then pass its entire `target` object into inspection. Model revision, view switch, document reopen or process/session changes reject subsequent reads. Each page checks the target in the same API callback as the element reads. Refreshing context does not rebind the connection: the lab still checks its exact fixture and open-document instance.

`revit_elements` takes `target` and this `query` object:

```json
{
  "category": "doors",
  "after_id": "0",
  "limit": 50,
  "name_contains": "",
  "missing_mark": true,
  "ids": []
}
```

For the next page retain the target and filters and use `next_after_id`. IDs are canonical positive decimal strings, including values above 32 bits. An exact lookup uses at most 20 unique IDs; category is then an output grouping label and does not filter those IDs. Missing IDs are reported. Exact lookups can continue with the same ID list and returned cursor. Name/missing-mark filters cannot accompany an exact lookup.

Categories: doors, rooms, views, sheets, levels, walls, floors, ceilings, lighting fixtures, lighting devices, electrical equipment and warnings. Category searches return instances; exact IDs can inspect types. Linked-document traversal is unimplemented. Warnings use ordinal cursor IDs within the guarded revision, a failure-definition GUID and samples of at most 20 related native element IDs.

## Health semantics and limits

Health takes `target` and `limit` (1–50 sample findings per check). Checks cover missing templates on eligible graphical views, duplicate view names, duplicate sheet numbers, missing door marks, unplaced rooms and Revit warnings. Counts describe affected rows, not duplicate groups. Duplicate keys ignore case and edge whitespace; they are review findings, not a claim that Revit permitted invalid native names/numbers. Templates and drawing sheets are excluded from duplicate view-name checks. Repeated sheet titles are valid.

Unplaced rooms have no location. Placed but unenclosed/zero-area rooms are distinct. All phases are included. The report does not infer which firm template a view needs and performs no fixes.

Each native category search examines at most 20,000 candidates and returns at most 100 rows per page. Pages shrink to a 55,000-byte budget while retaining continuation. One oversized row returns `result_limit`. Text is not silently shortened into a different duplicate key or mark. Revit's warnings API returns its own collection before pagination: returned data is bounded, but that API's allocation/duration cannot be bounded here.

Health examines at most 2,000 rows, 100 pages and approximately 2 MiB of page data per category, with a shared 20-second scheduling budget. An active native callback may finish after that budget. At a limit, `complete=false`; counts are lower bounds and zero is not a passed check. Sample truncation is independent: exact counts can accompany a short sample. MCP output is bounded to 1 MiB; reduce the sample if it exceeds that budget.

Old terminal read receipts may be evicted to keep inspection usable. Apply outcomes survive read traffic. The existing ten-minute retention horizon remains; never infer a successful write from an unknown outcome. Queue/credential/handshake failures and stale targets remain structured errors with no automatic target selection or mutation retry.

Connection rotation accepts an exact stopped prior run from another checkout. It verifies the prior plan/manifest and configured executable/binding, parses TOML, and preserves unrelated sections and settings such as `enabled`. It prepares a private candidate file before replacing the saved configuration. This avoids leaving an orphan setting after removing a raw snippet. Physical-path checks, backups and the prior-process guard still apply.

## Verification and installation

`project check-revit-lab` runs SDK compilation, Rust format/clippy/tests and real stdio → Windows credential → named-pipe → owner-thread queue checks against an offline host. Its fixture has independently specified marks, duplicate keys, templates, sheets, room placement and warning relationships. It does not execute Revit APIs.

`project revit-lab prepare` creates an immutable read-only bundle and exact paths. Follow [native lab installation](NATIVE-REVIT-LAB.md) for a new isolated session. The supplied session instructions require exact-step approval before installing new code/registration or creating/changing a disposable Revit fixture. Preserve the working session and its models. Record new actual-Revit inspection evidence separately; compilation and earlier View Range qualification do not qualify these tools.

After approved setup, `project revit-lab inspect <absolute-prepared-run-folder>` checks the immutable bundle, context, known fixture levels/plan, exact/missing ID lookup, health, wrong-document rejection and unchanged fixture hash/modified state. It retains raw observations. The empty fixture cannot qualify native door/family/room mapping or large-model performance; those require a later approved populated sample.

## Designs studied

| Pinned source, inspected 2026-10-13 | Lessons adopted |
| --- | --- |
| [pyRevit agent runtime](https://github.com/pyrevitlabs/pyRevit/blob/c89948d29d2298b0015ead1dabbd8ba1355538a9/docs/agent-runtime.md), GPL-3.0, revision `c89948d29d2298b0015ead1dabbd8ba1355538a9` | ExternalEvent dispatcher, bounded named inspection, internal-unit facts and shared automation boundaries |
| [pyRevit inspector](https://github.com/pyrevitlabs/pyRevit/blob/c89948d29d2298b0015ead1dabbd8ba1355538a9/dev/pyRevitLabs.PyRevit.Runtime/Agent/AgentInspector.cs) | Learn element facts through a fixed tool before requesting modifications |
| [BIMwright targeting](https://github.com/bimwright/rvt-mcp/blob/8d2de289f52659cdc347512159f8a13ae22e6f33/src/server/TargetSelection.cs), Apache-2.0, v1.1.0 revision `8d2de289f52659cdc347512159f8a13ae22e6f33` | Bind a process/generation; changed/dead targets need explicit recovery; discovery must not silently redirect execution |
| [BIMwright API dispatcher](https://github.com/bimwright/rvt-mcp/blob/8d2de289f52659cdc347512159f8a13ae22e6f33/src/shared/Infrastructure/McpEventHandler.cs) | Enforce read policy inside host execution and retain useful queue/call diagnostics |
| [MCP tools specification](https://modelcontextprotocol.io/specification/2025-11-25/server/tools) | Exact schemas, accurate read-only hints, structured results plus text and bounded failures |

No upstream implementation is copied or vendored. New code follows RevitThyme's Rust/.NET architecture. pyRevit's script read-only policy does not sandbox file/network/process effects; this milestone exposes named native reads. The weekly conversation is inspiration, not qualification evidence.

## Next bounded milestones

1. Qualify these tools in an approved isolated process, including native category/ID enumeration and independent health comparisons. Production enablement stays separate.
2. Add visible activity/history, connection status and explicit target selection to the owned utility UI. Expose recipes/skills only for implemented operations.
3. Implement named sheet/view placement and export jobs with reviewed scope, owned artifacts, progress, cancellation and retained outcomes.
4. Implement parametric family recipes and standards fixes with preview, changed/skipped IDs, transactions/readback and isolated native qualification.
5. Prove multiple-instance targeting, install/recovery and suite parity before widening the model boundary or retiring another host.

Exports, family generation, arbitrary scripting, automatic fixes and a production-model MCP connection are unimplemented in this milestone.
