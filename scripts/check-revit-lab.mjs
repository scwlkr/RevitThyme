import {spawn} from "node:child_process";
import {createInterface} from "node:readline";
import {randomUUID} from "node:crypto";
import {mkdirSync,readFileSync,writeFileSync} from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";

const root=path.resolve(import.meta.dirname,".."),folder=path.join(root,"artifacts/revit-lab/offline",randomUUID());
mkdirSync(folder,{recursive:true});
const checks=[],observations=[];
function processLines(exe,args){
 const child=spawn(exe,args,{cwd:root,stdio:["pipe","pipe","pipe"],windowsHide:true});
 const waiting=[],buffer=[];let stderr="";
 child.stderr.on("data",v=>stderr+=v);
 createInterface({input:child.stdout}).on("line",line=>{const value=JSON.parse(line);if(waiting.length)waiting.shift()(value);else buffer.push(value);});
 const next=()=>buffer.length?Promise.resolve(buffer.shift()):new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error("Response timeout: "+stderr)),25000);waiting.push(v=>{clearTimeout(timer);resolve(v);});});
 return {child,next,command:async value=>{child.stdin.write(value+"\n");return next();},close:async()=>{const exit=new Promise(resolve=>child.once("exit",resolve));child.stdin.end();await exit;}};
}
const f=processLines("dotnet",["native/LabChecks/bin/Release/net10.0-windows/LabChecks.dll","--serve",folder]);
const {binding}=await f.next();
const app=path.join(root,"replacement/target/debug/revitthyme-app.exe");
let m;let serial=0;
const rpc=async(method,params={})=>m.command(JSON.stringify({jsonrpc:"2.0",id:++serial,method,params}));
const call=async(name,args={})=>(await rpc("tools/call",{name,arguments:args})).result;
const check=(value,name)=>{assert.ok(value,name);checks.push({name,passed:true});console.log("PASS "+name);};
async function start(file){
 m=processLines(app,["--lab-mcp",file]);
 const r=await rpc("initialize",{protocolVersion:"2025-11-25",capabilities:{},clientInfo:{name:"offline-check",version:"1"}});
 assert.equal(r.result.protocolVersion,"2025-11-25");
 m.child.stdin.write(JSON.stringify({jsonrpc:"2.0",method:"notifications/initialized"})+"\n");
 return m;
}
function preview(s){return {protocol:2,snapshot_id:s.snapshot_id,target:s.target,input_revision:1,axis:"x",fraction:0.5,unit:"ft",edits:Object.fromEntries(["top","cut","bottom","depth"].map(k=>[k,{value:s.original[k].offset_feet,unit:"ft",unlimited:s.original[k].unlimited}]))};}
let passed=false;
try{
 await start(binding);
 const listed=(await rpc("tools/list")).result.tools;
 check(listed.length===9&&listed.every(t=>t.inputSchema.type==="object")&&!listed.some(t=>t.name.includes("code")),"MCP initialize/tools/list advertise only named native lab operations");
 const context=(await call("revit_context")).structuredContent;
 observations.push({case:"context",value:context});
 check(context.target.process_id===JSON.parse(readFileSync(binding)).process_id&&context.internal_length_unit==="ft","Context identifies the native document without geometry capture");
 const query={target:context.target,query:{category:"doors",after_id:"0",limit:2,name_contains:"",missing_mark:true,ids:[]}};
 let page=await call("revit_elements",query);
 check(!page.isError&&page.structuredContent.items.length===2&&page.structuredContent.has_more,"Door query pages missing marks with stable 64-bit IDs");
 query.query.after_id=page.structuredContent.next_after_id;
 page=await call("revit_elements",query);
 check(page.structuredContent.items.length===1&&!page.structuredContent.has_more,"Door query continues without repeated elements");
 const idsQuery={...query,query:{...query.query,missing_mark:false,after_id:"0",ids:["4294967303","4294967301","9999999999"],limit:1}};
 const idsFirst=(await call("revit_elements",idsQuery)).structuredContent;
 const idsSecond=(await call("revit_elements",{...idsQuery,query:{...idsQuery.query,after_id:idsFirst.next_after_id}})).structuredContent;
 check(idsFirst.items[0].element_id==="4294967301"&&idsSecond.items[0].element_id==="4294967303"&&idsSecond.missing_ids.includes("9999999999"),"Exact ID inspection paginates in numeric order and reports missing IDs");
 const health=(await call("revit_project_health",{target:context.target,limit:2})).structuredContent;
 observations.push({case:"health_known_fixture",value:health});
 const counts=Object.fromEntries(health.checks.map(c=>[c.code,c.count]));
 check(counts.doors_missing_marks===3&&counts.unplaced_rooms===1&&counts.views_missing_templates===1&&counts.duplicate_view_names===2&&counts.duplicate_sheet_numbers===2&&counts.revit_warnings===1,"Health evaluates known findings through native transport and shared Rust rules");
 check(health.checks.find(c=>c.code==="doors_missing_marks").truncated&&health.complete&&(await f.command("observe")).transactions===0,"Health caps samples while retaining exact counts and opens no transaction");
 for(const bad of [{...query,query:{...query.query,limit:101}},{...query,unexpected:true},{...query,query:{...query.query,after_id:"-1"}},{...query,query:{...query.query,category:"rooms",missing_mark:true}}]){
  check((await call("revit_elements",bad)).isError,"Element query rejects malformed or unsupported inputs");
 }
 const wrong={...context.target,document_id:randomUUID()};
 const beforeInspection=await f.command("observe");
 check((await call("revit_elements",{...query,target:wrong})).structuredContent.code==="stale_target"&&(await f.command("observe")).calls===beforeInspection.calls,"Wrong inspection target is rejected before reading element facts");
 await f.command("inspection_partial");
 const partial=(await call("revit_project_health",{target:context.target,limit:2})).structuredContent;
 observations.push({case:"health_partial",value:partial});
 check(!partial.complete&&partial.checks.every(c=>!c.complete),"Incomplete native scans never claim a complete health result");
 await f.command("inspection_many");
 const large=(await call("revit_project_health",{target:context.target,limit:2})).structuredContent;
 observations.push({case:"health_row_limit",value:large});
 check(!large.complete&&large.checks.find(c=>c.code==="doors_missing_marks").count===2000,"Health bounds examined rows while preserving lower-bound counts");
 await f.command("inspection_wide");
 const wideQuery={...query,query:{...query.query,after_id:"0",limit:100}};
 const wide=(await call("revit_elements",wideQuery)).structuredContent;
 check(wide.items.length>0&&wide.items.length<100&&wide.has_more,"Native pages enforce serialized byte limits for Unicode labels");
 const wideNext=(await call("revit_elements",{...wideQuery,query:{...wideQuery.query,after_id:wide.next_after_id}})).structuredContent;
 check(BigInt(wideNext.items[0].element_id)>BigInt(wide.items.at(-1).element_id),"Byte-limited pages preserve the continuation without lost rows");
 await f.command("inspection_oversized");
 check((await call("revit_elements",wideQuery)).structuredContent.code==="result_limit"&&!(await call("revit_context")).isError,"Oversized element fails with a structured result and connection stays usable");
 await f.command("inspection_");
 check((await m.command("{not-json")).error.code===-32700&&!(await call("revit_context")).isError,"Malformed JSON gets a protocol error and the next native call succeeds");
 check((await rpc("tools/call",{name:"lab_capture",arguments:{},unexpected:true})).error.code===-32602,"MCP rejects unexpected call fields");
 check((await call("lab_capture",{code:"anything"})).isError,"Capture rejects arbitrary arguments");
 let result=await call("lab_capture");check(!result.isError,"MCP capture traverses real Windows pipe, credential handoff and API-context queue");
 let s=result.structuredContent.snapshot,request=preview(s);
 check((await call("view_range_preview",request)).structuredContent.section.segments.length>0,"MCP preview uses captured native geometry and shared Rust section code");
 let p=(await call("view_range_propose",request)).structuredContent;
 check(p.identical&&(await f.command("observe")).transactions===0,"Native proposal preserves original values without opening a transaction");
 request.edits.cut.value=5;p=(await call("view_range_propose",request)).structuredContent;
 const apply={protocol:2,request_id:randomUUID(),proposal_id:p.proposal_id,target:s.target,confirmed:false};
 check((await call("view_range_apply",apply)).structuredContent.code==="confirmation_required","MCP Apply refuses missing confirmation");
 apply.confirmed=true;result=(await call("view_range_apply",apply)).structuredContent;
 for(let i=0;i<100&&["queued","executing"].includes(result.status);i++)result=(await call("view_range_outcome",{protocol:2,request_id:apply.request_id,target:s.target})).structuredContent;
 check(result.status==="applied_verified"&&result.native_values[0].cut.offset_feet===5&&(await f.command("observe")).transactions===1,"Confirmed MCP Apply verifies exact readback in one offline transaction");
 check((await call("view_range_apply",apply)).structuredContent.status==="applied_verified"&&(await f.command("observe")).transactions===1,"Duplicate confirmed request reads outcome without a second transaction");
 for(let i=0;i<140;i++)assert.ok(!(await call("revit_context")).isError,"Repeated inspection must not exhaust retained mutation outcomes");
 check((await call("view_range_outcome",{protocol:2,request_id:apply.request_id,target:s.target})).structuredContent.status==="applied_verified","Repeated read-only inspections preserve original Apply outcome");
 await f.command("wrong_fixture");const before=await f.command("observe");
 check((await call("revit_context")).structuredContent.code==="wrong_fixture"&&(await f.command("observe")).calls===before.calls,"Context refuses a working file before model inspection");
 result=await call("lab_capture");const after=await f.command("observe");
 check(result.isError&&result.structuredContent.code==="wrong_fixture"&&after.calls===before.calls&&after.transactions===before.transactions,"Wrong working file is rejected at execution before any model API reads or writes");
 await f.command("restore");check(!(await call("lab_capture")).isError,"Original approved document remains usable after rejected wrong file");
 await f.command("reopen");result=await call("lab_capture");
 check(result.isError&&result.structuredContent.code==="reopened_fixture","Reopened fixture cannot silently replace the pinned document instance");
 await m.close();m=null;
 const b=JSON.parse(readFileSync(binding,"utf8"));b.allow_writes=false;
 const readonly=path.join(folder,"readonly.json");writeFileSync(readonly,JSON.stringify(b));await start(readonly);
 check(!(await rpc("tools/list")).result.tools.some(t=>t.name==="view_range_apply"),"Read-only run omits Apply from discovery and dispatch");
 await m.close();m=null;
 for(const key of ["process_id","process_start_ticks","session_id"]){
  const changed={...b};if(key==="process_id")changed[key]+=1000000;else if(key==="session_id")changed[key]=randomUUID();else changed[key]="1";
  // Keep names consistent to reach OS PID or native handshake verification where possible.
  if(key==="process_id"||key==="session_id"){changed.pipe=b.pipe;changed.credential_target=b.credential_target;}
  const file=path.join(folder,key+".json");writeFileSync(file,JSON.stringify(changed));
  if(key!=="process_start_ticks"){
   const child=spawn(app,["--lab-mcp",file],{cwd:root,stdio:["pipe","ignore","pipe"],windowsHide:true});let errors="";child.stderr.on("data",v=>errors+=v);
   const code=await new Promise(resolve=>child.once("exit",resolve));check(code!==0&&errors.includes("Invalid native lab binding"),"Altered "+key+" binding is rejected without fallback");
  }else{await start(file);result=await call("lab_capture");check(result.isError&&result.structuredContent.code==="native_disconnected","Wrong process start fails native handshake without fallback");await m.close();m=null;}
 }
 passed=true;
}finally{
 if(m)await m.close();await f.close();
 writeFileSync(path.join(folder,"report.json"),JSON.stringify({scope:"source_and_windows_mcp_offline",actual_revit:false,passed,checks,observations},null,2)+"\n");
 console.log("Evidence: "+path.join(folder,"report.json"));
}
