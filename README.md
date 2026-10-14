# RevitThyme

![RevitThyme](assets/branding/revitthyme-logo.svg)

RevitThyme is an open-source native Revit toolkit. Its shared C# operation layer serves WPF, CLI and an optional external AI interface through MCP.

This checkout implements **Foundation, MCP, Native utilities and Dynamic execution** from the [2026-10-14 plan](2026-10-14_NEW_PLAN.md). **View Range** edits the four native plan planes with a section preview. **Room Dimensions** creates an associative width/depth pair for rectangular rooms, leaving room for tags. Both work through native ribbon buttons, CLI and MCP using shared C# operations and one Revit Undo item per edit. The official C# MCP server exposes 18 tools, including opt-in trusted Roslyn C# query/modify. Native tools work without an AI account, Rust, Electron or pyRevit.

![Native WPF Inspect View](docs/images/foundation-wpf.png)

![Native WPF View Range](docs/images/native-view-range.png)
![Native WPF Room Dimensions](docs/images/native-room-dimensions.png)

## Build and install

Use Windows, PowerShell 7, .NET SDK 10.0.400 and licensed Revit 2027. The current native target is Revit **27.3.0.28**; other releases and another computer need qualification. Revit supplies its proprietary API assemblies locally.

```powershell
.\project.cmd doctor
.\project.cmd check
.\project.cmd package
```

The last command prints a package folder and checksum. Close Revit normally and run `install.ps1` in that folder, then open **RevitThyme → View Tools → View Range / Room Dimensions**. See [utility usage and limits](docs/NATIVE-UTILITIES.md) and [installation and recovery](docs/INSTALLATION.md). The toolkit has a reversible local installer; a signed production `.exe` and release bootstrap remain distribution work.

For CLI automation, inspect a chosen session into a private JSON capture, review it, then rename the exact captured view. See `project.cmd cli --help`. A successful rename creates one Revit Undo item; the tool never saves or syncs a project.

## Architecture

```mermaid
flowchart TD
    WPF[WPF tool] --> Core[C# operation dispatcher]
    CLI[C# CLI] --> Bridge[Local named pipe]
    MCP[Official C# stdio MCP] --> Bridge
    Codex[External Codex] --> MCP
    Bridge --> Core
    Core --> Host[ExternalEvent execution host]
    Host --> Operations[Shared C# operations]
    Operations --> Revit[Revit API and transactions]
```

See [architecture](docs/ARCHITECTURE.md), [MCP tools and Codex setup](docs/MCP.md), [Foundation evidence](verification/foundation.md), [MCP verification](verification/mcp.md) and [Native utilities verification](verification/native-utilities.md). Hosted CI builds the portable C#/WPF/MCP solution and runs offline checks. Real Revit tests use a new, pinned disposable process and populated fixture.

[Trusted C# execution](docs/DYNAMIC-EXECUTION.md) uses rollback queries, atomic modifications, structured diagnostics/results and retained request outcomes. It is disabled by default and runs with full process permissions. The independent [Cabin Eval](tests/CabinEval/README.md) scores actual native results, including a temporary 14-to-18-foot length probe, in a new disposable process/model. See [Dynamic execution verification](verification/dynamic-execution.md).

Built-in Codex Agent and Exploration remain [later stages](docs/ROADMAP.md). Superseded source and evidence are preserved in [legacy/pre-foundation](legacy/README.md); earlier qualification does not certify this new product. Existing installed tools and external TimberFold remain preserved.

## Contribute

Small tools and clear bug reports are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md). Public contributors need no Linear account or Codex runtime knowledge. The [issue templates](.github/ISSUE_TEMPLATE) collect small reproducible examples; [starter labels](.github/labels.json) describe contribution categories.

GPL-3.0-or-later. See [LICENSE](LICENSE), [third-party notices](THIRD-PARTY-NOTICES.md) and [security](SECURITY.md). Revit is supplied and licensed separately; Autodesk binaries are excluded from source packages.
