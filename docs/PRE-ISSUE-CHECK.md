# Pre-Issue Check

`preissue_check` reads a snapshot of the active host project document and evaluates deterministic rules. The ribbon button **RevitThyme > Pre-Issue Check** and POST `/revitthyme/preissue/check/` call the same operation. No transactions, regeneration, parameter changes, saves or synchronizations occur. Linked documents are outside this scope.

This is model housekeeping and configured firm QA. It does not certify code compliance or declare a project ready to issue. Geometry duplicates, dimension/annotation completeness and electrical or jurisdictional requirements are not checked.

| Check ID | Behavior |
| --- | --- |
| `room.placement` | No location: unplaced room. Placed with zero area: enclosure/redundancy review finding. Zero area alone cannot identify the cause. |
| `room.duplicate_number` | Repeated nonempty, trimmed, case-sensitive room number within the same created phase and design option. Main model is option `-1`. No spatial comparison and no combined main-model/option analysis. |
| `door.mark` | Empty instance Mark, read by built-in parameter rather than a localized display name. All phases/options in the host document are included. |
| `view.name` | Configured wildcard patterns for explicit Revit ViewType names. View templates and sheets are excluded from the view collection. |
| `view.template` | Configured requirement for an assigned template and/or exact allowed template names, within the same configured view types. It checks assignment, not individual template-controlled settings. |
| `sheet.name`, `sheet.number` | Configured wildcard patterns. Placeholder sheets are excluded unless opted in. |
| `sheet.duplicate_number` | Repeated trimmed, case-sensitive sheet numbers; same placeholder policy. |

Room and door checks cover all elements in the host document, including scheduled/unplaced rooms and historical phases. Findings may therefore concern work outside the current issue package. Every report records the scope; this release does not infer an issue selection or a firm's phase/option policy.

## Firm standards

Without a standards file, the intrinsic room/door/duplicate checks run. View name/template and sheet naming standards report `not_checked`; no naming policy is silently assumed.

Copy and adapt [the example](../config/preissue-standards.example.json) to `%LOCALAPPDATA%\RevitThyme\preissue-standards.json`. The check only reads this fixed user file. It is separate from TimberFold settings and replaceable extension code. The bundled example is illustrative and is never enabled automatically.

Naming patterns use case-sensitive wildcard matching of the complete trimmed value: `*` matches any sequence and `?` matches one character; other characters are literal. Matching has bounded work proportional to value length times pattern length, with no regex backtracking. No regular expressions, scripts or executable expressions are accepted. Template names match exactly. View `types` must be explicitly listed when view rules are configured; use API enum names such as `FloorPlan`, `CeilingPlan`, `Section` or `Elevation`. Configure `severity` per check ID as `info`, `warning` or `error`; the default is `warning`.

Standards use schema version 1 and reject unknown fields, invalid types, empty lists and overlong values. String lists are limited to 50 entries of at most 200 characters. Invalid standards return `invalid_standards` before reading elements. A malformed or unreadable user file is also an explicit configuration failure.

Configured view type names are checked against the actual host `ViewType` enum before element collection. A typo returns `invalid_standards`; unavailable host enum validation returns `host_validation_unavailable` and the check does not collect elements. A valid configured type absent from the model remains an explicitly empty checked scope.

Routes accepts only `target` and optional `standards`. Discover the target with `/revitthyme/status/` and submit requests serially, following the existing [client execution constraint](OPERATIONS.md#client-execution-constraint). The endpoint keeps pyRevit's `uiapp`/ExternalEvent execution; it does not open a new listener or bypass the Revit API context.

```json
{
  "target": {"session": "copy-from-status", "document": "copy-from-status"},
  "standards": {
    "schema_version": 1,
    "views": {"types": ["FloorPlan"], "require_template": true},
    "sheets": {"number_patterns": ["A-???"]}
  }
}
```

Omitting `standards` reads the fixed user file. An explicit `null` runs without firm standards for that request. No request accepts a file path. The operation rejects a missing/stale target, no document, family documents and unknown request fields using the shared operation contract.

## Reading a result

Outer operation `status: checked` means the report was produced; it is not a QA pass. Each `data.checks` entry has `checked` or `not_checked`, evaluated count, finding count, excluded IDs, unavailable IDs and diagnostics. Empty complete collections explicitly record `empty_checked_scope`. Partial collection or missing required fields mark the check `not_checked`, even when known findings can still be reported. Empty values that were successfully read remain distinguishable from extraction failure.

`data.findings` contain stable IDs, check/code/severity, element IDs/UniqueIds, messages and scalar evidence. IDs are deterministic from the check, finding code and affected element identities; unchanged findings retain IDs across repeated runs/order changes. If UniqueId extraction fails, the ID fallback is stable only within that document's element-ID lifetime. Compare reports only for the same document identity.

`summary.conclusion` is `findings_present`, `incomplete` or `no_findings`; `summary.incomplete` remains true when any check lacks coverage. A `no_findings` result describes only the recorded rules and scope. `readback` compares the document's modified flag before/after extraction. This narrow observation cannot prove full model equality, especially if the document was already modified. A changed flag returns `document_changed_during_check`.

## Verification and host qualification

Portable tests use scalar fixtures and a fake Revit adapter boundary. They cover deterministic output, phase/option grouping, standards validation, missing-data states, collection failures, shared operation/Routes targets, readback and the package allowlist. Run `python3 -m unittest discover -s tests -p 'test_preissue*.py'` from the checkout. No dependencies are added.

The adapter remains IronPython 2.7 compatible, but portable CPython execution does not prove IronPython/Revit behavior. On the authorized Windows/Revit host, still verify the packaged button and route, no-document/family/stale-target cases, real room/door/view/sheet extraction, configured/unconfigured results, and independent before/after model readback on a disposable fixture. Repeated Reload, concurrent clients and another machine remain separate host gates. No live model or host validation was performed on the Mac.

API assumptions were checked against Autodesk's official references for [Room members](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/75c9d2c7-a402-ea8b-9e7c-f8bc3510bbd5.htm), [spatial location](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/63092169-7a50-9b92-d886-f741adc211ec.htm), [area](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/6628cb12-ce88-a736-8fda-8e71f2d6361f.htm), [created phase](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/c6032e01-f7cb-b2ea-3312-697d14216a31.htm), [design option](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/5c20fe58-e301-6ddb-3438-666db5c586ee.htm), [view template assignment](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/2559f20b-87d4-e879-3139-7f555b251b71.htm) and [redundant-room area behavior](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/6e5bceec-5c87-f083-bd4d-12fa7ba78383.htm). These references do not replace verification against the exact installed Revit build.
