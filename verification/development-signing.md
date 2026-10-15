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

## Preservation during readiness preparation

The pre-existing Revit process/start identity, active document/view/revision and
unsaved state were unchanged in native readbacks. All 86 tracked installation,
registration and saved-model files matched the preservation baseline. No Revit
process was launched, closed or restarted, and no model was saved or synchronized.
At that handoff, the installed host, setup receipt and normal add-in registration
remained unchanged.

## Native acceptance after the authorized installation

The user saved and closed Revit, then authorized installation and native tests.
The exact candidate above is now installed in its immutable manifest-hash folder.
The prior installation is preserved, and registration/receipt backups match their
pre-install SHA-256 values. The saved working model, original test baseline and
development model files remained unchanged.

Two new Revit 2027.3 processes opened their disposable models without any trust
prompt or load approval. Both native journals identify `RevitThyme.Foundation.addin`
with `AddInCodeSigningStatus: Signed` and `AddInLoadFailureMessage: NoError`.

The development process retained its original PID/start/session/document identity
while running two different C# bodies. Native modification renamed view 1972
from `Level 1` to `Level 1 - Dev test`, reporting only that modified element ID.
An independent query read the changed native name. One native Ctrl+Z restored
`Level 1` and `IsModified=false`. All 2,425 element IDs matched the original hash,
and the model still had zero warnings. The development session was left open.

A second process with trusted execution unset rejected `run-query` with
`trusted_code_disabled`, then closed normally with its saved fixture unchanged.
No production model was opened for testing. Private native evidence is indexed
by `artifacts/dev/delivery.json` and the run's `native-acceptance.json`.

The first real wrapper call exposed an omitted optional GUID becoming null at
the parent-to-child PowerShell boundary. Source commit
`01d4aef92ee9067eea702f667f2d1b37e772a2ba` sets an explicit `Guid.Empty` default in
both scripts. Three regressions exercise the real script parameter boundary and
pending-ID recovery; all 46 offline development checks and the full toolkit gate
pass. The repair required no host rebuild, reinstall or Revit restart. A diagnostic
query missing a required collector filter also rolled back cleanly; its retained
failure is separate from successful acceptance.

This qualifies the requested startup, dynamic iteration, modification and Undo
flow on this machine. Wrong-target/lifecycle guards retain their offline coverage;
other machines and certificate renewal need separate validation. Follow
[the development guide](../docs/DEVELOPMENT.md) for daily use.

General compiled-assembly reload, untrusted-code isolation and public publisher
signing are outside this change.
