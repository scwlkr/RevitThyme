# TimberFold baseline captured 2026-10-19

This is the preserved Python/pyRevit implementation baseline for the authorized C# feature. It is not qualification of a new C# implementation. The owner approved planar exterior-shell scope, four original varied buildings and digital delivery with physical checks outstanding. See [support and acceptance](support.md).

This page records the historical capture. The owner has since accepted the C# replacement. Both old active source checkouts and the installed launcher are now removed after archive member hashes and complete Git history were verified. Recoverable private source/model/history archives and comparison datasets remain retained; current release status is in [delivery status](progress.md). The original observations and hashes below remain unchanged.

## Preservation receipt

| Item | Observed value |
| --- | --- |
| External checkout | Preserved private checkout; exact local path is retained in ignored `capture.json` |
| External source revision | `63422a61fdececcaf8621c6f7b222c80219ddcf2`, clean before/after |
| RevitThyme base | `bd4d5c6d452c4a1fcf9e80887293a3333bc85c34` |
| Working branch | `codex/timberfold-csharp-baseline` |
| Captured files | 178 files, 27,771,796 bytes |
| Private receipt | `artifacts/timberfold/baseline-20261019/capture.json` |
| Private source/kit snapshot | `artifacts/timberfold/baseline-20261019/TimberFold-source-and-kits.zip` |
| Snapshot SHA-256 | `71d194458f71cab97f55d87db8380986af5d5abfb77de5e14e3fe36748b521d5` |
| History | Verified `TimberFold.bundle` beside the snapshot |

Every archived member was re-read and SHA-256 compared. All selected source files were re-inventoried afterward; source revision/status and unrelated RevitThyme logo hash were unchanged. The archive includes source, editable Cedar RVTs, examples, S1 JSON, releases and prior run outputs. Git internals, the virtual environment, build targets and Python caches are excluded; the Git bundle separately preserves history. Private model content and archives remain ignored by Git.

## Features and implementation inventory

Paths below are relative to the external checkout. Source is preserved in place.

| Responsibility | Implementation and behavior |
| --- | --- |
| Extraction | `scripts/extract_model.py`: Fine geometry; exterior wall side faces, roof top faces, hosted windows/doors; configured or last phase; main/primary options; include/exclude overrides |
| Boundary conversion | Lines/arcs on planar faces; feet to model inches using `12 / scale`; chord-driven arc tessellation |
| Windows | Transparent or glass/glazing-named materials; projected glazing minus opaque geometry; retained bars; missing/unmatched glazing fails |
| Fabrication | `scripts/fabricate.py`: Shapely union/cleanup, inward material extrusion, coplanar merge, nearest roof-underside wall fit, projected material-overlap trimming |
| Collision | trimesh/earcut extrusion and manifold booleans; roof/larger-panel priority; reject removal above 35%; residual volume threshold `0.000001 in³` |
| Nesting | `scripts/preview_sheet_fit.py`, `scripts/make_outputs.py`: deterministic bounding-rectangle MaxRects, three sort orders, 0/90° rotation, margin/gap, oversize rejection |
| Cutting/guide | `scripts/make_outputs.py`, `scripts/render_model.py`, `scripts/write_guide.py`: DXF, coupons, part CSV, keyed maps, PNG previews, relationships, HTML guide |
| Native output | `scripts/revit_outputs.py`: drafting views, sheets, CAD imports, inch-unit AutoCAD 2013 DWGs, temporary round-trip imports/exports |
| Verification | `scripts/verify_dwgs.py`, `scripts/audit_geometry.py`, `scripts/audit_source.py`: contours/units, connected polygons/hole webs, source comparison |
| Runtime | `scripts/launcher.py`, `scripts/run_pipeline.py`: pyRevit settings/launcher, blocking external CPython process and independent timestamped run folders |

No grain constraint, automatic panel splitting, tabs/fingers or bevel machining is implemented. Clean cutting files contain closed planar CUT contours, without labels, stock borders or kerf compensation. No machine motion or power/speed commands are generated.

## Current nominal profile

`prototype-settings.json` selects 1:48; nominal 0.125 in material; provisional 24 × 18 in bed; 0.25 in border; 0.125 in spacing; 0.0625 in minimum bars; 0.001 in chord tolerance and joint clearance; 0.5 in roof-fit search. Plain glued edges, open doors, retained bars and optional removable roof are recorded. Kerf belongs to laser software. Caliper thickness, usable area/stock, grain, laser software and kerf are unconfirmed. These are regression inputs, not measured fabrication readiness.

## Existing examples and recorded evidence

The results below come from preserved 2026-10-02 reports, not fresh native tests.

| Dataset | Recorded result and limits |
| --- | --- |
| S1 frozen JSON | 84 source faces, 68 source elements, 70 windows, eight doors; 12 diagnostics including one empty wall face. No editable S1 RVT identified in the bounded fixture inventory. |
| Historical S1 release | 84 panels (58 wall/26 roof), four sheets (19/23/32/10), 169 house contours; 1/16 in stock and historical larger bed; native verification recorded. |
| S1 current nominal profile | 84 panels on five sheets (14/12/13/26/19); 80 panes, 43 fitted wall tops, 50 resolved collisions. Offline geometry/DXF evidence; updated native DWGs are pending. |
| Cedar Cottage | Original procedural editable RVT; seven walls, two roofs, nine windows/two open doorways; ten panels (seven wall/three roof), 18 panes, one sheet, 28 house/ten calibration contours, four review sheets. |

Cedar's records report live extraction, native DWG round trip, unchanged source geometry and identical offline reproduction. Two unresolved roof references and two omitted tiny tips are listed. Its kit contains cutting/calibration DWG and DXF, part geometry/CSV, joint relationships, keyed/assembly maps, images, verification reports and HTML guide. Physical cutting/assembly is explicitly `NOT_TESTED`.

Fresh baseline file SHA-256 readings:

| File relative to external root | SHA-256 |
| --- | --- |
| `baselines/S1-Type-House/source-model.json` | `53e131fd5ca9822354968307c60c42abfb420e34832aae2bbea900c668773653` |
| `releases/TimberFold-v1.0.0-house-kit.zip` | `fcebdde196441586e8bbdedc16cfcc6a7f8d00ab9063f2579def684f1fb96754` |
| `examples/Cedar-Cottage/Cedar-Cottage.rvt` | `1863c9ac9c7f6424bd691ac43c0ebacf22071ac2ffcc1cbe4d62e668132c05f5` |

## Fresh verification

All eight existing geometry tests passed using the external pinned virtual environment's Python with `-B`. Temporary DXF/calibration files and Matplotlib caches were redirected into the private artifact directory. Tests cover scale, hole order, material joints at two stock thicknesses, sloped roof fit, current-thickness coupons, retained bars, multisheet completeness and oversized rejection. Log and result: `artifacts/timberfold/baseline-20261019/tests/`.

No Revit process was running at initial inventory. No model was opened/saved, no generation was run, and no add-in installation changed. At that baseline checkpoint, new C# native behavior, current DWG export and physical assembly were untested. Later digital results are recorded separately in [qualification](qualification.md); physical assembly remains outstanding.

## Gaps the replacement must close

1. Collectors do not inventory arbitrary unsupported categories, despite broader README wording. Relevant excluded/unsupported source geometry needs explicit accounting.
2. Empty faces and unresolved references are broadly tolerated; reconciliation must distinguish recovered reference failures from lost geometry.
3. Hole-web checks do not establish strength of tapered exterior tips. Tiny omitted tips need explicit source and area records.
4. Native documentation is assimilated before all final checks. Later export/verification failure can leave documentation behind.
5. The launcher blocks for up to ten minutes; progress and cooperative cancellation are absent.
6. Source comparison checks selected extracted fields and deleted IDs; a reported no-save flag is not independent save detection.
7. Reports contain historical absolute paths; use captured-relative paths when replaying evidence.
8. Only two building datasets exist. Four new editable varied fixtures and small failure fixtures are required.
9. Stage benchmarks, physical checks, Python-free installation and upgrade/uninstall qualification are absent.

## Provenance and dependencies

The external repository has no root license. The Cedar procedural source states original design using installed Autodesk template/family content; that is provenance, not an explicit redistribution grant. The S1 baseline is private. Preserve them for local comparisons; decide redistribution separately before publication. The owner approved four original generated qualification buildings to avoid downloaded-model licensing dependencies.

The existing lockfile pins Shapely, trimesh, manifold3d, mapbox_earcut, ezdxf, NumPy, Matplotlib, Pillow and ReportLab plus support packages. Installed license metadata includes BSD-3-Clause/GEOS notices, MIT, Apache, ISC, PSF, MIT-CMU and mixed NumPy notices. This inventory is not a complete transitive notice audit. C# dependencies require their own source/license/deployment evaluation before selection.
