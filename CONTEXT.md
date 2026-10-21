# Terms

| Term | Meaning |
| --- | --- |
| Core | Autodesk-independent C# contracts, operations and execution policy |
| Add-in | Native Revit ribbon, API adapter and execution host |
| Operation | A named typed capability, shared across interfaces |
| ExternalEvent | Revit callback used to execute queued model access on its API thread |
| Capture | Inspection result containing the exact session, document, view, revision and prior value |
| Session | One Revit process/start identity and one add-in lifetime |
| Applied | Native value verified before/after commit; transaction group assimilated |
| RolledBack | Native rollback and original value independently verified |
| Unconfirmed | Completion/rollback cannot be verified; inspect before making another edit |
| Foundation | First stage of the 2026-10-14 plan; C#, WPF, basic CLI and native inspection/modification |
| MCP | Official C# stdio adapter for external agents using the shared native operation layer |
| Capture ID | Connection-local token referring to captured document/view/revision; refreshed after model changes |
| Request ID | Caller-supplied GUID for a new write; native session retains its result and rejects conflicting reuse |
| View Range | Four native plan planes with level references and offsets in feet; validated by Revit |
| Room dimension preview | Read-only plan containing native finish-face references, dimension lines, tag clearance and existing IDs; recomputed before creation |
| Trusted submission | C# method body run with full Revit process permissions; host opt-in and caller acknowledgement required, no sandbox |
| Rollback query | Trusted submission whose current-document transaction is always rolled back after serializing its JSON result |
| Cooperative budget | Cancellation token and elapsed check after code returns; cannot interrupt blocked/infinite code |
| Development signing | Persistent local code-signing certificate trusted by the current Windows user; signs staged RevitThyme binaries before package hashing |
| Development run | One owned Revit process and one copied disposable fixture, with separate discovery, Agent state and retained request results |
| Cabin Eval | Independent native 100-point model evaluator, including temporary global length change and verified rollback |
| Result-check plan | Declared native observations, expected values and permitted changes used to evaluate a tool or operation result |
| Check report | Attributed evidence and comparisons kept separate from the original native operation outcome |
| Native observation | Trusted readback performed without a writable model transaction, returning structured data for comparison |
| Unexpected change | An added, modified or deleted element ID outside the check plan's declared allowed changes |

Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## TimberFold terms

| Term | Meaning |
| --- | --- |
| Exterior-shell kit | Architectural model pieces representing the explicitly selected exterior walls, roofs and their openings |
| Source face | A captured supported building surface whose contribution must be accounted for in the kit |
| Panel | One connected, flat, constant-thickness fabrication piece with an outer contour and any inner cutouts |
| Stock sheet | Physical material rectangle available for nesting, after its configured border |
| Inspection | Source-scope and material-profile accounting, with diagnostics; it does not establish a complete cutting kit |
| Preview | A computed proposed kit and stock layout that has not created native review documentation |
| Generation | Production and verification of cutting files, guides and requested native review documentation |
| Digital verification | Numerical and native-software checks of scope, contours, scale, joints, layout, output and preservation |
| Physical qualification | Measured cutting and assembly checks on the actual stock and laser setup |
| Inspection evidence | Complete retained source snapshot and diagnostics underlying a bounded inspection summary |
| Unconfirmed generation | A run whose native commit or final publication cannot yet be reconciled safely |
| Reconciliation | A current check of an already committed kit that retains separate evidence without recreating documentation |
| Roof exposure | The part of a source roof surface visible on the combined building shell |
| Boundary refinement | An explicitly bounded and recorded contour adjustment at an operation intersection |
