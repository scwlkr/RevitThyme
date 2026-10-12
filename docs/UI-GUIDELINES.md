# RevitThyme dialog UI

Accepted direction: 2026-10-12, [WLK-231](https://linear.app/wlkr-labs/issue/WLK-231/record-compact-autodesk-style-dialog-guidance).

Build compact utility dialogs that feel at home in Revit and Autodesk software. The owner rejected the Visual View Range preview's excess helper text, decorative styling and generous control spacing. These rules apply to future tools and UI revisions; the current preview has not been redesigned by this document.

## Reference dialogs

The owner supplied Revit's Object Styles, Manage Links and Transfer Project Standards dialogs. Use their aligned controls, compact lists/tables, restrained surfaces and familiar actions as references. Empty space reserved for a useful table, list or preview is appropriate; padding around controls should stay tight. The references express appearance and interaction preferences, without changing the accepted React/Expo/Electron implementation stack.

## Defaults

- Put the tool name in the window title. Keep product branding small; reserve expressive artwork for ribbon icons and product identity.
- Use plain system-style typography, neutral backgrounds, subtle borders and conventional rectangular buttons. Color should identify selection, focus, status or meaningful diagram information.
- Align labels and inputs in compact rows or tables. Keep margins, row heights and spacing consistent. Avoid large cards, oversized headings, shadows, decorative separators and pill-shaped controls.
- Give the working content most of the window: the section preview, parameter rows, lists and tables. Fit the primary task and its actions together at the supported window size and display scaling. Scroll long content inside its own region.
- Use short labels. Put explanations in tooltips, a Help link or expandable details. Show necessary warnings and actionable errors when their conditions apply, rather than filling the normal view with repeated explanatory text.
- Place conventional actions consistently, usually at the lower right. Use familiar labels such as OK, Cancel and Apply when they accurately describe the behavior. Keep enabled/disabled states and visible keyboard focus clear.
- Maintain readable text, keyboard navigation, accessible labels and adequate control targets while tightening spacing.

## Applying this to Visual View Range

Suggested layout for a subsequent implementation: a narrow context/units row, the section preview beside a compact Top/Cut/Bottom/Depth table, a small slice control, and one action row. Keep units and level references concise. Move geometry limitations, orientation explanations and technical diagnostics behind Help/details; retain brief connection status and any warning needed for the current action.

Preserve the preview's meaningful geometry colors, distinct main-plan and underlay direction, deterministic validation, review before model changes, and accurate connection/partial-capture states. Simplifying presentation must preserve those behaviors.

Review a real rendered dialog at supported display scaling before accepting a UI revision. Check that the primary controls and actions fit, text remains readable, and useful content dominates the window. Source/style checks alone do not verify the appearance.
