# Install and release

The unreleased [M2 portable preview](RUST-VIEW-RANGE-M2.md) includes owned .NET adapter files and a registration template. It performs no automatic add-in installation. Do not use the legacy installer below for this adapter. Approved native discovery/install and disposable-Revit qualification remain separate gates; exact proposed steps are in the M2 handoff.

Version 0.2.0 is a per-user pyRevit extension with read-only operations. RevitThyme does not distribute Revit, pyRevit, TimberFold or private models.

## Requirements

Windows, licensed Revit 2027 and independently installed pyRevit with IronPython 2.7. The development host is Revit 2027.2 build 27.2.0.39 and pyRevit 6.5.5.26237+2044. Other builds and second-computer behavior are unverified. Optional Routes must be enabled on 127.0.0.1:48884; ribbon use needs no AI client. TimberFold inspection can use its existing folder; no worker is bundled.

## Install

Download RevitThyme-0.2.0.zip and its .sha256 from GitHub Releases. Compare Get-FileHash -Algorithm SHA256, then extract to a new folder. From that folder:

```powershell
& .\scripts\install.ps1
```

The installer verifies payload hashes and copies only extension code into %APPDATA%\pyRevit\Extensions\RevitThyme.extension. A new install may be staged while Revit runs. Load it on the next normal Revit start. Manual Reload crashed the current development host with Routes enabled; cold-start validation is required before recommending reload there. Never reload through a synchronous Routes request: the reload resets its own server. Updating or uninstalling requires closing Revit yourself first. The installer never saves or closes a model.

Use RevitThyme > Suite Status. Tool Settings selects the separate TimberFold folder; Inspect TimberFold reads the project. Generation remains in the existing TimberFold command.

The three buttons include transparent light/dark icons. An existing installation of the same version can add or refresh only those image assets while Revit stays open:

```powershell
& .\scripts\install.ps1 -Action Icons
```

This checks that every installed non-image file matches the package, preserves an image/ownership-record backup, and refuses code changes. Display changes take effect on the next normal Revit start. Normal code updates still require Revit to be closed.

The installer checks the physical destination before changing host files. Codex's packaged Windows environment can redirect AppData writes into its private cache even when a normal path is supplied. A directory handle alone does not reveal this; the check uses an automatically deleted, uniquely created file probe. Package-cache paths and redirected writes are refused. Run installation from ordinary PowerShell, or use an explicitly verified native path. On the development machine, a localhost administrative-share path reached the actual folders; the installer does not assume that share is available on other computers. Native Revit ribbon/file read-back establishes that the host loaded the result.

## Development-host Routes repair

The observed pyRevit Routes source starts its HTTP server twice and writes background HTTP diagnostics to a UI output stream. Windows events captured ScriptConsole failures on background threads. A developer-only diagnostic and guarded source repair is available from a clone:

```powershell
.\project.cmd repair-routes
.\project.cmd repair-routes --apply
```

The diagnostic runs actual server lifecycle code in an isolated CPython process before writing anything. Apply refuses redirected paths and unrecognized source, retains the exact original under artifacts/live/pyrevit-routes-server-before.py and applies a small patch to the selected pyRevit clone. It takes effect in a new Revit process. The CPython regression and repaired IronPython/Revit cold-start smoke test passed on the development host. Repeated Reload stability remains unverified. This repair is not automatically applied by the release installer and does not establish support for other pyRevit versions.

To recover the original, close Revit yourself and copy the saved backup over the repaired pyrevitlib/pyrevit/routes/server/server.py in that same clone. Keep the backup and local repair report. The patch is also retained in verification/pyrevit-routes.patch. Worker errors go to %TEMP%\pyRevit-Routes-errors.log; avoid publishing private paths or diagnostics from that file.

## Upgrade, uninstall and recovery

Close Revit and install the newer extracted package. Previous owned code moves into %APPDATA%\pyRevit\RevitThyme-backups. Modified or unmanaged files cause refusal, so they can be preserved manually.

```powershell
& .\scripts\install.ps1 -Action Uninstall
```

Uninstall retains a recoverable code backup and user settings. Recover by installing an older official ZIP, or moving a saved backup into the extension folder while Revit is closed and the destination is absent. Load it on the next normal Revit start.

## Packaging and versions

suite.json and extension.json use the same semantic version. Minor versions add compatible capabilities, patches fix them. After 1.0, major versions change public contracts; before 1.0, document contract changes in release notes. Tags are vX.Y.Z; retain previous release assets.

```powershell
.\project.cmd check-release
.\project.cmd package --require-clean
```

packaging/release-files.json is an explicit allowlist. Fixed ZIP order/timestamps make builds reproducible. release.json records revision and per-file hashes; the sidecar hashes the ZIP. Dirty packages identify themselves as development builds. The install record tracks file ownership.

Publish source, license, notes, package and checksum together. The manual GitHub release workflow refuses tag/version mismatch. Public CI checks portable packaging/installation and Rust tooling. Full local CI and live evidence are required before a maintainer release; public CI does not prove live Revit or physical fabrication.

A portable TimberFold worker, preview/generation checks and a real second-computer smoke test remain required before claiming a fabrication bundle or office deployment.
