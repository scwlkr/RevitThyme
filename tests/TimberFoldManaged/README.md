# Installed managed TimberFold qualification

From `C:\Revit\RevitThyme`, run the verifier with an explicit installed directory,
the retained signed package directory, its independently recorded manifest SHA256,
and a fresh evidence directory. This example pins source candidate
`0fffdf8397da507c10911e77a6fb1bb81120b4e1`, version `0.10.0-timberfold.4`, protocol 8.
Set the package path to the separately retained signed package:

```powershell
pwsh -NoProfile -File .\scripts\qualify-timberfold-managed-package.ps1 `
  -InstalledDirectory "$env:LOCALAPPDATA\RevitThyme\foundation\ae21953451541c57ee55b28202d00de94a761314c57dd446724e6c4803df91c5" `
  -PackageDirectory 'ABSOLUTE_RETAINED_PACKAGE_DIRECTORY' `
  -ExpectedManifestSha256 'AE21953451541C57EE55B28202D00DE94A761314C57DD446724E6C4803DF91C5' `
  -Destination "C:\Revit\RevitThyme\artifacts\timberfold\managed-package-$([Guid]::NewGuid().ToString('N'))"
```

PowerShell 7 and the .NET 10 SDK build only this executable. Its three binary
references require `ManagedPayloadDirectory`; there are no source project or NuGet
package references. Core, Clipper2 and IxMilia.Dxf are explicitly loaded from the
installed directory after complete installed/package inventory and hash checks.
The launcher also requires valid matching Core Authenticode publishers. The
expected manifest hash must come from retained candidate evidence, rather than
being inferred from the directory being tested.

The final candidate passes both replay passes with 36 identical canonical files,
all 89 payload files validated before/after, and all 12 command-lookup probes
confirmed absent. The public `TimberFabrication.Fit` → stock → DXF staging/readback → guides seam runs
twice for four immutable, original procedural captures. Literal expected panel and
opening counts are gable 6/3, irregular 10/4, multistorey 10/5 and flat 18/7. Source
identities, mappings and input hashes must survive. An independent BCL comparator
checks every canonical relative filename, SHA256, length and byte sequence between
passes, including fitted/stock JSON, DXFs, SVGs and staging receipts. Timing and
binary provenance remain outside those canonical output directories.

`replay/qualification.json` records before/after package validation, loaded assembly
locations/versions/hashes, fixture results, timings and the actual canonical file
inventory. `launcher.json` retains verifier identity, signature evidence and the
child command PATH. Existing evidence directories are refused and preserved.

During replay, PATH contains only Windows, System32 and System32/Wbem. Every such
directory is probed for `python.exe`, `python3.exe`, `py.exe` and `pyrevit.exe`;
finding any of them blocks qualification. Other parent environment variables are
inherited. This proves the observed managed replay with bounded command lookup;
it does not assert that Python is absent elsewhere on the machine.

This verifier makes no Revit API calls, creates no native views/sheets or DWGs,
changes no installation or model, and cannot establish physical fit or strength.
`ActualRevitNativeQualificationOutstanding` and `PhysicalOutstanding` remain true;
`ManufacturingReady` remains false. Replay timings are observations, not a formal
native performance qualification.

The companion packaged MCP check uses the actual installed stdio server and the
official SDK client, with explicitly empty session discovery so it never connects
to a native Revit host. The current candidate passes nine checks covering 25 tools
and seven TimberFold controls:

```powershell
dotnet run --project tests/Mcp -c Release -- --packaged-stdio `
  "$env:LOCALAPPDATA\RevitThyme\foundation\ae21953451541c57ee55b28202d00de94a761314c57dd446724e6c4803df91c5" `
  'AE21953451541C57EE55B28202D00DE94A761314C57DD446724E6C4803DF91C5' `
  "C:\Revit\RevitThyme\artifacts\timberfold\packaged-mcp-$([Guid]::NewGuid().ToString('N'))"
```
