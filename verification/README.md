# Current verification

[workflow-result-checks.md](workflow-result-checks.md) records the 0.13.0 candidate's exact installed package and module hashes, 277 actual-Revit assertions, 18 real Agent assertions plus 6 preservation checks, and separate source/installer evidence. It preserves the distinction between a native operation outcome and its result-check report.

[release-0.11.0.md](release-0.11.0.md) identifies the frozen 0.11.0 runtime package, native SDK results, installer checks and preservation boundaries. Release-source metadata and native runtime source are recorded separately.

[foundation.md](foundation.md) records the C# Foundation build, native Revit and installer evidence. Private machine-readable reports and fixtures remain under ignored `artifacts/foundation`.

[mcp.md](mcp.md) records the next stage's official SDK, offline protocol checks, native MCP tasks and three external Codex rounds. Private machine-readable evidence remains under `artifacts/mcp`.

[view-images.md](view-images.md) records the unreleased exact-view/sheet image export, external MCP edit/see/verify loop, retained recovery and normal installed-host qualification. [document-lifecycle.md](document-lifecycle.md) separately tracks the subsequent explicit open/activate/save work. Neither establishes a release.

[ui-lifecycle.md](ui-lifecycle.md) tracks Home-screen open, explicit clean close, view switching and independent outcome recovery. Prior native results remain attributed to their tested revisions.

[active-close.md](active-close.md) tracks explicit active-document handoff and third-party close-cancellation recovery. It keeps last-document close to Home as a separate unresolved boundary.

[modal-recovery.md](modal-recovery.md) records original-request recovery while native test dialogs block document opening, including real transport timeout and external MCP server loss.

[document-discovery.md](document-discovery.md) separates the installed-host targeting-gap reproduction from the subsequent read-only document inventory candidate and its qualification status.

[electrical-lab.md](electrical-lab.md) tracks the current-source electrical-lab MCP loop, real missing-reference warning recovery and the distinction from historical fixture acceptance.

Earlier Rust/Electron/pyRevit records are preserved under `legacy/pre-foundation/verification`. They remain evidence for their original revisions, not qualification of the new C# product.

[native-utilities.md](native-utilities.md) records actual ribbon/WPF, native transaction and utility MCP qualification. [dynamic-execution.md](dynamic-execution.md) records Roslyn/transaction/MCP/CLI qualification and independent native Cabin Eval scoring. Their private fixtures/results live under ignored `artifacts/utilities` and `artifacts/dynamic` respectively.

[agent.md](agent.md) records Built-in Agent protocol, runtime, package and actual dockable Revit qualification. Private runtime traces and disposable fixtures live under ignored `artifacts/agent`.
