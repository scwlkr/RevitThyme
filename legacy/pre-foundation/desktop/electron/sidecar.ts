import {spawn, ChildProcessWithoutNullStreams} from "node:child_process";
import {randomBytes,randomUUID} from "node:crypto";
import {createInterface} from "node:readline";
import path from "node:path";
import {app} from "electron";
import {ApiErrorSchema} from "../src/contracts/generated";
import {nativeLaunch} from "./native-launch";
let child:ChildProcessWithoutNullStreams|undefined;
let endpoint="";
let credential="";
let ready:Promise<void>=Promise.resolve();
export function start():Promise<void> {ready=launchOwned();return ready;}
async function launchOwned():Promise<void> {
  const native=await nativeLaunch();
  stop();credential=randomBytes(32).toString("hex");const session=randomUUID();
  const exe=app.isPackaged?path.join(process.resourcesPath,"revitthyme-app.exe"):path.resolve(__dirname,"../../replacement/target/release/revitthyme-app.exe");
  const args=native?["--native",session,native.pipe,String(native.pid),native.start,native.session]:["--synthetic",session];
  child=spawn(exe,args,{stdio:["pipe","pipe","pipe"],windowsHide:true});
  const owned=child;
  child.stdin.write(credential+"\n");
  if(native)child.stdin.write(native.credential+"\n");
  const startup=new Promise<void>((resolve,reject)=>{
    const timer=setTimeout(()=>reject(Error("Rust startup timed out. Reconnect.")),10000);
    owned.once("error",()=>{clearTimeout(timer);reject(Error("Rust component is missing. Restore this bundle."));});
    owned.once("exit",()=>{clearTimeout(timer);if(child===owned)endpoint="";reject(Error("Rust component stopped. Reconnect."));});
    owned.stderr.on("data",()=>{}); // Never forward private native diagnostics or credentials to renderer/logs.
    const lines=createInterface({input:owned.stdout});
    lines.once("line",(line)=>{
      try { const data=JSON.parse(line);if(data.protocol!==2 || data.version!=="0.1.1" || data.session!==session || data.mode!==(native?"native":"synthetic") || !Number.isInteger(data.port) || data.port<1 || data.port>65535)throw Error();
        endpoint="http://127.0.0.1:"+data.port;clearTimeout(timer);resolve();
      } catch {clearTimeout(timer);reject(Error("Incompatible Rust component. Restore a compatible bundle."));}
    });
  });return startup;
}
export function stop() { const owned=child;child=undefined;endpoint="";credential="";if(owned){owned.stdin.end();setTimeout(()=>{if(owned.exitCode===null)owned.kill();},2000).unref();} }
export async function call(route:"capture"|"preview"|"propose"|"apply"|"outcome"|"cancel",request:unknown) {
  await ready;if(!endpoint)throw Error("Rust disconnected. Reconnect and refresh.");
  const response=await fetch(endpoint+"/v1/"+route,{method:"POST",headers:{"Content-Type":"application/json",Authorization:"Bearer "+credential},body:JSON.stringify(request),signal:AbortSignal.timeout(10000),redirect:"error"});
  const result=await response.json();
  if(!response.ok){const error=ApiErrorSchema.parse(result);throw Error(error.code+": "+error.message);}
  return result;
}
