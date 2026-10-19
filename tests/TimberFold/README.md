# Original native TimberFold fixtures

This test-only assembly procedurally authors native editable Revit 2027 RVTs and an original hosted two-pane RFA. It is excluded from the product and installer. Nothing is derived from Cedar Cottage, S1, downloaded families or Autodesk sample buildings. The installed Autodesk `Window.rft` supplies the wall-hosting infrastructure; all template openings/forms are replaced by explicit original geometry. The installed `C:\ProgramData\Autodesk\RVT 2027\Templates\Default_I_ENU.rte` supplies native wall/roof/view types and project phases. The builder rejects unexpected pre-existing model geometry in that template. It does not assume the no-template `NewProjectDocument(UnitSystem)` overload supplies roof types. The template/API binaries are prerequisites and are not redistributed.

Build against the local Revit API:

```powershell
dotnet build C:\Revit\RevitThyme\tests\TimberFold\RevitThyme.TimberFold.Fixtures.csproj --nologo
```

Use the [isolated development workflow](../../docs/DEVELOPMENT.md) to prepare a pinned disposable process. The builder requires trusted execution, exactly the pinned baseline document open, and a new absolute output directory. It creates, commits, saves and closes only new documents. It does not edit/save the active baseline, switch the active document, start/stop Revit or install/register an add-in.

Run one fixture per request, using the actual run folder and a fresh destination:

```powershell
$run = 'C:\Revit\RevitThyme\artifacts\dev\ACTUAL_RUN_UUID'
$output = 'C:\Revit\RevitThyme\artifacts\timberfold\original-fixtures\NEW_UUID\gable'
pwsh -NoProfile -File C:\Revit\RevitThyme\tests\TimberFold\build-fixture.ps1 -RunFolder $run -Destination $output -Fixture gable
```

Repeat with new directories and `irregular`, `multistorey`, `flat`. A small generated reflection method body is retained under the development run; the complete builder is compiled, so it does not exceed the 24 KiB trusted source limit. Assembly bytes load without locking the build output. The runner verifies those exact bytes against the SHA-256 captured at preparation, and passes the verified hash and original file path to the builder. The builder verifies provenance again before opening documents or creating output; it never relies on `Assembly.Location` for a byte-loaded assembly. The native callback remains on the Revit API thread. Only one fixture request should run at a time.

The wrapper uses the 30 s maximum cooperative request budget. A blocked Autodesk call cannot be forcibly interrupted. A timeout can occur after a new RVT has saved: inspect the original request via `project.cmd dev-outcome`, the new directory and its receipts before taking further action. Query rollback applies to the pinned current document; creation and saving of these explicitly authorized new documents are deliberate external effects. A failed build retains partial outputs and `fixture-failed.json`; retries require another fresh directory. Do not rename/open the baseline inside an existing development run. To qualify product operations on a produced model, launch a new development run with that RVT as `-FixtureSource`.

| Fixture | Native walls | Roof elements | Hosted windows | Project openings | Analytic shell panels before joints |
| --- | ---: | ---: | ---: | ---: | ---: |
| `gable` | 4 | 1 | 2 | 1 | 6 |
| `irregular` | 6 | 2 | 3 | 1 | 10 |
| `multistorey` | 8 | 1 | 4 | 1 | 10 |
| `flat` | 16 | 2 | 4 | 3 | 18 |

The last column is a fixed analytic source expectation (one panel per wall exterior plus each roof top face). Intersections/joints can change connected part topology; the product must independently reconcile actual contours and every source face. These numbers must never be substituted for delivered part counts. Project opening counts exclude the opening embedded inside each hosted window family. Every ordinary window contains two separate native solids classified with a native `Glass` material. Native inspection checks counts and actual glass solids before the RVT saves; it does not infer panes from family names.

- `gable`: 24 × 16 ft, 10 ft eave, 6:12 native gable roof (14 ft ridge). Four native roof-attached walls, two 4 × 4 ft windows and one 3 × 7 ft door void.
- `irregular`: L outline `(0,0),(24,0),(24,28),(12,28),(12,16),(0,16)` ft. A 24 × 16 ft main gable intersects a 12 × 20 ft perpendicular wing, with a deliberate 12 × 8 ft overlap. Both are 6:12 at 10 ft eave. Five wall segments have native roof attachments; the sixth has an explicit editable stepped/gabled profile spanning both roofs. Actual roof intersections must be resolved by the product. Three windows and a door void.
- `multistorey`: 24 × 16 ft footprint; levels 0/10/18 ft and gable ridge 22 ft. Eight stacked wall segments with different top profiles, four windows aligned at x=6 ft and offset at x=18/17 ft, and a ground door void.
- `flat`: 28 × 20 ft lower shell, terrace at 10 ft, setback upper shell `(8,5)` to `(20,15)` ft, upper roof at 18 ft. Terrace has a 12 × 10 ft opening for the upper shell; upper roof has a 4 × 4 ft skylight opening. Two-foot parapets on both storeys; four windows and a door void.

All dimensions are native editable elements, family geometry, levels, sketches and profiles. Window frames and normal centre bars are 3 in in the building: exactly 0.0625 in at 1:48. Pane thickness is 0.25 in in the building. The builder source is the reproducible dimensional recipe; changing family width/height alone does not flex the fixed original forms. Edit the family forms or change/rebuild the recipe for alternate dimensions.

Small original cases use the same command:

| Fixture | Expected native inventory / purpose |
| --- | --- |
| `curved-wall` | One quarter-circle wall, radius 10 ft and height 10 ft; unsupported surface diagnostic |
| `slanted-wall` | One 12 × 10 ft wall tilted 15°; unsupported surface diagnostic |
| `oversized-wall` | One 120 × 10 ft wall; 30 in wide at 1:48 cannot fit nominal 24 in stock |
| `ambiguous-glazing` | One wall and hosted window; zero glass solids, opaque pane material; blocking missing-glazing diagnostic |
| `thin-bar` | One wall and two-pane window; 1.5 in model bar becomes 0.03125 in physically, below acceptance |
| `arc-roof` | One circular flat roof, radius 8 ft, two half-circle arcs; top area analytic oracle 64π ft² |
| `phase-scope` | Two walls, one created in earliest phase/demolished in latest and one created in latest; explicit phase inventory |
| `performance-100` | 100 isolated 12 × 10 ft walls with 4 ft separation; timing/cancellation observation |

Design-option, linked-model and injected export/publication failure qualification need additional dedicated cases; this fixture suite does not claim those checks. Settings-only invalid stock/grain tests belong at the shared operation seam. Physical cutting, measured calibration and assembly remain outstanding.

Each successful directory retains RVT SHA-256, builder assembly SHA-256, recipe version, dimensions, warnings, native element IDs/UniqueIds and role comments, native pane/count inspection, and a geometry fingerprint. The fingerprint sorts world-coordinate tessellated triangles rounded to 8 decimal feet, so it omits random IDs, timestamps and document GUIDs; actual repeatability must still be verified on multiple native builds. It is evidence about fixture geometry, not the product's canonical contour hash. RVT hashes and UniqueIds are expected to differ across freshly authored documents. Template SHA-256 and generated RFA SHA-256 are retained separately. A receipt records the actual installed Revit build.

Compilation verifies SDK/API compatibility only. Neither compilation nor the analytic expectations establish an actual-Revit pass. Run the builder and product qualification in pinned disposable hosts and retain their observed receipts.
