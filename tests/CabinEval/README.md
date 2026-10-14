# Cabin Eval v1

The Dynamic execution acceptance benchmark is independent C# inspection of an actual disposable Revit project, not a score supplied by the construction script. The fixture contract is one cabin with wall centerlines spanning 14 feet in X and 16 feet in Y, four straight 9-foot basic walls, a native floor and gable footprint roof, one hosted door, two hosted windows of different types, electrical devices with native connectors, two distinct appliances, associative dimensions and a nonreporting **Cabin Length** global length parameter driving a dimension. This first rubric is axis-aligned and intentionally reproducible; arbitrary site orientation, family artistry and production design quality are not scored.

`build-cabin.csx` is a trusted reference construction body. It runs through MCP `run_modify`, using Autodesk's locally installed English Imperial US door/window/electrical/appliance content. Content must exist locally; no Autodesk files are checked in, downloaded or packaged. The model is the output artifact; the construction script's returned IDs/counts do not influence the score. This baseline is deterministic execution qualification, not an autonomous-agent capability claim.

`CabinEvaluator.cs` compiles into the separate Revit integration test loader. It has no reference to the submitted body or execution output. It scores actual API values:

| Area | Points | Native checks |
| --- | --- | --- |
| Geometry | 25 | 14 x 16 centerline rectangle/9-foot height (10), floor area (5), two equal opposing roof slopes/solid (10) |
| BIM | 25 | Basic walls, hosted door, distinct hosted window types, electrical connectors, distinct appliances with solids (5 each) |
| Parametric | 35 | Driving global length parameter (5), wall stretch (10), floor (5), roof (5), hosted windows (5), native dimension response (5) |
| Integrity | 15 | No warnings (5), positive physical volumes (5), probe rollback restores IDs/bounds/parameter (5) |

The evaluator sets only Cabin Length from 14 to 18 inside a temporary native transaction group. It neither edits geometry directly nor reruns a construction routine to simulate parametric behavior. It commits the probe transaction, rolls back the group and verifies restored element IDs, wall extents and parameter value. A failed dependency earns zero for that item; it is not hidden by a passing total. Reports contain per-item evidence and pass/fail. Qualification also scores the empty fixture and a temporarily removed floor as negative controls.

Run with `project.cmd dynamic-prepare/start/check/cleanup`; see [execution guide](../../docs/DYNAMIC-EXECUTION.md). Private run artifacts include `cabin-score.json`, `cabin-negative-score.json`, the complete submitted calls/results, native checks and a separate `Cabin-Result.rvt` plus checksum. Timing, calls and errors are execution metrics, not score points. The qualified reference baseline scores **95/100**: its floor responds to the length change; its roof fails that dependency and earns zero for those five points. Future candidates can improve it against the same rubric; the execution-stage milestone requires an executed, independently scored model.
