import {chromium} from "playwright";
import {parseArgs} from "node:util";
import {mkdirSync,writeFileSync} from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";
import {verifySectionLayout} from "./section-layout.mjs";
const {values}=parseArgs({options:{cdp:{type:"string"},"source-sha":{type:"string"},"document-name":{type:"string"},"view-name":{type:"string"},case:{type:"string"},"idle-ms":{type:"string"},underlay:{type:"string"},"plan-direction":{type:"string",default:"down"},"apply-cut-feet":{type:"string"},"unlimited-plane":{type:"string"},"approved-writes":{type:"boolean",default:false},preview:{type:"boolean",default:false},"stale-review":{type:"boolean",default:false},"capture-rejection":{type:"string"},"display-unit":{type:"string",default:"ft"},"drag-slice":{type:"boolean",default:false},"reconnect-review":{type:"boolean",default:false},"review-only":{type:"boolean",default:false},"queued-action":{type:"string"}}});
const url=new URL(values.cdp);
assert.equal(url.hostname,"127.0.0.1");assert.equal(url.protocol,"http:");
assert.match(values["source-sha"],/^[0-9a-f]{40}$/);assert.match(values.case,/^[a-z0-9-]+$/);
assert.ok(values["document-name"]&&values["view-name"]);
if(values["apply-cut-feet"]!==undefined||values["stale-review"]||values["queued-action"])assert.ok(values["approved-writes"],"Mutation requires explicit approved-writes invocation");
const folder=path.resolve(import.meta.dirname,"../../artifacts/m3");mkdirSync(folder,{recursive:true});
const checks=[],errors=[],measurements=[];let passed=false,submitted=false,browser,page,displayed="";
const check=(value,name)=>{assert.ok(value,name);checks.push({name,passed:true});console.log("PASS "+name);};
try{
 browser=await chromium.connectOverCDP(values.cdp);page=browser.contexts()[0].pages()[0];page.setDefaultTimeout(20000);
 page.on("pageerror",error=>errors.push(String(error)));
 if(values["queued-action"]){
  assert.ok(["cancel","close"].includes(values["queued-action"]));
  await page.getByText(values["document-name"]+" / "+values["view-name"],{exact:false}).waitFor();
  await page.getByLabel("Confirm displayed target and range").check();
  await page.getByRole("button",{name:"Apply to Revit",exact:true}).click();submitted=true;
  await page.getByText("Native outcome: queued",{exact:true}).waitFor();
  check(true,"Actual Apply remains queued while Revit is in a modal native dialog");
  await page.getByText(/^Native outcome:/).locator("..").screenshot({path:path.join(folder,values.case+"-queued.png")});
  if(values["queued-action"]==="cancel"){
   await page.getByRole("button",{name:"Cancel queued Apply",exact:true}).click();
   await page.getByText("Native outcome: cancelled",{exact:true}).waitFor();
   check(true,"Actual queued cancellation returns cancelled without executing");
   await page.getByText(/^Native outcome:/).locator("..").screenshot({path:path.join(folder,values.case+"-cancelled.png")});
  }else {displayed=await page.locator("body").innerText();await page.close();check(true,"Actual desktop window closes with Apply queued");}
  passed=true;
 }else if(values["stale-review"]){
  await page.getByText(values["document-name"]+" / "+values["view-name"],{exact:false}).waitFor();
  await page.getByLabel("Confirm displayed target and range").check();
  await page.getByRole("button",{name:"Apply to Revit",exact:true}).click();submitted=true;
  await page.getByText("Native outcome: rejected",{exact:true}).waitFor();
  check(true,"Existing review is rejected after independent native target invalidation");
  await page.getByText(/^Native outcome:/).locator("..").screenshot({path:path.join(folder,values.case+"-outcome.png")});
  passed=true;
 }else if(values["capture-rejection"]){
  assert.equal(values["apply-cut-feet"],undefined);
  await page.getByRole("button",{name:"Refresh native capture",exact:true}).click();
  await page.getByRole("alert").filter({hasText:values["capture-rejection"]}).waitFor();
  check(await page.getByRole("button",{name:"Review Apply",exact:true}).isDisabled(),"Excluded native capture explains its restriction and disables review");
  check(errors.length===0,"No renderer errors");
  await page.screenshot({path:path.join(folder,values.case+".png"),fullPage:true});passed=true;
 }else{
 const captureStart=performance.now();
 await page.getByRole("button",{name:"Refresh native capture",exact:true}).click();
 await page.getByText(values["document-name"]+" / "+values["view-name"],{exact:false}).waitFor();
 check(await page.evaluate(async()=>await window.revitthyme.mode())==="native","Packaged UI is connected to actual native Revit mode");
 assert.ok(["ft","mm","m"].includes(values["display-unit"]));
 await page.getByLabel("Display units").selectOption(values["display-unit"]);
 await page.waitForFunction(unit=>document.querySelector('[aria-label="Display units"]').value===unit&&!document.body.innerText.includes("Converting display units"),values["display-unit"]);
 const cut=page.getByLabel("Cut Plane offset");await cut.waitFor();const original=await cut.inputValue();
 measurements.push({name:"refresh_to_editable_ms",value:performance.now()-captureStart});
 await verifySectionLayout(page);check(true,"Rendered main plane labels do not overlap");
 check(await page.getByText("Main plan: Looking "+values["plan-direction"]+(values["plan-direction"]==="up"?" ↑":" ↓"),{exact:true}).isVisible(),"Native main direction is independently displayed");
 if(values.underlay){
  const disabled=values.underlay==="none"||values.underlay.startsWith("none-");
  const direction=values.underlay.endsWith("up")?"up":"down";
  const text="Underlay Orientation: Look "+(direction==="up"?"Up ↑":"Down ↓")+" · "+(disabled?"None":"Enabled");
  check(await page.getByText(text,{exact:true}).isVisible(),"Native underlay orientation/enabled state matches independent setup");
  const band=page.getByLabel("Underlay level band");
  check(await band.count()===(disabled?0:1),"Enabled native underlay controls its rendered level band");
  if(!disabled){
   const arrow=page.getByLabel("Underlay looking "+direction+" direction");
   const [start,end]=await arrow.evaluate(e=>[Number(e.getAttribute("y1")),Number(e.getAttribute("y2"))]);
   check(direction==="up"?end<start:end>start,"Native underlay arrow points in the captured direction");
   if(values.underlay.startsWith("unbounded"))check(await page.getByText("Unbounded",{exact:false}).count()>0,"Native Unbounded top is disclosed");
   else check(await band.getAttribute("data-bottom-feet")==="0"&&await band.getAttribute("data-top-feet")==="10","Finite native underlay band matches 0/10 ft reference levels");
  }
 }
 if(values.preview){
  await cut.fill("5");await cut.blur();await page.getByRole("button",{name:"Reset",exact:true}).click();
  await page.waitForFunction(value=>document.querySelector('[aria-label="Cut Plane offset"]')?.value===value,original);
  check(true,"Numeric preview and Reset restore captured input");
  await cut.fill("5");await cut.blur();await page.keyboard.press("Escape");
  await page.getByText("Cancelled. Captured values restored; no model changes.",{exact:true}).waitFor();
  check(await cut.inputValue()===original,"Escape cancels preview input without Apply");
 }
 if(values["drag-slice"]){
  const handle=page.getByLabel("Cut Plane plane drag",{exact:true});await handle.scrollIntoViewIfNeeded();
  const box=await handle.boundingBox();assert.ok(box);const dragStart=performance.now();
  await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.down();
  await page.mouse.move(box.x+box.width/2,box.y+box.height/2-12,{steps:8});await page.mouse.up();
  await page.waitForFunction(value=>document.querySelector('[aria-label="Cut Plane offset"]').value!==value,original);
  measurements.push({name:"drag_to_display_ms",value:performance.now()-dragStart});check(true,"Actual pointer dragging changes cached preview input");
  await page.getByRole("img",{name:"Model section"}).screenshot({path:path.join(folder,values.case+"-drag.png")});
  await page.getByRole("button",{name:"Reset",exact:true}).click();
  await page.waitForFunction(value=>document.querySelector('[aria-label="Cut Plane offset"]').value===value,original);
  const section=page.getByRole("img",{name:"Model section"});const old=await section.innerHTML();
  await page.getByLabel("Slice axis").selectOption("x");
  await page.getByLabel("Slice position").fill("0.4");
  await page.waitForFunction(html=>document.querySelector('[aria-label="Model section"]').innerHTML!==html,old);
  check(true,"Actual slice controls render a different cached model section");
 }
 if(values["idle-ms"]){
  const idle=Number(values["idle-ms"]);assert.ok(idle>=31000&&idle<=45000);await page.waitForTimeout(idle);
 }
 if(values["apply-cut-feet"]!==undefined&&values["apply-cut-feet"]!=="current"){
  const requested=Number(values["apply-cut-feet"]);assert.ok(Number.isFinite(requested));await cut.fill(String(requested*(values["display-unit"]==="mm"?304.8:values["display-unit"]==="m"?0.3048:1)));await cut.blur();
 }
 if(values["unlimited-plane"]){
  for(const plane of values["unlimited-plane"].split(",")){
   assert.ok(["Top","Bottom","View Depth"].includes(plane));
   await page.getByLabel(plane+" Unlimited",{exact:true}).check();
  }
 }
 await page.waitForFunction(()=>!document.querySelector('[aria-label="Review Apply"]').disabled);
 await page.getByRole("img",{name:"Model section"}).screenshot({path:path.join(folder,values.case+"-section.png")});
 await page.getByRole("button",{name:"Review Apply",exact:true}).click();
 await page.getByLabel("Confirm displayed target and range").waitFor();
 check(await page.getByRole("button",{name:"Apply to Revit",exact:true}).isDisabled(),"Fresh native review succeeds and requires explicit target/range confirmation");
 await page.getByText(/^Apply preview ·/).locator("..").screenshot({path:path.join(folder,values.case+"-review.png")});
 if(values["reconnect-review"]){
  await page.getByRole("button",{name:"Reconnect",exact:true}).click();
  await page.getByText(values["document-name"]+" / "+values["view-name"],{exact:false}).waitFor();
  await cut.waitFor();check(await page.getByLabel("Confirm displayed target and range").count()===0,"Reconnect removes old review and confirmation");
  check(await page.getByRole("button",{name:"Apply to Revit",exact:true}).count()===0,"Old review cannot Apply after reconnect");
  await page.waitForFunction(()=>!document.querySelector('[aria-label="Review Apply"]').disabled);
  await page.getByRole("button",{name:"Review Apply",exact:true}).click();
  await page.getByLabel("Confirm displayed target and range").waitFor();check(true,"Fresh review succeeds after reconnect");
 }
 if(values["idle-ms"])check(true,"Actual native review remains available beyond the former idle disconnect");
 if(values["apply-cut-feet"]!==undefined&&!values["review-only"]){
  await page.getByLabel("Confirm displayed target and range").check();await page.getByRole("button",{name:"Apply to Revit",exact:true}).click();submitted=true;
  await page.getByText(/^Native outcome: (applied_verified|unchanged_verified)$/).waitFor();
  check(true,"Confirmed actual native Apply reaches a verified terminal outcome");
  const body=await page.locator("body").innerText();
  if(values["apply-cut-feet"]!=="current")check(body.includes("Verified native Cut: "+Number(values["apply-cut-feet"])+" ft"),"UI outcome contains exact requested native cut readback");
  await page.getByRole("button",{name:"Inspect Apply outcome",exact:true}).click();
  await page.getByText(/^Native outcome: (applied_verified|unchanged_verified)$/).waitFor();check(true,"Outcome inspection preserves terminal truth without resubmission");
  await page.getByText(/^Native outcome:/).locator("..").screenshot({path:path.join(folder,values.case+"-outcome.png")});
 }
 check(errors.length===0,"No renderer errors");
 await page.screenshot({path:path.join(folder,values.case+".png"),fullPage:true});passed=true;
 }
}catch(error){if(page){await page.screenshot({path:path.join(folder,values.case+"-failure.png"),fullPage:true});console.error((await page.locator("body").innerText()).slice(-2000));}throw error;}
finally{
 if(page&&!page.isClosed())displayed=await page.locator("body").innerText().catch(()=>"");
 writeFileSync(path.join(folder,values.case+".json"),JSON.stringify({scope:"actual_revit_packaged_ui",source_sha:values["source-sha"],case:values.case,document:values["document-name"],view:values["view-name"],passed,apply_submitted:submitted,checks,errors,measurements,displayed},null,2)+"\n");
 if(browser)await browser.close();
}
