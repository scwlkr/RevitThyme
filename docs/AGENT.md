# RevitThyme Agent

The optional Agent panel runs the official local Codex App Server and talks to the same RevitThyme MCP and shared C# operations as external agents. Native tools continue to work without an account. This stage adds conversation and skill access inside Revit; Exploration remains deferred.

## Use

1. Install the Agent package with Revit closed normally. It includes Codex CLI **0.162.1** for Windows x64, with its Apache-2.0 license and notices. No manual MCP JSON or API key is required.
2. Open Revit and select **RevitThyme → RevitThyme → Open Agent**. In the dockable panel, select **Connect Codex**, then **Sign in** if needed. Codex owns the ChatGPT browser sign-in and callback. Return to the panel and select **Refresh** after completing consent. **Cancel sign-in** cancels a pending login.
3. Start a conversation, choose a model from the runtime catalog, and enter your request. The document/view shown above the chat is refreshed before sending. A model catalog is not an entitlement guarantee; a completed turn confirms access for that request.
4. Choose **Automatic** skill selection or an available skill. Replies stream into the panel. Expand a tool entry to inspect its native results. Approval and question requests appear inside the panel with Accept/Decline and ordinary input fields. Unsupported forms remain declined.
5. Use **Stop** to interrupt an active turn. Review any unconfirmed Revit operation with `get_operation` before retrying. Use native **Undo** to reverse the resulting edit. Select a listed conversation and **Resume** to continue its saved history.

ChatGPT sign-in opens the supported system browser once. Revit work, conversations, questions and approvals remain in the dockable panel. Subscription access is subject to OpenAI's account and workspace policies. RevitThyme does not require the separate preview token-sharing program or its client registration.

## Local state and connection

The default Agent folder is `%LOCALAPPDATA%\RevitThyme\agent`: `codex-home` holds Codex-managed credentials and conversation history, `workspace` scopes panel conversations, and `skills` holds personal/company skills. Agent state persists across package upgrades. It is private user data and is excluded from packages and repository source. Codex itself manages authentication and token refresh; the panel never reads or logs tokens. Remove this folder only after preserving wanted conversations and signing out through Codex; uninstalling the toolkit leaves it intact.

The panel launches one owned stdio process with this Agent Codex home. Its MCP server is pinned to the parent Revit process/session file. Closing the panel's pane keeps the conversation running; closing Revit stops its child runtime. Reconnect uses a new client and refreshes session identities. Resuming history does not reuse a dead Revit connection: the new runtime receives this process's current MCP binding, and the agent must inspect fresh document/view context.

Shell, apps, plugins, hooks, browser/computer tools and agent delegation are disabled for this panel runtime. Other tools' configuration and credentials are not inherited. Personal skills are trusted instructions; install only skills you intend to run. The three bundled skills cover native inspection, verified edits and reusable tool creation. Each is an ordinary `SKILL.md` folder and can be version-controlled and shared. Personal skills are discovered by the runtime, not copied into the install bundle.

Trusted C# remains disabled by default and requires `REVITTHYME_TRUSTED_CODE=1` at Revit startup plus the existing per-call trust acknowledgement. The Agent adds no new execution bypass. Native operations retain target checks, expected-value guards, transactions, structured results and request history from Dynamic execution.

## Create a reusable tool

Ask the Agent to create a tool in plain language, for example: “Save a tool that counts walls whose names contain text I provide. Include an input and a test.” Choose **create-revit-tool** or leave skill selection on **Automatic**. The Agent inspects the relevant native data, writes a parameterized C# method body and saves it through `save_tool`. Its reply should include the tool ID, definition digest, inputs, expected result and test assumptions.

Saved definitions live in `%LOCALAPPDATA%\RevitThyme\tools`, outside the versioned installation and conversation history. They include the original request, typed input descriptions, execution mode, expected result and one or more executable test assertions. Saving a definition does not run it, compile it or establish that its test passes. Preserve the ID and digest to retrieve it in a later conversation with `get_tool`.

When asked to run a saved tool, the Agent reads its definition and submits validated inputs through `run_tool`, using a fresh native capture and a caller-owned request ID. Execution still requires trusted C# opt-in and acknowledgement. Test on a disposable model first. A test assertion runs only when explicitly requested with its test name; it returns its result through the rollback-query contract. This is an individual saved-tool workflow; automatic repair and a managed test runner are separate features.

See the [MCP contract](MCP.md#saved-revit-tools), [CLI commands](CLI.md#saved-revit-tools) and [execution limits](DYNAMIC-EXECUTION.md#saved-tool-inputs-and-tests).

## Define result checks

When a tool needs a verifiable result, ask the Agent to include a result-check plan: for example, confirm a renamed view's native name and allow only the intended view to be modified. The plan must state observable expectations and permitted changes before execution. The Agent should inspect native readback and report the original operation outcome alongside the check result.

Checking follows a definite successful operation; it does not convert uncertain execution into success. If a change was Applied but a check failed, the model may already contain that change. The Agent should explain the mismatch or uncertainty and inspect the retained outcome before proposing another edit. There is no automatic Undo or retry. A tool without a plan behaves as before. See [Workflow Result Checks](DYNAMIC-EXECUTION.md#workflow-result-checks).

## Development and qualification

Build with `project.cmd check`. The `RevitThyme.Codex` project uses BCL JSON/stdio APIs and has no WPF or Autodesk dependency. UI owns presentation, and Addin owns the pane and pinned connection. `project.cmd agent-prepare`, `agent-start`, `agent-check`, and `agent-cleanup` manage a new disposable Revit qualification process. All writes target its fixture; existing processes remain open. If an installed Foundation registration exists, the script parks only that exact hash-checked registration during the new process's startup, then restores it. Run cleanup after failure as well as success.

For development, `REVITTHYME_CODEX_PATH` can select an absolute executable and `REVITTHYME_AGENT_HOME` can select an isolated state folder. The package uses its own bundled runtime by default. `agent-start -ReuseCodexSignIn` copies the current CLI file credential only into the disposable test home; cleanup removes that temporary credential. It is an explicit qualification convenience, not product credential migration.

See [verification](../verification/agent.md) for source, package and actual Revit evidence. The protocol follows the official [Codex App Server documentation](https://learn.chatgpt.com/docs/app-server), with schemas generated from the pinned runtime. Credentials, worksharing/large-model behavior, other Revit releases, another machine and signed public distribution need separate qualification.
