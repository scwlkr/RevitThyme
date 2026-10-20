# Command-line interface

Setup adds `rthyme` to your Windows user PATH. Open a new terminal after installation. Contributors can run the corresponding source command through `project.cmd cli`.

## Discover commands and versions

```powershell
rthyme --help
rthyme --version
rthyme version --json
rthyme sessions --json
```

Version output separates the executing CLI, development checkout and registered installation. Update commands target the installed copy. Native commands require a running compatible add-in and an explicit session.

Version 0.10.0 uses protocol 8 and package `0.10.0-timberfold.4`; install matching CLI, MCP and add-in binaries together.

## Inspect a chosen session

Use an absolute binding path returned by `sessions`. Confirm process, document and view; do not select a process simply because it appears first.

```powershell
$session = 'C:\Test\sessions\session-id.json'
rthyme inspect --session $session --json
```

Captures contain private model paths and metadata. Model events invalidate them; reopening creates a new document identity.

## Discover open documents (unreleased)

The `0.10.0-lifecycle.5` candidate adds a read-only command, including at Home. Install matching protocol-13 binaries first:

```powershell
rthyme list-documents --session $session --limit 1 --request-id CALLER_GUID --json > C:\Test\documents.json
rthyme list-documents --session $session --capture C:\Test\documents.json --offset NEXT_OFFSET --limit 1 --json
```

Use each page's exact `nextOffset`; its captured target and inventory token must still agree with native state. Open/close or metadata changes require a new capture and first page. A returned ID identifies an already-open document; listing does not activate or authorize editing it. Flags include dirty, active, family, linked, workshared, read-only and saved-path state. No file is hashed or saved. Supplying `--request-id` lets callers retain the original read identity for recovery; recovered pages describe the original point in time. Keep captures private.

The read-only flag reports Revit's dynamic document state, not whether its disk file can be overwritten. Explicit save independently refuses native `IsReadOnlyFile` with `read_only_file`, even when the document is editable in memory or already clean. It does not save elsewhere or discard edits.

## Inspect parameters

```powershell
rthyme inspect-elements --session $session --element ELEMENT_UNIQUE_ID --json > C:\Test\parameters.json
rthyme inspect-elements --session $session --element ELEMENT_UNIQUE_ID --capture C:\Test\parameters.json --parameter-offset 20 --json
```

Replace 20 with the actual returned `nextParameterOffset`; stop at null. Parameters include exact IDs, typed raw values, shared status/GUID, built-in identity and native `hasValue`. Names alone are not identity, and assigned does not mean useful or nonempty. See [parameter identity](MCP.md#parameter-identity).

## Native command inventory

| Command | Purpose |
| --- | --- |
| `sessions` | Discover bindings; optional `--directory ABSOLUTE_PATH` |
| `inspect` | Read document/view context, optionally to a JSON capture |
| `list-documents` | Read visible session document IDs/flags; continue with exact `--capture PREVIOUS_RESULT --offset NEXT_OFFSET` |
| `inspect-elements` | Read paged parameters for `--element UNIQUE_ID` |
| `rename-view` | Rename the exact view using `--capture ABSOLUTE_FILE` and `--name NAME` |
| `view-range get` | Read all four planes into JSON |
| `view-range set` | Apply `--range SETTINGS_FILE` against `--capture RANGE_RESULT` |
| `room-dimensions preview` | Plan a pair for `--room UNIQUE_ID` |
| `room-dimensions create` | Create the exact `--capture PREVIEW_RESULT` pair |
| `timberfold inspect` | Inspect exterior-shell scope/profile; optional `--profile` and `--scope` JSON files |
| `timberfold preview` | Retain a layout under fresh `--run-id GUID`; optional profile/scope |
| `timberfold generate` | Generate from `--preview-run-id GUID` under a fresh `--run-id GUID` |
| `timberfold reconcile` | Check `--generation-run-id GUID` against the current native kit under a separate `--run-id GUID` |
| `timberfold status` | Read retained `--run-id GUID` progress/outcome |
| `timberfold cancel` | Cooperatively cancel retained `--run-id GUID` |
| `timberfold verify` | Rehash retained `--run-id GUID` evidence |
| `get-operation` | Recover an outcome using `--request-id GUID` |
| `run-query`, `run-modify` | Trusted C# with source, capture, trust and request ID |
| `update check`, `update download`, `update install` | Check/download/install a release |

Native commands take `--session ABSOLUTE_FILE`. `rthyme --help` gives exact flags. Generic parameter editing is exposed through MCP; there is no CLI `set-parameters` command.

## TimberFold cutting kits

```powershell
rthyme timberfold inspect --session $session --json
rthyme timberfold preview --session $session --run-id NEW_PREVIEW_GUID --json
rthyme timberfold status --session $session --run-id PREVIEW_GUID --json
rthyme timberfold generate --session $session --preview-run-id PREVIEW_GUID --run-id NEW_GENERATION_GUID --json
rthyme timberfold status --session $session --run-id GENERATION_GUID --json
rthyme timberfold verify --session $session --run-id GENERATION_GUID --json
rthyme timberfold reconcile --session $session --generation-run-id GENERATION_GUID --run-id NEW_RECOVERY_GUID --json
rthyme timberfold cancel --session $session --run-id SAME_GUID --json
```

Replace GUID placeholders with retained identities. Starts acknowledge acceptance; poll status until terminal. Generate requires `PreviewReady`. Generation creates annotation-only native documentation in one Undo group and writes cutting/guide files; it never saves the source RVT. Status/cancel use separate control channels. Verify rehashes delivered evidence; Reconcile checks current native documentation read-only and retains separate evidence. Preserve the original ID and exact request after a lost response; do not start another generation to resolve uncertainty.

`KitReady` means digital checks passed. Physical calibration, cutting and assembly remain outstanding; `ManufacturingReady` stays false. See [TimberFold usage](timberfold/README.md), [supported geometry](timberfold/support.md) and the [small physical-test download](timberfold/delivery.md).

## A checked rename

Try only on a disposable project:

```powershell
rthyme inspect --session $session --json > C:\Test\view.json
# Review the captured document and view before continuing.
rthyme rename-view --session $session --capture C:\Test\view.json --name 'Level 1 - Test' --json
```

The operation checks identity and prior values, verifies native readback and creates one Undo item. It does not save or synchronize. Use native Undo to restore the name. Reinspect after changes. If completion is uncertain, recover the original request and inspect native state before another write.

## Updates and errors

```powershell
rthyme update check --json
rthyme update download --destination C:\Test\Downloads
# Preserve unsaved work and close Revit first.
rthyme update install
```

Check process exit status, structured outcome and diagnostics; do not infer native success from text alone. See [updates](UPDATES.md), [recovery](MCP.md#scripted-operation-flow) and [troubleshooting](TROUBLESHOOTING.md).
