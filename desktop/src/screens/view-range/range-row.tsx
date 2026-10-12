import {useEffect,useState} from "react";
import {Input,Checkbox,Slider,TableRow,TableCell} from "../../components/ui";
import type {Plane,Unit} from "../../contracts/generated";
import type {PlaneKey} from "./use-editor";
export const labels={top:"Top",cut:"Cut Plane",bottom:"Bottom",depth:"View Depth"};
export function RangeRow({name,plane,unit,value,limits,disabled,onEdit,onInvalid}:{name:PlaneKey;plane:Plane;unit:Unit;value:number;limits:number[];disabled:boolean;onEdit:(value:number,unlimited?:boolean)=>void;onInvalid:(value:boolean)=>void}) {
 const [text,setText]=useState(String(Number(value.toPrecision(9))));
 useEffect(()=>{setText(String(Number(value.toPrecision(9))));onInvalid(false);},[value,unit]);
 function numeric(s:string){
   setText(s);
   const valid=/^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$/.test(s) && Number.isFinite(Number(s));
   onInvalid(!valid);if(valid)onEdit(Number(s));
 }
 return <><TableRow>
  <TableCell>{labels[name]}</TableCell>
  <TableCell className="range-level" title={plane.level_name+" · "+plane.level_id}>{plane.level_name}</TableCell>
  <TableCell><Input className="range-offset" aria-label={labels[name]+" offset"} inputMode="decimal" value={text} disabled={disabled||plane.unlimited} onChange={e=>numeric(e.target.value)}/></TableCell>
  <TableCell>{plane.allow_unlimited&&<Checkbox aria-label={labels[name]+" Unlimited"} checked={plane.unlimited} disabled={disabled} onChange={e=>onEdit(value,e.target.checked)}/>}</TableCell>
 </TableRow><TableRow className="range-adjustment"><TableCell colSpan={4}>
  <Slider aria-label={labels[name]+" slider"} min={limits[0]} max={limits[1]} step="any" value={Math.min(limits[1],Math.max(limits[0],value))} disabled={disabled||plane.unlimited} onChange={e=>onEdit(Number(e.target.value))}/>
 </TableCell></TableRow></>;
}
