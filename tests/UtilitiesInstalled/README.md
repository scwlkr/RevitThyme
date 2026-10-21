# Installed native utility qualification

This official-SDK client runs the current installed CLI/MCP and normal native add-in. It needs no test loader, trusted C# or registration swap. It extends the earlier source-built utility qualification with exact floor/RCP readback, template refusal, invalid-order rollback, PNG verification, room dimensions and retained results across MCP reconnections.

Use PowerShell 7 and a **closed disposable utility fixture** created by `tests/RevitIntegration/UtilityFixture.cs` in an earlier isolated `utilities-start` run. Preserve that run's model and receipts. The expected fixture has Utilities Plan, Utilities Ceiling, Template Controlled and Utilities 3D views, three test rooms and the original range settings. An arbitrary working project is not a fixture. No RVT or Autodesk binaries are distributed here.

```powershell
$launch = pwsh -NoProfile -File tests/UtilitiesInstalled/run.ps1 -Action start -FixtureSource C:\absolute\owned-utility-run\Foundation-Test.rvt | ConvertFrom-Json
$run = $launch.folder
pwsh -NoProfile -File tests/UtilitiesInstalled/run.ps1 -Action ready -RunFolder $run
pwsh -NoProfile -File tests/UtilitiesInstalled/run.ps1 -Action check -RunFolder $run
```

Start copies the source into a fresh ignored run folder, records the installed manifest, registration and existing process identities, then launches only its own Revit process with separate discovery/Agent directories. Ready verifies the native PID/start/session, exact copied path, nonworkshared/clean state and unchanged saved files. If startup is not ready, observe the same process; do not start another copy to bypass it.

The check writes intent before each caller-GUID operation, retains terminal receipts and blocks replay when `pending.json` or `complete.json` exists. It does not save. If interrupted, read the original pending GUID and recover through the installed CLI before any further edit:

```powershell
$input = Get-Content "$run\installed-input.json" -Raw | ConvertFrom-Json
$pending = Get-Content "$run\pending.json" -Raw | ConvertFrom-Json
& "$($input.bundle)\cli\revitthyme.exe" get-operation --session $input.session --request-id $pending.requestId --json
```

Retain an uncertain attempt, inspect its actual native state and resolve it before a fresh workflow. Close also refuses while `pending.json` exists, preserving the native session's volatile outcome history. After independently confirming the original terminal outcome and native state, retain those receipts and archive the resolved pending intent rather than deleting its evidence. Do not clear an unresolved or Unconfirmed intent merely to permit shutdown. Reconnecting and reading an already completed outcome does not test a lost response or an in-flight cancellation.

The client pauses at `undo-ready.json`. In the exact owned Revit process, verify the top Undo item is **RevitThyme Room Dimensions** and Undo that one group. Then write an acknowledgement to `$run\undo-done.json`. The client independently verifies that both dimensions disappeared and that the native room preview and original floor range remain intact. This UI action verifies native Undo; toolkit operations themselves use external MCP.

```powershell
pwsh -NoProfile -File tests/UtilitiesInstalled/run.ps1 -Action close -RunFolder $run
# Resolve only this owned copy's save prompt without saving the test edits.
pwsh -NoProfile -File tests/UtilitiesInstalled/run.ps1 -Action close -RunFolder $run
```

Close submits one normal request and never repeats it while outstanding or terminates Revit. After process exit, it verifies source/copy/registration hashes and the pre-existing process identities. All models, PNGs, requests and failed attempts remain private under `artifacts/utilities-installed`. No cleanup deletes their evidence.
