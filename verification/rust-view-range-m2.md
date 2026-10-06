# M2 verification and review

Work: WLK-111. Comparison base: `dfbf9632c3abe82364bce0d0a09026c17623c91b`, M1 PR #6. Exact final head, aggregate command/result and package hashes accompany the separate draft PR and Linear verification comment; the manifest records source SHA. See [decisions and gaps](../docs/RUST-VIEW-RANGE-M2.md).

## Source/offline Windows evidence

Installed-file inspection confirms executable/API 27.2.0.39, API net10.0 and SDK 10.0.400. Native source builds against those SDK files with zero warnings/errors, CopyLocal=false and no new NuGet packages. This establishes no actual Revit execution.

Check-m1 retains six Rust independent geometry/range/cache tests, generated OpenAPI/Zod/TS conformance, 47 authenticated API observations and installed Expo metadata checks. Managed frame checks now use the **production** DTO/reader: 17 observations cover exact negative feet/high IDs, protocol/UUID/target/confirmation, bounds/incomplete frames, closed/missing/null/nonfinite fields and one-body framing. Sequential frames legitimately permit subsequent frames; M1's provisional in-memory trailing-byte rule was replaced by the stream contract.

Check-m2 includes 61 Windows queue/transaction/pipe observations. An independent mutable model observes expected range values and transaction counts, injected capture/validate/start/set/regenerate/readback/commit/group/rollback failures, original restoration and uncertainty. It verifies pre/post-commit ordering, unchanged behavior, wrong identities, duplicate payloads, queued/executing cancellation, disconnect/invalidation/expiry, restrictions before/after capture and queue limits. Actual Windows pipes exercise valid/invalid credentials/session/protocol, oversized headers and closed frames; I/O never calls the model interface. The production Revit model remains unexecuted.

The native API driver exercises 18 Rust/Axum-to-.NET observations over the real framed pipe with a 260-triangle analytical fixture (three chunks). Expected endpoints at X=5 ft are independently [0,0] and [5,5]. It discards an Apply response body, queries the original ID and observes one transaction; duplicate same ID never reapplies. New input revision invalidates confirmation; stale native revision, queued cancellation, disconnect/reconnect and unknown outcomes fail safely. No add-in/RVT is created.

Reports: ignored artifacts/m2/check.json, native-api.json, artifacts/m1/check.json, api-evidence.json and final artifacts/local-ci/m2-final.json. Full CI also covers legacy source/distribution, foundation/CLI, scope/cache/idempotence and all replacement source/package/UI gates. Hosted replacement alignment is documented in SETUP-TODO.

## Packaged application evidence

Forge exports/launches the actual Windows x64 portable executable without a development server. Package checks verify ASAR/owned-adapter allowlists and all asset/sidecar/native hashes, excluding models/settings/TimberFold/Autodesk assemblies. No registration is installed.

M1's 33 packaged UI/security observations and missing/crashed sidecar recovery remain covered. Ten native UI observations run through production .NET DesktopLaunch, including its exact child-PID bootstrap channel, native target, mandatory confirmation, unchanged/no-transaction, precise mm-to-feet Apply, truthful post-Apply Escape/Cancel, outcome-only inspection, stale rejection, queued cancellation, reconnect and narrow preload. A test-only allocated loopback CDP port permits observation; production enables no debugging port. The model is explicitly an offline fixture. Artifacts/m2/native-ui.json and native-packaged.png are **packaged UI with offline adapter**, not actual Revit evidence.

## Actual Revit and preservation

R1–R12 are unexecuted. No installation, bridge/API request, active-document inspection, native capture/write/undo, fixture creation, save/sync or live job work occurred. Exact install/fixture steps are proposed in the handoff; approval remains required. No physical/TimberFold fabrication work occurred.

Main `518d673511c8392daf212b559a672c9dd8000154`, user logo and website `7a0667468804f13904ab337e829b37d2bf18f6de` are preserved. TimberFold stays independently clean at `45d49b4969d7cd67bb1bde821e20add0adf0a9b9`. Existing drafts/M1 are not rewritten. M2 remains draft/In Progress; nothing is merged/released.

| Acceptance | M2 evidence | Remaining gate |
| --- | --- | --- |
| O1–O7 | M1 retained; production DTOs/analytical chunk fixture | Actual geometry/references/performance R2/R11 |
| O8 | Queue identity/restriction/close/invalidation fake and SDK build | Actual context/restrictions R1/R5/R6 |
| O9 | Unchanged, dedup/conflict, discarded ack/outcome, cancellation | Native lost response, undo/redo/lifetime R4/R9/R10 |
| O10 | Orchestration failures, post-commit-before-assimilation, uncertainty | Native processing/Pending/finalizers R7/R8 |
| O11/O12 | ACL/remote-reject source; Windows pipe/PID bootstrap; retained security/reconnect | Cross-user/remote and actual multi-Revit trials |
| P1–P5 | Portable assets/children/hashes, both UI/security drivers | No installed/released/second-machine claim |
| P6–P8 | Registration template and proposed steps only | Approved discovery/install/component recovery |
| R1–R12 | Unexecuted | Approved M3 disposable qualification |

## Separate review axes

Standards: AGENTS/wstack/stack/smells, base/worktree, boundaries, ownership, credential/PID handling, bounded stores, meaningful tests and packaging. Replaced unused duplicate M1 native DTOs with production contracts and split the expanded Rust API by responsibility. Handwritten files remain below 300 lines; generated OpenAPI/lockfiles are exceptions. No parallel reviewer was authorized.

Spec: M2 source flow and O8–O12/P1–P5 orchestration, with explicit runtime/distribution gaps. Fixed the packaged Windows GUI-stdin launch failure using a one-shot ACL/PID bootstrap, the disconnect-before-execution race using immediate queued cancellation, and backend confirmation invalidation for newer inputs. These regressions failed before and pass after fixes. Polling queries read-only outcomes; missing responses never trigger repeated mutation. Actual native claims remain blocked on M3. Final review/check evidence accompanies the draft head.
