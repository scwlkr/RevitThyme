# RevitThyme project

The accepted product and architecture are defined by the complete [2026-10-14 plan](2026-10-14_NEW_PLAN.md). It supersedes the Rust/Axum and Expo/Electron replacement direction. RevitThyme is a native C# toolkit with optional AI interfaces.

Current work: [WLK-246](https://linear.app/wlkr-labs/issue/WLK-246/complete-the-c-native-utilities-stage-view-range-and-room-dimensions), **Native utilities only**, building on completed Foundation and MCP. Acceptance is View Range and rectangular Room Dimensions working through native ribbon buttons and MCP, verified against an isolated native Revit fixture. See [utility guide](docs/NATIVE-UTILITIES.md) and [evidence](verification/native-utilities.md).

The repository stays at `C:\Revit\RevitThyme`. Five product projects live under `src`; offline, stdio MCP and actual-Revit test projects live under `tests`. `project.cmd help` owns the developer command inventory. The build uses .NET 10/WPF, the pinned official MCP SDK and locally supplied Revit 2027 SDK binaries, with Autodesk redistribution disabled.

The superseded implementation, prior project decisions and historical verification records are preserved under `legacy/pre-foundation`; private root artifacts and existing installed bundles remain intact. [Migration](docs/MIGRATION.md) records the preservation boundaries. External TimberFold remains independently owned at `C:\Revit\Projects\TimberFold`.

Dynamic execution, built-in Agent and Exploration remain deferred. The MCP server exposes 12 generic and four utility tools using typed shared operations, with no arbitrary-code endpoint or built-in agent runtime.

For coding workflow read [w.md](w.md); for native execution or packaging read the relevant [architecture](docs/ARCHITECTURE.md), [acceptance](docs/FOUNDATION.md) or [installation](docs/INSTALLATION.md) guide. Working-model save/sync/close and public publication remain separate explicit actions. Local source conversion does not uninstall existing legacy tools.
