# RevitThyme

A Revit tool suite and execution host, starting with TimberFold.

RevitThyme brings named, repeatable tools to a company ribbon and an MCP interface. The host handles Revit execution and verification; each tool owns its workflow. TimberFold is the first registered tool and continues to live in its existing repository.

## Current state

**Version 0.1.0: project foundation.** This repository contains the project brief, architecture, build roadmap, tool registration and a read-only project check. RevitThyme's host, ribbon and MCP operations are planned, not implemented or installed. TimberFold's existing command remains available through its own installation.

RevitThyme is an add-in/tool-suite project for Autodesk Revit, not a modified distribution of Autodesk Revit. No upstream source has been copied into this repository.

## Start here

- [PROJECT.md](PROJECT.md): accepted direction, current evidence and next work.
- [Architecture](docs/ARCHITECTURE.md): host, tools, MCP and the draft operation interface.
- [Roadmap](docs/ROADMAP.md): small builds and their completion criteria.
- [TimberFold](tools/timberfold/README.md): existing implementation and integration gaps.
- [Deployment](docs/DEPLOYMENT.md): future installation and the first two-computer pilot.
- [AGENTS.md](AGENTS.md): instructions for development in this repository.
- [CHANGELOG.md](CHANGELOG.md): version history.

## Check this checkout

From PowerShell in `C:\Revit\RevitThyme`:

```powershell
.\project.cmd check-project
```

The Rust CLI requires an existing Rust/Cargo toolchain. Use `.\project.cmd --help` for commands and `.\project.cmd doctor` for prerequisites. The Unix-shell spelling is `./project check-project`; Revit and the diagnostic require Windows. The CLI is scaffolded and its runtime verification is pending; see [SETUP-TODO.md](SETUP-TODO.md) for the prerequisite and bootstrap diagnostic.

The diagnostic reads project metadata and the external TimberFold checkout. It does not connect to Revit, run TimberFold, install dependencies or modify source files. Passing it confirms the foundation's files and local source references; it does not confirm a working RevitThyme host.

`config/local.example.json` resolves TimberFold as `../TimberFold`, relative to this project root. For a different location, copy it to `config/local.json` and edit the tool root. The local file is ignored by Git. The checked-in example contains no credentials.

## Repository layout

| Location | Purpose |
| --- | --- |
| `suite.json` | Product identity, foundation version and tool registry |
| `config/` | Machine-specific source locations |
| `docs/` | Architecture, roadmap and deployment plan |
| `tools/timberfold/` | TimberFold registration and integration notes |
| `src/` | Intended implementation layout; no host implementation yet |
| `scripts/` | Read-only project diagnostics |
| `verification/` | Explicitly scoped verification records |

## Continue with Codex

Open `C:\Revit\RevitThyme` as a project folder and start with:

> Continue RevitThyme. Read PROJECT.md and AGENTS.md, run the project check, and inspect the current source before changes. Build the next roadmap milestone: [milestone]. Keep TimberFold's working installation and source artifacts intact.

The first implementation milestone is read-only host status and TimberFold inspection. A native C# host pilot will then be compared with the current pyRevit adapter using the same operation interface.
