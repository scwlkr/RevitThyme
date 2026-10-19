# TimberFold digital delivery

The delivery candidate is `0.10.0-timberfold.4`, protocol 8, built from source `0fffdf8397da507c10911e77a6fb1bb81120b4e1`. Its signed package manifest SHA-256 is `AE21953451541C57EE55B28202D00DE94A761314C57DD446724E6C4803DF91C5`; the production installer SHA-256 is `1DC392D8F0E3679C0666D81A2AB2B945E071496DE290A8A7B29FF02B35BAF10F`. Digital source/package/native qualification and peer review are complete. The private bundle is assembled and hash-verified; no merge or public release is claimed. Exact results are in [qualification](qualification.md) and [delivery status](progress.md).

The private delivery bundle contains 210 hash-verified files plus its SHA-256 inventory. It contains the production installer, signed package ZIP, matching source ZIP, four original editable RVTs and hosted-window families, qualified digital kits, independent readback reports, documentation and a SHA-256 inventory. Preserve that inventory with the bundle and verify it before using the files. Historical private comparison models/kits and the previous implementation remain retained separately.

## Install and run

Use the qualified Windows installer with Revit closed, then start Revit 2027 and open a supported project. Install matching host, CLI and MCP together; reconnecting an older installation does not add protocol-8 operations. Package/dependency notices travel with the installer. The native feature requires no Python or pyRevit runtime.

Open **RevitThyme → Fabrication → TimberFold**. Confirm physical stock dimensions, model scale, phase and design-option scope, then Inspect. Resolve every blocking diagnostic before Preview. Preview produces nominal panels, verified stock placement, DXF/calibration and traceable source maps without changing the model. Generate from the retained preview to create DWGs, mapped stock/assembly guides, native labels, drafting views and review sheets. Generation checks the exact source again and never saves it automatically.

The same workflow is available through CLI and MCP; see [usage and commands](README.md). Starts return acceptance and a retained run ID. Poll status until terminal; preserve that ID after a lost response. Use the same request by value when retrying, and inspect an uncertain native outcome before another generation. A separate generation ID deliberately creates a separate kit.

| Evidence or action | Meaning |
| --- | --- |
| `PreviewReady` | Nominal fabrication/layout/cutting checks passed; native kit and physical checks remain outstanding. |
| `KitReady` | Digitally verified native documentation and delivered files passed the recorded checks; physical checks remain outstanding. |
| Status | Reports retained progress/outcome; acceptance is not completion. |
| Verify | Rehashes historical delivered evidence. It does not establish that later manual model edits preserved the native kit. |
| Reconcile | Read-only check of a sealed committed generation against the current pinned native kit; retains a separate recovery run and preserves original files/timings. An absent or changed kit remains Unconfirmed. |
| Cancel | Cooperative request; an individual Autodesk call cannot be forcibly interrupted, and a confirmed commit wins a late cancellation. |
| Revit Undo | Removes the generated documentation as one native Undo item; saved cutting files remain retained. |

## Use the kit

Use CUT-only DWG/DXF files for cutting. Reference DXFs, SVG guides and native sheets contain outlines, labels and source/part mappings. Internal native reference inputs use a recorded 48× review transform with matching view scale; they are reference derivatives. Cutting and calibration files retain actual physical-inch coordinates. Keep the canonical guide/source map with the cut parts.

Kerf compensation belongs to the laser software and is not applied by TimberFold. The feature supplies nominal geometry and no machine power/speed or motion program. Read [supported geometry and numerical gates](support.md) before applying it to another building. Unsupported geometry, oversized panels and unresolved completeness/numeric checks block success rather than dropping parts or changing scale.

Digital delivery is owner-approved with **physical checks outstanding**. Before fabrication, measure stock thickness and kerf, cut the supplied calibration pieces, check representative joints and retained window bars, then assemble a representative kit. Physical dimensions, web strength, clearance and assembly fit have not been certified by the digital tests. `ManufacturingReady` remains false. The preserved external implementation has not been retired.
