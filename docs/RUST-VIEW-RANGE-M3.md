# Native Visual View Range qualification repairs

Work: [WLK-114](https://linear.app/wlkr-labs/issue/WLK-114/m3-visual-view-range-installed-revit-qualification). Base: draft PR #8, `db0b43a388d87f3b9cbf400f1d13fbdc6adf7f2d`. This bounded follow-up preserves the Rust logic, Expo/Electron UI and protocol-2 contract.

## Fixed decisions and authorization

The user granted full approval for necessary qualification work on the named disposable test house, including temporary underlay/range changes, repairs, installations, normal close/open and discarding test changes. Keep the file unsaved. Live jobs, save/sync, merge/release, legacy retirement and external TimberFold remain excluded.

Before each case independently inspect the exact path/process/view and native values. Only the existing approved plan's settings change for underlay and main Apply trials; no source geometry changes. Underlay cases use existing ground/upper levels at 0/10 ft, finite Look Up/Look Down and Unbounded. Each setup/restoration transaction closes within its API callback; no transaction/group remains open across external callbacks. Restore original settings and discard unsaved qualification changes on the final normal restart.

## Root causes and changes

`ConditionalWeakTable<Document,...>` compares managed wrapper identity. Actual Revit returned different wrappers for the same open document: ReferenceEquals=false, native Equals=true. Targets therefore changed between capture and validation. A host-lifetime Dictionary uses Revit's native equality/hash, keeps a random document identity and removes it on closing. Existing process/start/session/view/revision/original checks remain mandatory. A canceled close still invalidates the old snapshot and requires refresh.

The old pipe started its 30-second timeout while waiting for a new frame. Idle review lost its connection/facts before the ten-minute snapshot expiry. The authenticated reader now waits for the first byte until disconnect/shutdown; only assembling a started header/body has a 30-second timeout. Handshake and reply deadlines, frame size, ACL, PID/session credential checks and queued disconnect cancellation remain active. A real Windows pipe regression failed before the fix, then passed; a deliberately incomplete header still disconnects.

Adapter 0.2.2 packages with the existing Rust/desktop 0.1.1 and protocol 2. No new dependencies or Autodesk redistribution. The qualification DLL is separately built, never staged/registered/shipped. Its closed failure-point enum wraps the production Model/Change/Apply inside a valid API callback, with exact approved path/view guards and independent production readback. It can post a native warning/error and throw before/after set, regeneration, commit, readback or assimilation. It does not expose a product endpoint or arbitrary code.

## Verification drivers and limits

`project check-m2` builds the production adapter and excluded qualification harness, exercises offline orchestration and real Windows pipes. `project m3 ui` connects only to an explicitly launched loopback CDP diagnostic preview and drives the packaged production UI. Mutations require its explicit `--approved-writes` invocation, native target review and confirmation. Screenshots/results stay in ignored artifacts/m3.

Source/package checks do not qualify Revit. Actual results must record the installed SHA, native readbacks, DocumentChanged/Undo/Redo evidence, geometry/file preservation and screenshots separately. R1–R12 aggregate qualification and the manifest's qualified-build list remain pending until every required actual scenario is assessed. Inaccessible Pending/finalizer, multiple-instance/cross-user/remote, complete geometry and second-machine scenarios must remain explicit gaps.
