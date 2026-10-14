# Third-party notices

Original RevitThyme source is GPL-3.0-or-later; see LICENSE.

The C# toolkit uses Microsoft .NET 10 and WPF supplied by the user's environment and packages no .NET runtime. The MCP executable bundles the official ModelContextProtocol/Core 2.2.0 SDK (Apache-2.0/MIT transition; retain both license texts) and Microsoft.Extensions/System.Diagnostics dependencies (MIT, copyright .NET Foundation and Contributors). Full notices are in [MCP license](installer/notices/MCP-LICENSE.txt) and [Microsoft .NET license](installer/notices/DOTNET-LICENSE.txt), distributed under `notices/` in the package. Exact dependencies are pinned in `src/RevitThyme.Mcp/packages.lock.json`; the package includes only its allowlisted runtime files. Users supply licensed Autodesk Revit 2027 and its proprietary API assemblies. CopyLocal is disabled; Autodesk binaries are not redistributed.

External TimberFold source, models and dependencies are not bundled. Its independent redistribution and licensing decisions remain its own responsibility.

The preserved `legacy/pre-foundation` tree contains the former pyRevit extension, Rust dependencies and Expo/Electron UI, their lockfiles and [original notices](legacy/pre-foundation/THIRD-PARTY-NOTICES.md). It also preserves a pyRevit Routes patch with upstream GPL-3.0 excerpts. These are excluded from the C# Foundation package; retain their notices if migrating or redistributing them.
