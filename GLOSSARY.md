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

Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
