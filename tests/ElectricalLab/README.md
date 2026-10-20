# Electrical-lab native qualification

This opt-in driver uses the official MCP SDK against an existing immutable installed bundle. It starts at Home in one owned Revit process, opens independent copies of an electrical lab and a handoff project, inspects the native baseline, duplicates a Power view without detailing, and separately copies two inspected Generic Annotation instances. The canonical lab views remain comparison targets. Each edit retains the existing 512 changed-ID limit.

The input reference is a private accepted inventory containing four `views` (view/template/source IDs, names, detail, scale and annotation counts), category `counts`, three `schedules` (IDs, names and footer arrays), `wireCount`, `sourceL1LightingAnnotationCount` and a historical non-type `elements` count. Do not commit the model, reference, captures, images or transcripts. This harness is specific to that reviewed fixture; it is not an arbitrary-model automation command.

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

If a model warning or transport failure blocks opening, dependent work stops. Keep the original intent and query its original GUID on the pinned session. A Pending outcome is not terminal; do not retry under a fresh ID, dismiss dialogs automatically, sanitize the reference or substitute an easier fixture to make the run pass. A failed run has no `complete.json`; the normal-close action intentionally refuses it. Diagnose the retained native state before any explicit cleanup.
