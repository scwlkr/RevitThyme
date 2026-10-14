# Native MCP inspection milestone - 2026-10-13

Work: [WLK-238](https://linear.app/wlkr-labs/issue/WLK-238/build-native-mcp-model-inspection-and-project-health-milestone). Baseline: `6104656081738e8ff81d51f065d23e4f75a34252`. Local branch: `codex/WLK-238-native-mcp-inspection`. Native installation and isolated actual-Revit read-only qualification are complete. No push or default-branch landing is claimed.

The implementation and limits are in [NATIVE-MCP.md](../docs/NATIVE-MCP.md). It preserves the installed native bundles, the working Revit process and unrelated logo. The new server/lab adapter are version 0.2.0; shared ribbon adapter source is 0.2.7. Protocol 2 gains optional inspection payloads, omitted from existing requests/replies. Use a matched bundle for new tools; older hosts do not implement them.

## Source and Windows offline proof

- Revit executable/SDK file version read as `27.3.0.28`. The lab adapter compiles against that installed SDK with zero warnings/errors. Compilation is not Revit execution.
- `project check-revit-lab`: Rust formatting, clippy, unit/domain checks, native guard checks and 35 Windows MCP observations. The driver traverses stdio, Windows Credential Manager, the actual named pipe and the serialized owner-thread queue. Its host is an offline fixture, not Revit.
- Independent fixture findings: three missing door marks, one unplaced room (a separate placed zero-area room is excluded), one missing view template, two duplicate view-name rows (a template and a sheet title are excluded), two duplicate sheet-number rows and one warning.
- Exact counts survive sample limits; incomplete scans and a 2,000-row cap report lower bounds. Unicode byte-limit pages retain continuation, missing 64-bit IDs are reported, oversized rows fail without disconnecting, malformed JSON recovers and wrong targets fail before fact reads. No inspection transaction opens.
- Sustained read traffic preserves a prior verified Apply outcome and does not create another transaction. The regression failed before receipt eviction with `Repeated inspection must not exhaust retained mutation outcomes`, then passed after the fix. Existing View Range confirmation/readback/duplicate-request tests remain intact.
- The first expanded transport check failed on the unimplemented tool catalog before implementation. The retained fail/pass reports live under `artifacts/revit-lab/offline`; reports include raw context/health observations. No credentials are recorded.
- `project check-project` passes the structural diagnostic; TimberFold is only inspected as an existing external reference. No TimberFold integration/generation change is made.
- Configuration rotation tests preserve unrelated TOML settings and native enabled/disabled state while rejecting an unrelated previous target. The installer accepts an exact stopped prior run from another checkout and uses the parsed candidate. External client updates verify the active process, native/wire source, bundle and registration, retain immutable artifacts and back up the exact previous configuration.
- The native lab packages a distinct `RevitThyme.LabCore` assembly compiled from the same core source. The Windows offline driver checks its actual loaded identity, preventing CLR binding to an older installed preview core. The new lab also ran successfully with the installed 0.2.6 native preview in actual Revit.

Final aggregate CI is retained in `artifacts/native-mcp/ci.json` with its exact SHA/base, commands, cleanliness and result. The main checkout's unrelated untracked logo is preserved; use a temporary clean local checkout for `--require-clean`. Read the aggregate result before treating CI as passed. CI source/package/UI checks remain separate from actual Revit proof.

## Actual Revit proof

Run folder: `C:\Revit\RevitThyme\artifacts\revit-lab\dd494450-610b-4a46-9de7-290a2d43397b`. Native bundle source: `ce3fb0aeaf90357cb8dd887f8bb628a8b24f6a12`. External inspection client source: `b72384b5fa3265cf7f1cd60f58054d5ad06293e1`. Subsequent evidence-only documentation changes do not rebuild these artifacts.

- Installed only the separate per-user Lab manifest and exact `revitthyme_test` connection, retaining manifest/configuration backups. The user selected Always Load. Test process PID `35524`, start ticks `639275219405343308`, session `02febe34-fcdc-410a-835b-a8dea289704d`; working process PID `44244` remained running and received no model operations.
- The native startup created, saved and reopened only the new disposable `RevitThyme-Test.rvt`. Active plan: `RevitThyme Lab Plan`. Runtime evidence confirms Revit `27.3.0.28`, no pyRevit assemblies, and the preserved installed 0.2.6 preview. Apply remains disabled.
- `project revit-lab verify <run>` passed eleven read-only checks at native/client `ce3fb0a`: exact target, 12 x 1 x 9 ft solid, native range 8/4/0/-1 ft, metric conversion, shared Rust preview/native proposal and stale/wrong-target rejection. Report: `live-readonly.json`.
- First `inspect` passed native context, levels, plan and ID checks but failed health: the default floor/ceiling/structural plans legitimately share the name Level 1. Preserved report: `live-inspection-first.json`. An expanded offline regression failed before grouping duplicate names by view kind (`offline/94e98813-734c-461e-b0c5-1d94113448f9/report.json`) and passed afterward (`offline/5d93cdc0-bf0b-4002-ba4f-b73ac633eead/report.json`).
- `project revit-lab upgrade-mcp <run>` installed client `b72384b` while retaining the exact native session/document/binding and original bundle. Receipt: `mcp-upgrade.json`. Client SHA-256: `e75f6e29297acd35b09063d76367296325e9b24e64445deb321835120e0e1510`. No native registration change or Revit restart occurred.
- Updated `inspect` passed ten actual-Revit checks: eight-tool read-only discovery, exact unmodified document context, two known levels, known template-eligible plan, three default view kinds sharing a valid name, exact/missing 64-bit IDs, independently expected health, wrong-document rejection and unchanged target/modified state/saved bytes. Report: `live-inspection.json`, retaining raw observations and separate native/client SHAs.
- Health covered six views and empty sheets/doors/rooms/warnings: four eligible views lack templates, other five check counts are zero, and all coverage is complete for this fixture. This verifies the small fixture, not native populated category mapping or performance.
- Saved fixture SHA-256 remained `9171d0de69440aba1a88887cd3925a37f6d82750bf9e8df0f4336fde9ceddfa3`. Inspection opened no transaction. No save/sync/close or editing occurred in the working process.

## Review and remaining gates

Separate Standards and Spec passes inspect the task diff against the supplied session instructions, Wstack rules and issue acceptance. Fixes include exact-ID continuation, excluding valid repeated sheet titles, comparing view names within view kinds, preserving native enabled/disabled state and protecting Apply outcomes under read traffic. Final review/evidence is retained under `artifacts/native-mcp`.

The fixture cannot qualify native door/room/family parameter mapping or large-model performance. Plan levels currently use `Element.LevelId`, which is empty on these native plans; `ViewPlan.GenLevel` mapping remains a populated-fixture follow-up. Same-kind near-duplicate name and sheet-number rules have offline coverage; this actual Revit fixture does not contain those findings.

The user approved the exact installation/fixture plan on 2026-10-13 and requested proceeding with development, installations and disposable qualification without additional approval. AGENTS.md records that standing authorization. Wider production-model scope, an in-product health/activity UI, editing/export/family tools, multi-instance/second-machine proof and default-branch landing remain separate gates.
