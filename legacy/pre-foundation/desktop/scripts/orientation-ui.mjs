import assert from "node:assert/strict";
import {mkdirSync,writeFileSync} from "node:fs";
import path from "node:path";
import {verifySectionLayout,verifyUnderlay,captureWindow} from "./section-layout.mjs";

// Reuse the real packaged window and its inputs; no replacement renderer or app.
export async function orientationUi(app,page,folder,check){
 mkdirSync(folder,{recursive:true});
 const review=()=>page.getByRole("button",{name:"Review Apply",exact:true}).click();
 const refresh=()=>page.getByRole("button",{name:"Refresh fixture",exact:true}).click();
 for(const unit of ["mm","m","ft"]){
  await page.getByLabel("Display units").selectOption(unit);
  await page.getByRole("button",{name:"Reset",exact:true}).click();
  await review();
  for(const label of ["Cut Plane","Top","Bottom","View Depth"]){await page.getByLabel(label+" offset").fill("0");await review();}
  for(const [width,zoom] of [[1600,1],[1100,1],[900,1.25]]){
   const viewport=await app.evaluate(({BrowserWindow},{width,zoom})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(width,950);w.webContents.setZoomFactor(zoom);return w.getContentSize()[0]/zoom;},{width,zoom});
   await page.waitForFunction(width=>Math.abs(window.innerWidth-width)<=2,viewport);
   await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
   const layout=await verifySectionLayout(page);
   assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth+1),"Page has no horizontal overflow");
   const clipped=await page.locator('button,select,input,svg').evaluateAll(elements=>elements.filter(e=>{const b=e.getBoundingClientRect();return b.width>0&&(b.left<0||b.right>window.innerWidth+1);}).map(e=>e.getAttribute("aria-label")??e.textContent));
   assert.deepEqual(clipped,[],"Controls and chart stay inside the resized viewport");
   writeFileSync(path.join(folder,`layout-${unit}-${width}-${zoom}.json`),JSON.stringify({layout,viewport,metrics:await page.evaluate(()=>({innerWidth,outerWidth,devicePixelRatio})),window:await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].getContentBounds())},null,2));
   check(true,`Four coincident labels fit at ${width}px / ${zoom*100}% / ${unit}`);
   await page.getByRole("img",{name:"RevitThyme",exact:true}).scrollIntoViewIfNeeded();
   await captureWindow(app,path.join(folder,`coincident-${unit}-${width}-${zoom}.png`));
   await page.getByRole("img",{name:"Model section"}).scrollIntoViewIfNeeded();
   await captureWindow(app,path.join(folder,`section-${unit}-${width}-${zoom}.png`));
   await page.getByRole("img",{name:"Model section"}).screenshot({path:path.join(folder,`chart-${unit}-${width}-${zoom}.png`)});
  }
 }
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1400,950);w.webContents.setZoomFactor(1);});
 await page.getByLabel("Display units").selectOption("ft");
 await review();
 for(const [kind,direction] of [["floor","down"],["ceiling","up"],["engineering","up"],["engineering","down"]]){
  await page.getByLabel("Fixture view").selectOption(kind);
  if(kind==="engineering")await page.getByLabel("Fixture plan direction").selectOption(direction);
  await refresh();await review();
  await page.getByText("Main plan: Looking "+direction+(direction==="up"?" ↑":" ↓"),{exact:true}).waitFor();
  await page.getByText("Apply preview · identical values",{exact:true}).waitFor();
  await verifySectionLayout(page);
  check(true,"Packaged supported direction: "+kind+" / "+direction);
  await page.getByLabel("View Depth Unlimited").check();await review();
  await verifySectionLayout(page);
  const y=await page.getByRole("img",{name:"Model section"}).evaluate(svg=>{
   const group=[...svg.querySelectorAll("g")].find(g=>g.textContent.startsWith("View Depth ·"));
   return Number(group.querySelector("line").getAttribute("y1"));
  });
  assert.equal(y,direction==="up"?40:420,"Unlimited depth extends toward the viewing direction");
  check(true,"Unlimited depth is placed on the "+direction+" side of the section");
  await page.screenshot({path:path.join(folder,`${kind}-${direction}-unlimited.png`),fullPage:false});
  await page.getByRole("button",{name:"Reset",exact:true}).click();
  await page.getByLabel("View Depth offset").fill(direction==="up"?"7":"2");
  await page.getByText(direction==="up"?/Looking up: View Depth must be at or above Top/:/Looking down: View Depth must be at or below Bottom/).waitFor();
  assert.ok(await page.getByRole("button",{name:"Review Apply",exact:true}).isDisabled());
  check(true,"Packaged wrong-side depth cannot be reviewed: "+kind+" / "+direction);
 }
 await page.getByLabel("Fixture view").selectOption("floor");await refresh();
 for(const choice of ["none","up","down","unbounded_up","unbounded_down"]){
  await page.getByLabel("Fixture Underlay Orientation").selectOption(choice);await refresh();await review();
  await page.getByText("Main plan: Looking down ↓",{exact:true}).waitFor();
  await verifyUnderlay(page,choice);
  await page.getByText("Apply preview · identical values",{exact:true}).waitFor();
  check(true,"Underlay "+choice+" band and arrow preserve main floor range");
  await page.getByRole("img",{name:"Model section"}).scrollIntoViewIfNeeded();
  await captureWindow(app,path.join(folder,"underlay-"+choice+".png"));
 }
 await page.getByLabel("Fixture Underlay Orientation").selectOption("none");await refresh();
 await page.getByLabel("Display units").selectOption("mm");
 await page.getByRole("button",{name:"Reset",exact:true}).click();
}
