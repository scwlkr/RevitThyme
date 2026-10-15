# Keep Revit open during development

Use one signed RevitThyme installation and a separate Revit process for trusted C# development. Edit and rerun `.csx` method bodies in that process without reinstalling the toolkit. Changes to compiled host assemblies, startup registration, the ribbon or bundled dependencies still require a normal restart and installation.

The development launcher uses the installed host. It never swaps add-in registrations. It copies a known disposable, nonworkshared `.rvt` into a new run folder, enables trusted execution only in the child process and pins requests to that run's process, session and document. Normal Revit launches retain their existing environment and models.

## Prepare before the installation window

Contributors need the normal [build prerequisites](INSTALLATION.md#build-and-verify-a-candidate), Windows SDK SignTool and a local disposable Revit 2027 model. The fixture must be closed and nonworkshared. Use an existing test model that can safely be copied; never point these commands at a working model or a central/local workshared file.

Create a persistent development certificate once:

```powershell
pwsh -NoProfile -File .\scripts\dev-signing.ps1 -Action setup
pwsh -NoProfile -File .\scripts\dev-signing.ps1 -Action status
```

Setup creates a code-signing certificate named **WLKR LABS RevitThyme Development**. Its private key stays in the current user's Windows certificate store and is nonexportable. The public certificate is trusted in that user's Root and Trusted Publishers stores. Local configuration under `%USERPROFILE%\AppData\Local\RevitThyme\development` records the thumbprint. Keep the certificate and reuse it for later builds; creating a new identity for every build defeats persistent publisher trust. This certificate is for this machine's development builds, not a public publisher identity.

Build, sign and stage a candidate without installing it:

```powershell
.\project.cmd check
.\project.cmd package -DevelopmentSigning
.\project.cmd dev-prepare -FixtureSource C:\Revit\Models\Test\Development-Baseline.rvt -PackageDirectory $package
```

Replace the fixture example with an absolute path to your known disposable file. Set `$package` to the fresh signed package folder printed by `package`. If using another local signing configuration, pass `-DevelopmentSigningConfigPath ABSOLUTE_CONFIG` to `package` and `-SigningConfigPath ABSOLUTE_CONFIG` to `dev-prepare`. The signing script itself accepts `-ConfigPath ABSOLUTE_CONFIG`.

Packaging signs staged RevitThyme binaries and verifies their certificate before computing the package's hashes and manifest. Third-party binaries keep their original signatures. A missing, expired or unexpected certificate stops packaging. Signing changes file contents, so sign before generating integrity hashes. The certificate's private key and local configuration do not enter the repository or package.

`dev-prepare` validates the package, copies the fixture and records their identities in `plan.json` in the printed run folder. It remembers the fixture path locally for later `dev-start` calls. Preparation does not open, save, close or install anything in Revit. Nonworkshared status is checked natively after launch, before operations. Keep the preparation record and package until installation and native checks are complete.

## One-time install and restart

When ready for the installation window, save or otherwise preserve unsaved work and close every Revit process normally. Set `$package` to the exact signed package identified by `plan.json`. For an existing installation using the standard setup receipt, run this in 64-bit Windows PowerShell:

```powershell
$receipt = Join-Path $env:USERPROFILE 'AppData\Local\RevitThyme\setup\receipt.json'
& "$package\install.ps1" -PackageDirectory $package -ReceiptPath $receipt
```

Use the actual receipt path if your installation differs. The installer preserves the earlier receipt and registration in recoverable backups and updates the installed CLI path. Retain the paths it reports. See [installation](INSTALLATION.md) for ownership checks and rollback. Then launch the already prepared run:

```powershell
.\project.cmd dev-start -RunFolder $preparedRun
```

Set `$preparedRun` to the absolute run folder printed by `dev-prepare`. Start refuses a registered host whose package hash or configured signing identity differs from the prepared plan.

Reopen normal work after installation. The existing normal session cannot acquire the new host or trusted-execution setting while it is running. This setup does not close that session for you.

The signed add-in and trusted publisher are intended to remove RevitThyme's recurring publisher prompt. That outcome must be checked in actual Revit after installation. Signing the DLLs does not sign the Windows setup executable or establish trust for unrelated add-ins.

## Daily development

Start a fresh development process:

```powershell
.\project.cmd dev-start
```

To select a different baseline, provide `-FixtureSource ABSOLUTE_RVT`. Each start creates `artifacts\dev\<UUID>` with a private copy of the fixture, session discovery, results and a launch record. Keep the absolute run folder printed by the launcher:

```powershell
$run = 'C:\Revit\RevitThyme\artifacts\dev\RUN_UUID'
.\project.cmd dev-status -RunFolder $run
$query = (Resolve-Path .\examples\development\query.csx).Path
$modify = (Resolve-Path .\examples\development\rename-view.csx).Path
.\project.cmd dev-query -RunFolder $run -Source $query
.\project.cmd dev-modify -RunFolder $run -Source $modify
```

The launcher records existing Revit process/start identities and the registration hash in `launch.json`, then sets these variables only for its new Revit process:

| Variable | Purpose |
| --- | --- |
| `REVITTHYME_TRUSTED_CODE=1` | Enable the existing C# execution host at startup |
| `REVITTHYME_FOUNDATION_FIXTURE` | Bind ordinary operation targeting to this run's fixture |
| `REVITTHYME_FOUNDATION_SESSIONS` | Separate this process's discovery files |
| `REVITTHYME_AGENT_HOME` | Separate built-in Agent state |

The wrapper verifies the recorded PID, process start time, session and fixture before requests, then captures fresh context and supplies a new request ID. Edit the source file and rerun the command to test another method body in the same Revit process. Results retain the request identity and source hash. Source/result evidence is private and can contain local paths or model data.

Do not close and reopen the copied fixture inside the same run. Its document identity would change. Start a new run instead. Renaming or opening another model is not a way to retarget a run.

For an external MCP client, use:

```powershell
.\project.cmd dev-mcp -RunFolder $run
```

This starts the packaged MCP server pinned to the run's exact session. It does not modify global Codex configuration; `dev-status` and `dev-stop` remain available while it runs. Submit operations from one source at a time. Use `get_context` before each trusted request and retain request IDs for recovery, as described in [trusted C# execution](DYNAMIC-EXECUTION.md).

## Two method bodies to try

The checked-in `examples/development/query.csx` contains:

```csharp
return new {
    document = doc.Title,
    viewId = doc.ActiveView.Id.Value,
    viewName = doc.ActiveView.Name
};
```

Run `examples/development/rename-view.csx` only against the copied development fixture:

```csharp
var view = doc.ActiveView;
var before = view.Name;
view.Name = before + " - Dev test";
return new { viewId = view.Id.Value, before, after = view.Name };
```

Run the query, modify, then query again. Confirm the native view name and use Revit's Undo once to restore it. Use an ordinary editable project view; a template, sheet or otherwise restricted active view may reject renaming. The host owns the transaction; do not create a top-level transaction in submitted code.

## Recovery and stopping

If a request times out, inspect its original outcome before sending another modification:

```powershell
.\project.cmd dev-outcome -RunFolder $run -RequestId REQUEST_GUID
```

Omit `-RequestId` to recover the run's outstanding pending request. New submissions are blocked while that request is unresolved. A new request ID can execute the code again. An uncertain result requires inspection before another edit. See [execution recovery](DYNAMIC-EXECUTION.md#trust-bounds-and-recovery) for exact replay and cancellation behavior.

To finish a development run:

```powershell
.\project.cmd dev-stop -RunFolder $run
```

Stop requests a normal close of only the recorded process after verifying its start identity. It does not force termination or dismiss save dialogs. Resolve any save prompt yourself; the run folder, model copy and results remain available. The baseline model and other Revit sessions are preserved.

Trusted execution has the full Revit process's permissions. The fixture checks guard against accidental targeting; arbitrary C# can still access other documents, files and the network. Queries roll back only the current-document transaction. Source is limited to 24 KiB UTF-8 and returned JSON to 8 KiB. Execution budgets are cooperative; they cannot abort an infinite loop or blocked call.

## Qualification

Before installation, source checks, signing verification, package hashes and isolated installer tests establish readiness. They do not establish native Revit behavior.

After the installation window, verify the prepared package in actual Revit:

1. Launch normal and development Revit repeatedly and confirm no RevitThyme publisher prompt.
2. Run two revised method bodies and confirm the development PID/start identity is unchanged.
3. Modify the copied fixture, read back the native result and restore it with one Undo.
4. Confirm stale sessions and wrong document targets are rejected.
5. Confirm trusted execution is disabled in a normal launch.
6. Stop the development process and confirm other processes and model files are preserved.

Retain the preparation, installation and run records with the observed results. General compiled-module reloading is outside this workflow.
