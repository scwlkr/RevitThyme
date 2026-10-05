# Third-party notices

RevitThyme original code is GPL-3.0-or-later; see LICENSE.

pyRevit is an independently installed prerequisite, maintained by pyRevit Labs and contributors under GPL-3.0. The release ZIP contains no pyRevit binaries or implementation source. The source repository retains a narrow Routes maintenance diff under verification/pyrevit-routes.patch, including small upstream source excerpts covered by GPL-3.0. See [pyRevit](https://github.com/pyrevitlabs/pyRevit) and its upstream license. The developer repair script is optional and does not install pyRevit or alter it during ordinary package installation.

Autodesk Revit is proprietary software, neither included nor licensed by this project. Users need their own licensed installation.

TimberFold is external. This release includes registration metadata and a new read-only inspector. It contains no TimberFold source, dependencies, models or output artifacts. TimberFold redistribution and dependency notices must be resolved in its own release before bundling it here.

Rust and Python are development tools, not bundled runtimes. The extension uses the IronPython engine from the user's pyRevit installation.
