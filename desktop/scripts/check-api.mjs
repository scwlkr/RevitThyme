import {spawn} from "node:child_process";
import {createInterface} from "node:readline";
import {randomBytes,randomUUID} from "node:crypto";
import {mkdirSync,writeFileSync} from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";
import {build} from "esbuild";
import {pathToFileURL} from "node:url";
const root=path.resolve(import.meta.dirname,"../.."),folder=path.join(root,"artifacts/m1");
mkdirSync(folder,{recursive:true});
await build({entryPoints:[path.join(root,"desktop/src/contracts/generated.ts")],bundle:true,platform:"node",format:"esm",outfile:path.join(folder,"schemas.mjs")});
const s=await import(pathToFileURL(path.join(folder,"schemas.mjs")).href);
const token=randomBytes(32).toString("hex"),session=randomUUID();
const child=spawn(path.join(root,"replacement/target/debug/revitthyme-app.exe"),["--synthetic",session],{stdio:["pipe","pipe","pipe"],windowsHide:true});
const ready=new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error("Startup timeout")),10000);child.once("error",reject);createInterface({input:child.stdout}).once("line",line=>{clearTimeout(timer);resolve(JSON.parse(line));});});
child.stdin.write(token+"\n");
const {port}=await ready;
const api=async(route,request,auth=true)=>{const response=await fetch("http://127.0.0.1:"+port+"/v1/"+route,{method:"POST",headers:{"Content-Type":"application/json",...(auth?{Authorization:"Bearer "+token}:{})},body:JSON.stringify(request)});return {status:response.status,body:await response.json()};};
const samples=[],timings=[];
function check(value,name){assert.ok(value,name);samples.push({name,passed:true});}
try{
 let result=await api("capture",{protocol:1,view_kind:"floor",partial_fixture:false},false);
 check(result.status===401 && s.ApiErrorSchema.safeParse(result.body).success,"Unauthenticated Axum request rejected");
 result=await api("capture",{protocol:2,view_kind:"floor",partial_fixture:false});
 check(result.status===409 && result.body.code==="protocol_mismatch","Protocol mismatch rejected");
 for(const view_kind of ["floor","engineering","ceiling"]){
  const captured=await api("capture",{protocol:1,view_kind,partial_fixture:false});const snapshot=s.SnapshotSchema.parse(captured.body);
  check(snapshot.mode==="synthetic" && snapshot.native_write_available===false,"Explicit offline identity: "+view_kind);
  const edits=Object.fromEntries(["top","cut","bottom","depth"].map(k=>[k,{value:snapshot.original[k].offset_feet,unit:"ft",unlimited:snapshot.original[k].unlimited}]));
  const request={protocol:1,snapshot_id:snapshot.snapshot_id,target:snapshot.target,input_revision:1,axis:"y",fraction:0.015625,unit:"mm",edits};
  s.PreviewRequestSchema.parse(request);
  const original=s.ProposalSchema.parse((await api("propose",request)).body);
  check(original.identical && original.changed_ids.length===0 && original.side_effects.length===0,"Review untouched values preserves exact originals");
  for(const unit of ["mm","m","ft"]){const preview=s.PreviewSchema.parse((await api("preview",{...request,unit})).body);check(preview.proposed.cut.offset_feet===snapshot.original.cut.offset_feet,"Unit switch preserves native precision: "+unit);}
  const converted=s.PreviewSchema.parse((await api("preview",{...request,edits:{...edits,cut:{value:1524,unit:"mm",unlimited:false}}})).body);
  check(converted.proposed.cut.offset_feet===5 && converted.display_offsets.cut===1524,"Rust owns input and display conversion");
  for(const bad of [{...request,target:{...request.target,session_id:"another"}},{...request,snapshot_id:"expired"}, {...request,fraction:2},{...request,edits:{...edits,cut:{...edits.cut,unlimited:true}}}]){
   const rejected=await api("preview",bad);check(rejected.status>=400 && s.ApiErrorSchema.safeParse(rejected.body).success,"Target/range/slice rejection is structured");
  }
  const extra={...request,arbitraryCode:"refused"};
  check(!s.PreviewRequestSchema.safeParse(extra).success && (await api("preview",extra)).status===400,"Closed Rust/Zod request rejects extra fields");
  check(!s.PreviewRequestSchema.safeParse({...request,edits:{...edits,cut:{...edits.cut,value:Infinity}}}).success,"Zod rejects nonfinite values");
  for(let i=0;i<80;i++){const start=performance.now();const p=s.PreviewSchema.parse((await api("preview",{...request,input_revision:i,fraction:i/80})).body);timings.push({round_trip_ms:performance.now()-start,rust_ms:p.elapsed_ms});}
  const old=request;
  await api("capture",{protocol:1,view_kind,partial_fixture:true});
  check((await api("propose",old)).status===409,"Refresh invalidates old proposals");
 }
 const partial=s.SnapshotSchema.parse((await api("capture",{protocol:1,view_kind:"floor",partial_fixture:true})).body);
 check(partial.partial && partial.diagnostics.some(d=>d.startsWith("Partial")),"Partial capture includes omission reason");
 check((await api("capture",{protocol:1,view_kind:"floor",partial_fixture:false,oversized:"x".repeat(17000)})).status===400,"HTTP body limit enforced");
 const sorted=timings.map(t=>t.round_trip_ms).sort((a,b)=>a-b);
 writeFileSync(path.join(folder,"api-evidence.json"),JSON.stringify({scope:"source_offline",fixture:"synthetic courtyard house / 108 triangles",node:process.version,samples,timings,round_trip_p95_ms:sorted[Math.floor(sorted.length*.95)],budget_ms:100,passed:sorted[Math.floor(sorted.length*.95)]<100,actual_revit:false},null,2)+"\n");
 check(sorted[Math.floor(sorted.length*.95)]<100,"Representative fixture p95 preview under 100 ms");
 console.log(samples.length+" Axum/OpenAPI/Zod contract observations pass; p95 "+sorted[Math.floor(sorted.length*.95)].toFixed(2)+" ms.");
}finally{child.stdin.end();await new Promise(resolve=>child.once("exit",resolve));}

