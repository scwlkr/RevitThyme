import {chromium} from "playwright";
import {parseArgs} from "node:util";
import {readFileSync,writeFileSync} from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";
const {values}=parseArgs({options:{cdp:{type:"string"},"source-sha":{type:"string"},references:{type:"string"},case:{type:"string"}}});
const url=new URL(values.cdp);assert.equal(url.hostname,"127.0.0.1");assert.equal(url.protocol,"http:");
assert.match(values["source-sha"],/^[0-9a-f]{40}$/);assert.match(values.case,/^[a-z0-9-]+$/);assert.match(values.references,/^[a-z0-9-]+$/);
const folder=path.resolve(import.meta.dirname,"../../artifacts/m3");
const reference=JSON.parse(readFileSync(path.join(folder,values.references+".json"),"utf8"));
assert.equal(reference.modified,false);assert.ok(reference.cases.length>=3);
const results=[];let browser,passed=false;
// Distance to native BRep edge samples is independent of Rust's triangle slicing.
function distance(p,[a,b]){
 const dx=b[0]-a[0],dy=b[1]-a[1],length=dx*dx+dy*dy;
 const t=length?Math.max(0,Math.min(1,((p[0]-a[0])*dx+(p[1]-a[1])*dy)/length)):0;
 return Math.hypot(p[0]-a[0]-t*dx,p[1]-a[1]-t*dy);
}
try{
 browser=await chromium.connectOverCDP(values.cdp);const page=browser.contexts()[0].pages()[0];
 assert.equal(await page.evaluate(()=>window.revitthyme.mode()),"native");
 await page.getByRole("button",{name:"Refresh native capture",exact:true}).click();
 await page.getByText(reference.document_name+" / "+reference.view_name,{exact:false}).waitFor();
 await page.waitForFunction(()=>!document.querySelector('[aria-label="Review Apply"]').disabled);
 for(const c of reference.cases){
  const old=await page.getByRole("img",{name:"Model section"}).innerHTML();
  await page.getByLabel("Slice axis").selectOption(c.axis);await page.getByLabel("Slice position").fill(String(c.fraction));
  await page.waitForFunction(html=>document.querySelector('[aria-label="Model section"]').innerHTML!==html,old);
  await page.waitForFunction(()=>!document.querySelector('[aria-label="Review Apply"]').disabled);
  const fraction=Number(await page.getByLabel("Slice position").inputValue());assert.equal(fraction,c.fraction);
  const b=reference.bounds_feet,h=c.axis==="x"?1:0;
  assert.ok(Math.abs(b[c.axis==="x"?0:1]+fraction*(b[c.axis==="x"?3:4]-b[c.axis==="x"?0:1])-c.position)<1e-8);
  const pixels=await page.getByRole("img",{name:"Model section"}).locator('line[stroke="rgb(var(--timber))"]').evaluateAll(lines=>lines.map(e=>[Number(e.getAttribute("x1")),Number(e.getAttribute("y1")),Number(e.getAttribute("x2")),Number(e.getAttribute("y2"))]));
  const world=(x,z)=>[b[h]+(x-64)/500*(b[h+3]-b[h]),b[2]+(420-z)/380*(b[5]-b[2])];
  const segments=pixels.map(([x1,z1,x2,z2])=>[world(x1,z1),world(x2,z2)]);
  const samples=c.reference_segments.flatMap(([a,b],i)=>[0,.25,.5,.75,1].map(t=>({point:[a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1])],curved:c.curve_kinds[i]!=="Line"})));
  assert.ok(samples.length>=20,"Native reference must contain useful cut-face edges");
  const errors=samples.map(p=>Math.min(...segments.map(s=>distance(p.point,s))));
  // Native face meshes and native curve tessellation approximate arcs differently.
  // Straight geometry: 0.00001 ft. Curved hardware: 1/16 inch faceting budget.
  const max=Math.max(...errors),missing=errors.filter((e,i)=>e>(samples[i].curved?1/192:1e-5)).length;
  results.push({name:c.name,element_id:c.element_id,axis:c.axis,position:c.position,fraction,reference_segments:c.reference_segments.length,rendered_segments:segments.length,samples:samples.length,max_error_feet:max,straight_max_error_feet:Math.max(...errors.filter((_,i)=>!samples[i].curved)),curved_tolerance_feet:1/192,missing_samples:missing,rendered_world_segments:segments});
  await page.getByRole("img",{name:"Model section"}).screenshot({path:path.join(folder,values.case+"-"+c.name+".png")});
  assert.equal(missing,0,c.name+" native BRep cut-face samples must occur in actual displayed section within straight/faceted tolerances");
  console.log("PASS "+c.name+": "+samples.length+" native BRep samples, max error "+max+" ft");
 }
 passed=true;
}finally{
 writeFileSync(path.join(folder,values.case+".json"),JSON.stringify({scope:"actual_revit_packaged_ui_brep_comparison",source_sha:values["source-sha"],passed,reference_file:values.references,results},null,2)+"\n");
 if(browser)await browser.close();
}
