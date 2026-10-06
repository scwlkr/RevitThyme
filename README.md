# RevitThyme

![RevitThyme](assets/branding/revitthyme-logo.svg)

A Revit tool suite with a pyRevit extension and a native Visual View Range preview.

**Visual View Range 0.3.0-preview.1** is an unpublished candidate with native capture, interactive sections and reviewed Apply/Undo. Bounded checks 1–10 passed on the current Revit 2027.2 host, including startup/use with pyRevit disabled. Unresolved Above/Below references require named levels. See the [user guide](docs/VISUAL-VIEW-RANGE.md), [release notes](docs/VIEW-RANGE-RELEASE-NOTES.md) and [separate qualification evidence](verification/rust-view-range-m3-remaining.md). The existing tools remain installed; broader suite replacement and second-computer distribution are separate work.

**Version 0.2.0** provides a RevitThyme ribbon, shared read-only operations, a versioned ZIP and per-user installer. This is an independent extension built on pyRevit; no upstream implementation or Revit binaries are bundled.

The visual identity combines thyme leaves, earthy greens and timber colors. Each ribbon button has transparent artwork for light and dark Revit themes. Editable sources are in [branding](assets/branding/README.md).

| Available now | Still planned |
| --- | --- |
| Suite Status: host versions and document identity | Broader native suite migration |
| Native Visual View Range preview: capture, review, Apply and Undo | Second-computer native qualification |
| TimberFold inspection: candidate walls/roofs and source-file diagnostics | Preview-bound fabrication generation |
| Shared ribbon and loopback Routes | Named MCP server and asynchronous jobs |
| Allowlisted ZIP, hashes, backup/upgrade/uninstall tooling | Bundled TimberFold worker and second-computer pilot |

TimberFold generation continues through its separate working installation. Inspection counts do not prove fabrication support.

## Install

Install pyRevit for Revit 2027. Download the ZIP and checksum from [Releases](https://github.com/scwlkr/RevitThyme/releases), verify and extract it. From the extracted folder:

```powershell
& .\scripts\install.ps1
```

Load the extension on a normal Revit start, then open **RevitThyme > Suite Status**. Tool Settings can select your existing TimberFold folder. The first-host cold-start smoke test passed with icons and read-only operations. The development host previously crashed during Reload and uses a separately documented local Routes repair; repeated Reload stability and another-machine behavior remain unverified. See [deployment](docs/DEPLOYMENT.md) for compatibility and recovery.

## Develop

The [native adapter](docs/RUST-VIEW-RANGE-M2.md) provides capture/Apply and ribbon-launched connection in the Rust/Expo/Electron feature. Standalone executable launch retains the offline fixture. [M3](verification/rust-view-range-m3-remaining.md) records bounded actual Revit qualification separately from Windows source/package checks. The portable bundle does not install an add-in automatically.

The [M1 report](docs/RUST-VIEW-RANGE-M1.md) preserves the earlier synthetic offline milestone. Later M2/M3 work adds the native adapter and actual Revit Apply qualification while preserving the shipped pyRevit host.

Clone the source repository for development; the install ZIP omits the developer CLI. Read [PROJECT.md](PROJECT.md), [AGENTS.md](AGENTS.md), [architecture](docs/ARCHITECTURE.md) and [roadmap](docs/ROADMAP.md). Automation uses Windows Python 3.13 and Rust 1.99.0; the extension stays IronPython 2.7 compatible.

```powershell
.\project.cmd check-project
.\project.cmd check-release
.\project.cmd ci --base HEAD^ --require-clean --full
.\project.cmd package --require-clean
```

Full local CI uses external TimberFold configured in config/local.example.json or ignored config/local.json. Public package checks need no TimberFold checkout. Keep private models outside this repository.

Use project.cmd check-live after installing to exercise Routes and identity failure cases. Live reports stay in ignored artifacts; publish only sanitized evidence. See [operations](docs/OPERATIONS.md), [contributing](CONTRIBUTING.md), [security](SECURITY.md) and [changelog](CHANGELOG.md).

## License

GPL-3.0-or-later. See [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.md). Users supply licensed Revit and independently installed pyRevit. TimberFold source and dependencies are not in v0.2.0.
