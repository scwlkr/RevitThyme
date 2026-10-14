# Visual View Range M1

Replacement execution: [WLK-109](https://linear.app/wlkr-labs/issue/WLK-109/m1-rust-visual-view-range-offline-feature-and-packaged-desktop). Design baseline: spec PR #5, `e128a67917e393259b079838c8ac28b88a36f3b2`. Historical shipped baseline: `518d673511c8392daf212b559a672c9dd8000154`.

M1 delivers the **offline feature slice** in the [replacement milestones](specs/RUST-WSTACK-REPLACEMENT.md#migration-milestones). The packaged Windows x64 app displays a synthetic architectural section, Top/Cut/Bottom/View Depth, precise numeric inputs, draggable planes/sliders, X/Y slice position, units, Reset, Cancel/Escape, and before/after Apply review. Native Apply is visibly unavailable. There is no Revit connection, adapter installation, model transaction or save.

## Run and verification

Use the isolated branch/worktree while the installed 0.2.0 host and other drafts remain intact. New machines need explicit approval before dependency installation. This session approved project-local Cargo/pnpm dependencies and running the portable app.

```powershell
.\project.cmd check-m1
.\project.cmd package-m1
.\project.cmd check-m1-ui
.\project.cmd ci --base e128a67917e393259b079838c8ac28b88a36f3b2 --full --require-clean
```

The portable folder is `desktop/out/RevitThyme-win32-x64`; launch its `RevitThyme.exe`. Forge packages exported Expo assets and a Rust sidecar outside ASAR. The allowlist contains bundled main/preload, package metadata, Expo assets, sidecar and a hash manifest. Electron's own licenses remain in its distribution. No Autodesk assemblies, adapter registration, private model or TimberFold payload is bundled.

Reports and screenshots stay in ignored `artifacts/m1`. The full CI report includes command results at its exact source SHA. The package manifest records source SHA, component protocol, sidecar and every asset hash. Package checks inspect ASAR entries and hashes; UI drivers launch the **packaged executable**, with no development server or downloaded browser.

Hosted CI currently checks the legacy portable release only. Local full CI is the M1 source/package/application gate; its exact clean SHA and results accompany the draft PR. See [verification details](../verification/rust-view-range-m1.md) and [tooling alignment](../SETUP-TODO.md#rust-m1-check-alignment---wlk-109).

## Fixed and selected decisions

| Item | Decision |
| --- | --- |
| User-fixed runtime/UI | Rust/Axum, Expo Router with React Native primitives, NativeWind/Tailwind, Electron Forge, OpenAPI/TypeScript/Zod |
| Native target | Minimal .NET 10 adapter for Windows Revit 2027.2; remains M2 source / M3 qualification |
| Dependency set | Expo 57.0.27, Router 57.0.25, React 19.2.3, RN 0.86.3, RN Web 0.21.3, NativeWind 4.2.7, Tailwind 3.4.19, Electron 44.5.1, Forge 8.0.1, TS 6.0.3, Zod 3.25.76; exact transitive versions in pnpm lockfile |
| Rust | Existing Rust 1.99; Axum 0.8.9, Tokio 1.53.2, Utoipa 5.5.0, Serde 1.0.229; Cargo lockfile committed |
| Contract generation | Utoipa emits OpenAPI from Rust routes/types. A deliberately narrow generator derives strict Zod schemas; TS types infer from them. Unsupported schema shapes fail generation. Drift is checked. |
| Computation convention | Project coordinates and offsets in internal feet; Rust owns input/display conversion. Untouched edits retain original feet with no display rounding round trip. |
| Cache and proposals | One immutable geometry snapshot, 600-second expiry, 50,000-triangle limit, 16 KiB requests; refresh/reconnect invalidates identities. No proposal/job persistence or mutation endpoint in M1. |
| Desktop boundary | Typed capture/preview/propose/reconnect only; random credential over owned stdin, authenticated allocated loopback port, sandbox/context isolation, allowlisted local protocol, CSP and sender checks |
| pnpm layout | pnpm 11 `nodeLinker: hoisted` in workspace YAML, strict peers; build scripts limited to approved tooling |
| Native execution contract | .NET 10 DTO/frame checks: little-endian 4-byte length, 64 KiB frame, closed operation enum, required non-null identities/planes, process/start/session/document/view/revision, string level IDs and finite internal feet. No listener or API execution. |

Compatibility was checked against [Expo's SDK matrix](https://docs.expo.dev/versions/latest/), [NativeWind's stable installation guidance](https://www.nativewind.dev/docs/getting-started/installation), [pnpm settings](https://pnpm.io/settings), the installed Forge/Packager source and [Electron security guidance](https://www.electronjs.org/docs/latest/tutorial/security). The SDK package validator passes using the pinned installed Expo compatibility metadata. No mobile or other OS claim.

## Evidence boundaries and remaining choices

| Acceptance | M1 evidence / gap |
| --- | --- |
| O1–O3 | Analytical tetrahedron/box sections, opening void, transformed furniture, finite limits, negative values, exact unit/reference preservation, floor/engineering/ceiling prechecks. Final native validity is M2/M3. |
| O4–O5 | Real packaged controls, drag/keyboard/numeric, units/reset/cancel/Escape, Apply review, delayed response races and invalid input retention. Window closes without a model connection. |
| O6 | Bounded geometry/request/startup frames, partial-capture reason and snapshot expiry. Native extraction element limit and performance remain M2/M3. |
| O7 | Rust/OpenAPI/derived Zod/TS drift and real authenticated API conformance; malformed, unknown-field, protocol, target and nonfinite cases |
| O8 | .NET frame identity/serialization checks only. Native API queue, template/dependency/document-close checks are M2/M3. |
| O9–O10 | No mutation endpoint; deduplication, cancellation, transaction/failure/readback/lost-ack orchestration and native outcomes remain M2/M3. |
| O11–O12 | HTTP authentication, sandbox, CSP, foreign-window IPC rejection, navigation, fresh session on reconnect and missing-child recovery. Native pipe ACL/session and Revit shutdown remain M2/M3. |
| P1–P5 | Real Windows x64 package assets/navigation, interactions/themes/scaling, sidecar launch/recovery, security and payload/hash checks. System fonts; existing branded SVG. |
| P6–P8 | Portable preview only. Installer/update/uninstall/rollback, native destination discovery and native compatibility recovery are future gates. |
| R1–R12 | **Not run.** No Revit API, fixture, adapter installation or model mutation in M1. |

Interactive budget is 100 ms p95 for the representative 108-triangle fixture on this Windows development machine; reports retain all timings and Node/runtime versions. This is not a performance qualification for 50,000-triangle native captures. Larger representative captures need measured budgets in M2/M3.

The installed executable/API runtime metadata were inspected read-only: Revit 27.2.0.39, RevitAPI runtime target net10.0, .NET SDK 10.0.400. This is installation evidence, not actual-Revit execution. No legacy bridge call was required; active document remains uninspected.

Unresolved choices: native capture/queue implementation and pipe ACL/registration paths; native transactional outcomes and approved failure qualification; installer/update format, signing and second-machine support; representative native performance; distribution notices and approved suite parity/cutover. Existing drafts #1–#4 are preserved as deferred capabilities/behavioral references. WLK-101/102 remain separate. External TimberFold remains at `45d49b4969d7cd67bb1bde821e20add0adf0a9b9`, with source/geometry/baselines unchanged.

Before M3, propose exact adapter package SHA, physical registration destination, active document and separate disposable fixture/transactions/cleanup. Obtain approval first. Never reuse a live production project or TimberFold baseline implicitly.
