# Compact dialogs and reusable components

2026-10-12, [WLK-232](https://linear.app/wlkr-labs/issue/WLK-232/compact-visual-view-range-using-reusable-autodesk-style-ui-components). User requirements: compact native-ish Revit/Autodesk dialog appearance, less helper text/padding, an updated screenshot and an owned shadcn-style component library for future tools.

Comparison base: 84db981079e25c164501627f9bbc3ebab2d01795. Branch: codex/WLK-232-compact-view-range. The final clean commit and exact commands/results are pinned by the local CI report in ignored artifacts/local-ci/wlk-232.json.

## Implementation

Visual View Range, Apply review and Help compose desktop/src/components/ui. The library owns compact dialog layout, typography, neutral themes, group boxes, button variants, browser form/table adapters, conditional notices and closed disclosures. Feature state, Rust validation and Revit mutation authority remain in their existing owners. No new dependency or framework migration is included. See [UI-COMPONENTS.md](../docs/UI-COMPONENTS.md) for the reuse contract.

The window uses a narrow toolbar, section beside aligned plane rows and a fixed action row. Help and detailed diagnostics are outside the normal editing surface. Review Apply brings its comparison into view. Numeric display formatting removes binary floating-point noise without submitting rounded untouched values.

## Regression and packaged evidence

- The new rendered-bounds check failed against the previous package: Bottom offset was outside the viewport at the normal 1120-by-700 window. The first revised package passed at 100% but exposed wrapping at 125%; reducing panel basis widths and allowing shrink fixed the fit. Before screenshot/failure report remain under ignored artifacts/ui-feedback/2026-10-12/before-compact.*.
- project.cmd check-m1 checks Rust/core contracts, TypeScript and production .NET frame/API contracts. project.cmd package-m1 builds the actual Windows application and verifies packaged assets/sidecar/native-adapter hashes without installation.
- project.cmd check-m1-ui passes 63 packaged observations plus missing-sidecar recovery. Checks include all primary control bounds and aligned inputs at 100%/125%, non-overlapping section labels in mm/m/ft, visible review, numeric/keyboard/pointer edits, delayed responses, reset/cancel, errors, partial capture, stale targets, reconnect, themes, navigation and renderer isolation.
- project.cmd m2 ui passes 19 observations through the actual packaged UI and offline .NET launcher/model fixture. Confirmation remains required, identical Apply opens no transaction, mm edits reach exact 5-ft readback, stale Apply is rejected, queued cancellation prevents a transaction, and outcome inspection does not resubmit. This fixture uses no Revit document.
- Electron compositor screenshots at artifacts/m1/compact-100.png and compact-125.png show the real rendered application. They are captured before the driver's edit/error cases; no image reconstruction is used.

## Review passes and limits

Standards pass: checked repository/wstack instructions, source ownership, scoped changes, reusable components with real callers, small modules, shared theme styles and preserved IPC/mutation guards. No remaining blocking Standards finding. Display rounding is separate from internal values; the reset/identical native-fixture checks retain that independent contract.

Spec pass: compared the supplied Object Styles, Manage Links and Transfer Project Standards references with actual rendered screenshots. Primary controls fit together, labels align, surfaces are restrained, details are closed, and reusable composition is documented for future tools. Apply review automatically becomes visible while toolbar/actions stay available. No remaining blocking Spec finding for this desktop scope.

Actual Revit qualification of this new UI/package was not run. The installed legacy/native preview, pyRevit registration, external TimberFold and models remain preserved. The candidate is local and unpublished; default-branch landing and actual-host evidence are separate from these source/package checks.
