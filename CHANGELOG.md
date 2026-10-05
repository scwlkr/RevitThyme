# Changelog

## Unreleased — 2026-10-05 — Development setup

- Add wstack development standards and WLKR LABS Linear routing while preserving Revit-specific constraints and external TimberFold ownership.
- Scaffold a dependency-free Rust project CLI, connect the existing structural diagnostic with argument forwarding, and add a Windows command launcher.
- Record stack exceptions, missing Rust/Cargo, the setup checker's Windows launcher limitation, and pending CLI/runtime and local CI evidence in SETUP-TODO.md.

- Complete the authorized dependency bootstrap with Rust 1.99.0, rustfmt and clippy; use the existing Visual Studio C++ build tools.
- Repair the owning wstack-setup skill's Windows launcher and permission handling; retain its patch and verify the prior regression now passes.
- Add real CLI boundary checks and local CI with scope routing, aggregate results at a Git SHA, and Cargo cache reuse/invalidation checks.
- Establish `master` as the local default for verified checkpoints.

This setup verifies offline repository automation. Revit host behavior remains planned.

## 0.1.0 — 2026-10-02 — Project foundation

- Establish RevitThyme as a separate local Revit suite/host project.
- Document purpose, host/tool separation, a draft operation interface, build milestones and a second-computer deployment plan.
- Register TimberFold 1.1.0 as an external implementation, referencing its inspected source revision without copying its repository or models.
- Add portable local-source configuration and a read-only project diagnostic.
- Preserve the existing TimberFold installation and Revit model state; this foundation performs no live Revit work.

Host execution, a ribbon, MCP operations and an installer remain planned capabilities.
