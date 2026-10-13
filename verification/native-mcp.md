# Native MCP inspection milestone - 2026-10-13

Work: [WLK-238](https://linear.app/wlkr-labs/issue/WLK-238/build-native-mcp-model-inspection-and-project-health-milestone). Baseline: `6104656081738e8ff81d51f065d23e4f75a34252`. Local branch: `codex/WLK-238-native-mcp-inspection`. No push, default-branch landing, native installation or new actual-Revit qualification is claimed.

The implementation and limits are in [NATIVE-MCP.md](../docs/NATIVE-MCP.md). It preserves the installed native bundles, the working Revit process and unrelated logo. The new server/lab adapter are version 0.2.0; shared ribbon adapter source is 0.2.7. Protocol 2 gains optional inspection payloads, omitted from existing requests/replies. Use a matched bundle for new tools; older hosts do not implement them.

## Source and Windows offline proof

- Revit executable/SDK file version read as `27.3.0.28`. The lab adapter compiles against that installed SDK with zero warnings/errors. Compilation is not Revit execution.
- `project check-revit-lab`: Rust formatting, clippy, unit/domain checks, native guard checks and 35 Windows MCP observations. The driver traverses stdio, Windows Credential Manager, the actual named pipe and the serialized owner-thread queue. Its host is an offline fixture, not Revit.
- Independent fixture findings: three missing door marks, one unplaced room (a separate placed zero-area room is excluded), one missing view template, two duplicate view-name rows (a template and a sheet title are excluded), two duplicate sheet-number rows and one warning.
- Exact counts survive sample limits; incomplete scans and a 2,000-row cap report lower bounds. Unicode byte-limit pages retain continuation, missing 64-bit IDs are reported, oversized rows fail without disconnecting, malformed JSON recovers and wrong targets fail before fact reads. No inspection transaction opens.
- Sustained read traffic preserves a prior verified Apply outcome and does not create another transaction. The regression failed before receipt eviction with `Repeated inspection must not exhaust retained mutation outcomes`, then passed after the fix. Existing View Range confirmation/readback/duplicate-request tests remain intact.
- The first expanded transport check failed on the unimplemented tool catalog before implementation. The retained fail/pass reports live under `artifacts/revit-lab/offline`; reports include raw context/health observations. No credentials are recorded.
- `project check-project` passes the structural diagnostic; TimberFold is only inspected as an existing external reference. No TimberFold integration/generation change is made.

Final aggregate CI is retained in `artifacts/native-mcp/ci.json` with its exact SHA/base, commands, cleanliness and result. The main checkout's unrelated untracked logo is preserved; use a temporary clean local checkout for `--require-clean`. Read the aggregate result before treating CI as passed. CI source/package/UI checks remain separate from actual Revit proof.

## Review and remaining gates

Separate Standards and Spec passes inspect the task diff against the supplied session instructions, Wstack rules and issue acceptance. Fixes include exact-ID continuation, excluding valid repeated sheet titles and protecting Apply outcomes under read traffic. Final review/evidence is retained under `artifacts/native-mcp`.

The new `project revit-lab inspect` driver is prepared for later actual-Revit qualification. It records raw observations, matches immutable bundle hashes, checks known fixture levels/plan, exact/missing ID lookup, health, wrong-document refusal and unchanged modified state/file hash. It has not been run inside Revit in this milestone. The empty fixture cannot qualify native door/room/family parameter mapping or large-model performance.

The supplied session instructions require exact-step approval before registration/code installation or fixture creation/change. A newly prepared read-only bundle can be reviewed without installing it. Wider production-model scope, an in-product health/activity UI, editing/export/family tools, multi-instance/second-machine proof and default-branch landing remain separate gates.
