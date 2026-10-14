# Features

Current user-facing features span the preserved pyRevit suite and the native replacement. Detailed support and installation boundaries live in the linked guides.

| Feature | Behavior |
| --- | --- |
| Suite Status | The legacy ribbon reports suite/host versions and current document identity. Its read-only operation is shared with the legacy bridge. |
| Tool Settings | Select the existing external TimberFold location without copying its implementation into the suite. Local configuration stays outside release packages. |
| TimberFold inspection | Inspect candidate walls/roofs and diagnose source-file configuration through the preserved legacy tool. Fabrication generation belongs to the independently maintained TimberFold installation. |
| [Visual View Range](docs/VISUAL-VIEW-RANGE.md) | Capture a supported native plan, inspect interactive sections and edit range offsets/references. Review changes before native Apply; retained outcomes and readback support recovery. Imperial controls use 1/8-inch display/editing increments while metric options remain available. |
| [Native context and element inspection](docs/NATIVE-MCP.md) | The fixture-bound MCP identifies the exact process/document/view and inspects category pages or numeric IDs. Results include continuation, missing IDs and explicit scan limits. |
| [Project Health Check](docs/NATIVE-MCP.md) | Review missing view templates, duplicate names/numbers, missing door marks, unplaced rooms and warnings. Counts, samples and coverage describe the findings; the operation applies no fixes. |
| [Installation and recovery](docs/DEPLOYMENT.md) | Versioned packages, hashes and backups support the preserved suite installer. The native lab has a separate immutable bundle, registration and guarded MCP target rotation. |

Production-model native MCP operations, activity/history UI, sheet/export jobs and family recipes are planned; they are not available through the current fixture-bound server.
