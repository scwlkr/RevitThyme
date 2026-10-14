# Development order

This roadmap follows the development stage table in [the full plan](../2026-10-14_NEW_PLAN.md). The current request authorizes only the Install RevitThyme experience on lines 81–85, packaging the existing toolkit through Dynamic execution.

| Stage | Scope | Acceptance |
| --- | --- | --- |
| Foundation | C# solution, native add-in, shared operations, WPF, basic CLI | Inspect and modify a disposable native Revit model; verify rollback and Undo |
| MCP | Official C# MCP SDK, 8–12 generic tools, external Codex connection | Reliable scripted tasks through MCP |
| Native utilities | View Range and Room Dimensions | Both tools work by button and MCP |
| Dynamic execution | Roslyn, transactions, disposable models, Cabin Eval | Evaluation executes and scores actual native results |
| Built-in Agent | Codex App Server, authentication, sessions and skills | Work inside Revit without leaving it |
| Exploration | Optional planning and reflection experiments | Measured improvement over baseline |

Foundation uses Inspect View and Rename View to prove the shared stack. MCP adds 12 generic tools, native inspection, selection and atomic parameter editing. Native utilities adds shared View Range and rectangular Room Dimensions with WPF ribbon buttons, CLI and four MCP tools. Dynamic execution adds Roslyn query/modify, explicit trust opt-in, transactions, disposable model qualification and independent native Cabin Eval scoring. See [execution guide](DYNAMIC-EXECUTION.md) and [verification](../verification/dynamic-execution.md). Built-in Agent and Exploration remain deferred. Earlier implementations remain migration references; their historical evidence does not certify this C# product.

The [install stage](INSTALLATION.md) adds one per-user `.exe`, a verified GitHub bootstrap, supported-version/runtime detection and recoverable installation of the existing add-in, CLI and MCP. The current build supports full Revit 2027 only. Release candidates are unsigned and local; public publication, signing, additional Revit builds and another-machine qualification remain separate work. No package manager or updater is added.

See [Foundation acceptance](FOUNDATION.md) for current evidence and limitations.
