# Install RevitThyme native toolkit

This is a local development package for Revit 2027 on Windows, requiring .NET 10. It has no AI dependency. A production signed `.exe` installer and GitHub bootstrap remain distribution work.

From a source checkout with .NET SDK 10.0.400 and licensed Revit 2027 installed:

```powershell
.\project.cmd check
.\project.cmd package
```

The package command prints a fresh folder, ZIP and adjacent SHA-256 file under `packages`. It includes native Addin/Core/UI assemblies, framework-dependent CLI and official-SDK MCP server, allowlisted dependencies, third-party notices and installation scripts. Autodesk assemblies, test loaders and models are excluded. If you received the ZIP, verify its SHA-256 against the separately trusted checksum and extract it first.

Close Revit normally, preserving your unsaved work. In the extracted package folder:

```powershell
& .\install.ps1
```

The installer detects local Revit 2027, verifies the exact file allowlist and hashes, copies an immutable bundle under your physical `AppData\Local\RevitThyme\foundation` folder, and registers `RevitThyme.Foundation.addin` under the native per-user Revit 2027 Addins folder. The registration/storage names remain stable for reversible Foundation upgrades. It prints absolute CLI and MCP paths. It does not change PATH, Codex configuration or other add-ins. Reinstallation retains earlier bundles and backs up the previous owned registration; unknown or modified packages are refused. Native path probes reject MSIX-redirection mistakes.

Open Revit normally and load the owned add-in when prompted. Choose **RevitThyme → View Tools → View Range** or **Room Dimensions**; see [utility usage and limits](NATIVE-UTILITIES.md). **Foundation → Inspect View** remains available for inspection and renaming. Revit Undo reverses a successful utility edit. A stale capture is rejected; refresh before another edit. No tool saves or syncs the project. The legacy Visual View Range registration remains intact; distinct command IDs let both tools share an existing View Tools panel.

For scripts, use the printed `revitthyme.exe` path:

```powershell
& $cli sessions --json
& $cli inspect --session $session --json | Set-Content -LiteralPath $capture -Encoding utf8
& $cli rename-view --session $session --capture $capture --name 'Reviewed View Name' --json
```

Set `$cli` to that executable, `$session` to the absolute session file shown by `sessions`, and `$capture` to a private absolute JSON file. Choose the intended process explicitly. Each capture records its document, view, revision and previous value. After renaming, inspect again before another edit. Exit codes: `0` successful/unchanged, `1` rejected/rolled back/unconfirmed operation, `2` invalid command or connection. An unconfirmed edit needs native inspection before another edit.

Connect external Codex using the printed MCP executable and [MCP setup](MCP.md). The add-in, CLI and MCP share protocol version 4; older bindings cannot connect. Upgrade them together. The package includes pinned Roslyn compiler dependencies; [trusted C#](DYNAMIC-EXECUTION.md) requires an explicit Revit-process opt-in. Built-in Agent and Exploration remain deferred.

To unregister, close Revit normally and run the installed or packaged `uninstall.ps1`. It backs up the exact verified owned registration and removes that one file. Bundles, sessions, fixtures and other add-ins remain recoverable. Restore the saved registration only after verifying its referenced bundle.
