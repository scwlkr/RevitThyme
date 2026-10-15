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

## Native acceptance passed — 2026-10-15

The exact product revision loaded and tested in actual Revit was **`2a880cfcbb61ee008a9aa5db7c43fc12771a328b`**. Revit reported `Application.VersionNumber = 2027`, `VersionBuild = 27.3.0.28` and executable file build `27.3.0.28`. Loaded Addin/Core assembly informational versions both contained that SHA; their hashes matched the prepared signed candidate:

| Identity | SHA-256 |
| --- | --- |
| Package manifest | `53d5f765d5723f5b42104530bec4e5af99a44e401c6ded196932515c710db293` |
| Addin | `36357c1ce11152a28866da4fb471e0b7045b7c520d0ca375cbe6039753ddc560` |
| Core | `684dec907993d69212c08e1da7a044dc050b451d87732da22ee1d73d919a1195` |

Installation followed an explicitly agreed close-Revit window and retained recoverable registration/receipt backups. `dev-start` opened one copied, nonworkshared baseline in a separate process with exact process/start/session/document pinning. The [fixture helpers](../tests/ParameterIdentity/README.md) created one synthetic Generic Model instance and independently read its API properties. The installed candidate's CLI executable and official-SDK MCP server supplied the actual inspection/edit results; the oracle did not call the candidate reader.

| Acceptance | Observed result |
| --- | --- |
| Same-name built-in/shared identity | Native Comments `-1010106` returned `ALL_MODEL_INSTANCE_COMMENTS`, false shared status and null GUID. Two shared Comments fields had distinct known GUIDs ending `000000000001` and `000000000002`, exact local IDs, and null built-in identities. CLI/MCP matched direct API observations. |
| Ordinary nonshared field | Native family parameter `PI Ordinary` returned false shared status and both nullable identities null; no inapplicable GUID access or exception. |
| Native HasValue | Designated assigned text: true. Unassigned Integer with raw zero: false. Explicitly assigned Integer zero: true. Explicitly assigned empty String: true. Every inspected flag matched the direct native API, independently of raw values. |
| Pagination, values and truncation | All 58 parameters matched native IDs, raw values, data types, read-only/truncation and identity fields through both clients at offsets 0, 20 and 40. Same-name shared fields were at native indices 29 and 52, on different pages. Long text was clipped to 512 and refused for editing. Largest observed operation JSON was 7,047 bytes; largest MCP text-plus-structured response was 18,653 bytes, within existing bounds. |
| Read-only inspection | Native before/after raw digest, all parameter IDs, revision 5, modified state, element count 2,515 and saved fixture hash were unchanged. Typed inspection opens no transaction. Independent evaluator queries used their existing rollback wrapper. No project save/sync occurred. |
| Exact-ID edit and safeguards | Only selected shared parameter ID `3646` changed to the reviewed value. Native post-edit oracle confirmed every other field unchanged. Read-only/truncated and stale expected-value edits were rejected; an old capture was rejected after the edit. |
| Native Undo | Exactly one posted Undo produced a native `TransactionUndone` event for `RevitThyme Set Parameters`. Direct API digest and all typed values/identities returned to baseline, with unchanged count and saved hash. |

Native harness stages passed **54 inspection + 67 edit + 28 post-Undo checks** (149 total), with direct native oracle snapshots before/after inspection, after the edit and after Undo. These checks concern this one fixture and supported build; they do not expand supported Revit versions or claim broader model qualification.

After evidence capture, one further native Undo removed fixture preparation, restoring the original 2,425-element baseline with `IsModified = false`. Only the owned development process was closed normally. Both the copied RVT and original baseline retained SHA-256 `cf84cc50d691d3bc81ad5c04351b9abf531a56725950f8c5e92803ed378ede2e`. Scratch family/shared definitions, scripts, source hashes, complete client results, native comparisons, loaded binary hashes and installation/cleanup receipts remain in private ignored artifacts.

The follow-up changes add only qualification helpers and evidence; product source remains identical to the exact native-tested revision above. PM/maintainer review remains required. The PR is unmerged and unreleased; no tag or version change was made.
