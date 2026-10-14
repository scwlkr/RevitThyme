# Security

Report vulnerabilities privately through the repository owner's GitHub profile or GitHub private reporting. Keep private models, credentials, document paths and session captures out of public issues.

The C# Foundation bridge is local to Windows, restricted to the current user's SID, rejects remote pipe clients and verifies the server process. It accepts only Inspect and captured-target RenameView operations. The host executes them through Revit ExternalEvent; model-changing code uses verified transactions and rollback. No arbitrary-code execution, HTTP listener, model save or synchronization endpoint is present.

Treat local session files and inspection captures as private metadata. Processes running as your Windows user share this trust boundary. A submitted edit whose connection is lost is unconfirmed: inspect the native model before another edit. Requests are not automatically replayed.

Installation verifies an exact file allowlist and hashes, retains immutable bundles/registration backups, rejects unknown modified installations and blocks while Revit runs. SHA-256 establishes integrity against a trusted checksum, not publisher identity. The foundation package is unsigned local development output; public signing/distribution is future work.

Historical pyRevit/Rust behavior in `legacy/pre-foundation` is outside the active Foundation build and package. Existing legacy installations remain subject to their original security boundaries.
