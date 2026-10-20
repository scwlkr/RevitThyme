# Project overview

RevitThyme is an open-source native toolkit for full Revit 2027. Ribbon tools, CLI, a 31-tool source MCP server and the optional built-in Agent share C# operations and a native execution host. Image export and explicit document lifecycle are unreleased (`0.10.0-lifecycle.3`, protocol 12). Published version 0.10.0 remains at protocol 8 and the qualified `0.10.0-timberfold.4` package, without typed image export.

## Current scope

Available: view/element/parameter inspection, checked renaming, atomic parameter edits, View Range, rectangular Room Dimensions, TimberFold cutting kits and native review documentation, opt-in trusted C#, dockable Codex Agent, per-user installation and update checks. TimberFold's seven shared controls include explicit read-only reconciliation. Its digital qualification is complete; physical fabrication remains outstanding and manufacturing readiness stays false. See [features](FEATURES.md), [roadmap](docs/ROADMAP.md) and [changelog](CHANGELOG.md).

Six C#/.NET 10 product projects live under `src`, with WPF presentation and separate test projects. See [architecture](docs/ARCHITECTURE.md). Proprietary Autodesk binaries are supplied locally and excluded from distribution. Native tools need no Rust, Electron, pyRevit or AI account.

This is pre-1.0 software. Protocol changes require coordinated client/add-in upgrades. Native qualification is scoped to recorded revisions, hosts and fixtures; [verification](verification/README.md) separates this from CI.

## Project workflow

[Contributing](CONTRIBUTING.md) covers public setup and review. `project.cmd help` owns the command inventory. [Isolated development](docs/DEVELOPMENT.md) protects working models. Public GitHub issues and PRs are sufficient; maintainer Linear tracking is supplementary.

The [October 14 plan](2026-10-14_NEW_PLAN.md) is a historical design input. Completed milestones and later improvements are reflected in current guides. [Legacy sources](legacy/README.md) and their evidence remain historical, outside the current build.

[Website maintenance](docs/WEBSITE.md) describes shared content generation and drift checks. [Governance](GOVERNANCE.md) records decisions and release responsibility.
