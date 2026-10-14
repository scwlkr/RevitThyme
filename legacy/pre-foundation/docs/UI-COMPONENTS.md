# Owned dialog components

[WLK-232](https://linear.app/wlkr-labs/issue/WLK-232/compact-visual-view-range-using-reusable-autodesk-style-ui-components) establishes the suite's reusable dialog components in [desktop/src/components/ui](../desktop/src/components/ui/index.ts). Visual View Range, its Apply review and Help screen use them.

Follow [UI-GUIDELINES.md](UI-GUIDELINES.md). Compose these controls for new tools instead of copying layout and raw input styling into each screen. This follows the owned-code and composition approach described by [shadcn/ui](https://ui.shadcn.com/docs); the implementation remains compatible with the project's Expo/NativeWind structure.

## Available components

| Component | Contract |
| --- | --- |
| Button | onPress, disabled, variant default / primary; compact shape, accessible name/state and keyboard focus |
| Label | Standard dialog typography; React Native Text props |
| DialogLayout | toolbar, body children, footer; toolbar/actions stay visible while long body content scrolls; revealKey brings a newly created bottom panel into view |
| GroupBox | title, children and optional native View style; bounded group of related controls |
| Input, Select | Controlled browser form props; shared density, colors and focus styles |
| Checkbox | Controlled checkbox props and optional visible label; always provide a meaningful accessible name |
| Slider | Controlled range props; native browser keyboard/pointer behavior |
| Table, TableRow, TableHead, TableCell | Semantic table props; aligned compact headers and cells |
| Notice | Conditional error/warning or tone info message; live region |
| Details | title, children; explanations/technical details closed by default |

    import {Button,DialogLayout,GroupBox,Input,Label} from "../components/ui";

    <DialogLayout toolbar={<Label>Captured view</Label>}
      footer={<Button disabled={!valid} onPress={review}>Review Apply</Button>}>
      <GroupBox title="Settings">
        <Input aria-label="Offset" value={text} onChange={event=>edit(event.target.value)}/>
      </GroupBox>
    </DialogLayout>

The components own presentation, semantics and control interaction. Tool state, numeric parsing, Revit validation, bridge calls and confirmation authority belong to the feature. Rounded display text must not replace untouched internal values.

Reusable length formatting/parsing is in desktop/src/lib/length.ts. Visual View Range defaults to feet/inches at nearest 1/8 inch. Reuse this helper for length fields and labels; keep raw captured feet as the model source. Metric choices remain available.

## Theme and platform boundary

Neutral light/dark colors, focus, control density and desktop table/form styles live in [global.css](../desktop/src/global.css). NativeWind reads the semantic color variables through [tailwind.config.cjs](../desktop/tailwind.config.cjs). Update these shared defaults once when refining the suite's appearance.

Containers, text and buttons use React Native primitives. The web-controls and table modules are explicit browser adapters for the current Windows desktop surface. They preserve native HTML form/table semantics. A future native target needs corresponding platform adapters and its own verification; this change establishes Windows desktop reuse.

## Verification

Run project.cmd check-m1, project.cmd package-m1, project.cmd check-m1-ui and project.cmd m2 ui through the existing project CLI. The packaged drivers exercise real controls, review/confirmation, errors, recovery, orientation, themes and resizing. The compact-dialog check measures rendered control bounds/alignment and captures the actual Electron compositor at 100% and 125% scaling.

These are source/package/desktop checks. Actual Revit qualification remains separate. New UI components must have a real caller and useful behavior; add further components when a tool needs them.
