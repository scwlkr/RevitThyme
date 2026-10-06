import {View,Text} from "react-native";
import type {Underlay,UnderlayBand} from "../../contracts/generated";

export function UnderlayInfo({underlay:u}:{underlay:Underlay}){
 const direction=u.direction==="up"?"Look Up":"Look Down";
 return <View className="gap-1">
  <Text className="text-ink text-sm">Underlay Orientation: {direction} {u.direction==="up"?"↑":"↓"} · {u.enabled?"Enabled":"None"}</Text>
  {u.enabled&&<Text className="text-muted text-sm">Underlay range: {u.base_level_name} ({u.base_elevation_feet} ft) → {u.top_unbounded?"Unbounded":u.top_level_name+" ("+u.top_elevation_feet+" ft)"}</Text>}
  <Text className="text-muted text-xs">The shaded band shows underlay levels and viewing direction. Revit controls halftone and final plan visibility.</Text>
 </View>;
}
export function UnderlaySection({bands,z}:{bands:UnderlayBand[];z:(feet:number)=>number}){
 return <>{bands.map((band,i)=>{
  const top=z(band.top_feet),bottom=z(band.bottom_feet);
  const inset=Math.min(8,(bottom-top)/4),size=Math.min(7,(bottom-top)/4);
  const start=band.direction==="up"?bottom-inset:top+inset,end=band.direction==="up"?top+inset:bottom-inset;
  const head=band.direction==="up"?size:-size;
  return <g key={i}>
   <rect aria-label="Underlay level band" data-bottom-feet={band.bottom_feet} data-top-feet={band.top_feet} x={64} y={top} width={500} height={bottom-top} fill="rgb(var(--thyme))" fillOpacity={0.12}/>
   <line aria-label={"Underlay looking "+band.direction+" direction"} x1={548} x2={548} y1={start} y2={end} stroke="rgb(var(--ink))" strokeWidth={2}/>
   <polyline points={`542,${end+head} 548,${end} 554,${end+head}`} fill="none" stroke="rgb(var(--ink))" strokeWidth={2}/>
  </g>;
 })}</>;
}
