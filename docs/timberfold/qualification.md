# TimberFold qualification evidence

Status: native fixture/inspection qualification and internal fabrication foundations, not completed cutting-kit qualification. Digital delivery remains authorized with physical checks explicitly outstanding.

## Original editable fixtures

On 2026-10-19, the test-only [builders](../../tests/TimberFold/README.md) saved four original editable projects and their original hosted window families in pinned disposable Revit 2027.3 processes (build `27.3.0.28`). The independent native oracle checked source counts, actual solid roof faces, planarity, opening loops, dimensioned roof areas and two native glass solids per ordinary window before saving. The v2 recipes additionally check outward wall orientation, literal exterior boundary planes, actual roof-underface coverage and native attachment targets. The active authoring baselines remained unmodified and were closed normally after production.

| Original building | Walls | Roofs | Windows | Native project openings | Analytic shell faces |
| --- | ---: | ---: | ---: | ---: | ---: |
| Gable | 4 | 1 | 2 | 1 | 6 |
| Irregular/intersecting | 6 | 2 | 3 | 1 | 10 |
| Multistorey | 8 | 1 | 4 | 1 | 10 |
| Flat/setback/parapets | 16 | 2 | 4 | 3 | 18 |

These are independent source expectations, not delivered part counts. Small saved fixtures cover curved/slanted walls, oversized stock, ambiguous glazing, a thin bar, circular roof boundaries, phases and 100 isolated walls. A second v2 gable build reproduced geometry fingerprint `afc1c5d13ba80b5f84c01137820d55e7be7ebacfdbe383dde8c629bcaa06e1d4`; native RVT hashes and element identities differ across separately authored projects as expected.

The first recipe's outward-face and roof-coverage assumptions did not withstand examination of captured native geometry: wall exteriors faced inward and roof attachments left corner slivers. V2 defines the approved footprint as exterior wall faces, offsets centrelines inward by half the native wall thickness, projects hosted openings to their actual host lines and checks the resulting geometry independently. Earlier RVTs and failed attempts are preserved for comparison. The irregular model intentionally retains overlapping roofs; attachment checks record gaps to the highest envelope separately from agreement with the actual attached roof underside. Fabrication must still reconcile that envelope.

Native production exposed and corrected fixture-builder assumptions: new-document files must close before hashing, the footprint roof API needs an initialized output collection, slope values are rise/run, and returned footprint edges require geometric correspondence rather than collection order. The 6:12 gable now passes the unchanged analytic roof area of `429.32505167995964 ft²` within `1e-6 ft²`.

Revit's installed API reference documents `Face.Area` as potentially estimated. The circular roof returned `201.08107415631684 ft²`, while its two actual radius-eight-foot circular arcs independently integrate to exactly `64π = 201.06192982974676 ft²`. The fixture oracle retains both values and checks the analytic boundary integral at the original tolerance; straight-roof native area checks remain in place.

## Installed native inspection

The signed inspection candidate launched in dedicated development processes. The native ribbon and settings window were observed; its scrolling body and fixed actions remained visible. Inspecting the empty baseline correctly reported `empty_shell`, zero faces and blocked preview readiness without modifying the document.

The first original-gable inspection blocked because `GetTopFaces` returned three references, including one unresolved reference, for two actual planar upward faces. The adapter now reconciles every resolved reference against the independently enumerated complete solid-face inventory and retains an explicit warning. The installed `.2` candidate passed inspection on all four corrected v2 buildings: every source ID/UniqueId, selected phase, expected face/opening and full-evidence hash was reconciled; source/copy file hashes and native target/revision remained unchanged. Every owned qualification process closed normally, followed by closed-file preservation checks.

All eight small cases passed their expected inspection outcomes. Curved/slanted walls and ambiguous glazing block readiness with source-specific diagnostics. Oversized panels and thin bars remain inspectable source geometry: fabrication/layout must reject them at their respective later gates. The 100-wall native inspection took `1,052 ms` in this run, excluding Revit startup and fixture auditing. These timings are observations, not a complete generation benchmark.

The [inspection qualification script](../../scripts/qualify-timberfold-inspection.ps1) runs against an already pinned development copy and its independently qualified fixture receipt. It records source/copy file hashes, exact native target/revision, unsaved state, face/opening counts, diagnostic expectations and full-evidence hashes. It cannot qualify fabrication, collisions, nesting or delivered cutting files.

## Internal fabrication foundations

The pure shared contour module passes analytic cases for feet-to-inch scaling, holes/concavity, constrained triangulation, signed glazing-minus-opaque projections, door notches, source correspondence, thin remaining material and cancellation. Public drafts use immutable nested collections and always report manufacturing readiness as false. Tapered tips and concave outer-only necks still require separate qualification.

The independent convex-prism verifier uses exact rational arithmetic on the defined binary-valued affine panel geometry, sums all disjoint cell intersections per panel pair and compares against exactly `1/1,000,000 in³`. Independent partition guards reject missing, overlapping and hole-filling triangles. Empty input, cancellation and arithmetic/work limits remain unresolved. Exact arithmetic does not remove extraction, chord, grid or frame uncertainty. Analytic contact/threshold/skew/hole cases pass. One local Release run measured 100-panel checks at 31 ms for rectangles, 4.6 s for 32-triangle arc-derived panels and 59 ms for perforated panels; these exclude extraction and outputs.

Deterministic conservative rectangle layout passes spacing, multi-sheet, quarter-turn and grain cases. Explicit grain aligns each panel's local U axis with the selected stock axis; fixed rotation plus height-directed grain is rejected. Oversized geometry reports physical dimensions instead of rescaling or dropping parts.

A replay of the corrected gable's actual captured snapshot produced six contour panels, three opening mappings and one planned stock sheet. The independent verifier detected a `0.046875 in³` roof-ridge overlap. This is retained failing evidence for the next joint-fit stage, not a successful manufacturing preview. Joint resolution, complete preview/run operations, cutting files, native documentation and end-to-end qualification remain pending.

Private RVTs, RFA files, failed attempts, receipts, source maps, exact run identities and hashes are retained under the ignored `artifacts/timberfold/` and `artifacts/dev/` directories. Physical calibration, cutting, web strength and assembly remain outstanding. The external implementation and comparison kits remain preserved.
