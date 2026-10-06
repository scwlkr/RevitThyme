# Troubleshooting

## The RevitThyme tab is missing

Check pyRevit, installer completion and your per-user Extensions folder. Start Revit normally.

Reload previously crashed the development host with Routes enabled; repeated Reload remains unverified. Use [Installation & updates](../../docs/DEPLOYMENT.md) for recovery.

## Inspect TimberFold says it is unconfigured

Select your separate TimberFold folder in **RevitThyme → Tool Settings**. Candidate counts alone do not establish readiness or fabrication support.

## Can I generate a laser-cut kit?

Use TimberFold's separate installation. RevitThyme v0.2.0 provides inspection only. Suite generation is [planned](../../docs/ROADMAP.md).

## Do I need MCP or an AI account?

No. Use the ribbon. Optional Routes use **127.0.0.1:48884**; standalone MCP is planned.

## Routes is disconnected or returns a wrong-target error

Check the bridge. After restart, reload or document changes, request fresh status and use its exact session/document target. Send requests serially; concurrent clients are unverified.

The optional development-host repair targets a specific pyRevit version and is not bundled. Follow [Installation & updates](../../docs/DEPLOYMENT.md) before applying it elsewhere.

## The installer refuses a destination

Preserve your files. Use ordinary PowerShell and close Revit before code updates. Modified/unmanaged files and redirected package-cache paths are refused. Backups and user settings remain recoverable.

## Report a problem

Search [issues](https://github.com/scwlkr/RevitThyme/issues). Include versions, steps and sanitized diagnostics; remove private paths, models and credentials. Use [Security](../../SECURITY.md) for vulnerabilities.
