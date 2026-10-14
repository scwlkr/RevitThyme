import {useEffect,useState} from "react";
import {Input,Checkbox,Slider,TableRow,TableCell} from "../../components/ui";
import type {Plane,Unit} from "../../contracts/generated";
import type {PlaneKey} from "./use-editor";
import {offsetText,parseOffset,roundEighth,eighthInchFeet} from "../../lib/length";
export const labels={top:"Top",cut:"Cut Plane",bottom:"Bottom",depth:"View Depth"};
export function RangeRow({name,plane,unit,value,limits,disabled,onEdit,onInvalid}:{name:PlaneKey;plane:Plane;unit:Unit;value:number;limits:number[];disabled:boolean;onEdit:(value:number,unlimited?:boolean)=>void;onInvalid:(value:boolean)=>void}) {
 const [text,setText]=useState(offsetText(value,unit));
 const [focused,setFocused]=useState(false);
 const [valid,setValid]=useState(true);
 useEffect(()=>{if(!focused&&valid)setText(offsetText(value,unit));},[value,unit,focused,valid]);
 function numeric(s:string){
   setText(s);
   const parsed=parseOffset(s,unit);
   setValid(parsed!==undefined);onInvalid(parsed===undefined);if(parsed!==undefined)onEdit(parsed);
 }
 return <><TableRow>
  <TableCell>{labels[name]}</TableCell>
  <TableCell className="range-level" title={plane.level_name+" · "+plane.level_id}>{plane.level_name}</TableCell>
  <TableCell><Input className="range-offset" aria-label={labels[name]+" offset"} inputMode={unit==="ft"?"text":"decimal"} title={unit==="ft"?`Feet and inches, e.g. 5' 3 1/8"` : undefined} value={text} disabled={disabled||plane.unlimited} onFocus={()=>setFocused(true)} onBlur={()=>setFocused(false)} onChange={e=>numeric(e.target.value)}/></TableCell>
  <TableCell>{plane.allow_unlimited&&<Checkbox aria-label={labels[name]+" Unlimited"} checked={plane.unlimited} disabled={disabled} onChange={e=>onEdit(value,e.target.checked)}/>}</TableCell>
 </TableRow><TableRow className="range-adjustment"><TableCell colSpan={4}>
  <Slider aria-label={labels[name]+" slider"} min={limits[0]} max={limits[1]} step={unit==="ft"?eighthInchFeet:"any"} value={Math.min(limits[1],Math.max(limits[0],value))} disabled={disabled||plane.unlimited} onChange={e=>{setValid(true);onInvalid(false);onEdit(unit==="ft"?roundEighth(Number(e.target.value)):Number(e.target.value));}}/>
 </TableCell></TableRow></>;
}
