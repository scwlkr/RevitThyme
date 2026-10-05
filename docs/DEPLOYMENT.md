# Install and release

Version 0.2.0 is a per-user pyRevit extension with read-only operations. RevitThyme does not distribute Revit, pyRevit, TimberFold or private models.

## Requirements

Windows, licensed Revit 2027 and independently installed pyRevit with IronPython 2.7. The development host is Revit 2027.2 build 27.2.0.39 and pyRevit 6.5.5.26237+2044. Other builds and second-computer behavior are unverified. Optional Routes must be enabled on 127.0.0.1:48884; ribbon use needs no AI client. TimberFold inspection can use its existing folder; no worker is bundled.

## Install

Download RevitThyme-0.2.0.zip and its .sha256 from GitHub Releases. Compare Get-FileHash -Algorithm SHA256, then extract to a new folder. From that folder:

```powershell
& .\scripts\install.ps1
```

The installer verifies payload hashes and copies only extension code into %APPDATA%\pyRevit\Extensions\RevitThyme.extension. A new install may be staged while Revit runs; use the pyRevit ribbon's Reload button to load it. Never reload through a synchronous Routes request: the reload resets its own server. Updating or uninstalling requires closing Revit yourself first. The installer never saves or closes a model.

Use RevitThyme > Suite Status. Tool Settings selects the separate TimberFold folder; Inspect TimberFold reads the project. Generation remains in the existing TimberFold command.

## Upgrade, uninstall and recovery

Close Revit and install the newer extracted package. Previous owned code moves into %APPDATA%\pyRevit\RevitThyme-backups. Modified or unmanaged files cause refusal, so they can be preserved manually.

```powershell
& .\scripts\install.ps1 -Action Uninstall
```

Uninstall retains a recoverable code backup and user settings. Recover by installing an older official ZIP, or moving a saved backup into the extension folder while Revit is closed and the destination is absent. Reload afterward.

## Packaging and versions

suite.json and extension.json use the same semantic version. Minor versions add compatible capabilities, patches fix them. After 1.0, major versions change public contracts; before 1.0, document contract changes in release notes. Tags are vX.Y.Z; retain previous release assets.

```powershell
.\project.cmd check-release
.\project.cmd package --require-clean
```

packaging/release-files.json is an explicit allowlist. Fixed ZIP order/timestamps make builds reproducible. release.json records revision and per-file hashes; the sidecar hashes the ZIP. Dirty packages identify themselves as development builds. The install record tracks file ownership.

Publish source, license, notes, package and checksum together. The manual GitHub release workflow refuses tag/version mismatch. Public CI checks portable packaging/installation and Rust tooling. Full local CI and live evidence are required before a maintainer release; public CI does not prove live Revit or physical fabrication.

A portable TimberFold worker, preview/generation checks and a real second-computer smoke test remain required before claiming a fabrication bundle or office deployment.
