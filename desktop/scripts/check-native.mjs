import {spawn,execFileSync} from "node:child_process";
import {createInterface} from "node:readline";
import {randomBytes,randomUUID} from "node:crypto";
import {mkdirSync,writeFileSync} from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";
import {fixture,root} from "./native-fixture.mjs";
const f=await fixture(),token=randomBytes(32).toString("hex"),session=randomUUID(),b=f.binding;
const child=spawn(path.join(root,"replacement/target/debug/revitthyme-app.exe"),["--native",session,b.pipe,String(b.process_id),b.process_start_ticks,b.session_id],{stdio:["pipe","pipe","pipe"],windowsHide:true});
const ready=new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error("Rust startup timeout")),10000);child.once("error",reject);createInterface({input:child.stdout}).once("line",line=>{clearTimeout(timer);resolve(JSON.parse(line));});});
child.stdin.write(token+"\n"+f.credential+"\n");
const {port}=await ready,samples=[];let passed=false;
const api=async(route,request)=>{const r=await fetch(`http://127.0.0.1:${port}/v1/${route}`,{method:"POST",headers:{"Content-Type":"application/json",Authorization:"Bearer "+token},body:JSON.stringify(request)});return {status:r.status,body:await r.json()};};
function check(value,name){assert.ok(value,name);samples.push({name,passed:true});console.log("PASS "+name);}
function edits(s){return Object.fromEntries(["top","cut","bottom","depth"].map(k=>[k,{value:s.original[k].offset_feet,unit:"ft",unlimited:s.original[k].unlimited}]));}
const capture=async()=>{const r=await api("capture",{protocol:1,view_kind:"floor",partial_fixture:false});assert.equal(r.status,200,JSON.stringify(r.body));return r.body;};
const previewRequest=s=>({protocol:1,snapshot_id:s.snapshot_id,target:s.target,input_revision:1,axis:"x",fraction:0.5,unit:"mm",edits:edits(s)});
async function terminal(r){for(let i=0;i<100&&["queued","executing"].includes(r.status);i++){await new Promise(resolve=>setTimeout(resolve,50));r=(await api("outcome",{protocol:1,request_id:r.request_id,target:r.target})).body;}return r;}
try{
 let s=await capture();
 check(s.mode==="native"&&s.target.process_id===b.process_id&&s.target.session_id===b.session_id&&s.triangle_count===260,"Real Windows framed capture preserves native session and chunks");
 const p=previewRequest(s);let section=(await api("preview",p)).body;
 check(section.section.segments.some(seg=>seg.some(point=>point[0]===0&&point[1]===0)&&seg.some(point=>Math.abs(point[0]-5)<1e-8&&Math.abs(point[1]-5)<1e-8)),"Rust independently intersects captured triangle at expected project coordinates");
 check((await f.command("observe")).transactions===0,"Capture and cached preview open no transaction");
 let review=(await api("propose",p)).body;
 check(review.native_write_available&&review.identical&&review.after.cut.offset_feet===4.000000000000123,"Native review preserves exact original values");
 let apply={protocol:1,request_id:randomUUID(),proposal_id:review.proposal_id,target:s.target,confirmed:false};
 check((await api("apply",apply)).body.code==="confirmation_required","Apply refuses missing explicit confirmation");
 apply.confirmed=true;let result=await terminal((await api("apply",apply)).body);
 check(result.status==="unchanged_verified"&&result.changed_ids.length===0&&(await f.command("observe")).transactions===0,"Identical native Apply verified without transaction");
 s=await capture();let request=previewRequest(s);request.edits.cut.value=5;
 review=(await api("propose",request)).body;
 apply={protocol:1,request_id:randomUUID(),proposal_id:review.proposal_id,target:s.target,confirmed:true};
 // Deliberately discard the accepted HTTP response body, then recover only by the original ID.
 const dropped=await fetch(`http://127.0.0.1:${port}/v1/apply`,{method:"POST",headers:{"Content-Type":"application/json",Authorization:"Bearer "+token},body:JSON.stringify(apply)});
 await dropped.body.cancel();
 result=await terminal((await api("outcome",{protocol:1,request_id:apply.request_id,target:apply.target})).body);
 check(result.status==="applied_verified"&&result.native_values[0].cut.offset_feet===5&&result.changed_ids[0]===s.target.view_id,"Changed Apply returns exact native readback and affected view");
 let observed=await f.command("observe");
 check(observed.transactions===1&&observed.trace.indexOf("post_commit_read")<observed.trace.indexOf("assimilate"),"One transaction; post-commit verification before assimilation");
 result=(await api("apply",apply)).body;
 check(result.status==="applied_verified"&&(await f.command("observe")).transactions===1,"Lost response/duplicate same ID queries outcome, never retries Apply");
 check((await api("apply",{...apply,proposal_id:"different"})).body.code==="request_id_conflict","Changed request ID payload rejected");
 check((await api("preview",request)).body.code==="no_snapshot","Accepted Apply invalidates Rust cached preview");
 s=await capture();request=previewRequest(s);request.edits.cut.value=6;review=(await api("propose",request)).body;
 await api("preview",{...request,input_revision:2,edits:{...request.edits,cut:{value:7,unit:"ft",unlimited:false}}});
 check((await api("apply",{protocol:1,request_id:randomUUID(),proposal_id:review.proposal_id,target:s.target,confirmed:true})).body.code==="stale_proposal"
   &&(await f.command("observe")).transactions===1,"New input revision invalidates prior native confirmation before writes");
 s=await capture();request=previewRequest(s);request.edits.cut.value=6;
 review=(await api("propose",request)).body;
 await f.command("pause");
 apply={protocol:1,request_id:randomUUID(),proposal_id:review.proposal_id,target:s.target,confirmed:true};
 result=(await api("apply",apply)).body;
 check(result.status==="queued","Native API busy state is observable as queued");
 result=(await api("cancel",{protocol:1,request_id:apply.request_id,target:apply.target})).body;
 await f.command("resume");
 check(result.status==="cancelled"&&(await f.command("observe")).transactions===1,"Queued cancellation prevents a second transaction");
 s=await capture();request=previewRequest(s);request.edits.cut.value=6;review=(await api("propose",request)).body;
 await f.command("invalidate");
 apply={protocol:1,request_id:randomUUID(),proposal_id:review.proposal_id,target:s.target,confirmed:true};
 result=await terminal((await api("apply",apply)).body);
 check(result.status==="rejected"&&(await f.command("observe")).transactions===1,"Model/revision change between review and execution cannot write");
 s=await capture();request=previewRequest(s);request.edits.cut.value=6;review=(await api("propose",request)).body;
 await f.command("pause");
 apply={protocol:1,request_id:randomUUID(),proposal_id:review.proposal_id,target:s.target,confirmed:true};
 result=(await api("apply",apply)).body;assert.equal(result.status,"queued");
 await f.command("disconnect");await f.command("resume");
 // Outcome read is permitted to reconnect; this is never a mutation retry.
 for(let i=0;i<10;i++){const r=await api("outcome",{protocol:1,request_id:apply.request_id,target:apply.target});if(r.status===200){result=r.body;break;}await new Promise(resolve=>setTimeout(resolve,100));}
 check(result.status==="cancelled"&&(await f.command("observe")).transactions===1,"Disconnect/reconnect cannot resurrect queued writes");
 check((await api("propose",request)).status>=400,"Old native proposal rejected after reconnect");
 check((await api("outcome",{protocol:1,request_id:randomUUID(),target:s.target})).body.status==="outcome_unconfirmed","Unknown/expired outcome does not imply no change");
 passed=true;
}finally{
 child.stdin.end();f.stop();
 mkdirSync(path.join(root,"artifacts/m2"),{recursive:true});writeFileSync(path.join(root,"artifacts/m2/native-api.json"),JSON.stringify({sha:execFileSync("git",["rev-parse","HEAD"],{cwd:root,encoding:"utf8"}).trim(),scope:"source_offline_real_windows_pipe",actual_revit:false,fixture:".NET Adapter.Checks triangle/transaction orchestration fixture",passed,samples},null,2)+"\n");
}
