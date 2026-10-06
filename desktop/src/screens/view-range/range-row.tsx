import {useEffect,useState} from "react";
import {View,Text} from "react-native";
import type {Plane,Unit} from "../../contracts/generated";
import type {PlaneKey} from "./use-editor";
export const labels={top:"Top",cut:"Cut Plane",bottom:"Bottom",depth:"View Depth"};
export function RangeRow({name,plane,unit,value,limits,disabled,onEdit,onInvalid}:{name:PlaneKey;plane:Plane;unit:Unit;value:number;limits:number[];disabled:boolean;onEdit:(value:number,unlimited?:boolean)=>void;onInvalid:(value:boolean)=>void}) {
 const [text,setText]=useState(String(value));
 useEffect(()=>{setText(String(Number(value.toPrecision(12))));onInvalid(false);},[value,unit]);
 function numeric(s:string){
   setText(s);
   const valid=/^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$/.test(s) && Number.isFinite(Number(s));
   onInvalid(!valid);if(valid)onEdit(Number(s));
 }
 return <View className="gap-3 border-b border-line py-4">
  <View className="flex-row flex-wrap items-center justify-between gap-2">
   <Text className="font-semibold text-ink">{labels[name]}</Text>
   {plane.allow_unlimited && <label className="text-sm"><input aria-label={labels[name]+" Unlimited"} type="checkbox" checked={plane.unlimited} disabled={disabled} onChange={e=>onEdit(value,e.target.checked)}/> Unlimited</label>}
  </View>
  <View className="flex-row flex-wrap items-center gap-2">
   <input style={{width:160}} aria-label={labels[name]+" offset"} inputMode="decimal" value={text} disabled={disabled||plane.unlimited} onChange={e=>numeric(e.target.value)} />
   <Text className="text-muted">{unit}</Text>
   <Text className="text-muted text-xs flex-1 min-w-[100px]">{plane.level_name} · {plane.level_id}</Text>
  </View>
  <input type="range" aria-label={labels[name]+" slider"} min={limits[0]} max={limits[1]} step="any" value={Math.min(limits[1],Math.max(limits[0],value))} disabled={disabled||plane.unlimited} onChange={e=>onEdit(Number(e.target.value))}/>
 </View>;
}
