# Document discovery qualification

This Autodesk-only, signed test application prepares disposable fixtures and records native metadata independently of RevitThyme. All toolkit acceptance requests use the installed MCP server through the official external SDK. Probe preparation and teardown do not qualify toolkit lifecycle behavior.

Use an installed protocol-13 / 32-tool candidate, a closed nonworkshared Revit 2027 RVT, and a local family template. Preserve every working Revit process. The runner refuses registration changes and startup while any Revit process exists. Build this project and sign its DLL with the existing persistent development identity; do not reinstall the toolkit.

```powershell
dotnet build tests/DiscoveryProbe/RevitThyme.DiscoveryProbe.csproj -c Release
# Sign the probe DLL, then verify its Authenticode certificate matches development/signing.json.
pwsh -File tests/DiscoveryProbe/run.ps1 -Action register
pwsh -File tests/DiscoveryProbe/run.ps1 -Action prepare -Mode normal -FixtureSource C:\Revit\Models\Test\Disposable.rvt
# Use the exact folder printed by prepare:
pwsh -File tests/DiscoveryProbe/run.ps1 -Action start -RunFolder $run
pwsh -File tests/DiscoveryProbe/run.ps1 -Action ready -RunFolder $run
dotnet build tests/Mcp/RevitThyme.McpTests.csproj -c Release
dotnet tests/Mcp/bin/Release/net10.0/RevitThyme.McpTests.dll --document-scope-live $run
pwsh -File tests/DiscoveryProbe/run.ps1 -Action close -RunFolder $run
```

Wait for the exact live process to produce `ready.json` before running `ready`. Repeat preparation/start/ready/test/close with `-Mode fixture`, then `-Action unregister`. Keep the printed plan, launch, immutable signed probe, original intents, metadata receipts, transcript, completion and cleanup reports under ignored `artifacts/document-boundaries`.

The native oracle begins with active saved A, background saved B, unsaved project and unsaved family. It retains the created Document references; it never obtains the toolkit's opaque IDs. Snapshot commands are separate from explicit `close-extra` and `cleanup` fixture commands. The SDK caller awaits every product response before teardown. Teardown closes only owned background fixtures, never the active A or an unexpected document. A clean A remains for normal process close. Failed checks stop acceptance; inspect retained state before cleanup, never force termination or use another model.

Normal mode compares every visible flag and exact page membership against the native oracle. Fixture mode verifies hidden documents do not appear or alter the visible inventory token, while native revision changes still invalidate old captures. Completed original reads are recovered after client replacement. This pass does not claim interrupted-request proof; the separate document-discovery qualification retains that experiment. Empty unsaved paths exercise argument rejection, not family-specific native lifecycle restrictions.
