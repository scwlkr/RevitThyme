# Security

Report vulnerabilities through [GitHub private vulnerability reporting](https://github.com/scwlkr/RevitThyme/security/advisories/new) when available. If that route is unavailable, use a contact channel on [the maintainer's profile](https://github.com/scwlkr). Do not disclose exploit details in public issues. Include the affected version, impact and a minimal sanitized reproduction. There is no guaranteed response SLA; fixes focus on the current release. Keep private models, credentials, document paths and session captures out of reports unless privately requested and necessary.

## Trust boundary

The C# bridge is local to Windows, restricted to the current user's SID, rejects remote pipe clients and verifies the server process. The official-SDK MCP server runs over local stdio. It explicitly binds sessions and captures native identity/revision. The host executes model access through Revit ExternalEvent; typed edits verify expected values, transactions and whole-batch rollback. No HTTP listener or dedicated model save/synchronization endpoint is present.

Trusted C# query/modify is disabled unless the Revit process starts with `REVITTHYME_TRUSTED_CODE=1`; every call also requires `trusted=true` and an exact captured target. It is **not sandboxed** and has full process privileges. Current-document transactions can roll back Revit changes, including every query, but cannot reverse file/network effects, UI changes or deliberate access to other documents. Use reviewed code and disposable models. Both tools advertise potential destructive effects. Budgets are cooperative; infinite or blocked code cannot be safely interrupted in-process. See [bounds and recovery](docs/DYNAMIC-EXECUTION.md).

Treat session files, captures, parameter data and qualification transcripts as private metadata. Processes running as your Windows user share this trust boundary. A submitted edit whose connection is lost is unconfirmed: retrieve its retained outcome with `get_operation` and inspect native values before another edit. Requests are not automatically replayed; an intentional retry must use the same GUID and arguments. Queued cancellation prevents mutation, while running work completes and retains its result.

Installation verifies an exact file allowlist and hashes, retains immutable bundles/registration backups, rejects unknown modified installations and blocks while Revit runs. SHA-256 establishes integrity against a trusted checksum, not publisher identity. The current public Windows installer is unsigned. Public publisher signing remains future work; persistent local development signing does not establish a public publisher identity.

Historical pyRevit/Rust behavior in `legacy/pre-foundation` is outside the current C# build and package. Existing legacy installations remain subject to their original security boundaries.
