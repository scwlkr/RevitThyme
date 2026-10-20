# Explicit active-document close

Unreleased `0.10.0-lifecycle.3`, protocol 12, keeps 31 MCP tools. It extends `close_document` with an optional exact already-open clean document to activate before closing the captured active document. It does not close the last active document to Home. The [contract](../docs/MCP.md#home-clean-close-and-exact-view-switching) distinguishes preflight refusal, verified completion and partial native lifecycle uncertainty.

## Design and test boundaries

The caller explicitly supplies both document identities, canonical paths and saved SHA-256 values. The existing queue synchronously activates the selected document, revalidates both native identities/clean state/hashes after activation and directly closes the original background document. Completion verifies absence of the closed target, unchanged saved files and the selected active document/view. No posted Close command, new scheduler, implicit open/save/discard or automatic restoration is included. Interrupted/cancelled native calls retain the original request's outcome; recovery does not repeat activation or close.

Core tests use the approved shared operation/retained-queue seam. `tests/Mcp/ActiveCloseLive.cs` is an official-SDK external client of the installed MCP executable. The test-only `tests/LifecycleProbe` add-in cancels one exact native `DocumentClosing` event in an explicitly scoped disposable run; it never executes lifecycle operations or changes models. Production lifecycle code has no dependency on the probe. With no run environment variable the probe is inert. Its temporary registration and signature must be inventoried and removed after qualification.

## Reproduced installed-host failure — 2026-10-20

The previously qualified installed `0.10.0-lifecycle.2` native source `e0d8d2e7bb41675c4ba2a976f48018d406326030` reproduced a cancellation identity bug in actual Revit 2027.3. An external client opened copied A/B, activated A and requested background B close. The one-shot third-party cancellation was observed natively. The request returned retained `lifecycle_unconfirmed` and B remained open, but inspecting the already-open B returned a different host document ID. Both saved copies and their original retained SHA-256 `3ae0941f3b5043c430a8df4e3e2e902189a899b4b2ef81b85336e6fb39bfd173`; the exact owned process closed normally.

The repair retires identity only for invalid native document objects observed after `DocumentClosed`, instead of discarding it at `DocumentClosing`. Cancelled closes therefore preserve identity while still invalidating old captures through the revision counter. Earlier successful close/image/Home/lifecycle results remain attributed to their tested revisions; this additional failure does not redefine those tests.

## Candidate qualification

Source checks and both review axes pass. New installed-candidate qualification is pending; implementation or a green source test is not native proof. The external client requires cancelled background and active closes, first activation of a background-only handoff, wrong/stale/reopened/dirty/hash-mismatched targets, lost original response, retained completion without replay, reverse handoff and explicit last-document refusal. Private process/session bindings, intents, SDK transcripts and preservation receipts stay under ignored `artifacts/active-close`.

Last-active-document close to Home, wider modal/native I/O failures, other machines and bounded model promotion remain separate gaps. Keep the >512 changed-ID work separate. No release, website deployment or promotional posts. Physical TimberFold fabrication remains outstanding.
