# C# quality checks

Run these commands from the repository root on Windows with .NET 10, Git and uv available:

```powershell
.\project.cmd quality prepare
.\project.cmd quality test
.\project.cmd quality collect
.\project.cmd quality crapper
.\project.cmd quality dryer
.\project.cmd quality mutator --file src/RevitThyme.Core/TimberFold/TimberDxfReadback.cs --scan
.\project.cmd quality mutator --file src/RevitThyme.Core/TimberFold/TimberDxfReadback.cs
```

`prepare` installs pinned dependencies into this checkout's ignored `artifacts/quality` directory. The pins live in `tools/Quality/toolchain.json`. Crapper currently uses the C# contribution submitted in [upstream PR #6](https://github.com/unclebob/crapper/pull/6), pinned to its exact commit on the contributor fork. No globally installed tool is replaced.

`collect` builds the solution and executes the offline Unit, MCP, Agent, UI and quality-tool suites under the pinned .NET coverage collector. It archives earlier conventional Cobertura files before collection and records source, Git revision, command and report hashes. `coverage-status` checks that receipt. Source changes, new revisions, changed report bytes or extra stale reports require fresh collection. Crapper validates the receipt before scoring. Its default threshold is CRAP below 10; `--no-coverage` explicitly requests a syntax-only audit and cannot demonstrate coverage. Cobertura attributes coverage by source lines and has limited precision when multiple executable bodies share a line.

The native C# dryer compares normalized executable-body syntax with Jaccard similarity, retaining called and constructed type names. It reports candidates at similarity 0.8 or higher, with minimum sizes of four lines and twenty syntax nodes. Review candidates against the domain: the report identifies structural similarity, which alone does not justify sharing distinct contracts. It exits 3 when candidates remain.

The native C# mutator changes one Roslyn-selected arithmetic, comparison, Boolean, logical or integer-zero/one site at a time. A scan lists sites without executing them; optional `--coverage PATH` annotates observed line hits but never filters an executed run. Each execution copies Git-tracked and nonignored files into its own worker, preserving original source bytes and encoding. It builds a passing baseline before mutating, runs the configured product suites, restores the original bytes after each attempt and retains logs. `tools/Quality/QualityCommands.json` defines the exact build and test argument vectors. Commands run directly without a shell.

A compiler-invalid mutant is separate from a killed mutant. Build infrastructure failures and timeouts remain unconfirmed. Surviving or unconfirmed mutants fail the gate. At least one killed mutant and a complete catalog are required for a pass. `--max N` bounds a run and exits 4 until complete. Repeating the same command resumes only when source, commands, engine and catalog match; reports from changed inputs are rejected. Keep incomplete reports and their workers until you decide whether to resume or remove that exact run. Reports must stay under this checkout's `artifacts` directory.

For a focused cut-file mutation run, append `--config tools/Quality/CutFileMutationCommands.json`. This builds the unit project and runs its public cut-file behavior checks using `--suite TimberFoldCutFiles`. The unit executable accepts exact suite names; invalid or incomplete selectors exit 2, and no arguments still run every suite. Focused mutation evidence does not replace the full `project.cmd check` regression gate.

These checks execute offline. They do not install assemblies, exercise Autodesk Revit, prove fabrication tolerances in a physical part or qualify a release. Changes to native behavior still require the pinned disposable development session and exact-build checks in [development](DEVELOPMENT.md).
