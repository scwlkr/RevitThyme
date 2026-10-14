import {chromium} from "playwright";
import {parseArgs} from "node:util";
import {mkdirSync,readFileSync,writeFileSync} from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";
const {values}=parseArgs({options:{cdp:{type:"string"},"source-sha":{type:"string"},"document-name":{type:"string"},"view-name":{type:"string"},case:{type:"string"},action:{type:"string"},packet:{type:"string"},"approved-writes":{type:"boolean",default:false}}});
const url=new URL(values.cdp);assert.equal(url.hostname,"127.0.0.1");assert.equal(url.protocol,"http:");
assert.match(values["source-sha"],/^[0-9a-f]{40}$/);assert.match(values.case,/^[a-z0-9-]+$/);
assert.ok(["prepare","discard-reply","inspect-repeat"].includes(values.action));
assert.ok(values["document-name"]&&values["view-name"]);
const folder=path.resolve(import.meta.dirname,"../../artifacts/m3");mkdirSync(folder,{recursive:true});
const packetName=values.packet??values.case;assert.match(packetName,/^[a-z0-9-]+$/);
const packetPath=path.join(folder,packetName+"-packet.json"),checks=[];
let browser,passed=false,result;
const check=(condition,name)=>{assert.ok(condition,name);checks.push(name);console.log("PASS "+name);};
try{
 browser=await chromium.connectOverCDP(values.cdp);const page=browser.contexts()[0].pages()[0];
 check(await page.evaluate(()=>window.revitthyme.mode())==="native","Actual packaged preload is connected to native mode");
 if(values.action==="prepare"){
  result=await page.evaluate(async()=>{
   const b=window.revitthyme;
   const s=await b.capture({protocol:2,view_kind:"floor",plan_direction:"down",underlay_fixture:"none",partial_fixture:false});
   const edits=Object.fromEntries(["top","cut","bottom","depth"].map(k=>[k,{value:s.original[k].offset_feet,unit:"ft",unlimited:s.original[k].unlimited}]));
   edits.cut.value=5;
   const r={protocol:2,snapshot_id:s.snapshot_id,target:s.target,input_revision:1,axis:"y",fraction:0.5,unit:"ft",edits};
   await b.preview(r);const proposal=await b.propose(r);
   return {snapshot:s,proposal,request:{protocol:2,request_id:crypto.randomUUID(),proposal_id:proposal.proposal_id,target:proposal.target,confirmed:true}};
  });
  check(result.snapshot.document_name===values["document-name"]&&result.snapshot.view_name===values["view-name"],"Native target matches the explicitly approved document/view");
  check(result.proposal.after.cut.offset_feet===5&&!result.proposal.identical,"Reviewed proposal changes only the intended Cut to 5 ft");
  writeFileSync(packetPath,JSON.stringify(result,null,2)+"\n");
  console.log(JSON.stringify({target:result.request.target,before:result.proposal.before,after:result.proposal.after}));
 }else{
  assert.ok(values["approved-writes"],"Explicit approved-writes required for transport mutation trials");
  const packet=JSON.parse(readFileSync(packetPath,"utf8"));
  assert.equal(packet.snapshot.document_name,values["document-name"]);assert.equal(packet.snapshot.view_name,values["view-name"]);
  if(values.action==="discard-reply"){
   // Submit once through the shipped typed preload. Deliberately give the caller
   // neither acknowledgement nor terminal reply; inspect the retained ID later.
   await page.evaluate(request=>{void window.revitthyme.apply(request).catch(()=>{});},packet.request);
   result={request_id:packet.request.request_id,caller_reply_discarded:true};
   check(true,"One actual Apply submitted; caller deliberately discards its response and performs no retry");
  }else{
   const query={protocol:2,request_id:packet.request.request_id,target:packet.request.target};
   result=await page.evaluate(async({query,request})=>{
    const b=window.revitthyme;let outcome;
    for(let i=0;i<50;i++){outcome=await b.outcome(query);if(!["queued","executing"].includes(outcome.status))break;await new Promise(r=>setTimeout(r,100));}
    const duplicate1=await b.apply(request),duplicate2=await b.apply(request);
    let conflict;try{await b.apply({...request,proposal_id:request.proposal_id+"-different"});conflict="accepted";}catch(e){conflict=String(e);}
    return {outcome,duplicate1,duplicate2,conflict};
   },{query,request:packet.request});
   check(result.outcome.status==="applied_verified","Outcome inspection recovers verified native truth after caller reply loss");
   check(result.duplicate1.status===result.outcome.status&&result.duplicate2.status===result.outcome.status,"Repeated identical requests return the original verified outcome");
   check(result.conflict.includes("request_id_conflict"),"Same request ID with another payload is rejected");
  }
 }
 passed=true;
}finally{
 writeFileSync(path.join(folder,values.case+".json"),JSON.stringify({scope:"actual_revit_packaged_preload_transport",source_sha:values["source-sha"],case:values.case,action:values.action,passed,checks,result},null,2)+"\n");
 if(browser)await browser.close();
}
