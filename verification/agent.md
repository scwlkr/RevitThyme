# Built-in Agent verification

Scope: only the Built-in Agent row of the complete `2026-10-14_NEW_PLAN.md`, building on completed Dynamic execution. Exploration has not been started. All delivery is local.

## Source and runtime

The C# solution and portable filter build with warnings treated as errors. `project.cmd check` passes **96 Core**, **35 MCP** and **13 Agent** checks. The Agent checks exercise out-of-order RPC correlation, streaming while approval is pending, opaque approval IDs, RPC errors, cancellation/late responses, disconnect completion, initialization, ChatGPT login protocol, listed-session resume, unrelated-session rejection, explicit skill input, failed/early turn completion and unsupported requests. PowerShell syntax, six-project architecture and proprietary-binary exclusion checks pass.

The clean committed-source rebuild changes .NET's embedded `AssemblyInformationalVersion` Git revision from the base revision used during the pre-commit qualification build. The delivered package retains the exact native-qualified DLLs. A BCL PEReader comparison confirms identical method IL, exception regions, signatures, type/field definitions, literal strings and non-revision custom attributes across Addin/Core/UI/Codex. The private comparator and report are `artifacts/agent/compare-binaries` and `binary-comparison.json`; this is build verification, not a product reflection-discovery feature.

The real pinned **Codex CLI 0.162.1** runtime probe passes authentication read, model catalog, skill discovery, sole `revitthyme` MCP discovery and ChatGPT browser-login initiation/cancellation. Probe evidence is `artifacts/agent/probe/c0d6cf35-c98c-490f-ab44-0ccbdf2c155d/result.txt`; temporary copied authentication was removed. Full fresh browser consent was not performed; authenticated tests use an existing user-owned ChatGPT sign-in copied only into the disposable test home. Product sign-in is owned by Codex and opens its supported browser flow.

The initial configuration probe showed Codex merging user MCP configuration with overrides. The final client therefore uses a dedicated Codex home, verified to expose only the process-pinned RevitThyme server. Native WPF requests use ordinary fields, with unsupported form shapes left for Decline. Shell/apps/plugins/hooks/browser/computer/delegation tools are disabled in this runtime. Dynamic C# retains its existing host opt-in and caller trust gates.

## Package

The final local package is `packages/RevitThyme-0.9.0-agent-087b9f3e5fa94ca7ae16875d9b889fb5`, with adjacent ZIP/SHA-256. **18 isolated installer checks pass**, including installed official-runtime startup, skill/client inclusion, modified-client rejection, tamper/identity/reinstall/receipt checks and recoverable unregister. Evidence: `artifacts/foundation/install-test/06be8690-f21a-4ec1-9f2d-16fa53819bf8/result.json`. `artifacts/agent/package-native.json` confirms that native product assemblies, Roslyn dependencies, MCP DLL/Core, Codex executable and bundled skills exactly match the prepared native test bundle.

The runtime ZIP is pinned to upstream `rust-v0.162.1`, SHA-256 `4b17206ed3045de64064b2016627d51fba3bde386d03838312054882a64d781d`. Apache-2.0 license and upstream notices are retained. The package includes no Autodesk DLLs, test loader, model, credential, previous implementation or voice assets.

The corrected Windows installer passes **14 executable setup checks**, including Windows PowerShell 5.1 installation, runtime startup, repeat install, foreign-registration refusal, recoverable uninstall and failed-child rollback. Evidence: `artifacts/foundation/install-test/d2f400e4-f62e-47c2-a3e8-22365ac68963/setup-result.json`. The final local installer is `artifacts/agent/setup-release-final/RevitThyme-0.9.0-win-x64.exe`, with adjacent SHA-256 and release metadata. Two separate production guard checks refuse test-mode arguments and installation while Revit is open, preserving original process identities and registration hashes (`artifacts/agent/production-guards.json`). Bootstrap checks pass **174 cases**; nothing was published or installed into production.

The first executable test found the new Agent allowlist helper missing from the uninstaller's retained scripts. The corrected setup retains `agent-files.ps1`; the failed run and superseded executables remain preserved, and only the corrected output is delivered.

## Native qualification

The final run `artifacts/agent/live/f490c933-3667-411a-9bc1-69c96c911cee` passes **17 actual Revit checks**, in owned process **8540**, Revit **27.3.0.28**, using its new `Foundation-Test.rvt`. The user loaded the two unsigned qualification add-ins. Built-in Agent's native milestone is accepted; Exploration remains deferred.

The real Codex App Server completed two streamed turns using **six native MCP calls**, **268 message deltas**, **zero tool errors**, and only `revitthyme`. Independent Revit API readback verifies the requested view names and element/file invariants. One native Undo restores each edit. The stored conversation resumes after a complete client restart through a fresh process-pinned MCP connection. A third turn is interrupted through the real runtime and reports `interrupted`. The actual docked WPF approval controls approve only the explicitly authorized fixture renames. The native pane is visible and laid out; [native capture](../docs/images/native-agent.png) is produced by its Revit-hosted WPF visual, with no mock UI or image reconstruction.

Reports include `native-result.json`, `events.jsonl`, `approval-3.json`, `approval-5.json`, `mcp-status.json`, `thread.json`, `agent-panel.png`, `plan.json`, `launch.json`, and `cleanup.json`. Cleanup restores the original registration, removes the temporary copied credential, stops only the owned test/runtime, and independently verifies the two original Revit process/start identities and all original registration hashes. No production model is a fallback, save or sync target.

Failed/interrupted runs `125c8193-913e-4391-a524-f7cbd7b55f48` and `f8b32600-1ff1-42d9-ac61-be4df83fe4d0` remain preserved. The first exposed the fixture reader's required FileShare.ReadWrite setting. The second connected through actual Revit MCP and reached a native write approval; its approval was not answered before the explicit test abort. Cleanup verified the two pre-existing Revit process/start identities and registration hashes, restored the exact installed Foundation registration and removed temporary copied authentication. Test cleanup never saves or synchronizes a working model.

## Limits

The installer remains unsigned. New account browser consent, another computer, other Revit versions, production worksharing/large models and broader form schemas need separate qualification. The product delegates runtime behavior to Codex; it adds no provider framework, reflection discovery, visual planning, autonomous workflow engine or model merge mechanism.
