# Native C# TimberFold

Status: owner-approved scope, numerical gates, test seams and seven-slice breakdown; implementation is authorized. Physical cutting/assembly may remain explicitly outstanding. Baseline and geometry-library research are separate evidence, not a completed feature.

## Problem Statement

TimberFold can produce useful architectural cutting kits, but its Python/pyRevit implementation is external to the native toolkit, qualified on too few buildings, and has gaps in completeness accounting, failure recovery and cancellation. Users need one inspectable RevitThyme workflow that turns supported native buildings into correctly scaled, traceable, digitally verified kits without changing or saving their source building.

## Solution

Expose one shared C# TimberFold module through native Revit settings/ribbon, CLI and MCP. Inspect the building and material profile, preview its parts/layout, generate cutting files and assembly guides with optional native review documentation, verify deliverables independently and retain a traceable run record. Explicitly reject incomplete or unsupported geometry. Qualify the complete path in pinned disposable Revit sessions on four original editable buildings.

## User Stories

1. As a modeller, I want to inspect exterior-shell scope before generation so that I can see what the kit includes.
2. As a modeller, I want phase and design-option choices recorded so that the kit represents the intended building.
3. As a modeller, I want excluded and unsupported element IDs with reasons so that missing architecture cannot pass unnoticed.
4. As a modeller, I want source identities and geometry fingerprints retained so that I can trace every part.
5. As a fabricator, I want explicit scale, stock size, thickness, border and spacing so that the kit matches my material.
6. As a fabricator, I want measured/provisional profile fields distinguished so that nominal settings do not imply physical readiness.
7. As a fabricator, I want retained window bars and door voids so that the shell keeps its architectural openings.
8. As a fabricator, I want wall tops fitted to roof undersides so that nominal-thickness pieces assemble digitally.
9. As a fabricator, I want butt joints and collision/fragility diagnostics so that intersecting panels do not conceal impossible assembly.
10. As a fabricator, I want fixed or quarter-turn rotation and explicit grain rules so that nesting respects stock constraints.
11. As a fabricator, I want oversized parts rejected with dimensions so that no part is omitted or silently rescaled.
12. As a fabricator, I want clean inch-unit DWG/DXF cuts so that I can use ordinary laser software.
13. As a fabricator, I want calibration pieces and explicit laser-software kerf ownership so that compensation is applied once.
14. As an assembler, I want part IDs, source maps and assembly diagrams so that I can locate every piece.
15. As a modeller, I want review views/sheets grouped into one Undo item so that I can remove the documentation easily.
16. As a modeller, I want the source building and file preserved so that generating a kit cannot save or damage my work.
17. As a user, I want stage progress and cooperative cancellation so that I can control long runs.
18. As a user, I want retained request/run identities so that a lost response does not duplicate model changes.
19. As a user, I want previous outputs preserved and partial outputs clearly failed so that I can trust a successful manifest.
20. As an agent, I want the same typed operations through CLI/MCP so that automation matches native controls.
21. As a contributor, I want reproducible varied RVTs and small geometry fixtures so that failures can be reproduced.
22. As a maintainer, I want independent contour, scale, collision and completeness checks so that library self-agreement is insufficient.
23. As a maintainer, I want stage timings and repeatability hashes so that correctness/performance changes are measurable.
24. As an installer, I want operation without Python/pyRevit and recoverable upgrades so that deployment is self-contained.
25. As a user, I want physical cutting/assembly gaps stated so that digital qualification is not mistaken for measured physical fit.

## Implementation Decisions

- Keep Revit extraction and native documentation in the existing Add-in execution context. Core owns immutable Autodesk-independent snapshots, fabrication geometry, nesting, run state, files and verification. UI/CLI/MCP are adapters over the same typed interface.
- Use a retained in-host coordinator and unique file-backed runs. Start calls return a run handle promptly; status/cancel read coordinator state without waiting for the Revit API thread. Perform suitable computation on a background task. Geometry and full diagnostics remain in owned run files, outside bounded IPC replies.
- Queue native extraction/finalization through the existing ExternalEvent dispatcher. Reuse captured process/start/session/document/view/revision checks and retained request equivalence. Extend the protocol explicitly for typed TimberFold operations; install clients and host together.
- Public operations cover inspection, preview, generation from a verified preview, verification, status and cancellation. Caller-supplied request IDs are replayable by value; conflicting IDs are rejected. A run handle acknowledges acceptance, not successful fabrication. Internal native stages cannot retarget arbitrary documents or accept arbitrary geometry paths.
- Each run records versioned source/settings, source-to-part mapping, canonical contours/transforms, joints, layout, diagnostics, timestamps/timings, verification and content hashes. Retain terminal evidence across restarts; never resume native writes automatically in a new session.
- Fabricate vertical planar basic exterior walls and planar roof faces, including flat roofs, intersecting slopes, stepped profiles and multiple storeys. Tessellate line/arc boundaries to the physical chord tolerance. Preserve supported hosted door/window voids and glazing-derived panes with retained bars. Capture explicit phase and compatible design-option choices.
- Inventory relevant geometry and report every inclusion, exclusion and unsupported condition. Site/floors/furniture/MEP lie outside the shell kit scope. Unsupported selected architecture, unresolved source losses, ambiguous glazing and unrepresentable joints block success. Every supported face/opening is accounted for; reference errors need independent solid-face reconciliation.
- Convert feet to physical inches once and use stable local frames. Keep canonical contours separate from triangles. Use pinned managed Clipper2 for polygon operations and qualify its triangulation on holes/concavity; use the double LibTessDotNet adapter only if needed by that qualification. Keep custom 3D geometry bounded to convex prism intersections and deterministic through-thickness trims. Evaluate exact versions/notices against the research receipt before adding dependencies.
- Resolve material inwards and use deterministic butt-joint ownership. A general 3D subtraction is not a valid laser contour. Regenerate flat prisms after conservative through-thickness trimming and independently recheck collision, coverage and webs. Diagnose unsupported partial-thickness joints rather than silently bevel them.
- Use deterministic conservative rectangle nesting with configured border/gap, fixed or quarter-turn rotation and grain constraints. Every part is placed exactly once. Oversized/unplaceable parts fail; no automatic scale change or splitting.
- Cutting output contains closed planar inch-unit contours only; labels/borders remain in references. Produce DWG through native Revit and DXF through a managed writer. Read back independently, verify calibration/units/contours and retain per-file hashes. No machine motion, laser power/speed or automatic kerf offset.
- Complete background fabrication/layout checks before native documentation. In one native callback and rollback-capable TransactionGroup, create uniquely named review views/sheets, export, perform temporary readback and verify source invariants. Assimilate last into one Undo item. Never keep a transaction open across an await/callback.
- Filesystem and Revit commits are separate. Stage files in unique run directories; write a prepared native receipt before assimilation and atomically publish the successful manifest afterward. Publication failure after native commit is Unconfirmed, with retained created IDs and recovery evidence. Never blindly retry documentation.
- Invalid/cancelled/stale/export-failed runs retain diagnostics and never get a successful manifest. Cooperative cancellation checks stage/element/part boundaries. Individual blocked Autodesk calls cannot be forcibly interrupted. Previous successful runs remain immutable.
- Use the accepted nominal profile for regression: 1:48, 0.125 in stock, provisional 24 × 18 in bed, 0.25 in margin, 0.125 in spacing, 0.0625 in bars, 0.001 in chord/clearance and laser-software-owned kerf. Preserve measured/provisional flags in settings and reports.
- Follow native ribbon standards with a Fabrication task entry and compact settings/progress window. Preserve existing toolkit keys. Show support limits and actionable diagnostics; expose recovery without asking users to understand transport internals.
- Keep the existing external implementation and kits unchanged until accepted. Private S1/Cedar content is comparison evidence; do not redistribute it without an explicit provenance/license decision. Create four original procedural RVTs and small fixtures with editable dimensions, builders, expected counts and hashes.

## Testing Decisions

The owner approved the shared typed operation/run-module seam and the separate native adapter seam. Observe parts, files, diagnostics, run states and model behavior. Avoid private-method/mirrored-algorithm tests. Existing toolkit operation, pipe/stdio and pinned-native tests supply the patterns.

- Start each implementation slice red, add the smallest working behavior, and proceed one observable case at a time. Review against scope and repository standards before committing.
- Exercise settings, holes, panes/bars, nested rings, concavity, arc error, source mapping, material joints, prism volume, nesting/grain, clean DXF, retained request IDs and failure states through public module behavior.
- Use independent analytic numeric cases and separate readback/parser/oracles. Sum prism-cell overlaps per panel pair before the volume gate. Fail unresolved numeric uncertainty near thresholds.
- Native qualification runs extraction through final deliverables on gable, irregular/intersecting-roof, multistorey and flat-roof buildings. Check phase/options, unsupported geometry, units, source identity/preservation, one Undo and no automatic save. Keep saved source hashes and pre-existing inventories.
- Run negative/fault cases: invalid profiles, unsupported curves/slants/links, glazing ambiguity, thin details, oversized panels, cancelled queues/computation/finalization, failed export/publication, stale targets, repeated IDs and repeated generations.
- Canonical geometry/layout/content hashes must repeat for identical source/settings. Timestamps and run IDs do not affect canonical hashes.
- Initial measured target: 100 panels within 60 s excluding Revit startup, apportioned 15 s extraction / 20 s computation-layout / 25 s output-native checks. Record hardware and actual stage results; unmet targets stay explicit.
- Required numeric gates: 0.001 in maximum arc chord error; 0.00001 in straight-edge/reference/export contour error; 0.000001 in³ residual collision threshold; 0.0625 in minimum bars with 0.00001 in numeric allowance; 1 in calibration within 0.00001 in. All delivered contours are valid/closed/simple/planar; every part connected, accounted for and placed once.
- Qualify package dependencies/notices and isolated install/upgrade/uninstall/recovery with no Python/pyRevit runtime requirement. Run repository checks after implementation. Source/package checks remain separate from actual Revit evidence.

## Out of Scope

Curved/slanted wall surfaces, nonplanar roofs, linked-model fabrication, site/base/terrain/furniture/MEP conversion, automatic panel splitting/scaling, tabs/fingers/bevels, grain inference, machine motion or power/speed. Physical calibration, cutting, strength and assembly cannot be established in software and remain explicitly outstanding unless a person supplies measured evidence. Publishing private model content or retiring the old implementation is outside automatic delivery.

## Further Notes

Accepted 2026-10-19 in the goal conversation. The owner subsequently instructed autonomous continuation with no further approval prompts. Deliver reviewable changes and evidence; do not claim incomplete criteria passed. Maintained references: [baseline](baseline.md), [support/acceptance](support.md), [library research](geometry-libraries.md). The seven implementation slices remain open until their acceptance and default-branch landing are verified.
