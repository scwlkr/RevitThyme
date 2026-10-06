# Visual View Range preview

Visual View Range previews a plan section, lets you adjust its four range planes, and applies reviewed settings to the captured Revit plan. The feature runs through the native Revit adapter, Rust and the desktop app; pyRevit is not required for this feature. Existing RevitThyme and TimberFold tools remain separate and available.

The bounded qualification covers one Revit instance on the current Windows x64 host, Revit 2027.2 build 27.2.0.39. Floor, reflected ceiling and engineering plans are supported with the restrictions below. Another computer, multiple Revit instances and broader release qualification remain unverified.

## Use

1. Open a supported plan and choose **Add-Ins > RevitThyme Native Preview > Visual View Range**. Starting the desktop executable directly opens the synthetic offline fixture instead.
2. Capture the active plan. Confirm the displayed document and view. The section is a bounded geometry preview; it does not reproduce Revit's final projected visibility.
3. Drag a plane or enter its offset. Choose feet or millimeters and move the section slice as needed. These controls change only the preview.
4. Review the original and proposed Top, Cut, Bottom and Depth values. Confirm and choose **Apply in Revit**. Revit validates the range, applies one named Undo item and independently reads back the result.
5. Use Revit Undo to restore the applied range. The tool never saves or synchronizes the model. Reset and Cancel discard only preview edits.

If the document, view, model geometry, level, underlay or native range changes after capture, capture and review again. After disconnect or reconnect, make a fresh review. If an Apply reply is lost, inspect its retained outcome and native values before any further action; never assume the change failed.

## Supported references and exclusions

Use named levels for unresolved native **Level Above/Level Below** placeholders. Capture/Apply rejects an unresolved placeholder with `unresolved_relative_level`; select an explicit named level in Revit's View Range dialog, then recapture. The tool never guesses a replacement. Current, resolved positive level IDs and permitted Unlimited references remain supported. The user accepted this preview restriction on 2026-10-06.

Template-controlled range, dependent/primary-with-dependents, family, read-only, active edit/transaction and unsupported views reject with guidance. Revit remains the authority for valid range combinations. Bottom Unlimited with finite Depth can be invalid; permitted paired Unlimited values are supported. Main plan direction and underlay orientation are independent.

Capture warns when its 1,500-candidate or 50,000-triangle limit makes the section partial. Phase, design-option, hidden geometry and final projected visibility are not fully represented. Do not use a partial or approximate section as a construction visibility guarantee.

## Preview installation and recovery

The unpublished candidate is a portable Windows x64 bundle, not the v0.2.0 pyRevit ZIP. It contains no Autodesk binaries, models, credentials, TimberFold implementation or qualification harness. It relies on licensed Revit 2027.2 and Revit's .NET 10 runtime.

Verify `SHA256SUMS.txt` and extract into a new immutable folder. Keep Revit closed while changing add-in registration. In the included `resources/native/RevitThyme.addin.template`, replace `__APPROVED_ABSOLUTE_BUNDLE_PATH__` with the absolute extracted `RevitThyme-win32-x64` folder. Back up any existing registration with AddInId `D88A1886-4314-48D3-BEC8-F6EC1BA9B6A4`, then place the resulting `RevitThyme.NativePreview.addin` in the physical per-user Revit 2027 Addins folder. Check that AppData resolves to the host path, not an MSIX redirected path. Start Revit normally and use the native ribbon.

Keep prior bundles and registrations for recovery. With Revit closed, restore the previous owned registration or remove only the native preview registration to disable this feature. Never overwrite loaded DLLs or remove unrelated add-ins. Leave the existing pyRevit registration and tools intact. No automatic native installer or other-machine recovery is claimed by this candidate.

## Evidence and release preparation

The [numbered qualification assessment](../verification/rust-view-range-m3-remaining.md) separates source/package checks from actual Revit observations, including the pyRevit-disabled trial. Deferred cases remain unqualified; the manifest's aggregate qualified-build list stays empty.

After full exact-clean CI builds the matching package, `project prepare-view-range-release --ci-report <report>` creates the unpublished binary ZIP, corresponding tracked-source ZIP, checksums and metadata under ignored `artifacts/releases`. It verifies the package and every binary ZIP entry by round-trip comparison. Preparing this candidate does not publish a tag or release. Second-computer distribution, dependency notice clearance, signing and installer recovery remain distribution gates.
