import {useEffect,useState} from "react";
import {ScrollView,View,Text} from "react-native";
import {Link} from "expo-router";
import {Button} from "../../components/button";
import {useEditor,keys} from "./use-editor";
import {RangeRow,labels} from "./range-row";
import {Section} from "./section";
import {UnderlayInfo} from "./underlay";
import {NativeReview,NativeOutcome} from "./native-review";
import type {ViewKind,Unit,Axis,PlanDirection,UnderlayFixture} from "../../contracts/generated";
export function ViewRangeScreen(){
 const e=useEditor();const [dark,setDark]=useState(false);
 useEffect(()=>{document.documentElement.classList.toggle("dark",dark);},[dark]);
 return <ScrollView className="bg-background flex-1"><View className="p-6 gap-5">
  <View className="flex-row flex-wrap items-center justify-between gap-4 border-b border-line pb-4">
   <View className="flex-row flex-wrap items-center gap-4"><img src="/revitthyme-logo.svg" alt="RevitThyme" style={{width:150,height:54,background:"#fffffb",borderRadius:6,padding:6}}/><View><Text className="text-thyme text-sm font-semibold tracking-widest">{e.mode==="native"?"REVIT CONNECTION · DEVELOPMENT PREVIEW":"OFFLINE PREVIEW"}</Text><Text className="text-ink text-3xl font-semibold">Visual View Range</Text></View></View>
   <View className="flex-row flex-wrap items-center gap-3"><Link href="/about" className="text-thyme">About this preview</Link><Button onPress={()=>setDark(!dark)}>{dark?"Light theme":"Dark theme"}</Button></View>
  </View>
  <View className="bg-surface border-l-4 border-thyme p-4 gap-1">
   <Text className="text-ink font-semibold">{e.mode==="native"?"Native capture and validated Apply · M2 source preview":"Synthetic architecture fixture · Offline milestone M1"}</Text>
   <Text className="text-muted">{e.mode==="native"?"Capture the current Revit plan, review the exact target and explicitly confirm Apply. No save or sync. Actual Revit qualification remains pending.":"Explore a model section and review range changes. No Revit document is connected. Native Apply is unavailable."}</Text>
  </View>
  <View className="flex-row items-center flex-wrap gap-3">
   {e.mode==="synthetic"&&<><select aria-label="Fixture view" value={e.kind} onChange={ev=>e.setKind(ev.target.value as ViewKind)}><option value="floor">Floor Plan</option><option value="engineering">Engineering Plan</option><option value="ceiling">Ceiling Plan</option></select>
   {e.kind==="engineering"&&<select aria-label="Fixture plan direction" value={e.direction} onChange={ev=>e.setDirection(ev.target.value as PlanDirection)}><option value="down">Looking down</option><option value="up">Looking up</option></select>}
   <select aria-label="Fixture Underlay Orientation" value={e.underlay} onChange={ev=>e.setUnderlay(ev.target.value as UnderlayFixture)}><option value="none">Underlay: None</option><option value="up">Underlay: Look Up</option><option value="down">Underlay: Look Down</option><option value="unbounded_up">Underlay: Unbounded / Look Up</option><option value="unbounded_down">Underlay: Unbounded / Look Down</option></select>
   <label><input aria-label="Partial capture fixture" type="checkbox" checked={e.partial} onChange={ev=>e.setPartial(ev.target.checked)}/> Partial capture fixture</label></>}
   <Button disabled={e.busy} onPress={()=>void e.capture()}>{e.mode==="native"?"Refresh native capture":"Refresh fixture"}</Button>
   <Button disabled={e.busy} onPress={()=>void e.capture(true)}>Reconnect</Button>
   <View className="flex-1"/><Text className="text-muted">Offsets in</Text>
   <select aria-label="Display units" value={e.unit} onChange={ev=>e.changeUnit(ev.target.value as Unit)}><option value="mm">mm</option><option value="m">m</option><option value="ft">decimal ft</option></select>
   {e.request && e.request.unit!==e.unit && <Text accessibilityLiveRegion="polite" className="text-muted">Converting display units…</Text>}
  </View>
  {e.snapshot && <Text className="text-muted text-sm">{e.snapshot.document_name} / {e.snapshot.view_name} · revision {e.snapshot.target.revision}</Text>}
  {!!e.error && <Text accessibilityRole="alert" className="text-ink bg-surface border-l-4 border-timber p-3">{e.error}</Text>}
  {e.mutation&&<NativeOutcome result={e.mutation} busy={e.busy} onInspect={()=>void e.inspectOutcome()} onCancel={()=>void e.inspectOutcome(true)}/>}
  {e.inputInvalid && <Text accessibilityRole="alert" className="text-timber">Enter a finite numeric offset. Review Apply is disabled until every input is valid.</Text>}
  <View className="flex-row flex-wrap gap-5">
   <View className="bg-surface border border-line rounded-lg p-4 gap-4" style={{flexBasis:620,flexGrow:1,minWidth:0}}>
    <View className="flex-row flex-wrap gap-2 justify-between"><Text className="text-ink font-semibold">Architectural section</Text><Text className="text-muted text-sm">{e.snapshot?.triangle_count??0} cached triangles</Text></View>
    {e.snapshot && <View className="gap-1"><Text className="text-ink font-semibold">Main plan: Looking {e.snapshot.plan_direction} {e.snapshot.plan_direction==="up"?"↑":"↓"}</Text><UnderlayInfo underlay={e.snapshot.underlay}/></View>}
    <View style={{aspectRatio:2,minHeight:260}}>{e.preview?<Section preview={e.preview} unit={e.unit} direction={e.snapshot!.plan_direction} onDrag={(k,value)=>e.edit(k,value,undefined,"ft")}/>:<Text className="text-muted">{e.busy?"Capturing fixture…":e.error?"Correct the proposal or refresh to see a valid section.":"Preparing section…"}</Text>}</View>
    {e.request && <View className="gap-2 border-t border-line pt-4"><View className="flex-row flex-wrap items-center gap-3"><Text className="text-ink">Slice locator</Text><select aria-label="Slice axis" value={e.request.axis} onChange={ev=>e.update({axis:ev.target.value as Axis})}><option value="x">X axis</option><option value="y">Y axis</option></select><Text className="text-muted">{Math.round(e.request.fraction*100)}%</Text></View><input aria-label="Slice position" type="range" min="0" max="1" step="0.001" value={e.request.fraction} onChange={ev=>e.update({fraction:Number(ev.target.value)})}/></View>}
    <Text className="text-muted text-xs">Section elevations are in feet. Plane labels show level-relative offsets. Arrows beside offsets mark planes outside the captured section. Move the slice locator to explore geometry.</Text>
   </View>
   <View style={{flexBasis:310,flexGrow:1,minWidth:0}}><Text className="text-ink font-semibold">Level-relative planes</Text>
    {e.preview && keys.map(k=><RangeRow key={k+"-"+e.inputReset} name={k} plane={{...e.preview!.proposed[k],unlimited:e.request!.edits[k].unlimited}} unit={e.unit} value={e.preview!.display_offsets[k]} limits={e.preview!.display_limits} disabled={e.busy} onEdit={(v,u)=>e.edit(k,v,u)} onInvalid={invalid=>e.setInputInvalid(k,invalid)}/>)}
   </View>
  </View>
  {!!e.snapshot && <View className="gap-1">{e.snapshot.diagnostics.map((d,i)=><Text key={i} className={e.snapshot!.partial && i===2?"text-timber font-semibold":"text-muted text-xs"}>{d}</Text>)}</View>}
  <View className="flex-row flex-wrap items-center gap-3 border-t border-line pt-4">
   <Button disabled={!e.snapshot||e.busy||!!e.mutation} onPress={e.reset}>Reset</Button><Button disabled={!e.snapshot||e.busy||!!e.mutation} onPress={e.cancel}>Cancel</Button>
   <Text accessibilityLiveRegion="polite" className="text-muted flex-1">{e.mutation?"Native outcome shown above. Reset/Cancel do not undo Apply.":e.cancelled?"Cancelled. Captured values restored; no model changes.":e.preview?"Preview only · "+e.preview.elapsed_ms.toFixed(2)+" ms Rust section":"Waiting for a valid preview"}</Text>
   <Button primary disabled={!e.ready||e.busy} onPress={()=>void e.review()}>Review Apply</Button>
  </View>
  {e.proposal && <View accessibilityLiveRegion="polite" className="bg-surface border border-line p-5 gap-3">
   <Text className="text-ink text-xl font-semibold">Apply preview · {e.proposal.identical?"identical values":"proposed change"}</Text>
   <Text className="text-muted">Target: {e.proposal.target.document_id} / {e.proposal.target.view_id} · revision {e.proposal.target.revision}</Text>
   <View className="gap-2">{keys.map(k=><Text key={k} className="text-ink">{labels[k]} · {e.proposal!.before[k].level_name} [{e.proposal!.before[k].level_id}] · {e.proposal!.before[k].unlimited?"Unlimited":e.proposal!.before[k].offset_feet+" ft"} → {e.proposal!.after[k].unlimited?"Unlimited":e.proposal!.after[k].offset_feet+" ft"}</Text>)}</View>
   <Text className="text-muted">{e.proposal.message}</Text><Text className="text-muted">Affected view: {e.proposal.target.view_id}. {e.proposal.native_write_available?"No mutation submitted for this review.":"Changed IDs: none. Side effects: none."}</Text>
   <NativeReview key={e.proposal.proposal_id} proposal={e.proposal} busy={e.busy} onApply={()=>void e.applyConfirmed()}/>
  </View>}
 </View></ScrollView>;
}
