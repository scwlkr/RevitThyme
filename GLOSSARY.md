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

Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
