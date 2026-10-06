import {_electron as electron} from "playwright";
import {renameSync,existsSync,mkdirSync,writeFileSync} from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";
const root=path.resolve(import.meta.dirname,"../.."),folder=path.join(root,"artifacts/m1");
const executablePath=path.join(root,"desktop/out/RevitThyme-win32-x64/RevitThyme.exe");
const sidecar=path.join(path.dirname(executablePath),"resources/revitthyme-app.exe"),backup=sidecar+".test-backup";
assert.ok(!path.relative(root,sidecar).startsWith("..") && !existsSync(backup));
let app;
try{
 renameSync(sidecar,backup);
 app=await electron.launch({executablePath});
 const page=await app.firstWindow();page.setDefaultTimeout(15000);
 await page.getByText(/Rust component is missing/).waitFor();
 assert.ok(await page.getByRole("button",{name:"Review Apply",exact:true}).isDisabled());
 renameSync(backup,sidecar);
 await page.getByRole("button",{name:"Reconnect",exact:true}).click();
 await page.getByRole("button",{name:"Review Apply",exact:true}).click();
 await page.getByText("Apply preview · identical values",{exact:true}).waitFor();
 mkdirSync(folder,{recursive:true});writeFileSync(path.join(folder,"recovery-evidence.json"),JSON.stringify({scope:"packaged_application",missing_child_rejected:true,reconnect_after_restore:true,actual_revit:false},null,2)+"\n");
 console.log("Missing packaged sidecar rejects safely; restoring the owned file and reconnecting recovers.");
}finally {if(app)await app.close();if(existsSync(backup))renameSync(backup,sidecar);}

