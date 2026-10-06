import type {Preview,Unit,PlanDirection} from "../../contracts/generated";
import {keys,PlaneKey} from "./use-editor";
import {labels} from "./range-row";
import {UnderlaySection} from "./underlay";
export function Section({preview,unit,direction,onDrag}:{preview:Preview;unit:Unit;direction:PlanDirection;onDrag:(key:PlaneKey,feet:number)=>void}) {
 const [left,bottom,right,top]=preview.section.bounds_feet;
 const x=(v:number)=>64+(v-left)/(right-left||1)*500;
 const z=(v:number)=>420-(v-bottom)/(top-bottom||1)*380;
 const positions=keys.map(key=>{
  const positive=key==="top" || key==="depth"&&direction==="up";
  const raw=preview.proposed[key].unlimited?(positive?40:420):z(preview.elevations_feet[key]);
  return {key,y:Math.min(420,Math.max(40,raw)),raw,labelY:0};
 }).sort((a,b)=>a.y-b.y);
 // Move only the callouts. Model elevations and drag coordinates stay unchanged.
 for(let i=0;i<positions.length;i++)positions[i].labelY=Math.max(60,positions[i].y,i?positions[i-1].labelY+56:0);
 for(let i=positions.length-1;i>=0;i--)positions[i].labelY=Math.min(400,positions[i].labelY,i<positions.length-1?positions[i+1].labelY-56:400);
 const rough=(top-bottom)/5||1,power=10**Math.floor(Math.log10(rough));
 const step=([1,2,5,10].find(v=>v*power>=rough)??10)*power;
 const ticks=Array.from({length:6},(_,i)=>Math.ceil(bottom/step)*step+i*step).filter(v=>v<=top);
 return <svg aria-label="Model section" role="img" viewBox="0 0 920 460" style={{width:"100%",height:"100%"}}>
  <rect x="64" y="40" width="500" height="380" fill="none" stroke="rgb(var(--line))"/>
  <UnderlaySection bands={preview.underlay_bands} z={z}/>
  {ticks.map(v=><g key={v}><line x1={64} x2={564} y1={z(v)} y2={z(v)} stroke="rgb(var(--line))" strokeDasharray="2 6"/><text x={4} y={z(v)+5} fill="rgb(var(--muted))" fontSize={14}>{Number(v.toPrecision(4))} ft</text></g>)}
  {preview.section.segments.map(([a,b],i)=><line key={i} x1={x(a[0])} y1={z(a[1])} x2={x(b[0])} y2={z(b[1])} stroke="rgb(var(--timber))" strokeWidth={2}/>)}
  {positions.map(({key:k,y,raw,labelY})=>{
    const plane=preview.proposed[k],color="var(--"+k+")";
    const value=plane.unlimited?"Unlimited":Number(preview.display_offsets[k].toPrecision(7))+" "+unit;
    const outside=!plane.unlimited&&(raw<40||raw>420);
    return <g key={k}><line x1={64} x2={564} y1={y} y2={y} stroke={color} strokeWidth={k==="cut"?3:2} strokeDasharray={k==="cut"?"":"8 5"}/>
     <polyline points={`564,${y} 590,${y} 622,${labelY} 634,${labelY}`} fill="none" stroke={color}/>
     {!plane.unlimited && <line aria-label={labels[k]+" plane drag"} x1={64} x2={564} y1={y} y2={y} stroke="transparent" strokeWidth={16} style={{cursor:"ns-resize"}}
      onPointerDown={ev=>ev.currentTarget.setPointerCapture(ev.pointerId)}
      onPointerMove={ev=>{if(!ev.currentTarget.hasPointerCapture(ev.pointerId))return;const svg=ev.currentTarget.ownerSVGElement!;const point=svg.createSVGPoint();point.x=ev.clientX;point.y=ev.clientY;const local=point.matrixTransform(svg.getScreenCTM()!.inverse());const elevation=bottom+(420-local.y)/380*(top-bottom);onDrag(k,elevation-plane.base_feet);}}
      onPointerUp={ev=>ev.currentTarget.releasePointerCapture(ev.pointerId)}/>}
     <text x={640} y={labelY-3} fill={color} fontSize={19}><tspan x={640}>{labels[k]} ·</tspan><tspan x={640} dy={23} fontSize={17}>{value}{outside?(raw<40?" ↑":" ↓"):""}</tspan></text></g>;
  })}
 </svg>;
}
