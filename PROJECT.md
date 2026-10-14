# RevitThyme project

The accepted product and architecture are defined by the complete [2026-10-14 plan](2026-10-14_NEW_PLAN.md). It supersedes the Rust/Axum and Expo/Electron replacement direction. RevitThyme is a native C# toolkit with optional AI interfaces.

Current work: [WLK-244](https://linear.app/wlkr-labs/issue/WLK-244/convert-revitthyme-to-c-and-complete-the-2026-10-14-foundation-stage), **Foundation only**. Its acceptance is a native tool that inspects and modifies Revit through the shared C# operation layer, with WPF and a basic CLI. See [Foundation](docs/FOUNDATION.md) and its [evidence](verification/foundation.md).

The repository stays at `C:\Revit\RevitThyme`. Four product projects live under `src`; offline and actual-Revit test projects live under `tests`. `project.cmd help` owns the current developer command inventory. The active build uses .NET 10/WPF and locally supplied Revit 2027 SDK binaries, with redistribution disabled.

The superseded implementation, prior project decisions and historical verification records are preserved under `legacy/pre-foundation`; private root artifacts and existing installed bundles remain intact. [Migration](docs/MIGRATION.md) records the preservation boundaries. External TimberFold remains independently owned at `C:\Revit\Projects\TimberFold`.

The MCP, native utilities, dynamic execution, built-in Agent and exploration stages require later implementation and qualification. This foundation introduces no placeholder AI functionality or arbitrary-code endpoint.

For coding workflow read [w.md](w.md); for native execution or packaging read the relevant [architecture](docs/ARCHITECTURE.md), [acceptance](docs/FOUNDATION.md) or [installation](docs/INSTALLATION.md) guide. Working-model save/sync/close and public publication remain separate explicit actions. Local source conversion does not uninstall existing legacy tools.
