import {spawn, ChildProcessWithoutNullStreams} from "node:child_process";
import {randomBytes,randomUUID} from "node:crypto";
import {createInterface} from "node:readline";
import path from "node:path";
import {app} from "electron";
import {ApiErrorSchema} from "../src/contracts/generated";
let child:ChildProcessWithoutNullStreams|undefined;
let endpoint="";
let credential="";
let ready:Promise<void>;
export function start():Promise<void> {
  stop();credential=randomBytes(32).toString("hex");const session=randomUUID();
  const exe=app.isPackaged?path.join(process.resourcesPath,"revitthyme-app.exe"):path.resolve(__dirname,"../../replacement/target/release/revitthyme-app.exe");
  child=spawn(exe,["--synthetic",session],{stdio:["pipe","pipe","pipe"],windowsHide:true});
  const owned=child;
  child.stdin.write(credential+"\n");
  ready=new Promise((resolve,reject)=>{
    const timer=setTimeout(()=>reject(Error("Rust startup timed out. Reconnect.")),10000);
    owned.once("error",()=>{clearTimeout(timer);reject(Error("Rust component is missing. Restore this bundle."));});
    owned.once("exit",()=>{clearTimeout(timer);if(child===owned)endpoint="";reject(Error("Rust component stopped. Reconnect."));});
    owned.stderr.on("data",()=>{}); // Never forward private native diagnostics or credentials to renderer/logs.
    const lines=createInterface({input:owned.stdout});
    lines.once("line",(line)=>{
      try { const data=JSON.parse(line);if(data.protocol!==1 || data.version!=="0.1.0" || data.session!==session || data.mode!=="synthetic" || !Number.isInteger(data.port) || data.port<1 || data.port>65535)throw Error();
        endpoint="http://127.0.0.1:"+data.port;clearTimeout(timer);resolve();
      } catch {clearTimeout(timer);reject(Error("Incompatible Rust component. Restore a compatible bundle."));}
    });
  });return ready;
}
export function stop() { const owned=child;child=undefined;endpoint="";credential="";if(owned){owned.stdin.end();setTimeout(()=>{if(owned.exitCode===null)owned.kill();},2000).unref();} }
export async function call(route:"capture"|"preview"|"propose",request:unknown) {
  await ready;if(!endpoint)throw Error("Rust disconnected. Reconnect and refresh.");
  const response=await fetch(endpoint+"/v1/"+route,{method:"POST",headers:{"Content-Type":"application/json",Authorization:"Bearer "+credential},body:JSON.stringify(request),signal:AbortSignal.timeout(10000),redirect:"error"});
  const result=await response.json();
  if(!response.ok){const error=ApiErrorSchema.parse(result);throw Error(error.code+": "+error.message);}
  return result;
}
