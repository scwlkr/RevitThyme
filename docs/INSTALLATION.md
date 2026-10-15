# Install RevitThyme

Version **0.9.1** includes the dockable Agent client, ordinary skills, pinned official Codex CLI 0.162.1, separate development/installed version reporting and installed-copy update commands. Setup automatically exposes `rthyme` globally. The source package version is **0.9.1-cli**. See [Agent use and private state](AGENT.md) and [manual update commands](UPDATES.md).

The Windows x64 installer includes the native add-in, CLI and MCP server. It supports **full Revit 2027**; other years and Revit LT are not supported by this build. Revit 2027 supplies the required [.NET 10 runtime](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-8D7A4715-EAF8-4BD1-BE78-061F900D0BCE). Setup verifies the x64 Desktop and Core runtimes; no SDK, PowerShell 7, AI account or administrator permission is needed to install.

## Windows setup or one-line bootstrap

Close Revit normally, preserving unsaved work. Run this in PowerShell to install the **latest stable GitHub release**:

```powershell
irm https://revitthyme.wlkrlabs.com/install | iex
```

To keep the installation pinned to **v0.9.1**, including after newer releases appear:

```powershell
irm https://github.com/scwlkr/RevitThyme/releases/download/v0.9.1/install.ps1 | iex
```

Both commands download and verify the Windows installer before running it. Setup detects supported installed versions from Autodesk registration and the standard install location, then registers the bundled add-in for the current user. For a manual installation, download `RevitThyme-0.9.1-win-x64.exe` and its `.sha256` from the [0.9.1 release](https://github.com/scwlkr/RevitThyme/releases/tag/v0.9.1), verify the SHA-256, and run the executable.

The **0.9.1 installer is unsigned**. Checksum verification proves consistency with the release assets; it is not a publisher signature. The [0.9.1 release](https://github.com/scwlkr/RevitThyme/releases/tag/v0.9.1) contains the installer, bootstrapper, adjacent checksums and source/package metadata. It is separate from the older legacy release.

`/install` selects the latest stable release; the older `/install-0.8.1` remains pinned to 0.8.1. HTTPS, strict GitHub download URLs, installer SHA-256 and available asset-digest checks remain in place. Piping to `iex` executes the downloaded script, so it trusts `revitthyme.wlkrlabs.com`, GitHub and this repository. You can download and inspect the script first instead. The 0.9.1 release's `install.ps1` asset defaults to 0.9.1; `install-revitthyme.ps1` retains its latest-release default.

For a saved bootstrapper, `-Version 0.9.1` selects an exact release, `-Silent` runs unattended, and `-DownloadOnly -Destination C:\Offline` saves a verified installer without installing. Existing downloads are preserved. Failures retain a setup log and return an error. Setup never closes Revit or restarts Windows.

## Installed files and recovery

Setup verifies the package's exact file allowlist and hashes, then copies an immutable bundle under the physical `%USERPROFILE%\AppData\Local\RevitThyme\foundation\<manifest-sha256>` directory. `RevitThyme.Foundation.addin` in the native per-user Revit 2027 Addins folder points to that bundle. Earlier bundles and registration backups remain recoverable; unknown registrations and modified packages are refused. Native path probes reject AppData redirection. Other add-ins, models and agent configuration remain preserved.

The physical installation receipt at `%USERPROFILE%\AppData\Local\RevitThyme\setup\receipt.json` contains absolute `cli`, `global_command`, `mcp`, `installed` and `registration` paths. Setup prepends the immutable bundle's CLI directory to your user PATH. Open a new PowerShell window, then run `rthyme --version`, `rthyme update check` or `rthyme update install` from any folder. Source and installed builds are reported separately, and updates compare against the installed copy. Existing terminals may keep their earlier CLI until reopened; a running Revit window may keep its loaded add-in until restarted normally.

Setup replaces only its owned PATH entry on upgrade and removes only an entry it added on uninstall. It preserves the optional `.dotnet\tools` developer command and other PATH entries. A shell alias, function or earlier machine PATH command can take precedence; inspect `Get-Command rthyme -All`, or use the receipt's `global_command` path explicitly. Use the receipt paths for external [MCP configuration](MCP.md). Bridge protocol 4 requires compatible clients and add-in. Trusted C# remains disabled unless explicitly enabled for the Revit process; see [execution](DYNAMIC-EXECUTION.md).

Open Revit normally and load the owned add-in when prompted. Built-in tools need no AI account. See [native tools](NATIVE-UTILITIES.md) for usage and limits. The optional [Agent](AGENT.md) uses your existing Codex account. Exploration remains deferred.

Uninstall from Windows **Installed apps → RevitThyme**, with Revit closed. The uninstaller verifies the receipt, registration ownership, installed bundle and command ownership, saves a recoverable registration, and removes that registration, its owned PATH entry and setup's own files. Immutable bundles, sessions, models, backups and other add-ins remain. A changed or foreign registration blocks uninstall and preserves the recovery files. Restore a registration backup only after verifying the referenced bundle.

Source-package users can still run `install.ps1` or `uninstall.ps1` in Windows PowerShell. Explicit custom install roots must also be passed to uninstall. `-CheckOnly` validates prerequisites and ownership without installing or unregistering.

## Build and verify a candidate

Contributors need Windows, PowerShell 7, .NET SDK 10.0.400 and locally supplied licensed Revit 2027 API binaries. Autodesk binaries and test models are excluded from the package.

```powershell
.\project.cmd check
.\project.cmd check-bootstrap
.\project.cmd package
.\project.cmd check-installer -PackageDirectory $package
.\project.cmd check-global-command -PackageDirectory $package
.\project.cmd setup -PackageDirectory $package -Isolated
.\project.cmd check-setup -PackageDirectory $package -SetupExecutable $testSetup
.\project.cmd setup -PackageDirectory $package
```

Set `$package` to the fresh package folder and `$testSetup` to the isolated setup path printed by those commands. The setup build acquires project-local Inno Setup **6.7.3**, checking its pinned SHA-256 and publisher signature first; an explicitly supplied matching `-CompilerPath` is also supported. Release outputs have stable versioned names, adjacent SHA-256 files, the bootstrapper and `release.json` with source/package identities. Existing output folders are refused.

Isolated setup builds have a `-test.exe` suffix and require a UUID test root with simulated Revit discovery. They create no Windows uninstall registry entry and cannot be published by the release workflow. Tests exercise actual setup/install/uninstall processes through Windows PowerShell 5.1 while leaving real Revit registrations untouched. These are installer tests, not new actual-Revit tool qualification or another-computer evidence.

The manual [release workflow](../.github/workflows/release.yml) requires a deliberately provisioned `revitthyme-release` Windows x64 runner with licensed local Revit API binaries. It checks and uploads review artifacts. Only an explicit `publish_draft` dispatch from the matching clean `v0.9.1` tag creates a **draft**, after verifying asset hashes and source identity. Public publication requires owner review. Signing and another-machine qualification remain separate work. The [release checker](UPDATES.md) uses the existing installer; no background service or separate package manager is installed.
