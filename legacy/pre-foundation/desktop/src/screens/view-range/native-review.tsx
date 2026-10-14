import {useState} from "react";
import {View} from "react-native";
import {Button,Checkbox,Details,GroupBox,Label as Text,Notice,Table,TableRow,TableHead,TableCell} from "../../components/ui";
import {keys} from "./use-editor";
import {labels} from "./range-row";
import type {Proposal,MutationResult,Plane,Unit} from "../../contracts/generated";
import {lengthText} from "../../lib/length";

function offset(plane:Plane,unit:Unit){return plane.unlimited?"Unlimited":lengthText(plane.offset_feet,unit);}
export function ApplyReview({proposal:p,unit,context,busy,onApply}:{proposal:Proposal;unit:Unit;context:string;busy:boolean;onApply:()=>void}){
 return <View accessibilityLiveRegion="polite" testID="apply-review">
  <GroupBox title={"Apply preview · "+(p.identical?"identical values":"proposed change")}>
   <Text>{context}</Text>
   <Table aria-label="Apply review">
    <thead><TableRow><TableHead>Plane / Level</TableHead><TableHead>Original</TableHead><TableHead>Proposed</TableHead></TableRow></thead>
    <tbody>{keys.map(k=><TableRow key={k}><TableCell title={p.before[k].level_id}>{labels[k]+" · "+p.before[k].level_name}</TableCell><TableCell title={p.before[k].offset_feet+" ft (exact)"}>{offset(p.before[k],unit)}</TableCell><TableCell title={p.after[k].offset_feet+" ft (exact)"}>{"→ "+offset(p.after[k],unit)}</TableCell></TableRow>)}</tbody>
   </Table>
   <NativeReview proposal={p} busy={busy} onApply={onApply}/>
   <Details title="Target details">
    <Text className="text-muted">Target: {p.target.document_id} / {p.target.view_id} · revision {p.target.revision}</Text>
    <Text className="text-muted">Revit process {p.target.process_id} · start {p.target.process_start_ticks} · session {p.target.session_id}</Text>
    <Text className="text-muted">{p.message}</Text>
   </Details>
  </GroupBox>
 </View>;
}
function NativeReview({proposal,busy,onApply}:{proposal:Proposal;busy:boolean;onApply:()=>void}){
 const [confirmed,setConfirmed]=useState(false);
 if(!proposal.native_write_available)return <View className="gap-2"><Text className="text-muted">Offline preview. No model changes.</Text><Button disabled onPress={()=>{}}>Native Apply unavailable</Button></View>;
 return <View className="gap-2">
  <Text>{proposal.side_effects.length?proposal.side_effects.join(" "):"Identical native values: no transaction or undo item."}</Text>
  <View className="flex-row flex-wrap items-center justify-between gap-2">
   <Checkbox label="I confirm this document, view and range." aria-label="Confirm displayed target and range" checked={confirmed} disabled={busy} onChange={e=>setConfirmed(e.target.checked)}/>
   <Button variant="primary" disabled={!confirmed||busy} onPress={onApply}>Apply to Revit</Button>
  </View>
 </View>;
}
export function NativeOutcome({result,unit,busy,onInspect,onCancel}:{result:MutationResult;unit:Unit;busy:boolean;onInspect:()=>void;onCancel:()=>void}){
 return <Notice tone={["applied_verified","unchanged_verified","cancelled"].includes(result.status)?"info":"warning"}>
  <Text>Native outcome: {result.status}</Text><Text>{result.message}</Text>
  {!!result.native_values.length&&<Text>Verified native Cut: {lengthText(result.native_values[0].cut.offset_feet,unit)} · reference {result.native_values[0].cut.level_id}</Text>}
  <View className="flex-row flex-wrap gap-2"><Button disabled={busy} onPress={onInspect}>Inspect Apply outcome</Button><Button disabled={busy||result.status!=="queued"} onPress={onCancel}>Cancel queued Apply</Button></View>
  <Details title="Outcome details"><Text className="text-muted">Request {result.request_id}. Changed views: {result.changed_ids.join(", ")||"none reported"}. An unconfirmed outcome does not establish no change. Apply is never retried automatically.</Text></Details>
 </Notice>;
}
