# Third-party notices

RevitThyme original code is GPL-3.0-or-later; see LICENSE.

pyRevit is an independently installed prerequisite, maintained by pyRevit Labs and contributors under GPL-3.0. The release ZIP contains no pyRevit binaries or implementation source. The source repository retains a narrow Routes maintenance diff under verification/pyrevit-routes.patch, including small upstream source excerpts covered by GPL-3.0. See [pyRevit](https://github.com/pyrevitlabs/pyRevit) and its upstream license. The developer repair script is optional and does not install pyRevit or alter it during ordinary package installation.

Autodesk Revit is proprietary software, neither included nor licensed by this project. Users need their own licensed installation.

TimberFold is external. This release includes registration metadata and a new read-only inspector. It contains no TimberFold source, dependencies, models or output artifacts. TimberFold redistribution and dependency notices must be resolved in its own release before bundling it here.

Rust and Python are development tools, not bundled runtimes. The extension uses the IronPython engine from the user's pyRevit installation.

## Unreleased Rust replacement preview

The M1 source/package is separate from the legacy extension. Its Rust sidecar links Cargo-lockfile dependencies; Electron and exported Expo/React/React Native Web/NativeWind assets are bundled. Direct dependency licenses include MIT (Axum, Tokio, Expo, React, React Native Web, NativeWind, Tailwind, Zod, Electron, Forge), MIT OR Apache-2.0 (Serde), and MIT OR Apache-2.0 (Utoipa). Preserve upstream dependency license files and Electron's LICENSE/LICENSES.chromium.html. The preview contains no Autodesk assemblies or TimberFold dependencies.

This is a development preview, not a redistribution-clearance claim. A complete bundled dependency notice inventory, signing and installer recovery are required before distribution/release; see [M1 evidence boundaries](docs/RUST-VIEW-RANGE-M1.md).
