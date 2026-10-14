# 2026-10-14 C# migration

The complete [new plan](../2026-10-14_NEW_PLAN.md) supersedes the Rust/Axum and Expo/Electron product architecture. The active solution, developer launcher, CI, installer and documentation now use C#/.NET and WPF. Only Foundation is in scope.

`legacy/pre-foundation` preserves the exact prior `config`, `contracts`, `desktop`, `docs`, `extensions`, `native`, `packaging`, `replacement`, `scripts`, `tools` and `verification` trees, including ignored local builds. Former root metadata and launchers are retained there too. Historical links and scripts retain their original paths and assumptions; use the old Git revision for reproduction. They are reference material, not the active build or product.

Migration inventory at `artifacts/foundation/migration.json` records SHA-256 hashes of 216 selected tracked files and the source revision `c7971af`. All were compared after the physical move. No working models, external TimberFold source, installed preview/lab bundles, private artifacts, unpublished packages, user plan, user AGENTS edits or logo work were removed. Existing `artifacts`, `runs` and `packages` remain at the project root.

The active foundation has no Rust, Python, Node, Electron, pyRevit or Codex runtime requirement. The old utilities/MCP are not silently exposed through the new add-in: named C# migration, acceptance and live verification belong to their later stages. TimberFold remains independently maintained at `C:\Revit\Projects\TimberFold`.

Restoring the former architecture is a deliberate source decision, not a file merge into a Revit model. Use Git and the preserved trees; retain new work before switching. Existing installed legacy tools are not uninstalled by this source conversion.
