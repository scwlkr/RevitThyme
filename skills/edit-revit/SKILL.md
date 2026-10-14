---
name: edit-revit
description: Change Revit view names, parameters, view range or room dimensions with native readback and Undo.
---

1. Inspect with get_context and the relevant native tools. Resolve the exact target and original typed values. The user's requested edit authorizes that edit; clarify consequential ambiguity before proceeding.
2. For View Range and Room Dimensions, preview the shared operation and inspect its result. For parameter edits, provide the inspected Expected values in one atomic batch.
3. Use a fresh request GUID and capture for each edit. An unconfirmed or timed-out response requires get_operation for that request ID before retrying.
4. Read the native result back with a fresh capture. Report the affected elements or view, the verified values, and the available Revit Undo action. Finish only when readback agrees with the requested change.

The server is pinned to this Revit process. Refresh context after document/view changes. Saving, syncing and closing require explicit user intent. Trusted C# requires the host opt-in and acknowledgement; its external effects are outside Revit Undo. Prefer the existing typed operations when they cover the request.
