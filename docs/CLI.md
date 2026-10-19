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

## Inspect a chosen session

Use an absolute binding path returned by `sessions`. Confirm process, document and view; do not select a process simply because it appears first.

```powershell
$session = 'C:\Test\sessions\session-id.json'
rthyme inspect --session $session --json
```

Captures contain private model paths and metadata. Model events invalidate them; reopening creates a new document identity.

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
| `inspect-elements` | Read paged parameters for `--element UNIQUE_ID` |
| `rename-view` | Rename the exact view using `--capture ABSOLUTE_FILE` and `--name NAME` |
| `view-range get` | Read all four planes into JSON |
| `view-range set` | Apply `--range SETTINGS_FILE` against `--capture RANGE_RESULT` |
| `room-dimensions preview` | Plan a pair for `--room UNIQUE_ID` |
| `room-dimensions create` | Create the exact `--capture PREVIEW_RESULT` pair |
| `get-operation` | Recover an outcome using `--request-id GUID` |
| `run-query`, `run-modify` | Trusted C# with source, capture, trust and request ID |
| `update check`, `update download`, `update install` | Check/download/install a release |

Native commands take `--session ABSOLUTE_FILE`. `rthyme --help` gives exact flags. Generic parameter editing is exposed through MCP; there is no CLI `set-parameters` command.

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
