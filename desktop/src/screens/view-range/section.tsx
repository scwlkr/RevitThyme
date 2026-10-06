import type {Preview,Unit} from "../../contracts/generated";
import {keys,PlaneKey} from "./use-editor";
import {labels} from "./range-row";
export function Section({preview,unit,onDrag}:{preview:Preview;unit:Unit;onDrag:(key:PlaneKey,feet:number)=>void}) {
 const [left,bottom,right,top]=preview.section.bounds_feet;
 const x=(v:number)=>50+(v-left)/(right-left||1)*680;
 const z=(v:number)=>430-(v-bottom)/(top-bottom||1)*380;
 return <svg aria-label="Model section" role="img" viewBox="0 0 920 500" style={{width:"100%",height:"100%"}}>
  <rect x="50" y="50" width="680" height="380" fill="none" stroke="rgb(var(--line))"/>
  {[0,2,4,6,8,10].map(v=><g key={v}><line x1={50} x2={730} y1={z(v)} y2={z(v)} stroke="rgb(var(--line))" strokeDasharray="2 6"/><text x={12} y={z(v)+4} fill="rgb(var(--muted))" fontSize={12}>{v} ft</text></g>)}
  {preview.section.segments.map(([a,b],i)=><line key={i} x1={x(a[0])} y1={z(a[1])} x2={x(b[0])} y2={z(b[1])} stroke="rgb(var(--timber))" strokeWidth={2}/>)}
  {keys.map(k=>{
    const y=Math.min(450,Math.max(30,z(preview.elevations_feet[k])));
    return <g key={k}><line x1={50} x2={735} y1={y} y2={y} stroke={"var(--"+k+")"} strokeWidth={k==="cut"?3:2} strokeDasharray={k==="cut"?"":"8 5"}/>
     {!preview.proposed[k].unlimited && <line aria-label={labels[k]+" plane drag"} x1={50} x2={735} y1={y} y2={y} stroke="transparent" strokeWidth={16} style={{cursor:"ns-resize"}}
      onPointerDown={ev=>ev.currentTarget.setPointerCapture(ev.pointerId)}
      onPointerMove={ev=>{if(!ev.currentTarget.hasPointerCapture(ev.pointerId))return;const svg=ev.currentTarget.ownerSVGElement!;const point=svg.createSVGPoint();point.x=ev.clientX;point.y=ev.clientY;const local=point.matrixTransform(svg.getScreenCTM()!.inverse());const elevation=bottom+(430-local.y)/380*(top-bottom);onDrag(k,elevation-preview.proposed[k].base_feet);}}
      onPointerUp={ev=>ev.currentTarget.releasePointerCapture(ev.pointerId)}/>}
     <text x={745} y={y+4} fill={"var(--"+k+")"} fontSize={13}>{labels[k]} · {preview.proposed[k].unlimited?"Unlimited":Number(preview.display_offsets[k].toPrecision(7))+" "+unit}</text></g>;
  })}
  <text x={50} y={478} fill="rgb(var(--muted))" fontSize={12}>Project coordinates · Revit internal feet · opening is a void in the triangle mesh</text>
 </svg>;
}
