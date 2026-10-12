# Native Visual View Range cutover verification

2026-10-12, WLK-233. Installed and actually tested source: 225bdf2e1906eed747ceb7459ab48220ae36a947. Revit 27.3.0.28, native adapter 0.2.6.0, protocol 2. This is local qualification; no publication or default-branch landing is claimed.

## Source and package

All 40 full local CI checks passed on the exact clean source revision. The package includes the compact Visual View Range UI and owned dialog components. Immutable installation compared every copied package file and retained the previous native registration files. Both review passes found no unresolved in-scope Standards or Spec findings.

The primary checkout was fast-forwarded to that same source revision on its existing WLK-229 branch. Its 125 foundation checks passed. The unrelated untracked logo remains preserved. The default branch and remotes were not changed.

## Actual Revit

The dedicated process is PID 32388, start ticks 639274214382868710. Its active document is the newly created disposable RevitThyme-Test fixture, view RevitThyme Lab Plan. Runtime readback confirmed the installed preview path, active native host, adapter 0.2.6.0, initially unmodified document and an empty pyRevit assembly list.

The native ribbon launched the installed desktop UI and captured the intended fixture. Changing the UI preview to Cut 1524 mm left the native model at its original 4 ft. The Apply comparison showed Cut 4 ft to 5 ft with Top 8 ft, Bottom 0 ft and Depth -1 ft unchanged. The displayed document/view/range was explicitly confirmed through the UI.

The native outcome was applied_verified with one undo item. Independent read-only native MCP capture confirmed Cut 5 ft, unchanged other planes and level references, and unchanged captured geometry. One native Revit Undo restored Cut 4 ft. Independent readback passed again, and the refreshed UI displayed 1219.2 mm.

The fixture disk SHA-256 stayed 9804ae2d1b4a72bd323646a6de0400adbe98b1539dec5ca41531716cb1208f64 across baseline, Apply and Undo. No test mutation was saved. The native MCP driver performs no model writes and does not expose Apply. The existing working process 36548 and earlier test process 6820 remained open; the trial did not call their model APIs or save/close their documents.

## Disconnection and limits

pyRevit.addin was reversibly renamed to pyRevit.addin.disabled, with an identical retained backup. Codex revit and jev-revit entries are disabled; all unrelated TOML values were preserved. The native revitthyme_test entry now selects the qualified process and its exact fixture. Existing Codex connections may require reconnection/reload to adopt these settings.

Already running Revit processes retain their previously loaded pyRevit until normal exit/restart. The working session has unsaved changes: the user controls when to save and restart it. Future Revit starts load the new native preview without the pyRevit registration. Legacy suite buttons, TimberFold launcher and general Python/Jev model operations need native replacements; this qualification does not establish full-suite parity, other builds or other machines.

The active managed worktree must remain available while this lab session/registration is in use. Its private artifacts/revit-lab/bbf8fb53-5678-451f-bfe6-04983ed31b0f folder retains registration/config rollback files, native-install.json, pyrevit-disconnect.json, binding.runtime.json, live-readonly.json, live-applied.json, live-undo.json and native UI screenshots. Full CI is artifacts/local-ci/wlk-233.json. Keep these private records separate from release files and later documentation revisions.
