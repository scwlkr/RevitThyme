# Native parameter identity fixture

These helpers qualify the existing typed inspector in one disposable Revit 2027 development copy. They are not packaged product tools. Use the signed candidate and pinned run from [development setup](../../docs/DEVELOPMENT.md). Never use a working model. Compiled host changes must already be installed; scripts against an older host do not qualify a candidate.

`prepare.csx` creates one small Generic Model family/instance with an ordinary nonshared instance field, built-in Comments, two shared Comments fields with known different GUIDs separated by 22 filler definitions, assigned/unassigned/zero/empty cases and a 600-character string. Scratch RFA/shared definitions stay beside the copied RVT; the previous shared-parameter-file setting is restored. Run preparation once in a fresh copied project, using the host-owned transaction. It saves only the generated scratch family; it does not save the project.

```powershell
$run = 'C:\absolute\artifacts\dev\RUN_UUID'
$prepare = (Resolve-Path .\tests\ParameterIdentity\prepare.csx).Path
.\project.cmd dev-modify -RunFolder $run -Source $prepare -BudgetMilliseconds 30000 > "$run\fixture-info.json"
if ($LASTEXITCODE) { throw 'Inspect retained outcome before continuing.' }
pwsh -NoProfile -File .\tests\ParameterIdentity\collect-oracle.ps1 -RunFolder $run -Stage before
dotnet run --project tests/Mcp -c Release -- --parameter-identity-live $run inspect
pwsh -NoProfile -File .\tests\ParameterIdentity\collect-oracle.ps1 -RunFolder $run -Stage after-inspection
```

Compare before/after-inspection native raw digest, all parameter IDs, element count, snapshot revision/modified state and saved RVT hash. Each oracle page must have the same raw digest/count. The oracle reads the native API directly and pages its results beneath the trusted-query 8 KiB bound. CLI/MCP calls are sequential and pinned to the exact process/start/session/document and installed candidate. Inspection does not need trusted C#; these scripts only build and independently evaluate the test fixture.

```powershell
dotnet run --project tests/Mcp -c Release --no-build -- --parameter-identity-live $run edit
pwsh -NoProfile -File .\tests\ParameterIdentity\collect-oracle.ps1 -RunFolder $run -Stage after-edit
```

Compare the direct native fields: only `identity-edit-ready.json`'s selected local parameter ID may change, to its exact requested raw value. Element count, other fields and saved file hash must remain unchanged. The edit stage separately checks read-only/truncated rejection, stale expected values and stale captures. Do not run it again while the edit remains applied.

```powershell
$undo = (Resolve-Path .\tests\ParameterIdentity\undo.csx).Path
.\project.cmd dev-query -RunFolder $run -Source $undo
pwsh -NoProfile -File .\tests\ParameterIdentity\collect-oracle.ps1 -RunFolder $run -Stage after-undo
dotnet run --project tests/Mcp -c Release --no-build -- --parameter-identity-live $run undo
```

`undo.csx` posts exactly one native Undo and records the subsequent `DocumentChanged` transaction name/operation. It refuses to post again if that event file already exists. Confirm `TransactionUndone` for `RevitThyme Set Parameters`, then compare native raw digest and every value/identity with the original oracle. Retain all results, actual Revit build, loaded candidate hashes and exact product SHA. Restore or discard only the owned disposable model and close only its recorded process through `dev-stop`; preserve the baseline and working sessions. No merge or release is performed by these helpers.
