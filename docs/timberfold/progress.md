# TimberFold delivery status

The active goal is [the complete C# feature spec](spec.md), tracked in [Deliver a complete native C# TimberFold feature](https://linear.app/wlkr-labs/issue/WLK-368/deliver-a-complete-native-c-timberfold-feature). Status below distinguishes source readiness from actual Revit and physical evidence. The owner approved autonomous continuation on 2026-10-19.

| Checklist item | Current evidence |
| --- | --- |
| Existing baseline | Captured, archived and hash-verified; history bundle verified; eight existing geometry tests passed; external source unchanged. See [baseline](baseline.md). |
| Supported geometry | Owner-approved planar exterior-shell limits and explicit unsupported behavior. See [support](support.md). |
| Acceptance criteria | Owner-approved numeric, completeness, preservation, repeatability and performance gates, established before implementation. |
| Varied buildings | Owner selected four original gable, irregular/intersecting-roof, multistorey and flat-roof buildings. |
| Reproducible fixtures | Four original editable RVTs and eight small native cases saved and independently checked in Revit 2027.3. A second gable build reproduced the geometry fingerprint. See [qualification](qualification.md). |
| C# design | Immutable captured bytes cross the native callback boundary into retained background runs. Shared fitting, layout and DXF stages have bounded public summaries and full private evidence. [Library evaluation](geometry-libraries.md) and ADRs record the design. |
| Extraction and diagnostics | Installed native settings and empty-shell blocking observed. All four corrected v2 buildings and eight small cases passed installed shared inspection with source/phase/evidence/preservation checks. Extra unresolved roof references are reconciled against complete actual solid faces. |
| Fabrication geometry | Conservative through-thickness joint fitting resolves the captured gable ridge/corner overlaps: six panels, nine traced trims, three opening mappings and zero residual collisions. All other building fabrication qualification remains pending. |
| Stock layout | Deterministic rectangle layout, spacing, multiple sheets, rotation/grain and oversize rejection implemented/tested internally; integrated manufacturing qualification pending. |
| Cutting files and guides | Managed DXF plus square/bar/corner calibration staging passes independent parser checks and actual gable Revit import/readback within 0.00001 in. Canonical files repeat. Final DWG and assembly guides remain pending. |
| Native Revit documentation | Pending. |
| Controls and shared operations | Installed native ribbon/window, CLI and MCP previews qualified on the gable. Status/cancel return during an actually blocked native callback; listeners start outside Revit's UI synchronization context. Generation remains pending. |
| End-to-end qualification | Pending on all four buildings. |
| Performance | Initial targets accepted; actual stage measurements pending. |
| Failures and recovery | Queued cancellation, same-ID replay/conflicts, malformed/changed recovery evidence, bounded diagnostics, DXF corruption and blocked transport/connection controls pass source checks. Complete generation fault/cancel/export checks remain pending. |
| Physical fabrication | Explicitly outstanding by owner decision; digital delivery may proceed with this limit recorded. |
| Packaging and installation | Signed protocol-6 preview candidate installed with pinned Clipper2/IxMilia payloads and full notices. Thirty-three provenance/package checks pass under PowerShell 5.1 and 7; twenty isolated installer checks pass including .2→.3 upgrade/recovery. Complete-feature installation remains pending. |
| Review and delivery | First inspection slice reviewed and findings fixed; required source checks passed. Feature delivery/release and old-implementation retirement remain pending. |

## Implementation slices

1. [Inspect a building and fabrication profile safely](https://linear.app/wlkr-labs/issue/WLK-369/inspect-a-building-and-fabrication-profile-safely) — native inspection qualified; default-branch landing still pending.
2. [Preview and verify a complete gable-house cutting layout](https://linear.app/wlkr-labs/issue/WLK-370/preview-and-verify-a-complete-gable-house-cutting-layout) — installed CLI, MCP and native-window gable previews pass fitting/layout/DXF/calibration/readback and repeatability; default-branch landing remains pending.
3. [Generate a native documented gable-house kit](https://linear.app/wlkr-labs/issue/WLK-371/generate-a-native-documented-gable-house-kit) — pending preview.
4. [Generate an irregular intersecting-roof kit](https://linear.app/wlkr-labs/issue/WLK-372/generate-an-irregular-intersecting-roof-kit) — pending native kit.
5. [Generate multistorey and flat-roof kits with stock constraints](https://linear.app/wlkr-labs/issue/WLK-373/generate-multistorey-and-flat-roof-kits-with-stock-constraints) — pending irregular kit.
6. [Recover cancel and benchmark real TimberFold runs](https://linear.app/wlkr-labs/issue/WLK-374/recover-cancel-and-benchmark-real-timberfold-runs) — can progress after native kit while harder buildings qualify.
7. [Qualify installation and deliver complete TimberFold](https://linear.app/wlkr-labs/issue/WLK-375/qualify-installation-and-deliver-complete-timberfold) — pending all qualified buildings and recovery/performance.

Private evidence lives beneath the ignored `artifacts/timberfold/` directory. No model source, signed package or historical kit is added to the public source tree. The old TimberFold implementation remains intact.
