import {chromium} from "playwright";
import assert from "node:assert/strict";
import {mkdirSync,writeFileSync} from "node:fs";
import {execFileSync} from "node:child_process";
import path from "node:path";
import {fixture,root} from "./native-fixture.mjs";
const f=await fixture(),samples=[];let browser,page,passed=false;
const check=(value,name)=>{assert.ok(value,name);samples.push({name,passed:true});console.log("PASS "+name);};
try{
 // Use the same .NET DesktopLaunch as the actual ribbon, including its PID-bound bootstrap pipe.
 // An allocated loopback CDP port is enabled only in the offline fixture launcher for this driver.
 const launched=await f.command("launch "+path.join(root,"desktop/out/RevitThyme-win32-x64/RevitThyme.exe"));
 browser=await chromium.connectOverCDP(launched.cdp);const context=browser.contexts()[0];
 page=context.pages()[0]??await context.waitForEvent("page");page.setDefaultTimeout(15000);
 await page.getByText("Adapter orchestration fixture (no Revit)",{exact:false}).waitFor();
 check(await page.getByText("Native capture and validated Apply · M2 source preview").isVisible(),"Packaged UI presents explicit native connection and qualification boundary");
 const cut=page.getByLabel("Cut Plane offset");await cut.waitFor();
 await page.getByRole("button",{name:"Review Apply",exact:true}).click();
 const apply=page.getByRole("button",{name:"Apply to Revit",exact:true});
 check(await apply.isDisabled(),"Native Apply stays disabled until target/range checkbox confirmed");
 await page.getByLabel("Confirm displayed target and range").check();await apply.click();
 await page.getByText("Native outcome: unchanged_verified",{exact:true}).waitFor();
 check((await f.command("observe")).transactions===0,"Packaged identical Apply opens no fixture transaction");
 await page.getByRole("button",{name:"Refresh native capture",exact:true}).click();
 await cut.fill("1524");await cut.blur();
 await page.getByRole("button",{name:"Review Apply",exact:true}).click();
 await page.getByLabel("Confirm displayed target and range").waitFor();
 assert.equal(await page.getByText("Side effects: none.",{exact:false}).count(),0,"Changed native review must not claim no side effects");
 assert.ok(await page.getByText("One plan-view range transaction and undo item; source geometry unchanged.",{exact:true}).isVisible(),"Native mutation effect must be disclosed before confirmation");
 await page.getByLabel("Confirm displayed target and range").check();await apply.click();
 await page.getByRole("button",{name:"Inspect Apply outcome",exact:true}).click();
 await page.getByText("Native outcome: applied_verified",{exact:true}).waitFor();
 const observation=await f.command("observe");
 check(observation.transactions===1&&observation.cut===5,"Packaged numeric mm edit applies exactly 5 ft through Rust and native IPC");
 await page.keyboard.press("Escape");
 check(await page.getByText("Native outcome: applied_verified",{exact:true}).isVisible()&&await page.getByRole("button",{name:"Cancel",exact:true}).isDisabled(),"Escape/Cancel after native Apply cannot claim the mutation was cancelled or undone");
 await page.getByRole("button",{name:"Inspect Apply outcome",exact:true}).click();
 check((await f.command("observe")).transactions===1,"Inspect outcome never reruns mutation");
 await page.getByRole("button",{name:"Refresh native capture",exact:true}).click();await cut.fill("1828.8");await cut.blur();
 await page.getByRole("button",{name:"Review Apply",exact:true}).click();
 await f.command("invalidate");await page.getByLabel("Confirm displayed target and range").check();await apply.click();
 await page.getByRole("button",{name:"Inspect Apply outcome",exact:true}).click();await page.getByText("Native outcome: rejected",{exact:true}).waitFor();
 check((await f.command("observe")).transactions===1,"Native revision changed after review rejects packaged Apply without another transaction");
 await page.getByRole("button",{name:"Refresh native capture",exact:true}).click();await cut.fill("1828.8");await cut.blur();
 await page.getByRole("button",{name:"Review Apply",exact:true}).click();await f.command("pause");
 await page.getByLabel("Confirm displayed target and range").check();await apply.click();await page.getByText("Native outcome: queued",{exact:true}).waitFor();
 await page.getByRole("button",{name:"Cancel queued Apply",exact:true}).click();await page.getByText("Native outcome: cancelled",{exact:true}).waitFor();await f.command("resume");
 check((await f.command("observe")).transactions===1,"Packaged queued cancellation prevents a transaction");
 await page.getByRole("button",{name:"Reconnect",exact:true}).click();await cut.waitFor();
 check(await page.getByRole("button",{name:"Review Apply",exact:true}).isEnabled(),"Reconnect captures fresh native facts before another review");
 check(await page.evaluate(()=>typeof window.revitthyme?.apply==="function"&&typeof window.require==="undefined"&&!Object.keys(window.revitthyme).some(k=>/credential|pipe|file|shell/i.test(k))),"Native preload exposes only named operations; no credential/pipe/filesystem/Node access");
 mkdirSync(path.join(root,"artifacts/m2"),{recursive:true});await page.screenshot({path:path.join(root,"artifacts/m2/native-packaged.png"),fullPage:true});passed=true;
}catch(error){if(page)console.error((await page.locator("body").innerText()).slice(0,2200));throw error;}
finally{
 if(page)await page.close();if(browser)await browser.close();f.stop();mkdirSync(path.join(root,"artifacts/m2"),{recursive:true});
 writeFileSync(path.join(root,"artifacts/m2/native-ui.json"),JSON.stringify({sha:execFileSync("git",["rev-parse","HEAD"],{cwd:root,encoding:"utf8"}).trim(),scope:"packaged_application_with_offline_native_fixture",actual_revit:false,passed,samples},null,2)+"\n");
}
