# RevitThyme updates

RevitThyme checks for a newer stable release after Revit first becomes idle, at most once every 24 hours per Windows user. The check runs in the background. Offline checks stay quiet and retry the next day. A newer release must support Revit 2027 before it is offered.

Choose **RevitThyme → Foundation → Check for Updates** to check immediately. When an update is available, choose **Download update** or **Later**. The download saves the installer and its checksum under your Downloads folder and opens its location. Save your work and close every Revit window normally before running the installer. Setup updates the add-in, CLI, MCP and bundled Codex together; it preserves your Agent state and earlier bundles. It never closes Revit or restarts Windows.

## Command line

During development, use the existing project command:

```powershell
.\project.cmd cli --version
.\project.cmd cli version --json
.\project.cmd cli update check --json
.\project.cmd cli update download --destination C:\Revit\Downloads
.\project.cmd cli update install
```

`--version` includes the source commit. `version --json` separates the product version from build identity. The update check reports `available`, `current`, `ahead` or `incompatible`. Development builds ahead of the public release do not download or install an older version automatically.

Add `--version 0.9.0` to check, download or install a particular stable release. This explicit choice permits a deliberate older-version test. Download destinations must be absolute; existing files are preserved. Manual failures return a nonzero exit code. Installation refuses to start while any Revit process is running.

For an installed copy, get its executable from the installation receipt; setup does not change PATH:

```powershell
$receipt = Get-Content "$env:LOCALAPPDATA\RevitThyme\setup\receipt.json" -Raw | ConvertFrom-Json
& $receipt.cli version --json
& $receipt.cli update check
& $receipt.cli update install
```

Checks use the official GitHub release API and bounded HTTPS metadata downloads. Downloads invoke the packaged PowerShell bootstrap pinned to the checked version, verifying its installer SHA-256 and available GitHub asset digests. The installer remains unsigned. This is a release checker and the existing installer flow; no background service or separate package manager is installed.
