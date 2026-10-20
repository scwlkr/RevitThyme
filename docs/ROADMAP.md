# Roadmap

RevitThyme grows through focused tools, shared operations, inspectable results and explicit recovery. This roadmap describes direction, not release dates or unqualified compatibility promises.

## Available

| Area | Delivered scope |
| --- | --- |
| Foundation | C# Core, native add-in, WPF, inspection and checked renaming |
| MCP and CLI | 25 MCP tools, including seven TimberFold controls, explicit sessions, structured inspection, selection, atomic parameter edits and retained outcomes; protocol 8 |
| TimberFold | Qualified planar exterior-shell cutting kits, mapped guides, native review documentation, read-only recovery and single Undo; physical fabrication outstanding |
| Native utilities | Four-plane View Range editor and associative dimensions for bounded rectangular rooms |
| Trusted execution | Opt-in Roslyn query/modify, rollback queries and independent native Cabin Eval |
| Built-in Agent | Dockable Codex chat, sign-in, streaming, approvals, saved sessions and skills |
| Distribution | Per-user Windows installer, verified bootstrap, global `rthyme` and update checks |
| Development | Signed local packages and a separate process with a copied, pinned fixture |

[Features](../FEATURES.md) and [changelog](../CHANGELOG.md) describe behavior. [Verification](../verification/README.md) records what was checked.

## Next priorities

- Document open/activate/save and the [Home, background close and view-switching slice](../verification/ui-lifecycle.md) landed in PRs #39/#40. The next candidate adds synchronous active close only with an explicitly selected already-open clean handoff document, and preserves identities when a third-party add-in cancels close. Last-document close to Home still needs request-specific native command ownership; no posted Close fallback is included. Modal recovery and bounded model promotion remain separate; keep changes above 512 native IDs independent. Preserve the earlier [image](../verification/view-images.md) and [document lifecycle](../verification/document-lifecycle.md) evidence.

- Keep user, developer and agent onboarding aligned with behavior through shared docs and automated checks.
- Improve reproducible public examples and diagnostics.
- Broaden qualification across machines and representative models before expanding compatibility claims.
- Establish public publisher signing separately from local development signing.
- Measure TimberFold material/kerf, cut calibration and representative joints, and assemble the supplied small gable test before claiming physical acceptance. `ManufacturingReady` remains false.

## Under consideration

Exploration and planning experiments from the [original plan](../2026-10-14_NEW_PLAN.md) remain deferred. Additional tools and Revit versions require a use case, implementation and native acceptance. An issue or historical plan does not make a feature available.

Propose bounded improvements through [public issues](https://github.com/scwlkr/RevitThyme/issues/new/choose). See [contributing](../CONTRIBUTING.md) and [governance](../GOVERNANCE.md).
