# TimberFold delivery status

The [complete C# feature spec](spec.md) is tracked in [WLK-368](https://linear.app/wlkr-labs/issue/WLK-368/deliver-a-complete-native-c-timberfold-feature). Digital delivery is authorized with physical checks explicitly outstanding. The final candidate is `0.10.0-timberfold.4`, protocol 8, built from `0fffdf8397da507c10911e77a6fb1bb81120b4e1`; its signed package manifest SHA-256 is `AE21953451541C57EE55B28202D00DE94A761314C57DD446724E6C4803DF91C5`.

| Checklist item | Current evidence |
| --- | --- |
| Existing baseline | Captured, archived and hash-verified; external implementation and comparison kits remain unchanged. See [baseline](baseline.md). |
| Supported geometry and acceptance | Owner-approved planar exterior-shell scope, numerical gates, preservation, repeatability and performance targets. Unsupported or unresolved geometry blocks success. See [support](support.md). |
| Varied, reproducible fixtures | Four original editable gable, irregular/intersecting-roof, multistorey and flat/setback/parapet RVTs, plus focused native cases, independently checked in Revit 2027.3. |
| C# design | Immutable snapshots cross the native callback boundary into retained background runs. Core owns fabrication, stock, files and verification; native callbacks own extraction and documentation. Shared CLI/MCP/UI operations use bounded summaries and full private evidence. |
| Extraction and diagnostics | All four buildings and focused cases pass installed inspection with complete source/phase/opening accounting and saved-source preservation. Real native design-option membership and loaded-link default blocking/explicit exclusion pass separate scope checks. |
| Fabrication geometry | All four captured buildings pass complete fitting, joint/exposure/envelope accounting, retained openings and exact residual-collision/partition checks: 6/10/10/18 panels and 3/4/5/7 opening maps. |
| Stock layout | Deterministic spacing/borders, multi-sheet layout, quarter-turn/grain constraints and explicit oversize rejection pass source cases; every part is placed once in all four native kits and the 100-panel benchmark. |
| Cutting files and guides | Managed inch-unit DXF, native DWG, calibration coupons and source-mapped SVG/reference guides pass independent readback and canonical repeatability. Labels/borders stay outside CUT geometry; kerf remains laser-software-owned. |
| Native documentation | Annotation-only views, labels and sheets commit in one Undo group without saving the source. All four buildings repeat independent documentation/DWG checks and complete single-Undo restoration on the final candidate; every owned process closes normally with saved source hashes unchanged. |
| Controls and shared operations | Native window, CLI and official-SDK MCP generation/verification/reconciliation pass actual-host qualification. Status/cancel remain responsive during a marked blocked native query. |
| End-to-end qualification | Four original buildings pass installed CLI/MCP kits, independent native readback, repeatability and single Undo on historical candidate `74f6807` / manifest `26830C34…`. The final candidate repeats all four native kits/readbacks/canonical comparisons/Undo checks; final flat SDK generation/reconciliation also passes 56 checks. |
| Performance | Final candidate generates 100 panels on three stock sheets in 23,347.3833 ms, meeting the 60-second total and each stage budget. Independent documentation/DWG checks and one Undo pass. See [measured qualification](qualification.md). |
| Failures and recovery | Actual export failure and in-flight cancellation roll back with exact source restoration. Post-commit publication failure remains Unconfirmed and reconciles into separate evidence; edited or absent native kits are refused. Same-ID replay never repeats native writes; original run files/timings remain unchanged. |
| Physical fabrication | Measured thickness/kerf, physical calibration, cutting, web strength, joints and assembly are explicitly outstanding. `ManufacturingReady` remains false. |
| Packaging and installation | Final installed binaries replay all four captures twice with 36 identical canonical files, 89 payload files verified before/after, 12 absent Python/pyRevit command probes and nine disconnected SDK checks. Twenty isolated upgrade/recovery, twenty-two compiled setup-executable and seven CLI final checks pass, with normal registration/PATH/installed bytes and the stable native host preserved. |
| Review and delivery | Full checks pass 460 Unit, 68 MCP, 13 Agent, 6 WPF and 46 development-safety checks with zero build warnings/errors. Focused Standards/Spec findings are resolved. Digital qualification and peer review are complete; private bundle assembly is the remaining handoff step. Merge, release and old-implementation retirement have not occurred. |

## Implementation slices

1. [WLK-369 — Inspect a building and profile](https://linear.app/wlkr-labs/issue/WLK-369/inspect-a-building-and-fabrication-profile-safely): installed native inspection qualified.
2. [WLK-370 — Preview a complete gable layout](https://linear.app/wlkr-labs/issue/WLK-370/preview-and-verify-a-complete-gable-house-cutting-layout): fitting/layout/DXF/calibration/readback and repeatability qualified through shared controls.
3. [WLK-371 — Generate native documentation](https://linear.app/wlkr-labs/issue/WLK-371/generate-a-native-documented-gable-house-kit): actual DWGs, reference sheets/labels, recovery and one Undo qualified.
4. [WLK-372 — Fit intersecting roofs](https://linear.app/wlkr-labs/issue/WLK-372/generate-an-irregular-intersecting-roof-kit): complete irregular CLI/MCP kits and source restoration qualified; final-candidate recheck passes.
5. [WLK-373 — Multistorey and flat-roof kits](https://linear.app/wlkr-labs/issue/WLK-373/generate-multistorey-and-flat-roof-kits-with-stock-constraints): complete CLI/MCP kits qualified; final-candidate native rechecks and source restoration pass.
6. [WLK-374 — Recovery, cancellation and performance](https://linear.app/wlkr-labs/issue/WLK-374/recover-cancel-and-benchmark-real-timberfold-runs): native fault/recovery cases and final-candidate 100-panel performance target pass.
7. [WLK-375 — Installation and delivery](https://linear.app/wlkr-labs/issue/WLK-375/qualify-installation-and-deliver-complete-timberfold): final package/runtime, native, upgrade/setup and CLI checks pass; private bundle assembly is the remaining handoff step.

Implementation evidence is separate from default-branch landing and release. Private RVTs/RFAs, signed packages, receipts and failed attempts remain in retained private storage; public captured-fixture regressions document their original provenance. The previous TimberFold implementation remains intact.
