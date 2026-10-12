import {useEffect,useState} from "react";
import {View,useWindowDimensions} from "react-native";
import {Link} from "expo-router";
import {Button,DialogLayout,GroupBox,Label as Text,Notice,Select,Slider,Details,Table,TableRow,TableHead} from "../../components/ui";
import {useEditor,keys} from "./use-editor";
import {RangeRow} from "./range-row";
import {Section} from "./section";
import {UnderlayInfo} from "./underlay";
import {NativeOutcome,ApplyReview} from "./native-review";
import {FixtureControls} from "./fixture-controls";
import type {Unit,Axis} from "../../contracts/generated";

export function ViewRangeScreen(){
 const e=useEditor(),{height}=useWindowDimensions();
 const [dark,setDark]=useState(false);
 useEffect(()=>{document.documentElement.classList.toggle("dark",dark);document.title="Visual View Range";},[dark]);
 const context=e.snapshot?e.snapshot.document_name+" / "+e.snapshot.view_name:"Capturing…";
 const toolbar=<>
  <View className="flex-row items-center gap-2">
   <img src="/revitthyme-logo.svg" alt="RevitThyme" style={{width:24,height:20,objectFit:"contain"}}/>
   <Text testID="captured-view" numberOfLines={1} className="text-ink flex-1" style={{minWidth:0}}>{context}</Text>
   <Link href="/about" accessibilityLabel="About this preview" className="text-thyme text-[13px] underline">Help</Link>
   <Button onPress={()=>setDark(!dark)}>{dark?"Light theme":"Dark theme"}</Button>
  </View>
  <View className="flex-row flex-wrap items-center gap-2">
   <Button disabled={e.busy} onPress={()=>void e.capture()}>{e.mode==="native"?"Refresh native capture":"Refresh fixture"}</Button>
   <Button disabled={e.busy} onPress={()=>void e.capture(true)}>Reconnect</Button>
   <View className="flex-1"/>
   {e.request&&e.request.unit!==e.unit&&<Text accessibilityLiveRegion="polite" className="text-muted">Converting display units…</Text>}
   <Text>Units</Text><Select aria-label="Display units" value={e.unit} onChange={ev=>e.changeUnit(ev.target.value as Unit)}>
    <option value="mm">mm</option><option value="m">m</option><option value="ft">decimal ft</option>
   </Select>
  </View>
 </>;
 const status=e.mutation?"Refresh to continue. Reset/Cancel do not undo Apply.":e.cancelled?"Cancelled. Captured values restored; no model changes.":e.preview?e.mode==="native"?"Revit connected":"Offline preview":"Waiting for a valid preview";
 const footer=<>
  <Text accessibilityLiveRegion="polite" className="text-muted flex-1">{status}</Text>
  <Button disabled={!e.snapshot||e.busy||!!e.mutation} onPress={e.reset}>Reset</Button>
  <Button disabled={!e.snapshot||e.busy||!!e.mutation} onPress={e.cancel}>Cancel</Button>
  <Button variant="primary" disabled={!e.ready||e.busy} onPress={()=>void e.review()}>Review Apply</Button>
 </>;
 return <DialogLayout toolbar={toolbar} footer={footer} revealKey={e.proposal?.proposal_id}>
  {!!e.error&&<Notice><Text>{e.error}</Text></Notice>}
  {e.inputInvalid&&<Notice><Text>Enter a finite numeric offset. Review Apply is disabled until every input is valid.</Text></Notice>}
  {e.snapshot?.partial&&<Notice><Text>Partial capture</Text>{e.snapshot.diagnostics.filter(d=>/partial|limit|omit|trunc/i.test(d)).map((d,i)=><Text key={i}>{d}</Text>)}</Notice>}
  {e.mutation&&<NativeOutcome result={e.mutation} busy={e.busy} onInspect={()=>void e.inspectOutcome()} onCancel={()=>void e.inspectOutcome(true)}/>}
  <View className="flex-row flex-wrap gap-2">
   <GroupBox title="Section" style={{flexBasis:440,flexGrow:2,flexShrink:1,minWidth:320}}>
    {e.snapshot&&<View><Text>Main plan: Looking {e.snapshot.plan_direction} {e.snapshot.plan_direction==="up"?"↑":"↓"}</Text><UnderlayInfo underlay={e.snapshot.underlay}/></View>}
    <View style={{height:Math.max(220,Math.min(350,height-290)),minWidth:0}}>
     {e.preview?<Section preview={e.preview} unit={e.unit} direction={e.snapshot!.plan_direction} onDrag={(k,value)=>e.edit(k,value,undefined,"ft")}/>:<Text className="text-muted">{e.busy?"Capturing…":e.error?"Correct the range or refresh the capture.":"Preparing section…"}</Text>}
    </View>
    {e.request&&<View className="flex-row items-center gap-2">
     <Text>Slice</Text><Select aria-label="Slice axis" value={e.request.axis} onChange={ev=>e.update({axis:ev.target.value as Axis})}><option value="x">X axis</option><option value="y">Y axis</option></Select>
     <View className="flex-1"><Slider aria-label="Slice position" min="0" max="1" step="0.001" value={e.request.fraction} onChange={ev=>e.update({fraction:Number(ev.target.value)})}/></View>
     <Text>{Math.round(e.request.fraction*100)}%</Text>
    </View>}
   </GroupBox>
   <View style={{flexBasis:330,flexGrow:1,flexShrink:1,minWidth:320}} className="gap-2">
    <GroupBox title="View Range">
     <Table aria-label="Level-relative planes">
      <colgroup><col style={{width:"24%"}}/><col style={{width:"26%"}}/><col style={{width:"30%"}}/><col style={{width:"20%"}}/></colgroup>
      <thead><TableRow><TableHead>Plane</TableHead><TableHead>Level</TableHead><TableHead>Offset ({e.unit})</TableHead><TableHead title="Unlimited">Unlimited</TableHead></TableRow></thead>
      <tbody>{e.preview&&keys.map(k=><RangeRow key={k+"-"+e.inputReset} name={k} plane={{...e.preview!.proposed[k],unlimited:e.request!.edits[k].unlimited}} unit={e.unit} value={e.preview!.display_offsets[k]} limits={e.preview!.display_limits} disabled={e.busy} onEdit={(v,u)=>e.edit(k,v,u)} onInvalid={invalid=>e.setInputInvalid(k,invalid)}/>)}</tbody>
     </Table>
    </GroupBox>
    {e.mode==="synthetic"&&<FixtureControls editor={e}/>}
    {e.snapshot&&<Details title="Capture details">
     <Text className="text-muted">Revision {e.snapshot.target.revision} · {e.snapshot.triangle_count} triangles</Text>
     {e.snapshot.diagnostics.filter(d=>!e.snapshot!.partial||!/partial|limit|omit|trunc/i.test(d)).map((d,i)=><Text key={i} className="text-muted">{d}</Text>)}
    </Details>}
   </View>
  </View>
  {e.proposal&&<ApplyReview key={e.proposal.proposal_id} proposal={e.proposal} context={context} busy={e.busy} onApply={()=>void e.applyConfirmed()}/>}
 </DialogLayout>;
}
