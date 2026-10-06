# Getting started

**v0.2.0 preview** provides three read-only ribbon tools.

## Requirements

- Windows and licensed Revit 2027.
- Independently installed [pyRevit](https://github.com/pyrevitlabs/pyRevit/releases) with IronPython 2.7.
- Tested: Revit 2027.2 (27.2.0.39), pyRevit 6.5.5.26237+2044. Other builds and second-computer use are unverified.

## 1. Download and verify

Download the [ZIP](https://github.com/scwlkr/RevitThyme/releases/download/v0.2.0/RevitThyme-0.2.0.zip) and [checksum](https://github.com/scwlkr/RevitThyme/releases/download/v0.2.0/RevitThyme-0.2.0.zip.sha256). Compare the hash before extracting:

```powershell
Get-FileHash .\RevitThyme-0.2.0.zip -Algorithm SHA256
```

Revit, pyRevit and TimberFold are separate installations.

## 2. Install the extension

Extract to a new folder. Open ordinary PowerShell there:

```powershell
& .\scripts\install.ps1
```

Start Revit normally. Close Revit yourself before updates or uninstalling. See [Installation & updates](../../docs/DEPLOYMENT.md) for recovery.

## 3. Open the ribbon

Open **RevitThyme → Suite Status**.

- **Suite Status**: versions, active document and readiness.
- **Tool Settings**: select your TimberFold folder.
- **Inspect TimberFold**: wall/roof candidates and unsupported conditions.

Inspection does not generate fabrication geometry. Generation runs through TimberFold separately.

## Next

- [Tools & operations](../../docs/OPERATIONS.md)
- [Troubleshooting](troubleshooting.md)
- [Roadmap](../../docs/ROADMAP.md)
