import assert from "node:assert/strict";
import {writeFileSync} from "node:fs";
export async function captureWindow(app,file){
 // Electron captures the compositor at its actual OS scale, avoiding CDP screenshot DPI clipping.
 const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString("base64"));
 writeFileSync(file,Buffer.from(png,"base64"));
}
export async function verifyUnderlay(page,choice,sectionTop=10.5){
 const bands=page.getByLabel("Underlay level band");
 if(choice==="none"){assert.equal(await bands.count(),0);return;}
 assert.equal(await bands.count(),1);
 assert.equal(await bands.getAttribute("data-bottom-feet"),"2");
 assert.equal(await bands.getAttribute("data-top-feet"),choice.startsWith("unbounded")?String(sectionTop):"6");
 const direction=choice.endsWith("up")?"up":"down";
 const arrow=page.getByLabel("Underlay looking "+direction+" direction");
 const [start,end]=await arrow.evaluate(e=>[Number(e.getAttribute("y1")),Number(e.getAttribute("y2"))]);
 assert.ok(direction==="up"?end<start:end>start,"Underlay arrow has the captured viewing direction");
}
// Measure the rendered text, independently of the callout layout implementation.
export async function verifySectionLayout(page) {
 const boxes=await page.getByRole("img",{name:"Model section"}).evaluate(svg=>{
  const bounds=svg.getBoundingClientRect();
  return {bounds:{left:bounds.left,right:bounds.right},labels:[...svg.querySelectorAll("text")]
   .filter(t=>/^(Top|Cut Plane|Bottom|View Depth) ·/.test(t.textContent))
   .map(t=>{const b=t.getBoundingClientRect();return {text:t.textContent,left:b.left,right:b.right,top:b.top,bottom:b.bottom};})};
 });
 assert.equal(boxes.labels.length,4,"All four plane labels are rendered");
 for(const a of boxes.labels){
  assert.ok(a.left>=boxes.bounds.left && a.right<=boxes.bounds.right,"Plane label stays inside the section: "+a.text);
  for(const b of boxes.labels){if(a===b)continue;
   assert.ok(a.right<=b.left || b.right<=a.left || a.bottom<=b.top || b.bottom<=a.top,"Plane labels overlap: "+a.text+" / "+b.text);
  }
 }
 return boxes;
}
