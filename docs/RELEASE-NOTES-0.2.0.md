# RevitThyme 0.2.0 preview

The first public RevitThyme package brings a plant-themed ribbon to pyRevit, with shared read-only status and TimberFold inspection. This preview establishes the open-source project and installation format for future fabrication tools.

- **Suite Status** reports Revit/pyRevit versions and the active session/document identity.
- **Tool Settings** points to your separately installed TimberFold folder.
- **Inspect TimberFold** reports candidate walls/roofs, unsupported wall types and source-file readiness. It does not generate fabrication files.
- Every button includes light and dark icons with editable vector sources. The supplied wordmark anchors an agricultural, eco and plant visual identity.
- The ZIP includes per-file hashes, an installer, recoverable backups and uninstall/recovery documentation. Private models, credentials, pyRevit and TimberFold are excluded.

Use licensed Revit 2027 and an independently installed pyRevit. Download the ZIP and SHA256 sidecar, verify the checksum, extract and run scripts/install.ps1. Load the extension on a normal Revit start. See [deployment](DEPLOYMENT.md) and [verification](../verification/release-0.2.0.md) for supported scope and recovery.

Offline package/installer checks and the installed first-host cold-start smoke test passed. Native ribbon read-back verified artwork on all three buttons; the user confirmed visible icons and Suite Status. Eight serial read-only Routes checks passed, and the installed inspection output ran through the shared ribbon implementation without a transaction. Manual Reload previously crashed the development host. A backed-up local Routes maintenance repair is loaded there; repeated Reload stability and another-computer installation remain unverified. The installer now refuses AppData paths redirected into a packaged application's cache. Use normal Revit startup and run project.cmd check-live from a source checkout after a healthy start.

TimberFold generation, a standalone MCP server, native C# host, broader Revit compatibility and physical fabrication verification are future milestones. Existing TimberFold generation remains in its separate installation.
