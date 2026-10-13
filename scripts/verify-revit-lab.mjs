import {spawn,spawnSync} from "node:child_process";
import {createInterface} from "node:readline";
import {readFileSync,writeFileSync} from "node:fs";
import {randomUUID,createHash} from "node:crypto";
import assert from "node:assert/strict";
import path from "node:path";

const root=path.resolve(import.meta.dirname,".."),folder=path.resolve(process.argv[2]??"");
const stage=process.argv[3]??"baseline";
assert.ok(["baseline","applied","undo"].includes(stage),"Expected baseline, applied or undo observation");
const expectedCut=stage==="applied"?5:4;
assert.equal(path.dirname(folder),path.join(root,"artifacts/revit-lab"),"Use the exact prepared run folder");
const plan=JSON.parse(readFileSync(path.join(folder,"plan.json"),"utf8"));
const binding=JSON.parse(readFileSync(path.join(folder,"binding.json"),"utf8"));
assert.equal(plan.folder,folder);assert.equal(binding.fixture_path,plan.config.fixture_path);
assert.equal(binding.allow_writes,false,"This driver qualifies the first read-only lab run");
const launch=JSON.parse(readFileSync(path.join(folder,"launch.json"),"utf8").replace(/^\uFEFF/,""));
assert.equal(binding.process_id,launch.pid);assert.equal(binding.process_start_ticks,launch.start_ticks);
const fixtureHash=createHash("sha256").update(readFileSync(binding.fixture_path)).digest("hex");
const baseline=stage==="baseline"?null:JSON.parse(readFileSync(path.join(folder,"live-readonly.json"),"utf8"));
if(baseline){assert.ok(baseline.passed);assert.equal(baseline.binding.process_id,binding.process_id);assert.equal(baseline.fixture_hash,fixtureHash,"UI Apply/Undo must not save the fixture");}
const inventory=spawnSync("pwsh",["-NoProfile","-Command","Get-Process -Name Revit | ForEach-Object { [pscustomobject]@{pid=$_.Id;start_ticks=$_.StartTime.ToUniversalTime().Ticks.ToString();title=$_.MainWindowTitle} } | ConvertTo-Json -AsArray -Compress"],{encoding:"utf8",windowsHide:true});
assert.equal(inventory.status,0);const processes=JSON.parse(inventory.stdout);
assert.ok(processes.some(p=>p.pid===binding.process_id&&p.start_ticks===binding.process_start_ticks));
const child=spawn(path.join(folder,"bundle/revitthyme-test.exe"),["--lab-mcp",path.join(folder,"binding.json")],{cwd:root,stdio:["pipe","pipe","pipe"],windowsHide:true});
const queue=[];let serial=0,errors="",passed=false,snapshot;const checks=[];
child.stderr.on("data",v=>errors+=v);
createInterface({input:child.stdout}).on("line",line=>{const next=queue.shift();if(next)next(JSON.parse(line));});
const rpc=(method,params={})=>new Promise((resolve,reject)=>{
 const timer=setTimeout(()=>reject(Error("Native lab response timeout: "+errors)),45000);
 queue.push(value=>{clearTimeout(timer);resolve(value);});
 child.stdin.write(JSON.stringify({jsonrpc:"2.0",id:++serial,method,params})+"\n");
});
const check=(value,name)=>{assert.ok(value,name);checks.push({name,passed:true});console.log("PASS "+name);};
const call=async(name,arguments_={})=>(await rpc("tools/call",{name,arguments:arguments_})).result;
try{
 const hello=await rpc("initialize",{protocolVersion:"2025-11-25",capabilities:{},clientInfo:{name:"RevitThyme live lab verification",version:"1"}});
 check(hello.result?.serverInfo.name==="revitthyme-test","MCP initialize on exact prepared native connection");
 child.stdin.write(JSON.stringify({jsonrpc:"2.0",method:"notifications/initialized"})+"\n");
 const tools=(await rpc("tools/list")).result.tools;
 const expected=["lab_capture","view_range_preview","view_range_propose","view_range_outcome","view_range_cancel"];
 if(tools.some(t=>t.name==="revit_context"))expected.push("revit_context","revit_elements","revit_project_health");
 check(tools.length===expected.length&&expected.every(name=>tools.some(t=>t.name===name)),"Read-only MCP tool list matches the implemented bundle and excludes Apply");
 let captured=await call("lab_capture");check(!captured.isError,"Actual Revit API capture succeeded");
 const s=captured.structuredContent.snapshot;snapshot=s;
 check(s.target.process_id===binding.process_id&&s.target.process_start_ticks===binding.process_start_ticks&&s.target.session_id===binding.session_id&&s.document_name==="RevitThyme-Test"&&s.view_name==="RevitThyme Lab Plan","Capture returns the exact test process, document and plan");
 check(["top","cut","bottom","depth"].every((k,i)=>Math.abs(s.original[k].offset_feet-[8,expectedCut,0,-1][i])<1e-9)&&s.bounds_feet.every((v,i)=>Math.abs(v-[0,0,0,12,1,9][i])<1e-8)&&s.triangle_count>0,"Native readback preserves all four expected range offsets and fixture dimensions");
 if(baseline)check(["top","bottom","depth"].every(k=>JSON.stringify(s.original[k])===JSON.stringify(baseline.snapshot.original[k]))&&s.original.cut.level_id===baseline.snapshot.original.cut.level_id&&s.triangle_count===baseline.snapshot.triangle_count,"Only the reviewed Cut offset changes; levels and captured geometry remain intact");
 const request={protocol:2,snapshot_id:s.snapshot_id,target:s.target,input_revision:1,axis:"x",fraction:0.5,unit:"ft",edits:Object.fromEntries(["top","cut","bottom","depth"].map(k=>[k,{value:s.original[k].offset_feet,unit:"ft",unlimited:s.original[k].unlimited}]))};
 check(!(await call("view_range_preview",request)).isError,"Shared Rust preview from actual test geometry");
 const millimetres=await call("view_range_preview",{...request,unit:"mm"});
 check(!millimetres.isError&&Math.abs(millimetres.structuredContent.display_offsets.cut-expectedCut*304.8)<1e-8,"Native cut readback converts internal feet to millimetres");
 const proposal=await call("view_range_propose",request);
 check(!proposal.isError&&proposal.structuredContent.identical&&!proposal.structuredContent.native_write_available,"Native proposal validates unchanged test range without write permission");
 const wrong=await call("view_range_propose",{...request,target:{...s.target,session_id:randomUUID()}});
 check(wrong.isError,"Wrong target proposal rejected without a model mutation");
 captured=await call("lab_capture");check(!captured.isError,"Test connection remains usable after rejection");
 check((await call("view_range_preview",request)).isError,"Previous capture cannot be silently reused after refresh");
 passed=true;
}finally{
 const exited=new Promise(resolve=>child.once("exit",resolve));child.stdin.end();await exited;
 const report={sha:plan.sha,scope:"actual_revit_native_lab_read_only",stage,actual_revit:true,passed,processes_at_start:processes,binding,fixture_hash:fixtureHash,snapshot,checks,limits:["This driver performs no model writes","No production document API access","UI actions require separate retained evidence","Wrong-file/reopen rejection covered offline; native enforcement requires a later approved negative-target fixture trial"]};
 const file=path.join(folder,stage==="baseline"?"live-readonly.json":"live-"+stage+".json");writeFileSync(file,JSON.stringify(report,null,2)+"\n");console.log("Evidence: "+file);
}
