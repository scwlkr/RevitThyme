# Deployment plan

## Present setup

This foundation is a local Git repository. It has no installer, remote repository or live RevitThyme host. The project diagnostic reads the sibling TimberFold checkout using config/local.example.json or a machine-specific config/local.json.

TimberFold continues to use its own installed command. Registering its metadata here does not move, install or upgrade it.

## First shared release

Build a versioned package for two computers before office-wide rollout. Include:

- Implemented host/adapter and UI, named operation metadata and help.
- Tested TimberFold code and its dependency lock, with a portable runtime location.
- Explicit Revit/pyRevit/.NET/Python compatibility information based on tested builds.
- Installer diagnostics, an uninstall path, release notes and previous-version recovery.
- An approved small demonstration model and onboarding steps.

Use an allowlist when packaging: approved code, manifests, dependency definitions, docs and demo assets. Keep private house baselines, project output runs, user settings, environments and credentials outside the package. An entire TimberFold repository/archive is not a distribution manifest.

Read-only host calls are the first post-install smoke test. Follow them with a preview and a verified generation in a dedicated demo project. Record the package checksum, source revisions, supported builds and smoke-test report. Confirm the user's intended save operation separately from installation.

## Configuration and credentials

Runtime paths come from local configuration or installer-managed metadata, not a developer's hard-coded drive. Keep live model files and generated runs distinct from installed code. A future tool bundle must include TimberFold's runtime dependencies rather than assume the developer's sibling checkout exists.

MCP is optional for ordinary ribbon use. Each AI user supplies their own approved client configuration and credentials through the secret manager. Keep the existing Routes bridge loopback-only; remote access is a separate design decision requiring authentication and authorization.

## Updates and ownership

Use tested versioned releases and retain the previous working package. Avoid silent updates of active tools while Revit is running. Keep local Git checkpoints now; choose a company-controlled remote when requested.

The redistribution license is not yet selected. Review third-party notices and obligations before a shared release. If pyRevit source is copied or a fork redistributed, its [GPL-3.0 license](https://github.com/pyrevitlabs/pyRevit/blob/develop/LICENSE.rtf) must be accounted for. This foundation contains no copied pyRevit implementation.
