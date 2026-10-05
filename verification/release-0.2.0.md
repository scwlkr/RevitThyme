# Version 0.2.0 verification

## Offline

Full exact-clean local CI passed at c72d21d9a12489cb2389b124750020eb19f4a236 against base ca512837de98ca8734a949e0cfbb09eed14d2a99. This includes structural diagnostics, public package checks, real CLI failure/report forwarding, Rust formatting/clippy, setup readiness/idempotence and cache reuse/invalidation. A later change requires another clean gate.

Package checks passed reproducibility, manifest/file hashes, exclusion of private models/configuration, actual per-user installation into a temporary path with spaces, rejection of tampered packages and unmanaged files, and blocking replacement/uninstall with Revit running. Closed-Revit upgrade/uninstall/recovery are exercised by the same driver when no Revit process runs; they are not yet observed on this development machine.

## Live

The shared IronPython implementation and its named Routes returned Revit 2027.2 build 27.2.0.39, pyRevit 6.5.5.26237+2044, exact session/document targets and candidate scope. Wrong/missing targets, invalid parameters and a disconnected endpoint produced explicit diagnostics. Observable document state stayed equal across those read-only calls. No operation opened a transaction or requested save/sync/close.

Those first live checks loaded the packaged code from an isolated staging folder and registered its Routes in the existing valid API context. They do not prove installed ribbon execution.

The clean package was then installed into the real per-user extension folder. An attempted synchronous bridge-triggered pyRevit reload timed out and reset bridge routes. Revit remained running and its window reported responding. Installed ribbon loading and post-reload bridge verification are pending user recovery through the pyRevit ribbon. No full geometry fingerprint, no-document live session, native CAD, physical cutting or second-computer test is claimed.

Raw document names, IDs and paths remain in ignored artifacts. TimberFold source/installation and the existing untracked logo were not modified or included.

## Publication

Public repository created at https://github.com/scwlkr/RevitThyme. Source push is pending Git Credential Manager sign-in. Hosted workflow and release asset publication remain unverified until their actual results are recorded.
