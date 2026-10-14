# RevitThyme project

The accepted product and architecture are defined by the complete [2026-10-14 plan](2026-10-14_NEW_PLAN.md). It supersedes the Rust/Axum and Expo/Electron replacement direction. RevitThyme is a native C# toolkit with optional AI interfaces.

Current work: [WLK-248](https://linear.app/wlkr-labs/issue/WLK-248/complete-the-c-dynamic-execution-stage-with-native-cabin-eval), **Dynamic execution only**, building on completed Foundation, MCP and Native utilities. Acceptance is Roslyn query/modify, native transactions and disposable models with an executable, independently scored Cabin Eval. See [execution guide](docs/DYNAMIC-EXECUTION.md) and [evidence](verification/dynamic-execution.md).

The repository stays at `C:\Revit\RevitThyme`. Five product projects live under `src`; offline, stdio MCP and actual-Revit test projects live under `tests`. `project.cmd help` owns the developer command inventory. The build uses .NET 10/WPF, the pinned official MCP SDK and locally supplied Revit 2027 SDK binaries, with Autodesk redistribution disabled.

The superseded implementation, prior project decisions and historical verification records are preserved under `legacy/pre-foundation`; private root artifacts and existing installed bundles remain intact. [Migration](docs/MIGRATION.md) records the preservation boundaries. External TimberFold remains independently owned at `C:\Revit\Projects\TimberFold`.

Built-in Agent and Exploration remain deferred. The MCP server exposes 12 generic, four utility and two explicitly trusted C# execution tools using shared operations. Dynamic code is disabled by default; there is no built-in agent runtime.

For coding workflow read [w.md](w.md); for native execution or packaging read the relevant [architecture](docs/ARCHITECTURE.md), [acceptance](docs/FOUNDATION.md) or [installation](docs/INSTALLATION.md) guide. Working-model save/sync/close and public publication remain separate explicit actions. Local source conversion does not uninstall existing legacy tools.
