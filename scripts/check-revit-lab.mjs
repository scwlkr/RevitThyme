import {spawn} from "node:child_process";
import {createInterface} from "node:readline";
import {randomUUID} from "node:crypto";
import {mkdirSync,readFileSync,writeFileSync} from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";

const root=path.resolve(import.meta.dirname,".."),folder=path.join(root,"artifacts/revit-lab/offline",randomUUID());
mkdirSync(folder,{recursive:true});
const checks=[];
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
 check(listed.length===6&&listed.every(t=>t.inputSchema.type==="object")&&!listed.some(t=>t.name.includes("code")),"MCP initialize/tools/list advertise only named native lab operations");
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
 await f.command("wrong_fixture");const before=await f.command("observe");
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
 writeFileSync(path.join(folder,"report.json"),JSON.stringify({scope:"source_and_windows_mcp_offline",actual_revit:false,passed,checks},null,2)+"\n");
 console.log("Evidence: "+path.join(folder,"report.json"));
}
