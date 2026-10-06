import {app,BrowserWindow,ipcMain,protocol,IpcMainInvokeEvent} from "electron";
import {readFile} from "node:fs/promises";
import {readFileSync} from "node:fs";
import {createHash} from "node:crypto";
import path from "node:path";
import {start,stop,call} from "./sidecar";
import {CaptureRequestSchema,SnapshotSchema,PreviewRequestSchema,PreviewSchema,ProposalSchema,ApplyRequestSchema,OutcomeRequestSchema,MutationResultSchema} from "../src/contracts/generated";
import {nativeRequested} from "./native-launch";
protocol.registerSchemesAsPrivileged([{scheme:"app",privileges:{standard:true,secure:true,supportFetchAPI:true}}]);
let window:BrowserWindow;
function sender(event:IpcMainInvokeEvent) {
 const url=new URL(event.senderFrame?.url??"about:blank");
 if(event.sender!==window.webContents || event.senderFrame!==window.webContents.mainFrame || url.protocol!=="app:" || url.hostname!=="bundle")throw Error("Untrusted IPC sender.");
}
app.whenReady().then(async()=>{
 const assets=app.isPackaged?path.join(process.resourcesPath,"dist"):path.resolve(__dirname,"../dist");
 const manifestPath=app.isPackaged?path.join(process.resourcesPath,"package-manifest.json"):path.resolve(__dirname,"../package-manifest.json");
 const manifest=JSON.parse(readFileSync(manifestPath,"utf8"));
 protocol.handle("app",async(request)=>{
   const url=new URL(request.url);if(url.hostname!=="bundle" || request.method!=="GET")return new Response("Refused",{status:403});
   let file=decodeURIComponent(url.pathname).replace(/^\//,"");if(!file || file.endsWith("/"))file+="index.html";
   if(!path.extname(file))file+=".html";
   const hash=manifest.assets[file];if(!hash)return new Response("Missing packaged asset",{status:404});
   const bytes=await readFile(path.join(assets,file));
   if(createHash("sha256").update(bytes).digest("hex")!==hash)return new Response("Asset integrity failure",{status:500});
   const type=file.endsWith(".html")?"text/html":file.endsWith(".js")?"text/javascript":file.endsWith(".css")?"text/css":file.endsWith(".svg")?"image/svg+xml":file.endsWith(".png")?"image/png":"application/octet-stream";
   const hashes=type==="text/html"?[...bytes.toString().matchAll(/<script(?![^>]*src=)[^>]*>([\s\S]*?)<\/script>/g)].map(m=>"'sha256-"+createHash("sha256").update(m[1]).digest("base64")+"'").join(" "):"";
   return new Response(bytes,{headers:{"Content-Type":type,"Content-Security-Policy":"default-src 'none'; script-src 'self' "+hashes+"; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'none'; base-uri 'none'; frame-src 'none'; object-src 'none'"}});
 });
 window=new BrowserWindow({width:1280,height:920,minWidth:900,minHeight:680,show:false,webPreferences:{preload:path.join(__dirname,"preload.cjs"),contextIsolation:true,sandbox:true,nodeIntegration:false}});
 window.removeMenu();
 window.webContents.setWindowOpenHandler(()=>({action:"deny"}));
 window.webContents.on("will-navigate",(e,url)=>{const u=new URL(url);if(u.protocol!=="app:" || u.hostname!=="bundle")e.preventDefault();});
 window.webContents.on("will-attach-webview",e=>e.preventDefault());
 window.webContents.session.setPermissionRequestHandler((_w,_p,callback)=>callback(false));
 ipcMain.handle("range:capture",async(e,r)=>{sender(e);return SnapshotSchema.parse(await call("capture",CaptureRequestSchema.parse(r)));});
 ipcMain.handle("range:preview",async(e,r)=>{sender(e);return PreviewSchema.parse(await call("preview",PreviewRequestSchema.parse(r)));});
 ipcMain.handle("range:propose",async(e,r)=>{sender(e);return ProposalSchema.parse(await call("propose",PreviewRequestSchema.parse(r)));});
 ipcMain.handle("range:mode",async(e)=>{sender(e);return nativeRequested?"native":"synthetic";});
 ipcMain.handle("range:apply",async(e,r)=>{sender(e);return MutationResultSchema.parse(await call("apply",ApplyRequestSchema.parse(r)));});
 ipcMain.handle("range:outcome",async(e,r)=>{sender(e);return MutationResultSchema.parse(await call("outcome",OutcomeRequestSchema.parse(r)));});
 ipcMain.handle("range:cancel",async(e,r)=>{sender(e);return MutationResultSchema.parse(await call("cancel",OutcomeRequestSchema.parse(r)));});
 ipcMain.handle("range:reconnect",async(e)=>{sender(e);await start();});
 start().catch(()=>{}); // UI presents recoverable startup failure on Capture.
 await window.loadURL("app://bundle/");
 window.show();
});
app.on("before-quit",stop);
app.on("window-all-closed",()=>app.quit());
