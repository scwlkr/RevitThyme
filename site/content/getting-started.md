# Make room for RevitThyme

RevitThyme is a growing Revit tool suite from WLKR LABS. The **v0.2.0 preview** adds three ribbon buttons through a custom pyRevit extension. Its current operations read your model; they do not save, synchronize or change it.

## Before you begin

- Windows and a licensed installation of Revit 2027.
- Independently installed [pyRevit](https://github.com/pyrevitlabs/pyRevit/releases) with IronPython 2.7.
- The development host uses Revit 2027.2 build 27.2.0.39 and pyRevit 6.5.5.26237+2044. Other builds and another-machine behavior remain unverified.

## 1. Download and verify

Get the [v0.2.0 ZIP](https://github.com/scwlkr/RevitThyme/releases/download/v0.2.0/RevitThyme-0.2.0.zip) and [SHA-256 checksum](https://github.com/scwlkr/RevitThyme/releases/download/v0.2.0/RevitThyme-0.2.0.zip.sha256). Compare the downloaded ZIP's hash with the checksum file before extracting.

```powershell
Get-FileHash .\RevitThyme-0.2.0.zip -Algorithm SHA256
```

The ZIP contains the extension and installer. Revit, pyRevit, TimberFold and private source models are supplied separately.

## 2. Install the extension

Extract into a new folder. Open ordinary PowerShell in that folder and run:

```powershell
& .\scripts\install.ps1
```

Load the extension on your next normal Revit start. The installer never saves or closes your model. Close Revit yourself before upgrading or uninstalling. See [Installation & updates](../../docs/DEPLOYMENT.md) for backups and recovery.

## 3. Meet the ribbon

Open **RevitThyme → Suite Status** to see suite and host versions, document identity and readiness.

**Tool Settings** selects an existing external TimberFold folder. **Inspect TimberFold** reports wall and roof candidates and obvious unsupported conditions. Inspection is a first check; it does not extract fabrication geometry or generate a cut kit.

## Where to go next

- [Tools & operations](../../docs/OPERATIONS.md) explains each implemented operation.
- [Troubleshooting](troubleshooting.md) covers a missing ribbon and common setup issues.
- [Roadmap](../../docs/ROADMAP.md) separates working features from upcoming milestones.
- [Contributing](../../CONTRIBUTING.md) shows how to build and check the source.

Ribbon use needs no AI client. A dedicated MCP server, fabrication preview and generation remain planned.
