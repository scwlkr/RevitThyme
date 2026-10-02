# Implementation layout

The host and adapters are planned. This folder intentionally contains design guidance rather than fake executable commands or an unverified .NET project.

Create implementation folders when their roadmap milestones begin:

| Proposed folder | Responsibility |
| --- | --- |
| `revit-host/` | Focused C# host, valid API execution, session/document handling |
| `pyrevit-adapter/` | Initial read-only operations through the existing bridge |
| `mcp/` | Named suite tools, validated requests and structured results |

TimberFold's current implementation stays in its external checkout. Start with the smallest working operation interface in docs/ARCHITECTURE.md. Confirm the installed SDK/runtime before selecting native project targets or adding build dependencies.
