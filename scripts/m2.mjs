import {spawnSync} from "node:child_process";
import {mkdirSync,writeFileSync} from "node:fs";
import path from "node:path";
const root=path.resolve(import.meta.dirname,".."),checks=[];
function run(program,args){const r=spawnSync(program,args,{cwd:root,encoding:"utf8",maxBuffer:32*1024*1024,timeout:180000,env:{...process.env,DOTNET_SKIP_FIRST_TIME_EXPERIENCE:"1",DOTNET_CLI_TELEMETRY_OPTOUT:"1"}});const output=(r.stdout??"")+(r.stderr??"");checks.push({command:[program,...args],passed:r.status===0,exit:r.status,output});console.log(output);if(r.status!==0)throw Error("Failed: "+program);}
const action=process.argv[2]??"check";
try{
 if(action==="build"||action==="check"){
  run("dotnet",["build","native/RevitAdapter/RevitAdapter.csproj","-c","Release","--ignore-failed-sources"]);
  run("dotnet",["build","native/Qualification/Qualification.csproj","-c","Release","--ignore-failed-sources"]);
 }
 if(action==="check"){
  run("dotnet",["run","--project","native/Adapter.Checks/Adapter.Checks.csproj","-c","Release","--ignore-failed-sources"]);
  run("node",["desktop/scripts/check-native.mjs"]);
 }
 if(action==="ui")run("node",["desktop/scripts/check-native-ui.mjs"]);
 if(!["build","check","ui"].includes(action))throw Error("Usage: project m2 [build|check|ui]");
}catch(e){console.error(e.message);process.exitCode=1;}
finally{mkdirSync(path.join(root,"artifacts/m2"),{recursive:true});const sha=spawnSync("git",["rev-parse","HEAD"],{cwd:root,encoding:"utf8"}).stdout.trim();const scope=action==="ui"?"packaged_application_with_offline_native_fixture":"source_offline";writeFileSync(path.join(root,"artifacts/m2",action+".json"),JSON.stringify({sha,base:"dfbf9632c3abe82364bce0d0a09026c17623c91b",scope,actual_revit:false,passed:!process.exitCode,checks},null,2)+"\n");}
