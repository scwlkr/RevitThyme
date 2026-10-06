import {useState} from "react";
import {View,Text} from "react-native";
import {Button} from "../../components/button";
import type {Proposal,MutationResult} from "../../contracts/generated";
export function NativeReview({proposal,busy,onApply}:{proposal:Proposal;busy:boolean;onApply:()=>void}){
 const [confirmed,setConfirmed]=useState(false);
 if(!proposal.native_write_available)return <Button disabled onPress={()=>{}}>Native Apply unavailable</Button>;
 return <View className="gap-3">
  <Text className="text-muted">Revit process {proposal.target.process_id} · start {proposal.target.process_start_ticks} · session {proposal.target.session_id}</Text>
  <Text className="text-muted">{proposal.side_effects.length?proposal.side_effects.join(" "):"Identical native values: no transaction or undo item."}</Text>
  <label><input aria-label="Confirm displayed target and range" type="checkbox" checked={confirmed} disabled={busy} onChange={e=>setConfirmed(e.target.checked)}/> I confirm this displayed document, view and range.</label>
  <Button primary disabled={!confirmed||busy} onPress={onApply}>Apply to Revit</Button>
 </View>;
}
export function NativeOutcome({result,busy,onInspect,onCancel}:{result:MutationResult;busy:boolean;onInspect:()=>void;onCancel:()=>void}){
 return <View accessibilityLiveRegion="polite" className="bg-surface border-l-4 border-thyme p-4 gap-2">
  <Text className="text-ink font-semibold">Native outcome: {result.status}</Text>
  <Text className="text-muted">{result.message}</Text><Text className="text-muted text-xs">Request {result.request_id}. Changed views: {result.changed_ids.join(", ")||"none reported"}. An unconfirmed outcome does not establish no change.</Text>
  {!!result.native_values.length&&<Text className="text-muted">Verified native Cut: {result.native_values[0].cut.offset_feet} ft · reference {result.native_values[0].cut.level_id}</Text>}
  <View className="flex-row gap-3"><Button disabled={busy} onPress={onInspect}>Inspect Apply outcome</Button><Button disabled={busy||result.status!=="queued"} onPress={onCancel}>Cancel queued Apply</Button></View>
  <Text className="text-muted">Refresh native capture before another review. Apply is never retried automatically.</Text>
 </View>;
}
