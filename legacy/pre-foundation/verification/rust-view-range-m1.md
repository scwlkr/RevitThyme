# Rust Visual View Range M1 verification

Work: [WLK-109](https://linear.app/wlkr-labs/issue/WLK-109/m1-rust-visual-view-range-offline-feature-and-packaged-desktop). Comparison base: spec PR #5, `e128a67917e393259b079838c8ac28b88a36f3b2`. Implementation checkpoint: `47a97a1`; subsequent contract/UI/evidence corrections are included in the final draft head. The exact final source SHA, full aggregate result and packaged manifest accompany the draft PR and Linear verification comment. No source self-reference is used as a substitute for checked-SHA evidence.

## Source and offline evidence

The project CLI runs Rust format/clippy/tests/build, OpenAPI generation drift, TypeScript compilation, .NET 10 frame conformance, authenticated Axum/Zod observations and Expo's installed SDK compatibility validator. Six Rust behavior tests use independent tetrahedron/box/void/transform expectations, unit and reference precision, invalid ranges, expiry and session identity. Sixteen managed frame checks cover canonical feet/64-bit level IDs, target fields, protocol, required values, length bounds, trailing bytes and arbitrary operations. These managed checks contain no Revit references or API execution.

The real API driver covers 47 observations, including floor/engineering/ceiling, mm/m/feet, exact untouched settings, converted edits, closed/malformed input, integer overflow, HTTP authorization, body limits, stale targets and partial diagnostics. It measures 240 sequential previews against the 108-triangle courtyard fixture. Its 100 ms p95 budget is limited to this fixture; JSON records individual timings, CPU/OS/memory, Node version, load description and source SHA. It does not qualify a maximum-size native capture.

Retained evidence: ignored `artifacts/m1/check.json`, `api-evidence.json`, generated-contract comparison and full `artifacts/local-ci/m1-final.json`. Full CI also checks the existing portable distribution/installer, foundation, CLI behavior, setup idempotence and cache routing/invalidation. Hosted CI remains the legacy portable boundary; it does not qualify M1.

## Packaged application evidence

`package-m1` builds Rust release, exports Expo routes, bundles main/preload and runs Forge for Windows x64. The package checker verifies the ASAR entry allowlist, app entry, component/protocol versions, every shipped sidecar/asset hash and absence of models, private settings, Autodesk assemblies, native registration and TimberFold payloads.

`check-m1-ui` launches the actual portable `RevitThyme.exe`. Its 33 observations cover assets/branding, numeric conversion, exact Reset, Cancel/Escape, keyboard slider and pointer drag with observed changes, computed slice geometry, Unlimited, three view kinds, partial warnings, stale review, delayed valid/invalid and unit-conversion responses, reconnect identity, crashed child, foreign-window IPC, renderer Node exclusion, CSP, navigation, dark/light presentation, 125% scaling and renderer errors. A second driver removes only the owned sidecar file from its disposable build folder, checks visible failure/disabled review, restores it and reconnects. It always restores the file in cleanup.

Screenshots are from the packaged synthetic feature, not Revit: `artifacts/m1/packaged-light.png` and `packaged-dark.png`. Reports: `package.json`, `ui.json`, `ui-evidence.json`, `recovery-evidence.json` under `artifacts/m1`. Manifest: `desktop/out/RevitThyme-win32-x64/resources/package-manifest.json`. The folder is a local unsigned preview; no installer or release is claimed.

## Actual Revit and external evidence

Actual Revit R1–R12: **unexecuted**. Installed files were inspected read-only: Revit executable 27.2.0.39, RevitAPI runtime target net10.0 and .NET SDK 10.0.400. The active document was not queried; no bridge request, adapter installation, native queue/transaction/readback, model edit or save occurred.

Native fake orchestration O8–O10, native pipe security/session shutdown, installer/update/uninstall/rollback and native path discovery remain M2/M3/M4 gates. The M1 frame checks cannot prove those behaviors. Dependency notices/signing, native performance and second-machine qualification remain unresolved distribution choices.

Start-of-session preservation references: main/installed legacy `518d673511c8392daf212b559a672c9dd8000154`; website work `bf7d928`; external TimberFold `45d49b4969d7cd67bb1bde821e20add0adf0a9b9`. Drafts #1–#5 and unrelated logo/source models remain intact. No physical or laser-kit work was performed.

## Review passes

Standards and Spec were assessed separately against the spec base and implementation changes; no parallel reviewer was authorized. Standards covered applicable AGENTS/stack rules, owned paths, credentials/process boundaries, dependency layout and meaningful CLI/UI verification. Fixed findings included lost existing Git attributes, native null/missing-field handling, explicit u32 schema bounds, artifact classification and dark-theme contrast. No remaining blocking Standards finding for M1; generated OpenAPI/lockfiles are the only large-file exceptions.

Spec covered M1 exit behavior and acceptance evidence boundaries. Strengthened the independent geometry assertions and actual drag/slice outcomes; fixed the busy-state keyboard driver and invalid-input synchronization. A delayed unit-switch regression failed against the prior packaged head: typing 1524 while millimetre values were still visible was interpreted as feet, producing an invalid range. The fix keeps the displayed units authoritative until the latest Rust conversion response arrives; the same packaged regression passes and reviews 5 ft. M1's interactive offline exit gate is covered. O8–O10 and R1–R12 are explicitly unqualified native scope, and P6–P8 remain distribution/native recovery scope. Those gaps block later native/release claims, not the bounded M1 deliverable. No remaining blocking Spec finding for M1.

Linear stays In Progress until accepted work lands and is verified on the default branch. Merge, release, native installation, fixture creation and legacy retirement require their own authorization.
