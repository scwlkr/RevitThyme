import {View} from "react-native";
import {GroupBox,Select,Checkbox,Label} from "../../components/ui";
import type {ViewKind,PlanDirection,UnderlayFixture} from "../../contracts/generated";
import type {useEditor} from "./use-editor";

export function FixtureControls({editor:e}:{editor:ReturnType<typeof useEditor>}){
 return <GroupBox title="Sample settings">
  <View className="flex-row flex-wrap items-center gap-2">
   <Label>Plan</Label><Select aria-label="Fixture view" value={e.kind} onChange={ev=>e.setKind(ev.target.value as ViewKind)}>
    <option value="floor">Floor Plan</option><option value="engineering">Engineering Plan</option><option value="ceiling">Ceiling Plan</option>
   </Select>
   {e.kind==="engineering"&&<Select aria-label="Fixture plan direction" value={e.direction} onChange={ev=>e.setDirection(ev.target.value as PlanDirection)}><option value="down">Looking down</option><option value="up">Looking up</option></Select>}
  </View>
  <Select aria-label="Fixture Underlay Orientation" value={e.underlay} onChange={ev=>e.setUnderlay(ev.target.value as UnderlayFixture)}>
   <option value="none">Underlay: None</option><option value="up">Underlay: Look Up</option><option value="down">Underlay: Look Down</option><option value="unbounded_up">Underlay: Unbounded / Look Up</option><option value="unbounded_down">Underlay: Unbounded / Look Down</option>
  </Select>
  <Checkbox label="Partial capture" aria-label="Partial capture fixture" checked={e.partial} onChange={ev=>e.setPartial(ev.target.checked)}/>
 </GroupBox>;
}
