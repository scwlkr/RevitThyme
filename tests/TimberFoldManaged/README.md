# Installed managed TimberFold qualification

From `C:\Revit\RevitThyme`, run the verifier with an explicit installed directory,
the retained signed package directory, its independently recorded manifest SHA256,
and a fresh evidence directory. This example pins candidate `11b091f`:

```powershell
pwsh -NoProfile -File .\scripts\qualify-timberfold-managed-package.ps1 `
  -InstalledDirectory "$env:LOCALAPPDATA\RevitThyme\foundation\76e7c32ec0e1d9db62c9db2045e89d6a7ccaeb6b7105be853663119befc3bbff" `
  -PackageDirectory 'C:\Revit\RevitThyme\packages\RevitThyme-0.10.0-timberfold.4-5536e25997da478ebb622a569a77ed31' `
  -ExpectedManifestSha256 '76E7C32EC0E1D9DB62C9DB2045E89D6A7CCAEB6B7105BE853663119BEFC3BBFF' `
  -Destination "C:\Revit\RevitThyme\artifacts\timberfold\managed-package-$([Guid]::NewGuid().ToString('N'))"
```

PowerShell 7 and the .NET 10 SDK build only this executable. Its three binary
references require `ManagedPayloadDirectory`; there are no source project or NuGet
package references. Core, Clipper2 and IxMilia.Dxf are explicitly loaded from the
installed directory after complete installed/package inventory and hash checks.
The launcher also requires valid matching Core Authenticode publishers. The
expected manifest hash must come from retained candidate evidence, rather than
being inferred from the directory being tested.

The public `TimberFabrication.Fit` → stock → DXF staging/readback → guides seam runs
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
  "$env:LOCALAPPDATA\RevitThyme\foundation\76e7c32ec0e1d9db62c9db2045e89d6a7ccaeb6b7105be853663119befc3bbff" `
  '76E7C32EC0E1D9DB62C9DB2045E89D6A7CCAEB6B7105BE853663119BEFC3BBFF' `
  "C:\Revit\RevitThyme\artifacts\timberfold\packaged-mcp-$([Guid]::NewGuid().ToString('N'))"
```
