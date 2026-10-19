# Contributing to RevitThyme

Help with a reproducible bug, clearer guide, offline test or focused native tool. Public GitHub issues and pull requests are the contribution workflow. No Linear account, AI subscription or maintainer's local folders are required.

Read the [code of conduct](CODE_OF_CONDUCT.md), [scope](PROJECT.md) and [architecture](docs/ARCHITECTURE.md). Discuss substantial features in a [public issue](https://github.com/scwlkr/RevitThyme/issues/new/choose) before implementation. Small fixes can go straight to a PR. Review times depend on maintainer availability.

## Choose your setup

| Contribution | Requirements | Check |
| --- | --- | --- |
| Docs and website | Git, Node.js 22+ (24 in CI), npm | In `website`: `npm ci`, `npm run build`, `npm run check` |
| Core, CLI, MCP, Codex protocol and WPF | Windows, PowerShell 7, .NET SDK from `global.json` | `project.cmd check -Portable` |
| Native add-in and live behavior | Above plus licensed full Revit 2027 and local API assemblies | `project.cmd check`, then disposable native qualification |

Fork on GitHub, clone your fork and create a focused branch. Maintainers normally use `codex/<purpose>`; contributors may use their own names. Do not commit credentials, models, session captures or Autodesk binaries. `legacy` is preserved reference material outside the current build.

## Documentation

Edit root Markdown, `docs/` or `verification/` directly. These are the website's canonical sources. Never edit `website/.generated` or `website/static/generated`. In `website`, run `npm start` for live editing; original Markdown changes regenerate automatically. Production builds exercise search and strict links.

[Website maintenance](docs/WEBSITE.md) covers navigation, metadata, CI, drift checks and deployment review. Documentation-only changes do not need live Revit tests.

## Code

1. Reproduce with a small public example or offline adapter.
2. Put typed contracts and shared behavior in `RevitThyme.Core`. Keep Autodesk access in `RevitThyme.Addin`, inside API callbacks. WPF, CLI and MCP adapt the same operations.
3. Add meaningful coverage for behavior or failure paths. Run `project.cmd check -Portable`, the hosted Windows CI route. `project.cmd test` runs the unit executable.
4. For native behavior, run `project.cmd check` and follow [isolated development](docs/DEVELOPMENT.md). Reuse a verified pinned development session or launch `project.cmd dev-start`. Never close/restart working sessions or use a working model as fallback.
5. Update user guides, interface references and changelog when behavior changes. MCP inventory and release metadata are checked against source by website CI.

`project.cmd build` builds against local Revit 2027. Use `-p:RevitApiPath=ABSOLUTE_PATH` for another local SDK folder. Keep Autodesk binaries outside source and distribution.

Choose the relevant [utility](docs/NATIVE-UTILITIES.md#qualification), [MCP](docs/MCP.md#repeatable-qualification), [execution](docs/DYNAMIC-EXECUTION.md) or [Agent](docs/AGENT.md#development-and-qualification) qualification. Record native readback, rollback or Undo and preservation of unrelated sessions. CI does not certify actual Revit behavior.

## Submit a pull request

Describe the problem, resulting behavior, checks and limits. Separate offline checks, packaging and actual-Revit evidence. Sanitized screenshots help UI review. Keep changes focused.

Maintainers review correctness, public contracts, model effects, recovery and docs. Applicable CI must pass; native qualification and public release are separate steps. See [governance](GOVERNANCE.md).

Contributions use **GPL-3.0-or-later**. There is no separate contributor license agreement.
