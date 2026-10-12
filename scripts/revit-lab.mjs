import {spawnSync} from "node:child_process";
import {mkdirSync,writeFileSync,copyFileSync,readFileSync} from "node:fs";
import {randomUUID,createHash} from "node:crypto";
import path from "node:path";

const root=path.resolve(import.meta.dirname,".."),action=process.argv[2]??"help",checks=[];
function run(program,args){
 const r=spawnSync(program,args,{cwd:root,encoding:"utf8",maxBuffer:32*1024*1024,timeout:240000,windowsHide:true});
 const output=(r.stdout??"")+(r.stderr??"");checks.push({command:[program,...args],passed:r.status===0,output});
 console.log(output.trim());if(r.status!==0)throw Error("Command failed: "+program);
}
function build(){
 run("dotnet",["build","native/LabAdapter/LabAdapter.csproj","-c","Release","--ignore-failed-sources"]);
 run("cargo",["build","--manifest-path","replacement/Cargo.toml","--offline","--locked","-p","revitthyme-app","--release"]);
}
try{
 if(action==="check"){
  run("dotnet",["build","native/LabAdapter/LabAdapter.csproj","-c","Release","--ignore-failed-sources"]);
  run("dotnet",["run","--project","native/LabChecks/LabChecks.csproj","-c","Release","--ignore-failed-sources"]);
  run("cargo",["fmt","--manifest-path","replacement/Cargo.toml","--all","--check"]);
  run("cargo",["clippy","--manifest-path","replacement/Cargo.toml","--offline","--locked","--all-targets","--","-D","warnings"]);
  run("cargo",["test","--manifest-path","replacement/Cargo.toml","--offline","--locked"]);
  run("cargo",["build","--manifest-path","replacement/Cargo.toml","--offline","--locked","-p","revitthyme-app"]);
  run("node",["scripts/check-revit-lab.mjs"]);
 }else if(action==="prepare"){
  if(process.argv.length!==3)throw Error("Prepare has no write-enabling flags. First live run is read-only.");
  build();const runId=randomUUID(),folder=path.join(root,"artifacts/revit-lab",runId),bundle=path.join(folder,"bundle");
  mkdirSync(bundle,{recursive:true});
  const files=[
   ["native/LabAdapter/bin/Release/net10.0-windows/RevitThyme.LabAdapter.dll","RevitThyme.LabAdapter.dll"],
   ["native/LabAdapter/bin/Release/net10.0-windows/Adapter.Core.dll","Adapter.Core.dll"],
   ["replacement/target/release/revitthyme-app.exe","revitthyme-test.exe"]
  ];
  const hashes={};for(const [source,target] of files){const dest=path.join(bundle,target);copyFileSync(path.join(root,source),dest);hashes[target]=createHash("sha256").update(readFileSync(dest)).digest("hex");}
  const config={run_id:runId,fixture_path:path.join(folder,"RevitThyme-Test.rvt"),binding_path:path.join(folder,"binding.json"),revit_version:"27.3.0.28",create_fixture:true,allow_writes:false};
  writeFileSync(path.join(folder,"config.json"),JSON.stringify(config,null,2)+"\n");
  writeFileSync(path.join(folder,"bundle-hashes.json"),JSON.stringify(hashes,null,2)+"\n");
  const escape=s=>s.replaceAll("&","&amp;").replaceAll("<","&lt;").replaceAll('"',"&quot;");
  const manifest=`<?xml version="1.0" encoding="utf-8"?>\n<RevitAddIns><AddIn Type="Application"><Name>RevitThyme Isolated Lab</Name><Assembly>${escape(path.join(bundle,"RevitThyme.LabAdapter.dll"))}</Assembly><AddInId>45F4DB43-1E27-4C0F-A629-0DCC5B43FB18</AddInId><FullClassName>RevitThyme.Lab.Host</FullClassName><VendorId>WLKR</VendorId><VendorDescription>RevitThyme native test session</VendorDescription></AddIn></RevitAddIns>\n`;
  writeFileSync(path.join(folder,"RevitThyme.Lab.addin"),manifest);
  const quote=s=>JSON.stringify(s);
  writeFileSync(path.join(folder,"mcp.toml"),`[mcp_servers.revitthyme_test]\ncommand = ${quote(path.join(bundle,"revitthyme-test.exe"))}\nargs = ["--lab-mcp", ${quote(config.binding_path)}]\nstartup_timeout_sec = 30\ntool_timeout_sec = 60\n`);
  const sha=spawnSync("git",["rev-parse","HEAD"],{cwd:root,encoding:"utf8"}).stdout.trim();
  writeFileSync(path.join(folder,"plan.json"),JSON.stringify({sha,source_dirty:spawnSync("git",["status","--porcelain"],{cwd:root,encoding:"utf8"}).stdout.trim()!=="",actual_revit:false,folder,config,hashes,registration:path.join(process.env.USERPROFILE,"AppData/Roaming/Autodesk/Revit/Addins/2027/RevitThyme.Lab.addin"),mcp_config:path.join(process.env.USERPROFILE,".codex/config.toml")},null,2)+"\n");
  console.log("Prepared only; no installation, Revit launch or model creation: "+folder);
 }else if(action==="verify"){
  if(process.argv.length!==4)throw Error("Verify requires the exact prepared run folder.");
  run("node",["scripts/verify-revit-lab.mjs",process.argv[3]]);
 }else if(["install","start","status"].includes(action)){
  const folder=process.argv[3];if(!folder)throw Error("Explicit prepared run folder required.");
  const approved=process.argv[4]==="--approved";
  if(process.argv.length!==(approved?5:4))throw Error("Unexpected arguments.");
  if(action!=="status"&&!approved)throw Error("Installation and fixture startup require exact-step user approval; then pass --approved.");
  run("pwsh",["-NoProfile","-NonInteractive","-File","scripts/revit-lab.ps1","-Action",action,"-RunFolder",folder,...(approved?["-Approved"]:[])]);
 }else if(action==="help")console.log("project revit-lab check | prepare | status <run-folder> | verify <run-folder> | install <run-folder> --approved | start <run-folder> --approved\nPrepare builds local artifacts only. Install/start need prior exact-step approval. First live run is read-only.");
 else throw Error("Unknown Revit lab command.");
}catch(e){console.error(e.message);process.exitCode=1;}
finally{
 if(action==="check"){const folder=path.join(root,"artifacts/revit-lab");mkdirSync(folder,{recursive:true});
 writeFileSync(path.join(folder,"check.json"),JSON.stringify({sha:spawnSync("git",["rev-parse","HEAD"],{cwd:root,encoding:"utf8"}).stdout.trim(),actual_revit:false,scope:"source_offline",passed:!process.exitCode,checks},null,2)+"\n");}
}
