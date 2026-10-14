# Glossary

| Term | Meaning in RevitThyme |
| --- | --- |
| Tool / operation | A named capability with validated inputs and a specific result. Ribbon and MCP callers share its implementation. |
| Native adapter | The minimal .NET add-in that executes Revit API work in the proper API context. Rust owns orchestration and domain rules. |
| ExternalEvent | Revit's mechanism for scheduling external requests into a valid Revit API callback. The native queue serializes those requests. |
| MCP | Model Context Protocol: the external tool interface. The current native test server uses stdio and session-bound Windows IPC. |
| Native lab | A separate Revit process and add-in limited to one exact disposable fixture. It has no working-model or Python bridge fallback. |
| Target | Process ID/start time, session, open-document identity, view and revision. A changed target invalidates a previously captured request. |
| Snapshot | Immutable facts captured from an exact target for preview/validation; it is not a saved Revit model. |
| Proposal | Requested range settings checked against captured facts and native validity before any Apply. |
| Apply | A confirmed, guarded native transaction with rollback/readback. It is absent from read-only MCP discovery. |
| Outcome | Retained execution status/readback for a specific request. An unknown outcome does not prove a successful mutation. |
| View range | A plan's top, cut, bottom and depth planes, including their level references and offsets. |
| Internal units | Revit lengths are reported in feet internally; UI formatting and metric conversions preserve underlying values. |
| Fixture | A disposable model with known inputs for isolated qualification. Portable/offline fixtures do not execute Revit APIs. |
| TimberFold | An external, independently owned fabrication tool at `C:\Revit\Projects\TimberFold`. RevitThyme preserves its source, installation and baselines. |

See [architecture](docs/ARCHITECTURE.md) for implementation boundaries and [PROJECT.md](PROJECT.md) for accepted product decisions.
