# Third-party notices

Original RevitThyme source is GPL-3.0-or-later; see LICENSE.

Windows setup is generated with [Inno Setup 6.7.3](https://jrsoftware.org/isinfo.php), copyright Jordan Russell and Martijn Laan. The compiler is a project-local build tool acquired from its official release with hash and publisher verification. It is not included in the native package; generated setup contains the Inno Setup runtime and its own copyright information.

The C# toolkit uses Microsoft .NET 10 and WPF supplied by the user's environment and packages no .NET runtime. The MCP executable bundles the official ModelContextProtocol/Core 2.2.0 SDK (Apache-2.0/MIT transition; retain both license texts) and Microsoft.Extensions/System.Diagnostics dependencies (MIT, copyright .NET Foundation and Contributors). Full notices are in [MCP license](installer/notices/MCP-LICENSE.txt) and [Microsoft .NET license](installer/notices/DOTNET-LICENSE.txt), distributed under `notices/` in the package. Exact dependencies are pinned in `src/RevitThyme.Mcp/packages.lock.json`; the package includes only its allowlisted runtime files. Users supply licensed Autodesk Revit 2027 and its proprietary API assemblies. CopyLocal is disabled; Autodesk binaries are not redistributed.

External TimberFold source, models and dependencies are not bundled. Its independent redistribution and licensing decisions remain its own responsibility.

Dynamic execution bundles Microsoft.CodeAnalysis.Common/CSharp **5.9.0** (Roslyn, MIT, copyright .NET Foundation and Contributors) beside the add-in. Its exact dependency graph is pinned in `src/RevitThyme.Addin/packages.lock.json`; only the two runtime compiler assemblies are packaged, along with the retained [Microsoft .NET MIT license](installer/notices/DOTNET-LICENSE.txt). Build-time Microsoft.CodeAnalysis.Analyzers is not distributed. The compiler API is documented by [Microsoft](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.csharpcompilation.create?view=roslyn-dotnet-5.0.0) and the pinned package by [NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/5.9.0).

The preserved `legacy/pre-foundation` tree contains the former pyRevit extension, Rust dependencies and Expo/Electron UI, their lockfiles and [original notices](legacy/pre-foundation/THIRD-PARTY-NOTICES.md). It also preserves a pyRevit Routes patch with upstream GPL-3.0 excerpts. These are excluded from the C# Foundation package; retain their notices if migrating or redistributing them.
