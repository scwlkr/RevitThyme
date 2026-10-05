# Implemented operations

Version 0.2.0 shares one IronPython implementation between the ribbon and Routes. Handlers accept uiapp, so pyRevit queues Revit API execution through ExternalEvent. No operation opens a transaction, saves, synchronizes or closes a document.

| Operation | POST path under http://127.0.0.1:48884/revitthyme | Input |
| --- | --- | --- |
| revitthyme_status | /status/ | {} or a target object |
| timberfold_inspect | /timberfold/inspect/ | {"target": ...} from status |

Status reports suite/pyRevit/Revit versions, build, session/document identity and readiness. Targets bind to process, pyRevit session, creation GUID and document instance. Reacquire after reload or document switch; these are concurrency identifiers, not authentication tokens.

Inspect returns wall counts, exterior wall/roof candidate IDs, skipped interior IDs and obviously unsupported wall IDs. It does not extract geometry, validate fabrication settings or prove support. Family/no-document, missing/wrong target, invalid arguments and configuration failures have explicit result states.

Only target is accepted as input. Preview/generate/verify and job operations are not exposed. A standalone named MCP server remains planned; the existing development bridge can invoke the shared implementation.

Ribbon: Suite Status, Tool Settings and Inspect TimberFold. Settings selects an existing external folder and writes user settings under LOCALAPPDATA/RevitThyme. Unconfigured inspection still reports model candidates and explicitly identifies missing configuration. Existing generation stays in TimberFold's own ribbon.

## Client execution constraint

Issue Routes requests serially. The development bridge and suite share pyRevit execution infrastructure; overlapping requests produced crossed/error responses in the first-host investigation. The verified eight-case contract driver runs serially. Concurrent clients and a dedicated queued MCP adapter remain unverified/planned in this preview.
