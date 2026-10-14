# Contributing

Read the [architecture](docs/ARCHITECTURE.md) and [current stage](docs/ROADMAP.md). The active implementation is C#/.NET 10, WPF and official-SDK MCP; the current scope stops at Dynamic execution. Public issues and pull requests are welcome without Linear access. Maintainer tracking uses WLKR LABS / RevitThyme.

1. Reproduce the problem in a small public sample or offline model adapter. Preserve unrelated source and user models.
2. Add typed requests/results and shared behavior in `RevitThyme.Core`. Implement Autodesk access in `RevitThyme.Addin`, entirely inside API callbacks. Keep WPF/CLI adapters free of duplicated operations.
3. Run `project.cmd check`. Use `check -Portable` without Autodesk SDKs. The unit test executable has no external test framework; `project.cmd test` runs it. Hosted Windows CI builds WPF and portable code; real Revit tests require licensed Revit on a separate Windows test host.
4. For native changes, use [Dynamic execution qualification](docs/DYNAMIC-EXECUTION.md), [Native utilities qualification](docs/NATIVE-UTILITIES.md), [Foundation acceptance](docs/FOUNDATION.md) or [MCP qualification](docs/MCP.md), record native readback/rollback and distinguish actual-Revit evidence from offline checks. Keep private paths, models and session captures out of public commits.
5. Submit the problem, resulting behavior, source revision, checks and limits in a focused PR.

`project.cmd build` builds the complete solution against local Revit 2027. An alternate SDK folder can be supplied through `-p:RevitApiPath=ABSOLUTE_PATH`. Autodesk binaries must stay outside the repository and package. Small contribution candidates include accessible status presentation, improved CLI diagnostics and test coverage for transaction failures; use `good first issue` for narrowly bounded public requests.

Contributions are licensed GPL-3.0-or-later. Superseded Rust/Electron/pyRevit sources in `legacy` are reference material and excluded from the current build.
