import {Link} from "expo-router";
import {DialogLayout,GroupBox,Label} from "../components/ui";

export default function About(){
 return <DialogLayout toolbar={<Label style={{fontWeight:"600"}}>Visual View Range Help</Label>}
  footer={<Link href="/" className="text-thyme text-[13px] underline">Back to Visual View Range</Link>}>
  <GroupBox title="Editing">
   <Label>Drag a plane or enter its level-relative offset. Use the slice axis and position to explore the section. The section elevation axis is in feet; plane values use the selected display units.</Label>
   <Label>Feet and inches are the default, rounded to the nearest 1/8 inch. Enter 5' 3 1/8", 3 1/8", or decimal feet. Display rounding and focusing a field do not change untouched captured values.</Label>
   <Label>Review Apply compares the original and proposed range. Native Apply requires target/range confirmation and creates one Revit Undo item for a change. It never saves or synchronizes.</Label>
   <Label>Reset, Cancel and Escape discard preview edits. After Apply, use Revit Undo to restore the model.</Label>
  </GroupBox>
  <GroupBox title="Capture and visibility">
   <Label>Floor, ceiling and engineering plans are supported within the separately qualified preview scope. Main plan direction and underlay orientation are independent. The shaded band represents underlay levels and direction; Revit controls halftone and final projected visibility.</Label>
   <Label>The section approximates captured model geometry. Links, annotations, crop, plan regions, phases, design options and final projected visibility may differ. Partial captures show a warning; expand Capture details for diagnostics.</Label>
   <Label>Revit validates the range. Unresolved Above/Below references require a named level in native View Range. Recapture after a target, model, range or connection change.</Label>
  </GroupBox>
 </DialogLayout>;
}
