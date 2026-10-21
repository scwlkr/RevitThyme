# RevitThyme 0.11.0 qualification

Version 0.11.0 uses package `0.11.0-mcp.1`, protocol 13 and 32 MCP tools for full Revit 2027. The release adds exact native view/sheet PNGs, session document discovery, Home-aware background open, explicit activation/save, clean close with explicit active-document handoff and exact view switching. Earlier operations remain available. No posted-command or caller-written callback substitutes for native completion.

## Frozen runtime and source provenance

| Identity | Qualified value |
| --- | --- |
| Native/CLI/MCP runtime source | `8aae9f9c11e9df629cfbb0a033cb9154e9536ddc` |
| Immutable package manifest SHA-256 | `bdfaa317a3b61d9b49277803d33f168b4b3fe0ecdb94ebd2f498a76976056445` |
| Native host | Revit 2027.3, build `27.3.0.28`, Windows x64 |
| Package identity | `0.11.0-mcp.1`, product `0.11.0` |

The runtime was compiled, signed with the existing local development identity, hashed and frozen before native qualification. Later evidence and release-guide commits do not rebuild or replace it. The final installer's `release.json` identifies its clean release-source revision and the same frozen package manifest; adjacent checksums identify the exact setup and both bootstrappers. Compare the source revision with the release tag and the package manifest with the table above. Runtime source and installer/documentation source are deliberately separate identities.

The package's bundled guides preserve their prepublication candidate wording. Current release status and usage are maintained in the canonical [installation](../docs/INSTALLATION.md), [MCP](../docs/MCP.md) and [CLI](../docs/CLI.md) guides. The Windows setup executable is unsigned. Local development signing establishes trust on the qualification machine; it is not a public publisher signature or another-machine qualification.

## Actual Revit results

Every run used newly copied disposable nonworkshared models, exact process/start/session pins and the installed MCP executable through the official external C# SDK. Failed attempts remain retained separately from passing evidence.

| Workflow | Actual result |
| --- | --- |
| Home, close/reopen and view switching | 56 checks; same-path reopen gets a fresh identity, retired/wrong IDs and stale views are refused, explicit save and dirty-close guards pass; four deliberately lost responses recover their original GUIDs. |
| Post-close discovery and native images | 16 checks; only the surviving exact document is discovered, 3D/sheet activation and inline PNG bytes/hashes/dimensions verify, stale image captures are refused. |
| Normal-host edit → see → verify | 59 checks with trusted C# disabled; native height and changed PNGs agree, original values and disk files are preserved, one real Pending precedes original export recovery without rerender. |
| Electrical workflow | 418 assertions in one invocation; bounded duplicate/copy, explicit save/reopen, native identity/type/placement persistence, image capture, stale-ID refusal and original-request recovery pass. Two native warnings require explicit agent intervention; the original GUIDs recover after 22 and 8 Pending observations. |

The Home regression exposed invalid native document hashing during `DocumentClosed`. The fix retires invalid entries by position while preserving native equality for live wrappers. The fixed run's journal contains no closed-handler exceptions. Dedicated cancelled-close qualification retains its earlier build attribution; that probe was not repeated on this package. [Home evidence](ui-lifecycle.md), [image evidence](view-images.md) and [electrical evidence](electrical-lab.md) retain exact host identities and outcomes.

## Source, installer and preservation checks

- Full Release build: zero warnings/errors; 600 unit, 76 MCP, 13 Agent, 6 WPF and 47 development-safety checks.
- Package validation retains the complete native/CLI/MCP/Agent/geometry/DXF payload and previous cohorts; 46 validator checks pass.
- Exact-runtime installer qualification: 20 isolated installer, 25 global-command and 22 setup executable checks. The production candidate installs successfully and refuses installation while actual Revit is open, preserving registration and receipt.
- Source website checks include dependency installation, production build, strict links/anchors, source contracts and metadata. These checks do not publish the website.
- All owned native processes close normally. Original models, untouched handoff copies, unrelated checkout and logo remain unchanged. Earlier immutable bundles and recoverable registration backups remain retained. Only the owned CLI PATH entry changes; unrelated raw entries and `REG_EXPAND_SZ` remain preserved.

Final release-asset checks and public GitHub metadata verification are performed separately against the final installer source and hashes. Source/installer checks do not replace the pinned native proof above.

## Remaining limits

Last-active-document close to Home is refused. There is no automatic save/discard, generic warning handler, SaveAs, workshared/cloud lifecycle support or persistent recovery after native shutdown. Execution budgets cannot forcibly interrupt an in-flight Revit API call. Native modification reporting remains bounded to 512 changed IDs.

Electrical current-source workflow acceptance does not establish historical Fine-detail acceptance. Identical study PNGs do not visually prove annotation copying; native identities, types, placements and saved persistence establish that result.

TimberFold retains its existing four-building digital evidence and original [small physical test](https://github.com/scwlkr/RevitThyme/releases/download/v0.10.0/TimberFold-physical-test.zip). Physical calibration, cutting and assembly remain outstanding; `ManufacturingReady` stays false. This release does not qualify other Revit builds, machines or every supported geometry/view subtype. Website deployment and promotional posts remain separate actions.
