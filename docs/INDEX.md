# Documentation

RevitThyme adds focused native tools to Revit 2027 and exposes the same operations through a CLI, MCP server and optional built-in Agent. Choose a path for your work.

## Use RevitThyme

- [Install](INSTALLATION.md): requirements, bootstrap, manual downloads and recovery.
- [First steps](QUICK-START.md): confirm installation and inspect your first view.
- [Native tools](NATIVE-UTILITIES.md): View Range and Room Dimensions, with supported geometry.
- [CLI](CLI.md): commands, explicit sessions, captures and JSON results.
- [Built-in Agent](AGENT.md): sign in, converse and manage local state.
- [Updates](UPDATES.md) and [troubleshooting](TROUBLESHOOTING.md).

## Connect an agent

Start with [agent integration](AGENT-INTEGRATION.md), then use the [MCP reference](MCP.md) for tool contracts, parameter identity, paging and recovery. [Trusted C#](DYNAMIC-EXECUTION.md) is optional, disabled by default and not sandboxed.

The website publishes `/llms.txt` and `/llms-full.txt` from these same sources for machine-readable context. These files are documentation aids, not permission to edit models.

## Build and contribute

[Contributing](../CONTRIBUTING.md) includes paths with and without licensed Revit. Read the [architecture](ARCHITECTURE.md), [isolated development workflow](DEVELOPMENT.md) and [website maintenance](WEBSITE.md). [Roadmap](ROADMAP.md), [governance](../GOVERNANCE.md) and [public issues](https://github.com/scwlkr/RevitThyme/issues) describe direction.

## Compatibility and trust

The distribution supports **Windows x64, full Revit 2027 and .NET 10 runtimes**. Revit LT and other years are unsupported. Native qualification targets **27.3.0.28** on the recorded host; other builds and computers need separate evidence. This is a pre-1.0 project with an unsigned installer.

[Features and limits](../FEATURES.md), [security](../SECURITY.md) and [verification](../verification/README.md) explain these boundaries. Older implementations in `legacy` are historical references.
