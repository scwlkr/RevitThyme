// Read-only actual-Revit qualification in the authorized isolated lab session.
import {spawn} from "node:child_process";
import {createInterface} from "node:readline";
import {readFileSync,writeFileSync,existsSync} from "node:fs";
import {createHash,randomUUID} from "node:crypto";
import path from "node:path";
import assert from "node:assert/strict";

const root=path.resolve(import.meta.dirname,".."),folder=path.resolve(process.argv[2]??"");
assert.equal(process.argv.length,3);assert.equal(path.dirname(folder),path.join(root,"artifacts/revit-lab"));
const plan=JSON.parse(readFileSync(path.join(folder,"plan.json"))),binding=JSON.parse(readFileSync(path.join(folder,"binding.json")));
assert.equal(plan.folder,folder);assert.equal(binding.fixture_path,plan.config.fixture_path);assert.equal(binding.allow_writes,false);
const hash=file=>createHash("sha256").update(readFileSync(file)).digest("hex");
for(const [file,expected] of Object.entries(plan.hashes))assert.equal(hash(path.join(folder,"bundle",file)),expected);
const upgradePath=path.join(folder,"mcp-upgrade.json"),upgrade=existsSync(upgradePath)?JSON.parse(readFileSync(upgradePath)):null;
let executable=path.join(folder,"bundle/revitthyme-test.exe"),bindingPath=path.join(folder,"binding.json");
if(upgrade){
 assert.match(upgrade.source_sha,/^[a-f0-9]{40}$/);assert.equal(upgrade.native_sha,plan.sha);
 assert.equal(upgrade.binary,path.join(folder,"mcp-upgrades",upgrade.source_sha,"bundle/revitthyme-test.exe"));
 assert.equal(upgrade.binding_path,path.join(folder,"mcp-upgrades",upgrade.source_sha,"binding.json"));
 assert.equal(hash(upgrade.binary),upgrade.binary_sha256);assert.equal(hash(upgrade.binding_path),upgrade.binding_sha256);
 assert.deepEqual(JSON.parse(readFileSync(upgrade.binding_path)),binding);
 executable=upgrade.binary;bindingPath=upgrade.binding_path;
}
const fixtureHash=hash(binding.fixture_path),observations=[],checks=[];
const child=spawn(executable,["--lab-mcp",bindingPath],{cwd:root,stdio:["pipe","pipe","pipe"],windowsHide:true});
const ended=new Promise(resolve=>child.once("exit",resolve));
const waiting=[];let serial=0,errors="",passed=false;
child.stderr.on("data",v=>errors+=v);
createInterface({input:child.stdout}).on("line",line=>waiting.shift()?.(JSON.parse(line)));
const rpc=(method,params={})=>new Promise((resolve,reject)=>{
 const timer=setTimeout(()=>reject(Error("Native inspection timed out: "+errors)),50000);
 waiting.push(value=>{clearTimeout(timer);resolve(value);});
 child.stdin.write(JSON.stringify({jsonrpc:"2.0",id:++serial,method,params})+"\n");
});
const call=async(name,args={})=>{
 const r=(await rpc("tools/call",{name,arguments:args})).result;
 observations.push({name,result:r});return r;
};
const check=(condition,name)=>{assert.ok(condition,name);checks.push({name,passed:true});console.log("PASS "+name);};
try{
 const hello=await rpc("initialize",{protocolVersion:"2025-11-25",capabilities:{},clientInfo:{name:"Native inspection qualification",version:"1"}});
 check(hello.result.serverInfo.version==="0.2.0","Exact MCP inspection bundle initialized");
 child.stdin.write(JSON.stringify({jsonrpc:"2.0",method:"notifications/initialized"})+"\n");
 const tools=(await rpc("tools/list")).result.tools;
 check(tools.length===8&&!tools.some(t=>t.name==="view_range_apply"),"Eight native tools advertised with Apply disabled");
 const first=await call("revit_context");assert.ok(!first.isError);const context=first.structuredContent;
 check(context.target.process_id===binding.process_id&&context.target.process_start_ticks===binding.process_start_ticks
  &&context.target.session_id===binding.session_id&&context.document_name==="RevitThyme-Test"&&!context.is_modified,"Context reads the exact unmodified disposable document");
 const query=(category,extra={})=>({target:context.target,query:{category,after_id:"0",limit:100,name_contains:"",missing_mark:false,ids:[],...extra}});
 const levels=(await call("revit_elements",query("levels",{name_contains:"Lab Level"}))).structuredContent;
 check(levels.scan_complete&&!levels.has_more&&levels.items.length===2,"Both independently specified fixture levels are enumerated");
 const views=(await call("revit_elements",query("views",{name_contains:"RevitThyme Lab Plan"}))).structuredContent;
 check(views.items.length===1&&views.items[0].view_template_id==="-1"&&views.items[0].template_eligible,"Known fixture plan is template-eligible and has no template");
 const defaultViews=(await call("revit_elements",query("views",{name_contains:"Level 1"}))).structuredContent;
 check(defaultViews.items.length===3&&new Set(defaultViews.items.map(v=>v.view_kind)).size===3,"Default floor/ceiling/structural plans legitimately share a name across view kinds");
 const exact=(await call("revit_elements",query("levels",{ids:[levels.items[0].element_id,"9223372036854775807"]}))).structuredContent;
 check(exact.items.length===1&&exact.items[0].unique_id===levels.items[0].unique_id&&exact.missing_ids.includes("9223372036854775807"),"Native ID lookup round-trips and reports an absent 64-bit ID");
 const healthResult=await call("revit_project_health",{target:context.target,limit:10});assert.ok(!healthResult.isError);
 const health=healthResult.structuredContent,counts=Object.fromEntries(health.checks.map(c=>[c.code,c.count]));
 check(health.complete&&counts.views_missing_templates>=1&&["duplicate_view_names","duplicate_sheet_numbers","doors_missing_marks","unplaced_rooms","revit_warnings"].every(k=>counts[k]===0),"Fresh fixture health agrees with independently known empty categories and missing template");
 const wrong=await call("revit_elements",{...query("levels"),target:{...context.target,document_id:randomUUID()}});
 check(wrong.isError&&wrong.structuredContent.code==="stale_target","Wrong document target is rejected");
 const last=(await call("revit_context")).structuredContent;
 check(JSON.stringify(last.target)===JSON.stringify(context.target)&&last.is_modified===context.is_modified&&hash(binding.fixture_path)===fixtureHash,"Inspection preserves target, modified state and saved fixture bytes");
 passed=true;
}finally{
 child.stdin.end();await ended;
 const file=path.join(folder,"live-inspection.json");
 writeFileSync(file,JSON.stringify({sha:upgrade?.source_sha??plan.sha,native_sha:plan.sha,executable,executable_hash:hash(executable),actual_revit:true,scope:"native_inspection_read_only",passed,binding,fixture_hash:fixtureHash,checks,observations,
  limits:["Empty fixture does not qualify native door/family/room mapping or larger models","No model transactions or production-model qualification requested"]},null,2)+"\n");
 console.log("Evidence: "+file);
}
