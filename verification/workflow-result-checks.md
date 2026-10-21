# Workflow Result Checks qualification

Product 0.13.0 / `0.13.0-checks.1` uses native bridge protocol 14 and 38 MCP tools. Result checks compare declared native observations and permitted changes with a retained operation. A failed check does not undo an Applied operation. This record separates actual Revit behavior, real Agent integration, offline checks and package verification.

## Qualified source and package

| Identity | Qualified value |
| --- | --- |
| Product and qualification-client source | `fe85d04b7a0a85db8569a49f904f157433da1524` |
| Immutable package manifest SHA-256 | `2aba918c2e7c9e3b819909aa0cd960844e41072146a9d9ba3b1db882994044a4` |
| Native host | Revit 2027.3, file version `27.3.0.28`, Windows x64 |
| Package / product | `0.13.0-checks.1` / `0.13.0` |
| Native qualification client SHA-256 | `729b333b2f95b1afb8c7c5ba316441fcbfe6b5cad8d442cbe1fbedec9745cdca` |
| Production Windows setup SHA-256 | `d7e9be8d42c61870afa364c0e07f0db246222e56e45258541a2e3ff270b3aafd` |

The qualification client verified each required loaded module against the installed package path, manifest hash and product version containing the exact source revision. Native requests used the installed MCP executable through the official C# SDK, with one copied nonworkshared fixture and exact process/start/session/document pins. Source models and raw machine-readable reports remain private.

| Qualified binary | SHA-256 |
| --- | --- |
| `RevitThyme.Addin.dll` | `4a82a5cc610a40fdf3b3a9188121a51291028b93d4c3de1a600d44d644ab7e9d` |
| `RevitThyme.Core.dll` | `6aff9832630644ae5db9f98d2a71c45db4843ce700d2db9acd39c833033c37e8` |
| `RevitThyme.Codex.dll` | `f7cfa5b26ced924165586534b338cb8e280470ec4dbebbea71937b2abcc061e8` |
| `RevitThyme.UI.dll` | `ce8d98782c5b0e7f14fa9cf3efd6a768ae0dadae89b19119c4ccbc327f8f915e` |
| `mcp/revitthyme-mcp.exe` | `72457a68be422b992b3404ef5a2a37493dcc83e275cc9f50da325b65b4c59070` |

The package was signed with the qualification machine's persistent local development certificate before hashing. This establishes local development trust, not a public publisher identity. The public Windows setup executable remains unsigned. Later documentation or installer-source revisions do not qualify replacement runtime bytes; compare the package manifest and loaded hashes above with the release metadata.

## Actual Revit results

The installed-package external SDK run passed **277/277 assertions**. The total includes repeated targeting, outcome and preservation assertions; it does not represent 277 distinct features.

| Case | Observed native result |
| --- | --- |
| Intended results | Equality against an input, existence, absence and numeric range checks pass against native readback. A pure observation preserves the native revision. |
| Query and modification | A checked query retains Inspected; a view rename retains Applied and its native changed IDs. Exact request replay and `get_operation` preserve the original action identity. |
| False success and unrelated edits | Returning a success value without the expected native change fails verification. An intended rename plus an unrelated project-information change reports the unexpected modified ID while preserving Applied. No automatic Undo occurs. |
| Additions | Native Generic Model direct shapes pass the declared category/count policy. Wrong permitted category and exceeded maximum fail independently of the intended-result comparison. Actual event IDs and separate native category readback agree. |
| Deletion | A deleted native object fails an existence check. Native Undo restores it; separate cleanup removes only the owned seed object. |
| Observation failures | Compile errors, runtime errors and oversized JSON produce errors without persisting checker changes. A direct `Parameter.Set` attempt and a checker-created transaction cannot persist a write or manufacture a passing result. |
| Cooperative budget | Immediate policy/readback succeeds at 100 ms without counting compilation or ID-scan overhead. A deliberately slow observation fails with budget exhaustion. This is not a hard interruption guarantee. |
| Attribution and recovery | Preparation IDs are deterministic and distinct from the action. Retained preparation and observation receipts survive MCP client restart. Changed plan, input or source identity, missing original/preparation receipts and an intervening native revision cannot produce a new passing observation. |
| Undo and preservation | One native Undo restores the intended and unrelated modifications, or removes the owned additions. Baseline and copied model file hashes, installed registration and native modification state are preserved. |

Recovery of a completed observation is historical evidence for the original checked target. It does not certify subsequent model changes. These tests exercise the declared current-document checks and guards, not every possible trusted C# side effect.

## Real Agent integration

The real local Codex App Server and `AgentSession` integration used by the dockable panel passed **18/18 assertions**, with **6/6 additional preservation assertions**. This was a real model/runtime round trip against the same installed native package, rather than a simulated Agent response. It does not claim new visual automation of the pane.

The Agent discovered the bundled creation skill, completed a plain-language request, saved/retrieved/ran a reusable definition through MCP and received passing automatic native verification. The stored definition included a typed input, expected result, executable assertion and result-check plan. Independent execution with another input compiled in Revit, passed its automatic check and returned true from its stored assertion.

A sensitivity probe changed the disposable native view name. The Agent-generated check then failed while the original query remained Inspected and returned the actual changed name. One native Undo restored the name and modification state. The copied model file and protected baseline remained unchanged. Temporary sign-in material was removed and the original authentication file was preserved; credentials and runtime traces are not published.

## Source and delivery preparation

- Full project checks: 600 Core, 150 MCP, 13 Agent, 6 WPF and 47 development-safety checks passed. Offline integration checks cover orchestration, input/plan validation, CLI behavior and interrupted-transport cases; they do not substitute for the native run above.
- Exact-package checks: 21 isolated installer, 25 global-command and 20 Windows setup checks passed, with package signatures verified. These use isolated installation targets and preserve unrelated state.
- The actual production setup refused test-only `/TESTROOT` parameters with exit 1 and refused installation while Revit was open with exit 7. It required normal Revit closure while preserving unsaved models; registration, setup receipt and the exact user PATH remained unchanged.
- The prepared seven-asset release set, adjacent checksums and `release.json` agree with the clean source revision, production setup and immutable package above. This is local candidate verification, not proof of public availability.
- Bootstrap checks passed 174 assertions under PowerShell 7 and 174 under Windows PowerShell 5.1.
- Website dependency installation, production build and source-contract checks must be rerun against the final documentation source before publication. Public asset and live website readback remain separate delivery gates.

The owned development Revit process was closed normally after native name/modification-state restoration. No forced termination was required.

## Reproduce within an owned development run

Use the [isolated development workflow](../docs/DEVELOPMENT.md) to prepare and launch a copied disposable fixture with the matching installed signed package. Confirm `dev-status`, process/start/session/document identity and loaded package before testing. Never substitute a working model. From the matching source checkout:

```powershell
$run = 'ABSOLUTE_OWNED_RUN'
$bundle = 'ABSOLUTE_INSTALLED_PACKAGE'
$env:REVITTHYME_QUALIFICATION_MCP = Join-Path $bundle 'mcp\revitthyme-mcp.exe'
$env:REVITTHYME_QUALIFICATION_SKILLS = Join-Path $bundle 'skills'
dotnet run --project .\tests\Mcp -c Release -- --result-check-live $run
dotnet run --project .\tests\Mcp -c Release -- --result-check-agent-live $run
```

The Agent probe requires a signed-in test account and creates a temporary isolated authentication copy, which it removes afterward. Preserve the returned private reports and exact hashes. Stop only the owned development process through the documented workflow after completing native readback and preservation checks.

## Limits

Checks cover their declared observations and allowed-change policy. They do not prove model completeness, universal correctness, security isolation or successful testing of arbitrary saved tools. Trusted C# has full process permissions; current-document guards cannot undo arbitrary file, network or external-document side effects. Budgets are cooperative and cannot forcibly stop blocked or infinite code.

Plans support 1–16 checks; source is bounded to 24 KiB, returned JSON to 8 KiB and native change coverage to 512 IDs. See the [MCP contract](../docs/MCP.md#workflow-result-checks) for input/data bounds and the [execution guide](../docs/DYNAMIC-EXECUTION.md#workflow-result-checks) for outcome/recovery rules. There is no automatic retry, Undo, repair, save/sync, managed fixture runner or durable cross-Revit-session operation history. Other machines, Revit builds, worksharing and cloud models need separate qualification. TimberFold physical fabrication remains outstanding.
