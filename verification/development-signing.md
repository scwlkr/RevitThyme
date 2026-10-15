# Development signing and session readiness — 2026-10-15

Implementation source: `5fea3ae6b6c4a5d765ae511f8b2cfdaaccfb5e3f`.
This record is a subsequent documentation-only change. The unrelated untracked
logo remains preserved; all tracked implementation files matched that commit.

## Verified before installation

- `project.cmd check`: full native-SDK build with zero warnings/errors;
  149 unit, 35 MCP and 13 Agent checks; architecture, script and preservation gates.
- 43 offline development safety checks: process/start/executable and session pinning,
  document lifetime, stale/ambiguous bindings, corrupt plans, exclusive command
  ownership, mismatched installation before launch, graceful close and recovery
  of uncertain requests without resubmission.
- 16 explicit local signing checks: persistent nonexportable RSA/SHA-256 identity,
  idempotent setup/signing, public-only trust, eleven first-party signatures,
  byte-identical CLI alias, source/third-party preservation and tamper refusal.
- Signed package verification and integrity hashes, followed by 18 isolated
  installer checks and 25 isolated command/upgrade checks. Real user PATH and
  native registrations were unchanged.
- A separate baseline was copied from the exact previously qualified disposable
  dynamic fixture. Its SHA-256 remained
  `cf84cc50d691d3bc81ad5c04351b9abf531a56725950f8c5e92803ed378ede2e`.

The signed candidate is
`packages/RevitThyme-0.9.2-ribbon-e41b65cce5404fb0b96a63af590e40c3`, with an
adjacent ZIP and checksum. Its manifest SHA-256 is
`312b43fe03f62cdffeae2d3179bd2c74211695b5ec7fff9b0284d07c1eb13b45`.
Private package/run/signature/preservation records are indexed by
`artifacts/dev/delivery.json`; keys and local signing configuration are excluded
from source and packages.

Certificate setup exposed a Windows provider-cache issue after native trust
import. Setup now imports public DER through a bounded owned certutil process
and reopens the native certificate stores for exact readback. The same original
nonexportable identity was retained throughout; setup itself returned ready.

## Preserved working state

The pre-existing Revit process/start identity, active document/view/revision and
unsaved state were unchanged in native readbacks. All 86 tracked installation,
registration and saved-model files matched the preservation baseline. No Revit
process was launched, closed or restarted, and no model was saved or synchronized.
The installed host, setup receipt and normal add-in registration remain unchanged.

## Remaining native acceptance

The user requested a handoff before installation/restart. This readiness does
not establish prompt-free Revit startup or qualify the new launch workflow live.
After saving/closing Revit normally and installing the exact signed candidate,
verify repeated prompt-free launches, revised C# in one process, native readback
and one Undo, wrong-target rejection, normal-launch execution opt-out and graceful
development shutdown. Follow [the development guide](../docs/DEVELOPMENT.md).

General compiled-assembly reload, untrusted-code isolation and public publisher
signing are outside this change.
