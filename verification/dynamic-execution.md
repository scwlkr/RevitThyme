# C# Dynamic execution — 2026-10-14

Tracking: [WLK-248](https://linear.app/wlkr-labs/issue/WLK-248/complete-the-c-dynamic-execution-stage-with-native-cabin-eval). Scope is only the Dynamic execution row of the complete `2026-10-14_NEW_PLAN.md`, building on completed Native utilities. Built-in Agent and Exploration are deferred.

**Dynamic execution is complete.** Opt-in Roslyn query/modify works through the shared native operation layer, CLI and official-SDK MCP. A disposable native Cabin Eval executes and scores actual results independently. This completes the stage milestone; it does not claim a perfect cabin or autonomous-agent benchmark.

## Source and offline checks

Qualified product/test source: **22385e22cc58ccbadce26e5dd2b018bbef0f6cb3**, branch `codex/wlk-248-dynamic-execution`, building on merged Native utilities at `9bd8c89`. A subsequent documentation-only commit records this acceptance without changing the qualified product/test implementation.

The five C# product projects remain the active architecture. Core adds captured-target/trust/bounds validation; Addin owns Roslyn and synchronous API-thread execution; CLI and MCP call those same operations. Query always rolls back its current-document transaction. Modify commits inside a group, records native DocumentChanged IDs and assimilates one Undo. Results serialize while rollback is possible. Roslyn **5.9.0** is pinned; protocol **4** requires clients/add-in to upgrade together. MCP exposes **18 tools**. Built-in Agent, App Server/sign-in, planning canvases, reflection discovery and Exploration are absent.

Full and portable .NET SDK **10.0.400** builds pass with zero warnings/errors. **96 unit checks and 35 MCP checks pass.** Existing Foundation/generic/utility checks remain passing. New coverage includes host/caller trust, stale targets, byte/budget/argument bounds, exact source replay, conflict rejection, queued cancellation, real Roslyn compilation/execution, mapped diagnostics, unwrapped runtime exceptions, synchronous-body enforcement and official stdio tool/schema/annotation discovery. `project.cmd check` also passes locked dependency restore, PowerShell syntax, five-project C# boundary, no copied Autodesk DLLs and legacy preservation checks.

## Actual Revit

Successful committed-source run: `artifacts/dynamic/live/75335056-1852-4a48-8751-6ea921c5427f`, Revit **27.3.0.28**, owned PID **14824**, start **639275964473687913**, session **ad9c8cd4-aed6-493b-a9f9-9ba1957b3e85**. Prepared bundle/manifest hashes are retained in `plan.json`; no working-model or legacy-bridge fallback occurred.

**24 direct native checks and 26 live official-SDK MCP/CLI checks pass.** Direct tests cover actual compilation, temporary query name edits with detached JSON and rollback, runtime/NaN serialization/oversized result/budget failures, cooperative cancellation, a 513-ID change exceeding the 512-ID limit with verified rollback after commit, and native added/deleted ID reporting. MCP covers query/modify, opt-in refusal, diagnostics, native change reporting, same-ID replay, retained recovery, conflicting source and stale-capture rejection. A real CLI process executes C# and its outcome is recovered through MCP. Independent native inspection verifies that one Undo removes both created walls and the renamed view; one Redo restores them, and another Undo restores the fixture before Cabin Eval.

The original fixture's **2,425 elements** are restored after smoke tests. Its saved SHA-256 remains **CF84CC50D691D3BC81AD5C04351B9ABF531A56725950F8C5E92803ED378EDE2E**. The completed cabin is saved separately as `Cabin-Result.rvt`, SHA-256 **4BB5CC78A05E9175F08F4DB147EBFC57CE0A42660837B3F289FFCF87ACBC33E2**; the original fixture is never overwritten.

### Cabin Eval result

The checked-in construction body executes via actual MCP `run_modify` and creates **325 native elements**, including geometry, types and dependencies. A separately compiled evaluator reads the model, ignoring submitted counts/IDs and descriptions. It sets only the real **Cabin Length** global parameter from 14 to 18 in a temporary transaction group, measures dependencies, commits the probe transaction and rolls back the group. Native IDs, wall bounds and the 14-foot parameter are verified restored.

| Area | Earned / possible |
| --- | --- |
| Geometry | 25 / 25 |
| BIM | 25 / 25 |
| Parametric behavior | 30 / 35 |
| Model integrity | 15 / 15 |
| Total | **95 / 100** |

Actual native walls span 14 x 16 feet at 9-foot height; a floor and two-slope gable roof have physical solids. One hosted door, two distinct hosted window types, two electrical devices with connectors, two distinct appliances and two native dimensions are present. The parameter probe stretches walls, floor, hosted windows and dimensions correctly. **The roof does not follow the changed length**, so that dependency receives zero of five points. The evaluator reports zero model warnings and verifies physical volumes and rollback.

Negative controls reject geometry/BIM/parametric points for the empty fixture and score the temporarily removed-floor model **80/100**, then restore it. Timing is recorded separately: the cabin construction uses **one MCP call, zero execution errors and 4,272 ms**, including compilation and native completion. Its source hash is **2cbb844298f38678abf9ac75a6c3fd174a4a0ceab652c6589bc412c76d76a7e8**. These are deterministic reference-body results, not new external Codex-agent qualification.

Reports: `native-query-result.json`, `ready.json`, `mcp-checks.txt`, `dynamic-mcp-complete.json`, `native-result.json`, `cabin-score.json`, `cabin-negative-score.json`, `cabin-metrics.json`, `cabin-artifact.json`, `launch.json`, `plan.json` and `cleanup.json`.

## Package and preservation

The local **0.7.0-dynamic** package is `packages/RevitThyme-0.7.0-dynamic-a03f9714dc5949bf92d26c69d5e5838c`, with adjacent ZIP/SHA-256. Its ZIP checksum is **ea5790ac9fcc1c207e3d100f4912dc6a2d1cc383de32939b80f2d0863f7c8ea0**. Addin/Core/UI, both compiler DLLs, native dependency metadata and CLI/MCP product binary hashes exactly match the native-qualified bundle. The package retains Microsoft MIT notices and excludes Autodesk DLLs, test loaders, models and legacy runtimes.

**11 isolated installer/upgrade checks pass**, upgrading from the prior 0.6.0 utilities package at `artifacts/foundation/install-test/c18d213e-cc78-40eb-8fa5-b17d76083d87`. They verify installed MCP startup, tamper rejection, immutable reinstall, recoverable unregister and preservation of the old bundle/registration. Actual production installation was not performed.

Cleanup stops only the owned PID/start and removes only the hash-verified qualification registration. Existing process/start and registration hashes are verified unchanged. The working model, legacy installations, all successful/failed fixtures, previous packages and the unrelated untracked logo are preserved. Local delivery only; no push, release publication or default-branch landing. Private delivery metadata is under `artifacts/dynamic/delivery.json`.

## Repairs and limits

Retained failed attempts exposed Revit's required collector filter, native document wrappers requiring Revit equality, an initialized roof mapping output, and an initial 256-ID limit too small for 325 cabin additions. Each repair is covered by the successful native run; failures are not counted as passing. Startup also displayed an existing legacy Native Preview failure; its installation was preserved. The user loaded the unsigned local qualification loader when prompted.

Trusted C# is disabled by default and requires host launch opt-in plus caller acknowledgement. It has full process permissions, with no sandbox or hard interruption of blocked/infinite code. Rollback covers the current document; file/network/UI/other-document effects are outside that guarantee. Source is bounded to 24 KiB, returned JSON to 8 KiB, changes to 512 IDs and the full write response to 32 KiB. Budgets are cooperative. See [guide](../docs/DYNAMIC-EXECUTION.md).

Cabin Eval v1 assumes one axis-aligned cabin and local English Imperial US Autodesk family content. Production worksharing/large projects, other Revit versions, another machine, untrusted isolation, automatic promotion/merge and a signed public installer remain unqualified. The reference roof's failed dependency is an explicit benchmark limitation. No subsequent development stage was started.
