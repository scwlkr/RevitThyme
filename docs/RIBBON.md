# Ribbon and icon standards

RevitThyme's ribbon helps people find a task, understand its effect, and run it with confidence. Organize it around the work users do in Revit. Use native controls and familiar Revit language, with SVG icons that remain clear at their actual display size.

This document owns RevitThyme's current layout and project-specific design decisions. The reusable authoring workflow lives in [the `revitthyme-ribbon` skill](https://github.com/scwlkr/skillsies/tree/main/personal/revitthyme-ribbon). Read this document when adding, moving, naming, or illustrating a ribbon command; update the command map when those decisions change.

## Organization

Group panels by user task, rather than implementation stage, assembly, or developer ownership. Prefer concrete task names such as **Views** and **Annotation**. Keep related commands together and give each command one predictable home. Place everyday work before optional assistance and toolkit maintenance. Preserve positions as the toolkit grows; adding one tool should not require relearning the whole tab.

A panel may start with one command when its purpose is distinct and useful. Add a panel when that distinction improves discovery; combine panels when their boundary is harder to explain than their contents. Avoid placeholder panels or commands added only to fill space.

### Current command map

| Panel | Command | Presentation | Reason |
| --- | --- | --- | --- |
| Views | View Range | Large push button, first | A direct task with its own editing workflow. |
| Views | Inspect View | Large push button, second | Inspect active-view context and review a view-name edit. |
| Annotation | Room Dimensions | Large push button | Creates view-owned dimensions; annotation is its task home. |
| RevitThyme | Open Agent | Large push button | The visible entry to the optional dockable assistant. |
| RevitThyme | Check for Updates | Push button in the panel slide-out | Infrequent maintenance, with background update checks already available. |

The **RevitThyme** panel is the toolkit's entry and maintenance area. Its broad name is deliberate at this scale. Revisit it if additional utilities need clearer task groups. The single Annotation command earns its panel through a different task purpose; this does not require every future command to have its own panel.

**Open Agent** opens the pane. **Connect Codex** remains the connection action inside that pane. A ribbon label must describe what clicking it immediately does.

## Choose a control

| Control | Use when | Design rule |
| --- | --- | --- |
| Large push button | A primary task, frequent entry, or important discovery point deserves direct access. | Keep a readable label and a distinct 32-unit icon. Prominence needs a task reason. |
| Small stacked buttons | Two or three related, lower-priority commands benefit from a compact visible group. | Each retains a label, 16-unit icon, and tooltip. Do not fill a stack with unrelated actions. |
| Pulldown | Two or more related commands share a clear parent concept and none is the obvious default. | Name the shared task; menu labels distinguish the choices. Avoid an extra click for a lone command. |
| Split button | A command family has one obvious default plus useful alternatives. | Keep the default stable. Remember the last command only when that behavior is intentional and explained. |
| Slide-out | Rare settings or maintenance can tolerate reduced discoverability. | Keep primary work visible and document where the secondary command lives. |
| Toggle group, combo box, or text box | A task genuinely needs a persistent mode, preset, or compact value. | Use a dialog when the input needs explanation, validation, or several related settings. |

Choose large versus small from importance, frequency, and discovery cost together. Destructive or consequential commands still need clear preview or confirmation in their actual workflow; icon size does not provide that protection. Introduce a dropdown only when existing commands form a useful family. The current five commands need none.

Use separators only where they express a real subgroup. Let Revit manage panel sizing, collapse, keyboard behavior, fonts, spacing, backgrounds, and hover states through the supported `Autodesk.Revit.UI` API. Ribbon restyling through internal visual trees or unsupported UI libraries is outside this standard.

## Labels, explanations, and availability

Use familiar Revit nouns and short action phrases. Keep the full label meaningful without its icon or panel name. Prefer one or two readable lines; put intentional line breaks between words. User-facing labels describe the task rather than protocol, implementation language, or internal stage. Preserve internal command IDs when changing labels or panel placement so shortcuts and automation keep their identities.

Every actionable command must have both:

- **Short tooltip:** One sentence stating the immediate purpose. Add a critical prerequisite when it determines whether the tool is useful.
- **Extended tooltip:** Explain the target or supported context, the expected next step or result, and relevant limits. State whether opening merely inspects/previews, and describe an edit or Undo only when the implementation supports it. Use plain user language instead of an internal safety checklist.

For example, a View Range short tooltip can say, “Inspect and edit the active plan's view range.” Its extended text can identify supported plan views, explain preview then Apply, and mention one native Undo item. A Room Dimensions tooltip must describe the supported rectangular room case rather than implying that every room shape works.

Provide F1 contextual help to a real, maintained command topic. Check that its destination exists and describes the shipped behavior. A relevant repository guide is acceptable; an invented page or a generic homepage is not useful command help. Put tooltips on the actionable child controls of split buttons or toggle groups.

Keep commands visible in predictable locations and disable them when a cheap context check establishes that they cannot run. Document/view-dependent tools need an appropriate document and view; toolkit utilities may work without a model. Keep availability checks fast and read-only. The operation must still validate its full preconditions and explain detailed refusals after invocation.

## SVG icon language

**SVG is the authored source of truth.** Maintain editable vector geometry in `assets/ribbon/svg/<id>.<16|32>.svg`; PNG files are generated delivery assets for Revit. Correct the SVG and regenerate rather than painting over an export.

- Draw separate **16 × 16** and **32 × 32** optical masters with matching `viewBox` values. Simplify the small master; scaling down the large one alone often loses useful gaps and details.
- Start with **1-unit strokes at 16** and **2-unit strokes at 32**. Align horizontal and vertical stroke edges to the pixel grid. Use consistent caps and joins; deviate only when the actual-size result needs it.
- Leave breathing room inside the canvas, normally about 1 unit at 16 and 2 at 32. Match apparent weight and occupied area across the set.
- Use a clear Revit-related object or task metaphor. Let silhouette carry meaning and use one restrained accent to identify the action. Related command variants should retain the same base metaphor.
- Keep backgrounds transparent. Use explicit vector paths and shapes without embedded raster images, external resources, fonts, filters, gradients, or shadows. Avoid letters, emoji, product logos used as task metaphors, and tiny decorative detail.
- Design light and dark variants together. Start with neutral **`#52606D` / `#D6DEE8`** and accent blue **`#006EAF` / `#70C5FF`**, respectively. These are palette defaults, not Autodesk brand specifications. Adjust with an actual-size contrast review; retain consistent meaning across themes.
- Use shape and label as well as color. Reserve warning/status colors for real states rather than routine command decoration. Let Revit supply disabled and interaction states.

An icon family should look native through clarity, restrained color, and consistent weight. Give RevitThyme its identity through this shared language rather than a badge on every icon. Original icons must be distinct from Autodesk-owned artwork.

## Export and loading

Use `scripts/export-ribbon-icons.ps1` as the reproducible SVG-to-PNG route. Generated names are `assets/ribbon/png/<id>.<theme>.<16|32>[@2x].png`, where theme is `light` or `dark` and `@2x` marks double-density output. Export each theme and optical master at 1× and 2×:

| Master | 1× export | 2× export | Logical size |
| --- | --- | --- | --- |
| 16-unit | 16 px at 96 DPI | 32 px at 192 DPI | 16 device-independent pixels |
| 32-unit | 32 px at 96 DPI | 64 px at 192 DPI | 32 device-independent pixels |

Keep alpha transparency, deterministic names, and the corresponding DPI metadata. The 2× export uses the same optical master, not a different drawing. Check pixel dimensions and DPI together; a larger image at 96 DPI changes its logical size. Supply both the small `Image` and `LargeImage`, including for large commands, so Quick Access Toolbar use has an icon. Select theme resources through supported Revit theme APIs and refresh them after a theme change.

Review a contact sheet for all icons on representative light and dark backgrounds, plus individual icons at actual size. Enlargement is useful for geometry inspection but cannot establish small-size legibility. Inspect neutral strokes, gaps, accent weight, clipping, alpha edges, and whether neighboring icons can be distinguished quickly.

## Completion evidence

Record the evidence actually obtained:

1. **Source and icon checks:** The command map, stable IDs, labels, both tooltip fields, real help destinations, availability rules, SVG masters, exports, dimensions, DPI, transparency, and theme variants agree. Regeneration is repeatable.
2. **Build/package checks:** Run the repository's required checks and confirm that all resources needed at runtime are packaged. A build establishes compile and resource integration, not rendered Revit behavior.
3. **Actual Revit checks:** In a separately pinned disposable process and fixture, inspect panel order, labels, hover/extended tooltips, slide-out discovery, F1, available/disabled contexts, theme switching, and actual command launch. Review small and large imagery at the host's available display scales. Read back fixture invariants after command checks.

Preserve working models and installations. Do not substitute a production model when the fixture or connection fails. Report missing host, theme, or display-scale evidence explicitly; a contact sheet or simulated ribbon remains design evidence.

## Native API references

Autodesk documents the supported controls, image properties, tooltips, help, and slide-out behavior in [Ribbon Panels and Controls](https://help.autodesk.com/cloudhelp/2025/ENU/Revit-API/files/Revit_API_Developers_Guide/Introduction/Add_In_Integration/Revit_API_Revit_API_Developers_Guide_Introduction_Add_In_Integration_Ribbon_Panels_and_Controls_html.html). Theme selection is exposed by [UIThemeManager](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/d6f7a05b-dd4b-275d-a7d4-6d0ba67c6f28.htm). Check the installed target Revit SDK before implementing an API-specific change.
