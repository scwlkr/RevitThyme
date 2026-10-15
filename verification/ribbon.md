# Native ribbon and SVG standards — 2026-10-15

Scope: organize and illustrate the five existing commands, establish the maintained [ribbon standard](../docs/RIBBON.md), and create/install the reusable [revitthyme-ribbon skill](https://github.com/scwlkr/skillsies/tree/main/personal/revitthyme-ribbon). No new modeling operations or public release.

## Source, assets and build

- Full Revit 2027 solution builds with zero warnings/errors; `project.cmd check` passes 149 unit, 35 MCP and 13 Agent checks.
- Ten original SVG optical masters produce forty transparent light/dark 1x/2x PNGs. The dependency-free Windows/WPF exporter checks exact regeneration, dimensions and 96/192 DPI. Contact sheet reviewed at actual logical sizes; malformed SVGs are rejected rather than silently approximated.
- All runtime images are embedded in the add-in. No external image deployment or new runtime package is required. The native theme event refreshes the images. Image density is sampled at startup/theme refresh; moving between monitors is not separately qualified.
- The reusable skill is canonical in `scwlkr/skillsies/personal/revitthyme-ribbon`, merged in PR #9 at `e4697b19ce76b64d9a0d973823899b0050ffda5b`. Skill metadata validation and global discovery linkage pass. Existing skillsies setup tests have Windows symlink-privilege/fcntl limitations; no setup/test implementation changed.

## Actual Revit

The passing run is `artifacts/agent/live/f4f2aec0-7416-49fa-92ef-de5de0425e56`, built from clean source `3c4991d951e64267e3d77590b537a2a1e127616d` on Revit **27.3.0.28**. Its owned process was **35668**, with a fresh `Foundation-Test.rvt` and an explicit disposable 3D view.

**27 native checks passed:** panel order, five retained toolkit keys, short/extended tooltip metadata, embedded small/large icons, no-document availability, native posting and resulting windows for all five commands, floor-plan availability, Inspect View's actual capture, dark/light theme refresh, logical image sizes, 3D availability, and preservation. Open Agent opened its pane without starting a connection. Check for Updates opened without a document.

The fixture retained **2,435 elements**, `IsModified=false`, and saved SHA-256 `5489221d0700c3581cf6f6b2662481573acd9151e90bcee3e1636108869cb97f`. No application tool applied a model edit. The original test theme was restored. The loader captures the original theme after startup completes because Revit can apply saved frame preferences after `OnStartup`.

The ribbon and maintenance slide-out were also inspected through Windows Computer Use in this exact fixture window. Full captures remain in `artifacts/ribbon/native-ribbon-light.jpg` and `native-ribbon-slideout.jpg`; [the documentation image](../docs/images/native-ribbon.png) is a crop of the native capture, without reconstructed controls.

Earlier runs are retained: the first exposed the unavailable dockable pane on Home, leading to an open-document requirement for Open Agent; the second exposed a test race that checked before posted native commands executed. The passing loader waits for observable results.

Tooltip content is qualified by native API readback. Sustained hover/expanded-popup presentation, F1 browser launch, Quick Access Toolbar rendering, and multiple monitor/display scales were not separately exercised. Help destinations point to existing maintained command documents. Moving panels changes Revit's generated native command paths; existing custom keyboard or Quick Access mappings may need reassignment, despite stable toolkit keys.

## Package and preservation

The local candidate is `packages/RevitThyme-0.9.1-cli-6e1a8d93e9504913aa79124cd670c8e7`. It passed **18 isolated installer checks** at `artifacts/foundation/install-test/2c334267-00a2-428f-82ed-966e6113bc4a`. Its add-in SHA-256 matches the qualified native bundle; `artifacts/ribbon/package-native.json` records the comparison.

Cleanup stopped only the owned test process, removed its temporary registration, restored the original production registration byte-for-byte, and verified the pre-existing Revit processes **2064** and **20896** and every existing registration. Global pyRevit remains disabled. The current working sessions retain their already-loaded production build; this task does not install over them or publish a new release.

Hosted portable CI passed for the qualified implementation. The final documentation, screenshot and test-loader theme-capture updates leave product source unchanged. A final native-solution build retained the exact qualified add-in hash `5281385f540919616aedd69c3ec66bf566caf98ef5f6c2c9cbc52d373e37b0d8`; the delivered local candidate is the matching qualified bundle above.
