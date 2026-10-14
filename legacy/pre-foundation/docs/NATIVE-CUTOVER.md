# Native Revit cutover

2026-10-12, WLK-233. The user requests Visual View Range without pyRevit and disconnection of pyRevit for future Revit work. This supersedes the previous requirement to keep its loader active; preserve its files and existing models.

## Current scope

The installed Revit is 27.3.0.28. The earlier native preview rejects this build because its startup guard accepts only 27.2.0.39. Candidate adapter 0.2.6 targets the observed 27.3 build, retains native protocol 2 and includes the compact UI/shared components. Old immutable bundles remain preserved. Historical 27.2 qualification is separate from current candidate evidence.

Native Visual View Range uses .NET API-context capture/transactions, the Rust engine and the Electron UI. The native test MCP supplies fixture-bound capture, preview, proposal, outcome and queued-cancellation operations; its read-only run does not expose Apply, general model queries or arbitrary code. There is no Routes fallback.

Disabling pyRevit also disables its existing suite/status/settings/inspection buttons, TimberFold launcher, general Revit Python MCP and Jev workflows after restart. Their source/installations remain present. Native replacements must be implemented as named operations when requested; this change does not claim full-suite parity or port external TimberFold.

## Bounded procedure

Use the project CLI from the exact clean candidate checkout. Prepare a fresh native lab run with project.cmd revit-lab prepare. Run full exact-clean CI and retain its report. Then use project.cmd native-cutover install with that absolute run folder and -CiReport pointing to the matching full report.

Installation verifies the clean source/package/lab/CI revisions and installed build, retains both native registration backups, copies the desktop into a new immutable per-user candidate folder, checks every copied file and changes only the two owned native registrations. Existing Revit processes keep their loaded assemblies/connections.

Run project.cmd native-cutover disconnect with the same folder. This retains the pyRevit manifest and Codex config backups, renames pyRevit.addin to pyRevit.addin.disabled, and disables only the revit and jev-revit MCP sections. Unrelated TOML settings are checked for exact semantic preservation. pyRevit source/extensions and models are not removed.

Use project.cmd revit-lab start with that exact folder and --approved to launch one new process and create its separately approved disposable fixture. The existing working and earlier test processes remain open. Runtime evidence records process/start identity, pyRevit assembly absence, native preview activity/version/path, document path and initial modification state. An unsigned-add-in prompt is a user gate.

Run project.cmd revit-lab verify for native read-only proof. Native ribbon/UI capture, review, confirmed Apply, independent readback and native Undo are separate actual-Revit trials on this same fixture. Preserve the saved file hash; do not save the test mutations.

The optional applied and undo arguments to revit-lab verify observe Cut 5 ft and restored Cut 4 ft respectively. They retain separate snapshots, compare the original fixture file hash, reference levels, other three planes and geometry, and perform no writes themselves. Apply and Undo remain explicit native UI actions.

After successful native-only proof, project.cmd native-cutover bind selects this exact native test MCP session. Status is available through project.cmd native-cutover status. The config preservation check is project.cmd native-cutover check and is included in full CI.

## Running sessions and recovery

An already running Revit window cannot unload pyRevit. It loses the loaded runtime only on normal exit/restart. The current working instance has unsaved Electrical Families changes and an open Norris job: do not save, discard or close them without a specific user decision. Disabling future startup can be completed while that window stays open. Codex may need MCP reconnect/reload to apply its disabled entries and updated native binding.

The exact run retains preview.addin.before-cutover, lab.addin.before-cutover, pyRevit.addin.before-disconnect and config.toml.before-disable/bind. Restore only the relevant owned registration/entry from those backups after checking the current value; preserve any later unrelated config edits. Re-enable pyRevit by restoring the same bytes to pyRevit.addin and enabling the two legacy MCP entries, then normally restart. Do not delete old bundles, model files or evidence to perform recovery.

Source/package checks do not establish actual Revit success. The run's native-install, pyrevit-disconnect, binding.runtime and live-readonly records plus retained UI/Apply/Undo evidence establish their separate outcomes. Publication, other machines/builds and unrestricted suite parity remain outside this cutover.
