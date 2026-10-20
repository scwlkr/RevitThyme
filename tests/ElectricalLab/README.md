# Electrical-lab native qualification

This opt-in driver uses the official MCP SDK against an existing immutable installed bundle. It starts at Home in one owned Revit process, opens independent copies of an electrical lab and a handoff project, inspects the native baseline, duplicates a Power view without detailing, and separately copies two inspected Generic Annotation instances. The canonical lab views remain comparison targets. Each edit retains the existing 512 changed-ID limit.

The input reference is a private accepted inventory containing four `views` (view/template/source IDs, names, detail, scale and annotation counts), category `counts`, three `schedules` (IDs, names and footer arrays), `wireCount`, `sourceL1LightingAnnotationCount` and a historical non-type `elements` count. Do not commit the model, reference, captures, images or transcripts. This harness is specific to that reviewed fixture; it is not an arbitrary-model automation command.

The reference may describe an earlier saved revision. Current-source qualification keeps its identities, annotation counts, model quantities and nonempty schedule totals as hard guards, reports historical detail/footer/membership differences, and freezes the complete current native inventory before editing. Current raw settings, footers and original membership must remain unchanged. A passing current-source loop does not certify historical fixture acceptance or restore older user work.

Run in PowerShell 7 from the repository root, supplying existing absolute paths:

```powershell
$plan = .\tests\ElectricalLab\run.ps1 prepare -FixtureSource $electricalLab -HandoffSource $handoffProject -ReferencePath $acceptedInventory | ConvertFrom-Json
.\tests\ElectricalLab\run.ps1 start -RunFolder $plan.folder
# After normal startup has created the isolated session binding:
.\tests\ElectricalLab\run.ps1 ready -RunFolder $plan.folder
dotnet run --project tests/Mcp -c Release -- --electrical-lab-live $plan.folder
# Inspect retained native inventories and exported PNGs before accepting the run.
.\tests\ElectricalLab\run.ps1 close -RunFolder $plan.folder
```

The launcher does not install or register anything. Startup opts only its child into full-trust C# execution, isolates session discovery and deliberately omits the single-fixture guard to permit typed A/B lifecycle tests. Every submitted C# body instead enforces the exact owned PID/start, active A path and A/B document membership. Never use a working process or model as a fallback.

Open, activate, view switching, image export, save and close use typed MCP operations. C# bodies only inspect or perform bounded model transactions under the host's transaction ownership. Results retain original request GUIDs, submitted source hashes, native additions and private images. Save is explicit and limited to A; the test closes/reopens A and verifies persisted study/annotation identities, then closes B. Completed edit/save outcomes are recovered after reconnect without claiming a new interrupted-request experiment.

If a submitted request returns Pending or Unconfirmed/transport_lost, the client retains its original intent/response and polls only that GUID through an independent SDK observer for at most three minutes. Pending response IDs are observations, not replacement operation IDs. Recovery preserves the full MCP reply, including image blocks. It never captures or resubmits an operation. Native failures, unknown outcomes and an expired recovery window stop dependent work.

Inspect any warning in the exact owned Revit process and explicitly resolve its known choice before continuing; the harness does not dismiss dialogs. Do not sanitize the reference or substitute an easier fixture. A failed run has no `complete.json`; the normal-close action intentionally refuses it. Diagnose the retained native state before any explicit cleanup.

The read-only observer reconnects twice through the official SDK, records original-GUID outcomes and preserves the original transcript:

```powershell
dotnet run --project tests/Mcp -c Release -- --electrical-lab-observe $plan.folder $originalRequestGuid
```

For the specific interruption before the first inventory, explicitly inspect and resolve the warning in the owned process, then continue with the original activation GUID:

```powershell
dotnet run --project tests/Mcp -c Release -- --electrical-lab-resume $plan.folder $originalActivationGuid
```

Resume requires exact retained activation completion and byte-identical recovery of both original open receipts, followed by fresh clean A context and A/B-only membership. Pending stops before context or dependent operations. It writes a separate transcript. This narrow entry point cannot resume later interrupted edits or saves; those require inspection of their original outcomes and native state.

The electrical-lab qualification also retains these fixture-specific diagnostic boundaries:

```powershell
# Temporary duplicate in a rollback-only query; this is not postcommit proof.
dotnet run --project tests/Mcp -c Release -- --electrical-lab-duplicate-diagnostic $plan.folder
# Directly inspect every Added identity of the sole original Applied duplicate.
dotnet run --project tests/Mcp -c Release -- --electrical-lab-inspect-applied $plan.folder $originalDuplicateGuid
# Continue that already-applied duplicate only before any subsequent writes.
dotnet run --project tests/Mcp -c Release -- --electrical-lab-continue-applied $plan.folder $originalDuplicateGuid
# Finish only the sealed saved/reopened workflow after recovering its original activation.
dotnet run --project tests/Mcp -c Release -- --electrical-lab-finish-reopen $plan.folder $originalReopenedActivationGuid
```

These modes reconstruct exact prior intents, bodies, results and hashes; they reject a different workflow boundary. Direct postcommit reads classify all native additions before permitting collector exclusions. Revit can create an internal unplaced Viewport absent from the general collector: native change accounting remains authoritative, and collector membership is checked separately. The saved-reopen tail verifies companion UIDs/roles/viewport associations and annotation identities after actual reopening. Prior files must retain their hashes before completion is recorded.

Inspect the PNGs as images, not just successful tool replies. In the qualified source revision the empty, copied and reopened study PNGs are identical; they establish exact-view export but do not visually prove the annotation copy. Native IDs, types, points and transform basis provide that proof. See [observed results and limits](../../verification/electrical-lab.md).
