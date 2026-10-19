# TimberFold

The C# feature is being implemented in seven verified slices. The current source adds native fabrication-profile and source-scope inspection through **RevitThyme → Fabrication → TimberFold**, CLI and MCP. Cutting-file generation, native kit documentation and four-building qualification remain open; the preserved external implementation is still available.

Open a project containing vertical basic exterior walls and planar roofs. Set the model scale and physical stock dimensions in inches, then choose **Inspect**. Review included/excluded source counts, phase/options, unsupported geometry and profile diagnostics. **Open files** gives the complete private source snapshot and diagnostic report when the window samples a larger inventory. Nominal settings do not establish physical fit. Inspection does not create model elements or cutting files and does not save the source.

```powershell
rthyme timberfold inspect --session ABSOLUTE_SESSION_JSON --json
rthyme timberfold inspect --session ABSOLUTE_SESSION_JSON --profile ABSOLUTE_PROFILE_JSON --scope ABSOLUTE_SCOPE_JSON --json
```

The MCP `timberfold_inspect` tool uses a fresh `get_context` capture ID and optional typed profile/scope. Settings use physical inches; captured Revit geometry uses feet. Scope omitted means the active view phase (or a reported fallback) and main/primary options. Explicit scope records a phase ID and compatible design-option IDs. Full support and numeric requirements are in [support/acceptance](support.md).

This source uses protocol 5; install matching host, CLI and MCP together after qualification. Older installed clients/hosts cannot acquire new operations by reconnecting.

Development references: [spec](spec.md), [preserved baseline](baseline.md), [library evaluation](geometry-libraries.md), [isolated native workflow](../DEVELOPMENT.md), and the original fixture builders under the test project. Actual new C# Revit qualification and physical fabrication must be reported separately from source/build tests.
