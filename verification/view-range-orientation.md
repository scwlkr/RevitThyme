# WLK-117 verification and review

Comparison base: M2 draft PR #7 at `5d6cd42febc87c089fe9830f7a997e8c3b9aac87`. M1 remains `dfbf9632c3abe82364bce0d0a09026c17623c91b`. Final head, exact clean Windows CI and package hashes are recorded by the retained reports/manifest and the draft PR. [Decisions, scope and unexecuted runtime proposal](../docs/RUST-VIEW-RANGE-ORIENTATION.md) distinguish Underlay Orientation from the main plan direction.

## Source/offline evidence

- `project m1 ui` against the prior M2 package failed with **Plane labels overlap: Bottom · 0 mm / View Depth · 0 mm**. Screenshot and failed report retained under ignored `artifacts/wlk-117/before-*`.
- `project m1 check` with the independent structural-up range expectation failed in the prior Rust core with **View Depth must be at or below Bottom**. The corrected captured-direction check passes and preserves structural-down/ceiling cases and absolute elevations from separate level references.
- Eight Rust behavior/cache tests cover geometry, units/precision, native prechecks, structural directions, underlay finite/clipped/Unbounded bounds and malformed limits. Strict Rust-derived OpenAPI/TS/Zod contracts and TypeScript compilation pass. Twenty-two production .NET frame observations preserve separate direction/underlay facts and refuse incomplete or unknown orientation captures and protocol 1.
- Seventy-four authenticated API observations include all main directions and disabled/finite up/down/Unbounded up/down underlay cases. Underlay choices leave the original main range exact. Synthetic fixture preview p95 is measured in `artifacts/m1/api-evidence.json`, with machine/runtime/load; it is not native model latency.
- Sixty-one existing Windows .NET scheduling/transaction/failure/pipe observations remain active. Thirty-six Rust-to-.NET real-pipe observations include captured main direction, wrong-side depth, underlay references/elevations/Unbounded and independent main-range readback. These use an explicitly offline .NET model fixture, not a Revit project.

## Packaged application evidence

Forge packages the actual Expo routes, Rust sidecar and owned adapter together. ASAR allowlist, component/protocol checks, assets and native/sidecar hashes pass. No models, Autodesk API assemblies, private settings or TimberFold payloads are bundled.

The actual portable executable passes 60 UI/security/recovery observations, including text-bound measurements for four coincident plane labels in mm/m/ft at 1600/1100 px and 900 px with 125% zoom; settled viewport and control bounds; pointer/keyboard/numeric editing; floor/ceiling/structural directions and directional Unlimited; and five underlay choices with correct shaded bounds/arrows and exact main-range review. Nineteen packaged native-mode observations use the offline .NET launcher/model fixture, including opposite underlay and all five underlay variants. None qualifies actual Revit transactions.

Screenshots and viewport metrics remain in ignored `artifacts/wlk-117`; native fixture screenshots explicitly identify **no Revit**. Electron compositor capture preserves actual OS scaling; CDP screenshots clipped pixels when OS and app zoom were combined, despite correct DOM bounds. No image reconstruction or rendering substitute is used. The driver closes only its own windows/children.

## Actual Revit — read-only only

Build 27.2.0.39, approved test house, active A101 Ground Floor. Reinspection found original main offsets 7.5/4/0/0 ft, all references unchanged, Underlay Base/Top unset (-1), LookingDown, document unmodified. Existing ceiling and structural types were also inspected without switching active view or changing a type. Native detached ceiling validity rejected depths below Top and accepted at/above Top. Original RVT disk hash and exact private native records remain local.

The updated protocol-2 adapter has **not** been installed or executed in actual Revit. Enabled underlays, actual lookup/down capture refresh/invalidation, installed geometry, main structural up, Apply/rollback/readback/undo/redo and other acceptance R1–R12 gaps remain pending. WLK-114's previously observed document identity and idle-disconnect defects are not fixed here. There is no merge/release/legacy-retirement or second-computer claim.

## Separate review passes

Standards pass: traced Rust ownership and strict generated contracts, narrow renderer bridge, native-only type/underlay capture, compatible protocol rejection, and model-context rechecks. Kept label layout in the section UI and underlay facts/band math in their respective owners. No unused adapter seam, new dependency, private payload, or unrelated work is included; changed source files remain around/below 300 lines (generated contract exempt). Retained existing independent geometry/transaction/security regressions.

Spec/behavior pass: checked the user's Underlay Orientation clarification, both finite orientations and Unbounded/None, independence from main planes and transactions, labels at actual elevations, and real package input/output observations. Reviewed screenshots and strengthened resize checks/capture after finding stale resize/DPI evidence. Full final plan/underlay visibility remains explicitly outside this section annotation, and actual-Revit evidence is separated from offline fakes. No new blocking source finding remains; runtime gaps require approved follow-up.
