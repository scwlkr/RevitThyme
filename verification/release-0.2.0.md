# Version 0.2.0 verification

## Offline

Full exact-clean local CI passed at cfd55456d3c9da509df3da3b3f15e570ce3f9901 against base ca512837de98ca8734a949e0cfbb09eed14d2a99. This includes structural diagnostics, public package checks, real CLI failure/report forwarding, Rust formatting/clippy, setup readiness/idempotence and cache reuse/invalidation. Final tagged packages record their exact revision in release.json; local reports are retained under artifacts/local-ci. Every later change requires a new clean gate before publication.

Package checks passed reproducibility, manifest/file hashes, exclusion of private models/configuration, actual per-user installation into a temporary path with spaces, rejection of tampered packages and unmanaged files, and blocking replacement/uninstall with Revit running. Closed-Revit upgrade/uninstall/recovery are exercised by the same driver when no Revit process runs; they are not yet observed on this development machine.

The icon checks additionally read all six packaged PNG headers, exercise adding images to an installation with no artwork, verify every unchanged code byte and reject a checksum-valid package that attempts a code change through the image-only action. Both light/dark exports were visually inspected against representative backgrounds.

## Live

The shared IronPython implementation and its named Routes returned Revit 2027.2 build 27.2.0.39, pyRevit 6.5.5.26237+2044, exact session/document targets and candidate scope. Wrong/missing targets, invalid parameters and a disconnected endpoint produced explicit diagnostics. Observable document state stayed equal across those read-only calls. No operation opened a transaction or requested save/sync/close.

Those first live checks loaded the packaged code from an isolated staging folder and registered its Routes in the existing valid API context. They do not prove installed ribbon execution.

The clean package was installed into the per-user extension folder. A synchronous bridge-triggered reload timed out and reset bridge routes. A later deferred Idling reload built the ribbon assembly; the user confirmed its buttons were visible. Six icons were installed with the guarded image-only action and all owned file hashes were read back. The user then reported that manual pyRevit Reload crashed Revit and the icons were still missing. Installed button execution, visible artwork and stable Routes remain unverified gates; file installation alone does not satisfy them.

Windows .NET events on 2026-10-05 identify background-thread pyRevit ScriptConsole failures: a WebBrowser InvalidCastException during reload and a later STA-required WPF window creation failure. The installed Routes source starts the same HTTP server in its constructor and again during activation, while background HTTP logging uses UI-backed stderr. A narrow local maintenance repair owns one server thread, stops before closing its socket, keeps request logging away from the UI and records worker errors in a file. Original source is backed up locally; the portable diff is verification/pyrevit-routes.patch.

The actual server classes were exercised in a separate CPython process with real loopback HTTP, excluding only Revit route execution. Unfixed source failed the single-thread and no-UI-output contracts; repaired source passed HTTP response, single-thread ownership, no worker UI output and clean stop. This proves the isolated lifecycle correction, not the Revit crash fix. Normal cold-start validation of the repaired IronPython host is pending. No full geometry fingerprint, no-document live session, native CAD, physical cutting or second-computer test is claimed.

Raw document names, IDs and paths remain in ignored artifacts. TimberFold source/installation were not modified or included. The supplied logo is preserved unchanged; a copy and new editable artwork are included under assets/branding following the user's request for icons and an agricultural plant identity.

## Publication

Public source was pushed to https://github.com/scwlkr/RevitThyme, with master as default. Hosted portable CI passed at cfd5545 (run 37321277924). The earlier 1a4cc72 run failed on a cold PowerShell installer timeout; the bounded timeout correction passed both local and hosted checks. Later release/source results must be checked on their exact revision. The manual release workflow creates a prerelease with the versioned ZIP and checksum; its run and release assets are publication evidence, separate from live Revit evidence.
