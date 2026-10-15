# Parameter identity qualification

Parameter inspection/readback now includes native shared status, nullable shared GUID, nullable built-in enum identity and native `HasValue`. GUID access is guarded by `IsShared`. The CLI's `inspect-elements` and MCP's existing `inspect_elements` return the shared contract. Exact document-local IDs, raw values, truncation, read-only rules, expected-value editing and pagination remain in place.

## Offline coverage

`project.cmd check` builds against the local Revit 2027 SDK and runs the repository's unit, MCP, Agent and development safety checks. The focused regressions cover:

- Canonical parameter JSON, CLI JSON/default output and MCP text/structured results, including explicit null identities and exact long ID strings.
- Same-name built-in/shared fields and two shared GUIDs on opposite sides of a 20-field page boundary.
- Independent assigned/unassigned booleans for zero and empty raw values, and an unchanged 512-character truncated value.
- Inspection without fake-model mutation/transactions, exact-ID edits among same-name fields, stale expected values and read-only/truncated edit rejection.
- Original response decoding by strict legacy parameter clients, opt-in enriched frames, unchanged operation replay arguments, strict unknown-member rejection and the existing 64 KiB frame bound.

These are contract/client regressions with an offline model adapter. They do not establish actual Revit behavior. Older version-4 clients receive the original parameter JSON unless they opt into identity metadata on the existing bridge envelope. Updated clients require the matching updated host; install Addin, CLI and MCP together.

## Native acceptance pending

The installed executable/SDK on the implementation machine reports Revit 2027.3, file build `27.3.0.28`. This is an environment inventory, not a candidate test result. No candidate native fixture was run. The active working Revit session and installed registration were preserved. Compiled Addin/Core changes require the installation window described in [development setup](../docs/DEVELOPMENT.md); script execution against the older installed host cannot qualify this candidate.

Before native sign-off, install the exact signed candidate with Revit closed normally, then use `project.cmd dev-start` with one known disposable, nonworkshared fixture. Retain its exact run/process/start/session/document identity and verify the loaded candidate hashes. Record the exact tested Git SHA and native `Application.VersionNumber`, `VersionBuild` and executable file build in private evidence.

Complete this checklist in that one disposable fixture, keeping working models separate:

- [ ] Include one same-name built-in/shared pair and two same-name shared definitions with different known GUIDs. Compare inspector identities with direct API `InternalDefinition.BuiltInParameter`, `IsShared` and guarded `GUID`, retaining the exact local IDs.
- [ ] Include an ordinary nonshared, non-built-in field. Verify both nullable identities are null and inspection does not access its inapplicable GUID.
- [ ] Include assigned, unassigned, zero and empty-string cases. Compare each returned `hasValue` with direct native `Parameter.HasValue`, without deriving it from the raw value.
- [ ] Inspect through the existing CLI and MCP paths; include duplicate fields across one pagination boundary. Iterate returned offsets and compare every ID, raw value, data type, read-only and truncation flag against native API observations. Check a long string remains clipped to 512 and blocked from edits. Check representative maximum pages remain within the existing response bound.
- [ ] Before and after inspection, verify unchanged revision, document modified state, element count, field values and fixture file hash. No inspection transaction, save or sync.
- [ ] Use the existing exact-ID edit with the chosen field's exact expected raw value. Verify only that same-name field changes, the other IDs/values remain intact, stale expected values are refused and one native Undo restores the edit. Readback must retain identity metadata.
- [ ] Retain the actual fixture/build/SHA, client results and direct native comparisons. Preserve the baseline fixture, working processes and installed registration; retain failures as well as passes.

Merge and release remain pending review and native acceptance. No version bump or release is part of this change.
