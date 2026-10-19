# First steps

Use a disposable project while exploring. You need Windows x64 and full Revit 2027; native tools need no AI account.

## 1. Install and confirm

Preserve unsaved work, close Revit normally and follow [installation](INSTALLATION.md). Open a new PowerShell window:

```powershell
rthyme --version
```

Output distinguishes the executing CLI from the registered installation. Launch Revit and find the **RevitThyme** tab. If either is missing, see [troubleshooting](TROUBLESHOOTING.md).

## 2. Inspect a view

Open a disposable project and an ordinary plan view. Choose **Inspect View** on the RevitThyme ribbon. Confirm that the displayed document and view match your project. This is a read-only connection check.

![Native Inspect View showing document and view context](images/foundation-wpf.png)

Success means the correct native context is displayed. Installation alone does not qualify every feature on your machine.

## 3. Try a native utility

For **View Range**, open a floor, ceiling or structural plan and select **RevitThyme → Views → View Range**. Refresh and inspect the four planes and preview. Close without applying to explore safely. On the disposable model, change a valid offset, apply, inspect the native range and use Revit Undo once to restore it. Template-controlled ranges block edits.

For **Room Dimensions**, use a floor plan with an enclosed rectangular room bounded by four straight basic walls. Select **RevitThyme → Annotation → Room Dimensions**, choose the room and Preview. Create only after reviewing the pair. See [geometry and clearance limits](NATIVE-UTILITIES.md#room-dimensions).

![View Range controls and section preview](images/native-view-range.png)

## 4. Choose another interface

- For scripts: use the [CLI](CLI.md).
- For an external agent: connect the [local MCP server](AGENT-INTEGRATION.md).
- Inside Revit: open the optional [Agent panel](AGENT.md).

Typed operations do not save, synchronize or close models. Trusted C# is a separate opt-in with broader process permissions. Review [security](../SECURITY.md) before enabling it.
