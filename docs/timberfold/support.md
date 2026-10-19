# TimberFold support and acceptance

Accepted by the owner on 2026-10-19: scope and numerical gates, four original buildings, and digital delivery with physical checks explicitly outstanding. These are requirements; they are not implementation or qualification evidence.

## Geometry and completeness

- Fabricate vertical planar basic exterior walls and planar roof top faces. Include flat roofs, multiple storeys, stepped wall profiles and intersecting planar roofs. Accept line and circular-arc boundaries on planar faces; tessellate arcs to the configured physical chord tolerance. Curved or slanted wall surfaces remain unsupported.
- Preserve hosted door voids and window panes with retained bars. Missing/ambiguous glazing or host correspondence is a blocking diagnostic. Model geometry determines panes; family names alone cannot establish success.
- Capture the chosen phase and design-option scope explicitly. Preview lists included, excluded and unsupported element IDs with reasons. Linked models, curtain walls, nonplanar roofs, imports and other unsupported architecture are never silently fabricated. The run blocks on unaccounted relevant elements. Site, floors, furniture and MEP are listed as outside the exterior-shell kit scope.
- No automatic splitting of oversized panels. Stop with physical dimensions and the failed sheet constraint. No automatic scale change. Plain glued butt joints with inward thickness; no tabs, fingers or bevel machining. Support fixed or quarter-turn rotation and explicit grain constraints.
- Every extracted face and source opening maps to a part, an explicit approved scope exclusion or a blocking diagnostic. Never inherit blanket acceptance of empty faces/unresolved references from the old pipeline.

## Numerical acceptance established before product implementation

All fabrication coordinates and tolerances below are physical model inches, after `12 / scale denominator` conversion from Revit feet.

| Requirement | Accepted gate |
| --- | --- |
| Topology | Every delivered outer/hole contour closed, simple, finite, planar; each part connected; no duplicate part IDs |
| Completeness | 100% of selected supported source faces and openings accounted for; zero unexplained losses |
| Curve chord error | At most 0.001 in, configurable with bounded positive validation |
| C# vs preserved straight-edged contour | Hausdorff deviation at most 0.00001 in for unchanged geometry; intentional topology fixes recorded per source ID |
| DWG/DXF readback | At most 0.00001 in contour deviation, correct hole topology/count and inch units; Z=0 |
| Calibration | 1 in square readback within 0.00001 in; corner and bar coupons reflect current settings |
| Collision | Zero non-joint material intersections above 0.000001 cubic in; joints resolved with 0.001 in nominal clearance |
| Window webs | At least 0.0625 in minus 0.00001 in numeric tolerance; tapered tips separately diagnosed, no claim of physical strength |
| Nesting | Every part exactly once, within stock border, configured spacing/rotation/grain obeyed; zero oversize omissions |
| Repeatability | Same source/settings gives identical canonical geometry, layout and content hashes; timestamps/run IDs excluded |
| Source preservation | No source save/sync; existing source element inventory, geometry/parameters and file SHA unchanged; only requested documentation additions in one Undo group |
| Failure | Failed/cancelled runs retain diagnostics and cannot contain a success manifest; previous run folders immutable; model rollback verified |
| Performance | Initial targets: 100 panels in 60 s end to end excluding Revit startup; extraction 15 s, computation/layout 20 s, output/native checks 25 s; record measured hardware/stages, then revise honestly if unmet |
| Cancellation | Check at stage/element/part boundaries; queued cancellation uses existing host semantics; no promise of interrupting a blocked Autodesk API call |

## Fixture plan

Create four original procedural, editable Revit 2027 fixtures with source builders, fixed dimensions, expected element/part counts, difficulty notes, hashes and isolated run copies:

1. Gable house: rectangular shell, gable end profiles, double-pane windows and door void.
2. Irregular house: L-shaped shell, intersecting roofs, concave outline and varied roof attachment conditions.
3. Multistorey building: two levels, stacked walls, aligned/misaligned openings and several wall heights.
4. Flat-roof building: parapets, terrace/setbacks, flat top faces and rectangular roof opening.

Use Cedar Cottage and S1 as preserved private comparison datasets. Add Cedar as an optional fifth qualification building when source completeness is independently reconciled. Do not publish private S1 geometry or redistribute existing example/model content without a provenance/license decision. Small fixtures exercise arcs, glazing ambiguity, unsupported surfaces, phase/options, links, zero/oversized stock, grain, thin tips, joints and cancellation.

## Nominal profile and physical boundary

Retain 1:48, nominal 0.125 in stock, provisional 24 x 18 in bed, 0.25 in border, 0.125 in gap, 0.0625 in bars and laser-software kerf ownership as regression inputs. Show unmeasured thickness/stock and unconfirmed kerf/grain clearly. Separate digital verification from fabrication readiness. A person must cut and measure calibration coupons, joints and complete kits; no software check can establish those outcomes. The old implementation remains available until the replacement is accepted.

## Owner decisions recorded

1. Approved planar exterior-shell scope and numerical gates.
2. Approved four original varied editable buildings with preserved private comparisons.
3. Approved digital delivery with physical cutting and assembly explicitly outstanding.
