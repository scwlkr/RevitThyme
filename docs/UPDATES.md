# RevitThyme updates

RevitThyme checks for a newer stable release after Revit first becomes idle, at most once every 24 hours per Windows user. The check runs in the background. Offline checks stay quiet and retry the next day. A newer release must support Revit 2027 before it is offered.

Expand the **RevitThyme** panel on the **RevitThyme** tab, then choose **Check for Updates** to check immediately. When an update is available, choose **Download update** or **Later**. The download saves the installer and its checksum under your Downloads folder and opens its location. Save your work and close every Revit window normally before running the installer. Setup updates the add-in, CLI, MCP and bundled Codex together; it preserves your Agent state and earlier bundles. It never closes Revit or restarts Windows.

## Command line

The CLI reports three separate values: its executing build, the detected development checkout, and the toolkit registered for Revit 2027. An open Revit window may still have an earlier bundle loaded until it is restarted normally. Installed-state checks verify the owned registration, manifest identity, relevant DLL hashes and any setup receipt. A damaged or inconsistent installation reports an error rather than claiming it is current.

During development, use the existing project command:

```powershell
.\project.cmd cli --version
.\project.cmd cli version --json
.\project.cmd cli update check --json
.\project.cmd cli update download --destination C:\Revit\Downloads
.\project.cmd cli update install
```

`--version` includes source commits; `version --json` exposes `cli`, `development` and `installed` separately. `update check` compares the latest stable release against **installed RevitThyme**. For example, development 0.9.0 plus installed 0.8.0 offers the installed-copy update to 0.9.0. The status is `available`, `current`, `ahead`, `not_installed` or `incompatible`. An installed build ahead of the stable release is not automatically downgraded. Updating the installed toolkit does not update your Git checkout.

Add `--version 0.9.0` to check, download or install a particular stable release. This explicit choice permits a deliberate older-version test. Download destinations must be absolute; existing files are preserved. Manual failures return a nonzero exit code. A needed installation refuses to start while any Revit process is running; an already-current installation needs no installer.

## Global rthyme command

Version 0.9.1 setup adds the bundled native `rthyme.exe` to your Windows user PATH. No SDK or administrator permission is needed. Open a new PowerShell window after installation, then use it from any folder:

```powershell
rthyme --version
rthyme version --json
rthyme update check
rthyme update download --destination C:\Revit\Downloads
rthyme update install
```

Setup puts the current immutable bundle's CLI directory before other user PATH entries. Upgrades replace only the entry setup owns; uninstall removes only an entry setup added. Other commands, developer tools and PATH entries remain intact. A PowerShell alias, function or command earlier in the machine PATH can still take precedence; `Get-Command rthyme -All` shows what will run.

An existing terminal retains its earlier PATH until reopened and may run an older CLI. Version output identifies that executing build separately from the registered installation. Update decisions still use the registered copy. Earlier bundles remain intact, so `rthyme update install` can finish while its original CLI process is running. Revit itself must be closed before a needed installation.

To select the installed command explicitly, use the physical installation receipt:

```powershell
$receipt = Get-Content "$env:USERPROFILE\AppData\Local\RevitThyme\setup\receipt.json" -Raw | ConvertFrom-Json
& $receipt.global_command --version
& $receipt.global_command update check
& $receipt.global_command update install
```

For source development, `project.cmd cli` builds and runs the current checkout. The optional `project.cmd cli-install` route still creates a separate [.NET global tool](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install) snapshot with SDK 10. Run it again after pulling CLI changes to refresh that developer tool and its checkout link. Setup preserves it; the installed toolkit's PATH entry takes precedence in new terminals. Use `project.cmd cli` to select source behavior explicitly. Remove only the developer tool with `dotnet tool uninstall --global RevitThyme.Cli`; Windows Installed apps owns the toolkit command.

Checks use the official GitHub release API and bounded HTTPS metadata downloads. Downloads invoke the packaged PowerShell bootstrap pinned to the checked version, verifying its installer SHA-256 and available GitHub asset digests. The installer remains unsigned. This is a release checker and the existing installer flow; no background service or separate package manager is installed.
