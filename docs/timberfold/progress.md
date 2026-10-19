# TimberFold delivery status

The active goal is [the complete C# feature spec](spec.md), tracked in [Deliver a complete native C# TimberFold feature](https://linear.app/wlkr-labs/issue/WLK-368/deliver-a-complete-native-c-timberfold-feature). Status below distinguishes source readiness from actual Revit and physical evidence. The owner approved autonomous continuation on 2026-10-19.

| Checklist item | Current evidence |
| --- | --- |
| Existing baseline | Captured, archived and hash-verified; history bundle verified; eight existing geometry tests passed; external source unchanged. See [baseline](baseline.md). |
| Supported geometry | Owner-approved planar exterior-shell limits and explicit unsupported behavior. See [support](support.md). |
| Acceptance criteria | Owner-approved numeric, completeness, preservation, repeatability and performance gates, established before implementation. |
| Varied buildings | Owner selected four original gable, irregular/intersecting-roof, multistorey and flat-roof buildings. |
| Reproducible fixtures | Original native builders compile; RVT production and native fixture checks still pending. |
| C# design | Managed immutable extraction snapshots, shared operations and file-backed evidence implemented for inspection. [Library evaluation](geometry-libraries.md) and complete design are recorded; fabrication runtime suitability remains unproven. |
| Extraction and diagnostics | Source compiles; Core accounting/negative cases and CLI/MCP tests pass. Actual pinned-Revit qualification pending. |
| Fabrication geometry | Pending. |
| Stock layout | Pending. |
| Cutting files and guides | Pending. |
| Native Revit documentation | Pending. |
| Controls and shared operations | Native settings/ribbon, CLI and MCP inspection implemented; progress/generation/cancellation remain pending. |
| End-to-end qualification | Pending on all four buildings. |
| Performance | Initial targets accepted; actual stage measurements pending. |
| Failures and recovery | Inspection validation/evidence recovery source checks pass; complete generation fault/cancel/export checks pending. |
| Physical fabrication | Explicitly outstanding by owner decision; digital delivery may proceed with this limit recorded. |
| Packaging and installation | Distinct signed inspection candidate prepared; offline allowlist/previous-version tests passed. Actual candidate installation/native startup and complete feature upgrade/uninstall qualification pending. |
| Review and delivery | First inspection slice reviewed and findings fixed; required source checks passed. Feature delivery/release and old-implementation retirement remain pending. |

## Implementation slices

1. [Inspect a building and fabrication profile safely](https://linear.app/wlkr-labs/issue/WLK-369/inspect-a-building-and-fabrication-profile-safely) — in progress; native qualification next.
2. [Preview and verify a complete gable-house cutting layout](https://linear.app/wlkr-labs/issue/WLK-370/preview-and-verify-a-complete-gable-house-cutting-layout) — pending inspection.
3. [Generate a native documented gable-house kit](https://linear.app/wlkr-labs/issue/WLK-371/generate-a-native-documented-gable-house-kit) — pending preview.
4. [Generate an irregular intersecting-roof kit](https://linear.app/wlkr-labs/issue/WLK-372/generate-an-irregular-intersecting-roof-kit) — pending native kit.
5. [Generate multistorey and flat-roof kits with stock constraints](https://linear.app/wlkr-labs/issue/WLK-373/generate-multistorey-and-flat-roof-kits-with-stock-constraints) — pending irregular kit.
6. [Recover cancel and benchmark real TimberFold runs](https://linear.app/wlkr-labs/issue/WLK-374/recover-cancel-and-benchmark-real-timberfold-runs) — can progress after native kit while harder buildings qualify.
7. [Qualify installation and deliver complete TimberFold](https://linear.app/wlkr-labs/issue/WLK-375/qualify-installation-and-deliver-complete-timberfold) — pending all qualified buildings and recovery/performance.

Private evidence lives beneath the ignored `artifacts/timberfold/` directory. No model source, signed package or historical kit is added to the public source tree. The old TimberFold implementation remains intact.
