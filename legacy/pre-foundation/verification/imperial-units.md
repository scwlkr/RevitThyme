# Visual View Range imperial units - WLK-234

Verified locally on 2026-10-12. Checked source/package/native candidate:
19d08060d1f06b2f7ea048e03fa24a98088247b4.
Comparison base: 4bb5985fcfde922631412d887c18680a67516f3f.

## Behavior

Feet and inches are the default. Fields, plane labels, underlay elevations,
Apply review and outcomes use reduced fractions at nearest 1/8 inch.
Architectural entry, signed offsets and decimal feet are accepted. Imperial
slider and plane-drag edits use 1/8-inch increments; metric choices remain.
Display formatting, focus/blur and unit selection retain exact untouched
captured feet. Reuse desktop/src/lib/length.ts for future length controls.

## Source and packaged verification

- Full clean-source local CI: 40 checks passed at the candidate SHA; private
  report artifacts/local-ci/wlk-234.json.
- Actual packaged UI: 70 observations plus recovery passed, including signed
  fractional entry, foot-boundary rounding, invalid fractions, imperial
  slider/drag increments and compact layout at normal/125% scale.
- Offline native orchestration: 22 observations passed. Independent fixture
  readback retained 4.001 ft while the imperial field displayed 4' 0"; focus,
  blur and unchanged Apply preserved that exact value without another Undo item.
- Fail-before evidence: the default-unit assertion rejected the previous
  225bdf2 desktop package; artifacts/m1/imperial-regression-before.json retains
  the failed result. This was a package baseline, not actual-Revit evidence.
- Separate Standards and Spec review passes found no unresolved findings.
  Automated source/package checks above do not qualify a live Revit host.

## Actual Revit verification

Prepared run 3e0b533c-df01-4b45-8514-7748d898312a opened a separate disposable
RevitThyme-Test / RevitThyme Lab Plan in Revit 27.3.0.28, PID 6688. Runtime
readback identified the exact installed candidate assembly and no pyRevit
assemblies. The working Revit process, PID 37180, stayed open.

The native ribbon launched the candidate with Feet & inches selected and
offsets 8' 0", 4' 0", 0' 0", -1' 0". Entering 5' 0 1/8" changed the cached
section and Apply review. Confirmation stayed unchecked; Apply was not
submitted. Reset and refresh restored the captured 4' 0" Cut.

Two independent native read-only verification runs passed all 11 checks.
Before/after native range values, captured triangle count and saved fixture
hash matched. Retained private evidence is under artifacts/revit-lab/<run>:
live-before-units.json, live-readonly.json, binding.runtime.json,
units-ui-smoke.json, imperial-default.png, imperial-review.png and
imperial-view-range.png. Screenshots are actual candidate UI captures.

## Installation and limits

The immutable candidate is installed under the per-user native-preview folder
at the checked SHA. Startup registrations have rollback copies in the run;
pyRevit and legacy Python/Jev MCP entries remain disabled. The native test MCP
was rebound only after successful exact-session proof and remains fixture-bound.

Already running working Revit and its older open dialog keep their loaded
225bdf2 bundle until a normal restart. No working-document API access, save,
close or restart occurred. New native Apply/Undo qualification was not repeated
for this formatting change; prior 225bdf2 evidence is in
[native-cutover.md](native-cutover.md). Preservation of sub-eighth-inch values
was checked independently in the offline native fixture, not on a live job.
No source, private models or evidence was pushed or published. The attached
worktree is retained because the running test session uses its lab bundle.
