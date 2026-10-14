import assert from "node:assert/strict";
import path from "node:path";
import {captureWindow,verifySectionLayout} from "./section-layout.mjs";

export async function compactDialogUi(app,page,folder,check){
 for(const zoom of [1,1.25]){
  await app.evaluate(({BrowserWindow},zoom)=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1120,700);w.webContents.setZoomFactor(zoom);},zoom);
  await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  const bounds=await page.evaluate(()=>{
   const labels=["Top offset","Cut Plane offset","Bottom offset","View Depth offset","Slice axis","Slice position","Review Apply","Cancel","Reset","Display units","Dark theme","About this preview"];
   return {width:innerWidth,height:innerHeight,controls:labels.map(label=>{
    const element=document.querySelector('[aria-label="'+label+'"]'),b=element.getBoundingClientRect();
    return {label,left:b.left,top:b.top,right:b.right,bottom:b.bottom};
   })};
  });
  for(const b of bounds.controls)assert.ok(b.left>=0&&b.top>=0&&b.right<=bounds.width+1&&b.bottom<=bounds.height+1,"Primary control fits without scrolling at "+zoom*100+"%: "+b.label);
  const offsets=bounds.controls.slice(0,4);
  assert.ok(offsets.every(b=>Math.abs(b.left-offsets[0].left)<1),"Four numeric offsets are aligned");
  await verifySectionLayout(page);
  check(true,"Compact dialog keeps range, section, slice and actions together at "+zoom*100+"%");
  await captureWindow(app,path.join(folder,"compact-"+zoom*100+".png"));
 }
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].webContents.setZoomFactor(1));
}
