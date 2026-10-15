# Installed version and global CLI verification

Scope: correct the CLI's development/installed version distinction and provide `rthyme` globally. Public v0.9.0 assets remain unchanged. No native model operations or Agent/Exploration work is added.

## Reproduction and fix

`project.cmd cli version --json` originally returned only `version` and `build` for its own assembly. `update check` compared that same build against GitHub; a 0.9 CLI therefore hid an older registered toolkit. The source now captures executing CLI, detected checkout and registered installation separately. Installed discovery checks the owned Revit registration, immutable manifest SHA-256, Addin/Core hashes and any setup receipt. New packages record product version explicitly; older packages use assembly product metadata.

Update eligibility uses the verified installed version. The deterministic dev-0.9/installed-0.8/latest-0.9 test offers an update and invokes the installer even though the executing CLI is already 0.9. Missing installations can be installed; inconsistent registrations fail closed. Installed versions ahead of stable do not automatically downgrade. Needed installations still require Revit closed.

## Evidence

- `project.cmd check`: **149 Core/CLI + 35 MCP + 13 Agent = 197** offline checks pass. Nineteen new checks cover installed-state ownership, hashes, external XML entities, structured version output, damaged optional checkout metadata and actual CLI update dispatch with injected installer boundaries.
- `project.cmd cli-install -ToolPath <owned test directory>` packages the existing CLI as a standard .NET global tool, with native `rthyme` apphost and its verified PowerShell downloader. No custom shell wrapper or additional product project.
- `project.cmd check-cli-tool -Executable <rthyme.exe>`: **7 checks pass** from a directory outside the checkout, covering text/JSON output, empty/quoted/spaced/Unicode/trailing-backslash arguments and nonzero exits. Standard streams explicitly use UTF-8.
- Actual `rthyme update download --version 0.9.0 --destination <private folder>` downloads the public installer and verifies SHA-256 `be2f4199bc145874343ee331ddfb64499375e8f6c605879c832b8ad66a26a78c`. The bundled runtime script is exercised from the installed tool package, not the source output directory.
- Toolkit packaging succeeds with the renamed NuGet package identity while preserving the installed executable name `revitthyme.exe`. **20 isolated installer/upgrade checks pass**, including upgrade from the public 0.9 package, preserved old bundle and registration recovery. Test root: `artifacts/foundation/install-test/14a1a4b4-2939-4313-b3f1-796d7b54a161`.

Global installation uses `project.cmd cli-install` from the primary checkout after landing. A private development-link file lets the snapshot find that checkout from another folder. Its native .NET tool package is installed per user; refresh it by rerunning the command after pulling source changes. No public NuGet or GitHub release is created by this route.

Existing Revit PID 20896 and its registered public 0.9.0 bundle are preserved. No working model save, close or sync occurs. No new actual-Revit functional qualification is claimed: native operation code and UI are unchanged; the tests cover discovery, CLI dispatch, native command execution and installer compatibility.
