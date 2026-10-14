# Built-in Agent verification

Scope: only the Built-in Agent row of the complete `2026-10-14_NEW_PLAN.md`, building on completed Dynamic execution. Exploration has not been started. All delivery is local.

## Source and runtime

The C# solution and portable filter build with warnings treated as errors. `project.cmd check` passes **96 Core**, **35 MCP** and **13 Agent** checks. The Agent checks exercise out-of-order RPC correlation, streaming while approval is pending, opaque approval IDs, RPC errors, cancellation/late responses, disconnect completion, initialization, ChatGPT login protocol, listed-session resume, unrelated-session rejection, explicit skill input, failed/early turn completion and unsupported requests. PowerShell syntax, six-project architecture and proprietary-binary exclusion checks pass.

The real pinned **Codex CLI 0.162.1** runtime probe passes authentication read, model catalog, skill discovery, sole `revitthyme` MCP discovery and ChatGPT browser-login initiation/cancellation. Probe evidence is `artifacts/agent/probe/c0d6cf35-c98c-490f-ab44-0ccbdf2c155d/result.txt`; temporary copied authentication was removed. Full fresh browser consent was not performed; authenticated tests use an existing user-owned ChatGPT sign-in copied only into the disposable test home. Product sign-in is owned by Codex and opens its supported browser flow.

The initial configuration probe showed Codex merging user MCP configuration with overrides. The final client therefore uses a dedicated Codex home, verified to expose only the process-pinned RevitThyme server. Native WPF requests use ordinary fields, with unsupported form shapes left for Decline. Shell/apps/plugins/hooks/browser/computer/delegation tools are disabled in this runtime. Dynamic C# retains its existing host opt-in and caller trust gates.

## Package

The final local package is `packages/RevitThyme-0.9.0-agent-087b9f3e5fa94ca7ae16875d9b889fb5`, with adjacent ZIP/SHA-256. **18 isolated installer checks pass**, including installed official-runtime startup, skill/client inclusion, modified-client rejection, tamper/identity/reinstall/receipt checks and recoverable unregister. Evidence: `artifacts/foundation/install-test/06be8690-f21a-4ec1-9f2d-16fa53819bf8/result.json`. `artifacts/agent/package-native.json` confirms that native product assemblies, Roslyn dependencies, MCP DLL/Core, Codex executable and bundled skills exactly match the prepared native test bundle.

The runtime ZIP is pinned to upstream `rust-v0.162.1`, SHA-256 `4b17206ed3045de64064b2016627d51fba3bde386d03838312054882a64d781d`. Apache-2.0 license and upstream notices are retained. The package includes no Autodesk DLLs, test loader, model, credential, previous implementation or voice assets.

## Native qualification

The prepared final run is `artifacts/agent/live/f490c933-3667-411a-9bc1-69c96c911cee`, pinned to a new disposable Revit process and `Foundation-Test.rvt`. It is currently waiting for the user to load the unsigned test add-ins. The native stage is not yet accepted.

The test will independently inspect actual native rename results, one-step Undo restoration, resumed conversation edits through a fresh connection, interruption, saved-fixture hash/element invariants and visible dockable-panel layout. It will operate the panel's approval controls only for the explicitly authorized disposable-fixture rename. It retains a native panel capture and runtime events. No production model is a fallback.

Failed/interrupted runs `125c8193-913e-4391-a524-f7cbd7b55f48` and `f8b32600-1ff1-42d9-ac61-be4df83fe4d0` remain preserved. The first exposed the fixture reader's required FileShare.ReadWrite setting. The second connected through actual Revit MCP and reached a native write approval; its approval was not answered before the explicit test abort. Cleanup verified the two pre-existing Revit process/start identities and registration hashes, restored the exact installed Foundation registration and removed temporary copied authentication. Test cleanup never saves or synchronizes a working model.

## Limits

The installer remains unsigned. New account browser consent, another computer, other Revit versions, production worksharing/large models and broader form schemas need separate qualification. The product delegates runtime behavior to Codex; it adds no provider framework, reflection discovery, visual planning, autonomous workflow engine or model merge mechanism.
