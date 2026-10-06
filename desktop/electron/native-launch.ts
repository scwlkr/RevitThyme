export interface NativeLaunch {pipe:string;session:string;pid:number;start:string;credential:string}
export const nativeRequested=process.argv.some(arg=>arg.startsWith("--revit-"));
let binding:Promise<NativeLaunch|undefined>|undefined;
export function nativeLaunch():Promise<NativeLaunch|undefined>{
 if(binding)return binding;
 if(!nativeRequested)return binding=Promise.resolve(undefined);
 binding=new Promise((resolve,reject)=>{
   const value=(key:string)=>{const indexes=process.argv.flatMap((arg,i)=>arg===key?[i]:[]);if(indexes.length!==1)throw Error("Missing or repeated native launch binding.");return process.argv[indexes[0]+1]??"";};
   try{
     const session=value("--revit-session"),pipe=value("--revit-pipe"),pid=Number(value("--revit-pid")),start=value("--revit-start");
     if(!/^[a-f\d-]{36}$/i.test(session)||!Number.isSafeInteger(pid)||pid<1||!/^\d{1,20}$/.test(start)||pipe!==`RevitThyme-${pid}-${session}`)throw Error("Invalid native launch binding.");
     const bootstrap=value("--revit-bootstrap");if(!/^RevitThyme-bootstrap-[a-f\d-]{36}$/i.test(bootstrap))throw Error("Invalid bootstrap channel.");
     const channel=createConnection("\\\\.\\pipe\\"+bootstrap);
     let frame="";const timer=setTimeout(()=>finish(Error("Native startup credential missing. Relaunch from Revit ribbon.")),10000);
     const receive=(chunk:Buffer)=>{frame+=chunk.toString("ascii");if(frame.length>65)return finish(Error("Invalid owned startup frame."));if(frame.endsWith("\n")){const credential=frame.slice(0,-1);finish(/^[a-f\d]{64}$/i.test(credential)?undefined:Error("Invalid native credential."),credential);}};
     function finish(error?:Error,credential=""){clearTimeout(timer);channel.off("data",receive);channel.destroy();if(error)reject(error);else resolve({session,pipe,pid,start,credential});}
     channel.on("data",receive);channel.once("error",()=>finish(Error("Native bootstrap unavailable. Relaunch from Revit ribbon.")));
   }catch(e){reject(e);}
 });return binding;
}
import {createConnection} from "node:net";
