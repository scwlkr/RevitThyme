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

With .NET SDK 10 installed, run this once from the checkout to install the developer CLI for your Windows user:

```powershell
.\project.cmd cli-install
```

Then use it from any folder:

```powershell
rthyme --version
rthyme version --json
rthyme update check
rthyme update download --destination C:\Revit\Downloads
rthyme update install
```

The command uses standard [.NET global-tool installation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install), with a local package feed. It requires no administrator permission and preserves native argument quoting and exit codes. If a terminal cannot find it, open a new PowerShell window to load the standard `%USERPROFILE%\.dotnet\tools` PATH entry.

The global CLI is a snapshot of this checkout. Run `project.cmd cli-install` again after pulling CLI changes to refresh it. Its private development-link configuration records the checkout so version reporting can find it from another folder. A checkout's `project.cmd cli` always builds/runs its current source. This optional developer tool is separate from the runtime-only Revit installer; it does not replace loaded add-in files. Remove the global command with `dotnet tool uninstall --global RevitThyme.Cli`.

For the CLI bundled with the Revit installation, get its executable from the installation receipt; the Revit installer does not change PATH:

```powershell
$receipt = Get-Content "$env:LOCALAPPDATA\RevitThyme\setup\receipt.json" -Raw | ConvertFrom-Json
& $receipt.cli version --json
& $receipt.cli update check
& $receipt.cli update install
```

Public 0.9.0 predates the separate installed/development version report. Use the current checkout or global developer CLI for that fix until a later toolkit release includes it.

Checks use the official GitHub release API and bounded HTTPS metadata downloads. Downloads invoke the packaged PowerShell bootstrap pinned to the checked version, verifying its installer SHA-256 and available GitHub asset digests. The installer remains unsigned. This is a release checker and the existing installer flow; no background service or separate package manager is installed.
