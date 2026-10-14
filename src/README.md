# Active implementation

The native toolkit uses `RevitThyme.Core`, `RevitThyme.Addin`, `RevitThyme.UI`, `RevitThyme.Cli` and `RevitThyme.Mcp`. Built-in Agent adds `RevitThyme.Codex`, a portable C# client for the official local Codex App Server; the WPF panel consumes it without duplicating Revit operations. See [architecture](../docs/ARCHITECTURE.md), [MCP](../docs/MCP.md), [utilities](../docs/NATIVE-UTILITIES.md), [execution](../docs/DYNAMIC-EXECUTION.md) and [Agent](../docs/AGENT.md). Exploration remains deferred.
