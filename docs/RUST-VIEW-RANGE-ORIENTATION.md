# Visual View Range labels and Underlay Orientation

Tracked in [WLK-117](https://linear.app/wlkr-labs/issue/WLK-117/visual-view-range-readable-labels-and-look-uplook-down-plan-support). This bounded follow-up starts at M2 draft PR #7, source `5d6cd42febc87c089fe9830f7a997e8c3b9aac87`. It preserves M1's Rust section/range logic and Expo Web/Electron feature. The user clarified that Look Up/Look Down means **Underlay Orientation**.

## Fixed behavior and decisions

- Plane callouts occupy separate rows, joined to their actual elevation by leader lines. Two-line values fit mm, m and decimal ft. Coincident planes remain physically coincident. Numeric controls still distinguish them; the chart retains its pointer drag behavior.
- Header, toolbar, section and input rows wrap. Native captures initially slice through the middle of the model. Elevation ticks follow the captured height; offset arrows identify planes outside the section. Unlimited depth is shown above for a main look-up plan and below for a main look-down plan.
- Native capture reads underlay base/top level IDs, names and **project elevations**, enabled state, orientation and an explicit Unbounded top. Rust clips a shaded underlay band to the section and preserves its separate sight direction. Look Up points from below; Look Down points from above. None draws no band. Level-relative main offsets and absolute underlay elevations remain distinct.
- The underlay band is a section annotation. It does not reproduce Revit's halftone or final projected plan visibility. Changing Underlay Orientation in Revit does not flip the section elevation axis or alter main range validation. The app does not change underlay settings; refresh reads Revit's current settings.
- Native main direction is captured independently: floor down, ceiling up, structural `ViewFamilyType.PlanViewDirection`. `ViewDirection.Z` is unsuitable for distinguishing reflected ceiling and floor plans. Rust prechecks use this captured main direction; the Revit adapter remains the final native validity authority.
- Before native validation or transaction start, the adapter rechecks captured main direction and underlay facts as well as the existing target/revision restrictions. Apply still changes only the four main range references/offsets.
- Required typed metadata makes this a **protocol 2** component bundle. Rust/core and desktop are 0.1.1; native adapter is 0.2.1. OpenAPI remains Rust-derived and TypeScript/Zod generated. HTTP operation paths remain `/v1/*`; the payload/native framing protocol is versioned separately. Protocol 1 and incomplete captures are rejected before writes. Update compatible components together and restart Revit normally.

Autodesk defines [Underlay Orientation and its level range](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-DocumentPresent/files/GUID-77184183-E245-4F3B-8486-617E9A9FB296.htm), [disabled and Unbounded level references](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/7d86a306-a362-5526-e84d-e5eae8437175.htm), and [main structural plan direction](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/80a92ad0-9f77-fb0d-3858-5cfc2c0f598b.htm). Signatures are also compiled against the installed 2027.2 SDK reference; documentation is not runtime qualification.

## Source and packaged verification

Existing project CLI routes own the checks; no separate test platform or new dependency is added.

```powershell
.\project.cmd m1 contracts
.\project.cmd check-m1
.\project.cmd check-m2
.\project.cmd package-m1
.\project.cmd check-m1-ui
.\project.cmd m2 ui
.\project.cmd ci --base 5d6cd42febc87c089fe9830f7a997e8c3b9aac87 --full --require-clean --report artifacts/local-ci/wlk-117-final.json
```

Regression evidence retains the old package's rendered Bottom/View Depth collision at zero and the old core's rejection of structural up depth. Tests inspect rendered text bounds independently of the layout algorithm. Packaged cases cover four coincident labels in all three units at 1600/1100 pixels and 900 pixels with 125% zoom, settled viewport/control bounds, main floor/ceiling/structural up/down, directional Unlimited, and None/finite up/down/Unbounded up/down underlay bands/arrows. Existing numeric/drag/keyboard, unit/race, cancellation, security and recovery tests remain active. Windows pipe tests carry captured underlay facts into Rust and preserve the main range. .NET transaction fakes prove orchestration only.

Final exact source/cleanliness/base and command results live in `artifacts/local-ci/wlk-117-final.json`. Package manifest records source SHA, protocol/components and asset/adapter hashes. Feature evidence remains local under `artifacts/m1`, `artifacts/m2`, and `artifacts/wlk-117`, including failed-before reports, layout metrics and screenshots. Private actual-Revit evidence is not committed or uploaded. Changed source/SHA requires a new exact clean check and package.

## Actual Revit evidence and open gates

Read-only bridge inspection on Revit **27.2.0.39** found the approved user test house open on a floor plan, document unmodified. Existing floor plans look down, ceiling plans up, and structural plans down; their underlays are disabled. Native `ViewDirection.Z` was +1 for all of those kinds, confirming why the type setting is needed. Detached ceiling-range validity rejected depth below Top and accepted depth at/above Top; no transaction or document change occurred. Exact view/level IDs, document path and native readbacks remain in private local evidence.

After the user's standing installation approval and authorized restart, the exact checked protocol-2 bundle from source `1a35d52740e53a9f7ac8bdb491d1d72b288957cb` is installed and adapter 0.2.1.0 loaded in Revit 27.2.0.39. Actual native capture confirms disabled-underlay metadata and unchanged main references/offsets; eleven read-only UI observations and mm/m/ft screenshots pass. The normal ribbon launch operation also opens the installed preview. Reopened document state and disk hash remain unchanged. See the [separate verification record](../verification/view-range-orientation.md).

Enabled-underlay capture and both orientation arrows on an actual Revit view remain unqualified, as do structural main look-up and all actual Apply/rollback/undo/redo cases. Installed geometry remains partial at the 50,000-triangle bound. WLK-114 retains the observed unstable document-wrapper identity and 30-second idle disconnect blockers. This change does not repair or claim qualification of those defects. Manifest `qualified_revit_builds` stays empty.

Unresolved choices are full final-plan/underlay visibility parity, approved runtime test-view writes, signing and another-machine qualification. Existing drafts, website/logo, legacy pyRevit/Fabrication tabs and external TimberFold installation/source/baselines stay preserved. The user permits project installation and normal close/open for this test session; no merge, release, save/sync or live job edits are authorized.

## Installation and remaining runtime proposal

Installation/read-only steps 1–2 below were executed for the exact bundle above after the user granted standing installation approval. No repeated installation approval is required for this project. Step 3 remains a separate, unexecuted test-view-write proposal. Documentation-only commits do not replace the installed artifact or its exact-SHA evidence; a later source package may be checked independently without reinstalling unchanged application code.

1. Once the final manifest/CI identify a clean source SHA, propose copying that whole portable bundle to `%LOCALAPPDATA%\RevitThyme\native-preview\<SHA>\RevitThyme-win32-x64` and updating only the owned `%APPDATA%\Autodesk\Revit\Addins\2027\RevitThyme.NativePreview.addin` registration. Verify physical destinations and existing ownership/hashes; back up the prior owned registration and retain the previous M2 bundle. Do this only after installation approval and the user has closed Revit normally; never force-kill it or close their document.
2. After restart, independently verify process/start/build, loaded assembly/package hashes and the exact approved test-house path. Run read-only capture/screenshot checks first. Do not submit main View Range Apply while its identity/idle qualification blockers remain open.
3. With **separate test-view-write approval**, use the existing A101 Ground Floor view only. Set Underlay Base to existing 01 Ground Floor (0 ft), Top to existing 02 Upper Floor (10 ft), exercise LookingUp/LookingDown, then Unbounded top. Capture and screenshot each state and independently read back all four main range planes. No geometry/level/type creation is needed. Before execution, refresh the exact view/level IDs and original underlay values. Preserve originals in a transaction group and roll it back at the end; independently confirm the original underlay/range values, zero committed test transactions and unchanged disk SHA. No save/sync, close or replacement. The group is a test fixture write and still requires approval.

The underlay-setting write proposal remains unexecuted and still requires separate approval. Standing installation and normal close/open permission do not authorize those view-setting writes.
