import {spawn} from "node:child_process";
import {createInterface} from "node:readline";
import {randomBytes} from "node:crypto";
import path from "node:path";
export const root=path.resolve(import.meta.dirname,"../..");
export async function fixture(){
 const credential=randomBytes(32).toString("hex");
 const child=spawn(path.join(root,"native/Adapter.Checks/bin/Release/net10.0-windows/Adapter.Checks.exe"),["--serve"],{stdio:["pipe","pipe","pipe"],windowsHide:true});
 const buffered=[],waiting=[];let stderr="";child.stderr.on("data",d=>stderr+=d);
 const lines=createInterface({input:child.stdout});lines.on("line",line=>{let value;try{value=JSON.parse(line);}catch{return;}if(waiting.length)waiting.shift().resolve(value);else buffered.push(value);});
 const next=()=>buffered.length?Promise.resolve(buffered.shift()):new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error("Fixture timeout: "+stderr.slice(-500))),10000);waiting.push({resolve:v=>{clearTimeout(timer);resolve(v);}});});
 child.stdin.write(credential+"\n");const binding=await next();
 return {credential,binding,child,command:async command=>{child.stdin.write(command+"\n");return next();},stop:()=>{child.stdin.end();setTimeout(()=>{if(child.exitCode===null)child.kill();},2000).unref();}};
}
