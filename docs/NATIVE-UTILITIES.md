# Native utilities

**View Range and Room Dimensions** use shared Core operations, the native ExternalEvent host and verified Revit transactions. Both tools work through the ribbon, CLI and MCP. [Trusted execution](DYNAMIC-EXECUTION.md) and the optional [built-in Agent](AGENT.md) build on the same foundation. See [first steps](QUICK-START.md) for a guided introduction.

## View Range

Open **RevitThyme → Views → View Range** in a floor, reflected ceiling or structural plan. Refresh captures the exact document/view/revision, four native planes and level choices. Choose a level and offset for Top, Cut plane, Bottom and View depth. The section shows their relative elevations; ceiling plans retain Revit's upward-looking range rules. Above/Below without a neighboring level cannot be plotted; Revit remains the validator of plane order and level choices.

Offsets default to architectural feet/inches, displayed to the nearest 1/8 inch; Up/Down changes a field by 1/8 inch. Decimal feet and signed architectural entry are supported. Millimeters and meters are available. Unedited fields preserve their exact native values behind rounded text. The diagram is a preview and creates no model elements.

Apply checks the captured revision and exact prior range, uses Revit's `CheckPlanViewRangeValidity`, sets all four planes and verifies native readback before/after commit. Invalid edits roll back. A successful edit has one Revit Undo item. A view template controlling the range blocks edits and explains the cause. Refresh after view/model changes. Plan regions and view templates themselves are outside this tool's initial scope. Projects with more than 100 levels are explicitly refused.

MCP flow: `get_context` → `get_view_range(captureId)` → `set_view_range(captureId, requestId, expectedRange, range)`. Each settings object contains `top`, `cut`, `bottom`, `depth`; each plane contains `level` and `offsetFeet`. Level is `Current`, `Above`, `Below`, `Unlimited`, or a returned native Level UniqueId. Supply all four planes and the exact settings read as `expectedRange`. Native Revit validates special choices separately for each plane.

## Room Dimensions

Open **RevitThyme → Annotation → Room Dimensions** on a floor plan. Select a room, set the inset and room-tag clearance in feet/inches, then Preview. A single selected room is preferred when the window opens. The initial room chooser lists up to 100 document rooms; MCP/CLI can address other rooms by UniqueId.

The first implementation supports a placed, enclosed rectangle bounded by **four straight basic native walls**, on the active view's level and phase. Rotated rectangles are supported. Linked walls, room separators, curved boundaries, split/multiple boundary segments, islands, slanted finish faces and irregular rooms are refused with a reason. Dimensions must be visible, and an active crop must be unsplit, rectangular and fully contain the room. This is the plan's bounded rectangular-room milestone.

Preview resolves actual finish-face references, computes width/depth and plans two unlocked associative Revit dimensions inset from the room edges. It reserves each visible room tag's projected bounds plus the requested clearance and tries the opposite inset side when necessary. If neither side is clear, move the tag or adjust the settings. It never moves the tag. Rooms too small for two insets are refused.

Create re-reads native boundaries, references, tag bounds and existing dimensions and requires the exact reviewed preview. Both dimensions are verified for owner view, references and measured values before/after commit. The pair is one Undo item. Repeating the same request ID returns the retained outcome; a fresh preview detects an existing valid pair and returns unchanged. A partial pair, duplicate dimensions or conflicting locked dimensions require manual resolution. The tool creates native dimensions using Revit's default linear dimension style; it creates no replacement geometry or labels.

MCP flow: discover the room using `query_elements(categoryId="-2000160")`; call `preview_room_dimensions(captureId, roomId, insetFeet=1.5, tagClearanceFeet=1)`; pass its exact `roomDimensions` object to `create_room_dimensions(captureId, requestId, preview)`. All lengths are feet. The host recomputes the plan rather than trusting caller-supplied geometry.

## CLI and recovery

```powershell
& $cli view-range get --session $session --json | Set-Content -LiteralPath $rangeCapture -Encoding utf8
& $cli view-range set --session $session --capture $rangeCapture --range $settingsFile --json
& $cli room-dimensions preview --session $session --room $roomId --json | Set-Content -LiteralPath $roomCapture -Encoding utf8
& $cli room-dimensions create --session $session --capture $roomCapture --json
```

Use absolute paths. `$settingsFile` contains a full desired ViewRangeSettings JSON object. Capture files contain private model identities and paths; keep them private. These utility operations do not save or synchronize a model. On an uncertain result, inspect native state and recover the original request ID through MCP `get_operation`; do not submit a new write ID. Version 0.11.0 uses protocol **13** and 32 MCP tools; upgrade add-in, CLI and MCP together.

## Qualification

The [installed-host client](../tests/UtilitiesInstalled/README.md) uses the immutable installed CLI/MCP executable and normal native host, with trusted execution disabled and no test loader or registration swap. It copies a known disposable utility fixture and verifies floor/RCP edits, template refusal, invalid-order rollback, native room dimensions, exact-view PNGs, reconnect recovery and one native Undo. The v0.11.0 run passed 85 external/native checks; [verification](../verification/native-utilities.md#installed-v0110-follow-up--2026-10-21) retains its exact runtime and host attribution.

The earlier source-built ribbon/WPF qualification remains available separately:

```powershell
.\project.cmd check
.\project.cmd check -Portable
.\project.cmd utilities-prepare
.\project.cmd utilities-start 'C:\absolute\artifacts\utilities\live\run-uuid'
.\project.cmd utilities-check 'C:\absolute\artifacts\utilities\live\run-uuid'
.\project.cmd utilities-cleanup 'C:\absolute\artifacts\utilities\live\run-uuid'
```

Start uses a new process and fixture and an immutable bundle. Its native loader is inert outside the prepared environment and excluded from packages. Revit may ask to load an unsigned development add-in. The evaluator posts the actual custom ribbon commands, exercises the real modeless WPF windows, verifies actual Revit values and Undo/Redo, then permits the official-SDK stdio MCP and installed-style CLI tests. Check can be repeated to observe startup/final native results. Cleanup removes only the owned PID/start identity and exact hash-verified test registration, preserves all fixture/evidence files, and checks existing processes and registration hashes. An unfinished run requires explicit `-Abort`.

See [verification](../verification/native-utilities.md) for observed results and limits. Earlier legacy qualification does not certify this C# implementation. API references: the installed Revit 2027 SDK's ViewPlan/PlanViewRange documentation and Autodesk's [native linear dimensions](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/454df2e4-5ccb-b00d-434b-f3e7fdb75e8e.htm).
