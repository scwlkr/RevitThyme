# A clear path back to work

## The RevitThyme tab is missing

Check that pyRevit is installed for your Revit 2027 host, that the installer completed successfully, and that the extension is in your per-user pyRevit Extensions folder. Load the extension on a normal Revit start.

The first development host passed a cold-start smoke test. Manual Reload previously crashed that host with Routes enabled; repeated Reload stability is still unverified. Prefer normal startup and read [Installation & updates](../../docs/DEPLOYMENT.md) before recovery.

## Inspect TimberFold says it is unconfigured

Choose your independently maintained TimberFold folder through **RevitThyme → Tool Settings**. The v0.2.0 download does not contain TimberFold. Inspection can report model candidates without a configured folder, but that does not establish tool readiness or fabrication support.

## Can I generate a laser-cut kit?

Generation continues through TimberFold's separate working installation. RevitThyme v0.2.0 integrates read-only inspection. Preview, generation and verification through the suite are planned; see the [Roadmap](../../docs/ROADMAP.md).

## Do I need MCP or an AI account?

No. The ribbon is the in-product interface. Optional named Routes run on **127.0.0.1:48884**. A standalone MCP server is planned.

## Routes is disconnected or returns a wrong-target error

Check the local bridge first. Request a fresh status result after a Revit restart, pyRevit reload or document switch. Reuse the exact returned session/document target for inspection. Send requests serially; concurrent clients remain unverified in this preview.

The development-host Routes repair is an optional, backed-up source maintenance procedure for the observed pyRevit version. It is not installed by the release ZIP. Follow [Installation & updates](../../docs/DEPLOYMENT.md) instead of applying it to an unknown version.

## The installer refuses a destination

The installer refuses modified or unmanaged files, running-host code updates and redirected package-cache destinations. Preserve your files and run installation from ordinary PowerShell. It retains recoverable code backups and user settings on uninstall.

## Report a problem

Search [existing issues](https://github.com/scwlkr/RevitThyme/issues) first. Include the suite version, Revit build, pyRevit version, steps and sanitized diagnostics. Remove private paths, models and credentials. See [Security](../../SECURITY.md) for security reports.
