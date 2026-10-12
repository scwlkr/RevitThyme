# Native test MCP qualification - 2026-10-12

Work: WLK-229. This record covers the dedicated native test connection, not broader suite replacement or production-model qualification.

## Qualified native bundle

- Native adapter and Rust MCP source: `e72c870a352b768901ba10d64357493099bf7d3a`.
- Revit executable/SDK: `27.3.0.28`; .NET 10, native lab adapter 0.1.0, native protocol 2.
- Active prepared run: `artifacts/revit-lab/8aa56bfb-9f50-4fb2-bc83-9b1af35d1bc1`.
- Native test Revit: PID 6820, process start ticks `639274139936828170`; separate working process PID 36548 retains its earlier start identity.
- Test file: that run's `RevitThyme-Test.rvt`; view: `RevitThyme Lab Plan`.
- Codex configuration readback confirms `revitthyme_test` enabled and bound to that exact run. Direct stdio MCP initialization/tool calls passed. This ongoing Codex chat does not yet expose the new tool namespace; reconnecting/reloading its MCP configuration may be needed for automatic tool discovery. The project verification driver already uses the native connection directly.

## Evidence and outcomes

The first run at `f8de3e68dee2b70a9d24b04a39adbdb878a52a57` created/saved a fixture but failed because Revit temporarily disabled the synchronous active-view setter after opening. Its `binding.error.json`, saved fixture and failed-startup receipt remain under `artifacts/revit-lab/65e58347-9684-4a2d-b669-10ff8fd71689`. Only that owned test process was stopped. The working instance was preserved.

The repair waits for opening to finish, requests the plan asynchronously on a later idle callback, and publishes the native binding after the requested plan is active. The previous-run installer verified the stopped process and exact prior manifest/MCP entry, backed up both, and replaced only that test entry. Existing NativePreview and pyRevit manifest SHA-256 hashes are unchanged.

`project revit-lab verify <active-run-folder>` passed **11 actual-Revit checks** in `live-readonly.json`:

1. MCP initialization identifies the native test server.
2. Five named read-only/proposal tools are listed; Apply is absent.
3. Capture executes successfully through the native Revit API.
4. Capture returns the exact process/start/session, test document and test plan.
5. Saved/reopened fixture range offsets are 8, 4, 0 and -1 internal feet; geometry bounds preserve the 12 by 1 by 9 ft solid.
6. Shared Rust preview uses actual captured geometry.
7. The four-foot cut offset displays as 1219.2 mm.
8. Native validation returns an identical proposal without model-write permission.
9. A wrong-target proposal is rejected without mutation.
10. Capture still works after that rejection.
11. Refresh makes the previous preview target stale.

The working bridge still reports its working document separately after these checks. No operation requested production geometry, transactions, save, sync or close. Guards may read document identity metadata to refuse an incorrect active file. The user can continue editing the working instance, so its changing UI/modified state is not treated as a test failure.

Offline evidence remains separate: foundation diagnostics, SDK build, Rust format/clippy/tests, native fixture-path/document/write guards, 16 Windows MCP/credential/pipe/transaction checks and existing M1/M2 source/package checks. Full local-CI reports retain their SHA and results beneath `artifacts/revit-lab`; the user's unrelated untracked logo makes `worktree_clean=false`. There is no push, merge or default-branch completion claim.

## Limits

This run allows capture, preview, native proposals, retained outcomes and queued cancellation. It does not expose arbitrary code, Apply, save, sync or close. Native model-writing qualification on 27.3 remains pending. Actual wrong-file/reopen negative fixture trials and foreground UI separation remain pending; their model-boundary rejection is covered offline. Existing pyRevit add-ins still load in the test process, but the tested MCP path uses only native pipes and has no Python/Routes dependency.

The test process stays running for use. Native API calls keep model targets separate; both Revit windows still share the Windows desktop. Each new session requires a fresh run and explicit previous-run connection rotation, never automatic discovery or rebinding. Publication, legacy retirement and broader suite migration remain separate work.
