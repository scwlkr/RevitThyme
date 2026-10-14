# Install stage — 2026-10-14

Tracking: [WLK-249](https://linear.app/wlkr-labs/issue/WLK-249/implement-windows-installer-and-verified-release-bootstrap-plan-lines). Read all 484 lines of `2026-10-14_NEW_PLAN.md`; implementation is restricted to **Install RevitThyme**, lines **81–85**. Existing tools through Dynamic execution are packaged together. Built-in Agent, authentication and Exploration remain deferred.

## Qualified implementation

Implementation/test source: **009c6a719795c7f3909fb16f3ed930669a59df22**, branch `codex/wlk-249-install-stage`, base **99e6d06**. The subsequent documentation-only evidence commit does not change the qualified implementation or package. All final checks below ran with a clean source tree at that implementation SHA.

One per-user Windows x64 `.exe` wraps the shared hash-checked PowerShell installer. A PowerShell 5.1-compatible bootstrap resolves latest or an exact GitHub Release, verifies exact asset names/origins, SHA-256 and available GitHub digests, then runs that same executable. Offline download and silent installation are supported. The toolkit installs no package manager, updater, AI runtime or agent configuration.

Supported build: **full Revit 2027 only**. Live read-only discovery found `C:\Program Files\Autodesk\Revit 2027`; the API assembly major version matches 27. Installed x64 .NET 10 Core/Desktop runtime records are detected from both registry views. Nonstandard Autodesk installation locations are read from Autodesk registration. Missing runtime or supported Revit is refused. Autodesk API assemblies, test loaders and models remain excluded from the bundle.

The installer keeps immutable versioned bundles, exact registration ownership, backups and native physical-path checks. Copying uses staging before the immutable-folder move. Receipt persistence is part of the registration transaction; a failure restores the previous owned registration. Setup runs the child in Inno's abortable installation event, propagates failure, and blocks changes while Revit is running. Uninstall preserves the bundle and recoverable registration, and refuses a changed or foreign registration.

## Checks

- `project.cmd check`: complete solution, locked restores, **96 unit checks**, **35 MCP checks**, PowerShell syntax and package/architecture boundaries pass; zero build warnings/errors. Evidence: `artifacts/mcp/checks/result.json`, `unit.txt`, `mcp.txt`.
- `project.cmd check-bootstrap` and Windows PowerShell 5.1 execution of the same suite: **152 assertions pass in each shell**. Web/process calls are scoped mocks; release transport, corrupt downloads, redirects, version selection, setup failures, one-line invocation and safe download ownership are exercised without executing a real downloaded installer.
- `project.cmd check-installer -PackageDirectory <candidate> -PreviousPackageDirectory <0.7.0-package>`: **17 checks pass**, including previous bundle retention, registration backups, idempotence, tampered dependency refusal, receipt persistence rollback, read-only receipt preservation, foreign registration refusal and recoverable uninstall. Final result: `artifacts/foundation/install-test/e6660bdc-1164-4b63-8ede-c42aad14476e/result.json`. The 15-check suite also passes directly in Windows PowerShell 5.1.
- `project.cmd setup ... -Isolated` followed by `project.cmd check-setup ... -ProductionSetupExecutable <candidate>`: **14 executable checks pass**. Actual Windows setup, Windows PowerShell 5.1 children and uninstaller processes install/reinstall/unregister the isolated fixture; installed CLI/MCP execute. Missing Revit, foreign registration, foreign uninstall and a child failure injected after preflight are refused. The production build rejects test mode and refuses installation while Revit is running. Final result: `artifacts/foundation/install-test/2381fa0e-41dc-4270-9466-b42d5e399ce7/setup-result.json` with adjacent logs.

The final setup suite verifies the real registration hash and both original Revit PIDs (**18188**, **36632**) remain unchanged. No working model was saved, synchronized, closed or used as an installer fixture. These checks qualify installation behavior in isolated directories, not a new actual-Revit tool run or another computer.

Review caught and corrected post-install exception handling that could report success after failure, receipt-write recovery, Windows PowerShell module inheritance, and PowerShell 5.1 atomic replacement semantics. Their behavior is covered by the retained checks.

## Retained candidate and identities

Local release folder: `artifacts/installer/RevitThyme-0.8.0/`.

| File or identity | SHA-256 |
| --- | --- |
| `RevitThyme-0.8.0-win-x64.exe` (5,677,549 bytes) | `cd9590a4264dac45d750c4a4a4a432845e2be7cae152f954be209cdd2e0e4de3` |
| `install-revitthyme.ps1` | `3a5bfd848352c7ee98d2799abd15711d964a63742fdf89ab54bc282ec40e35b8` |
| Packaged `manifest.json` | `fd4bfa9a021bd5b5ee6ec048182dd8614bbe7f99f595a6cbdef628fae8bd1fc9` |

Adjacent `.sha256` files and `release.json` retain the exact clean source SHA, supported-year matrix, compiler version and package identity. Native package: `packages/RevitThyme-0.8.0-install-e8529e9750df4eaab77c05e1c84a98b5`. Isolated executable: `artifacts/installer/qualification-009c6a7/RevitThyme-0.8.0-win-x64-test.exe`; it uses a separate test AppId, creates no Windows uninstall registry entry and is excluded from publication.

Inno Setup **6.7.3** was acquired under this checkout's `artifacts/buildtools`, after checking the official asset's pinned SHA-256 and valid Pyrsys B.V. signatures. The global wstack setup worker hit its known Windows `signal.SIGALRM`/`signal.alarm` error; the existing project notes, feature map and `project.cmd` workflow supplied the required project footprint. No global skill was edited.

## Delivery limits

The `.exe` is **unsigned and local**. Nothing was pushed, merged, tagged, published or dispatched to hosted CI. The GitHub release bootstrap cannot download this candidate until its release assets are published. Its behavior is verified with controlled transport fixtures; actual released-asset delivery remains a publication check. A manually dispatched workflow is implemented for an explicitly provisioned Windows x64 `revitthyme-release` runner with licensed Revit API binaries. It uploads review artifacts and optionally creates a guarded draft from the matching tag. Public publication, signing, other Revit years and another-machine qualification are separate actions.
