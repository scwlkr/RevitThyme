# Third-party notices

Original RevitThyme source is GPL-3.0-or-later; see LICENSE.

The active C# Foundation uses the Microsoft .NET 10 and WPF runtime supplied by the user's environment. It has no NuGet runtime dependencies and packages no .NET runtime. Users supply licensed Autodesk Revit 2027 and its proprietary API assemblies. CopyLocal is disabled; Autodesk binaries are not redistributed.

External TimberFold source, models and dependencies are not bundled. Its independent redistribution and licensing decisions remain its own responsibility.

The preserved `legacy/pre-foundation` tree contains the former pyRevit extension, Rust dependencies and Expo/Electron UI, their lockfiles and [original notices](legacy/pre-foundation/THIRD-PARTY-NOTICES.md). It also preserves a pyRevit Routes patch with upstream GPL-3.0 excerpts. These are excluded from the C# Foundation package; retain their notices if migrating or redistributing them.
