import {spawnSync} from "node:child_process";
import {mkdirSync,writeFileSync,readFileSync} from "node:fs";
import path from "node:path";
const root=path.resolve(import.meta.dirname,"..");
const desktop=path.join(root,"desktop");
const manifest="replacement/Cargo.toml";
const checks=[];
function run(program,args,cwd=root) {
  const actual=process.platform==="win32" && program==="pnpm" ? ["cmd.exe",["/d","/s","/c","pnpm",...args]]:[program,args];
  const r=spawnSync(actual[0],actual[1],{cwd,encoding:"utf8",env:{...process.env,CI:"1",DOTNET_SKIP_FIRST_TIME_EXPERIENCE:"1",DOTNET_CLI_TELEMETRY_OPTOUT:"1",EXPO_OFFLINE:"1"},maxBuffer:32*1024*1024,timeout:600000});
  checks.push({command:[program,...args],exit:r.status,passed:r.status===0,output:(r.stdout??"")+(r.stderr??"")});
  console.log(checks.at(-1).output);
  if(r.status!==0) throw Error("Failed: "+program+" "+args.join(" "));
  return r.stdout;
}
const action=process.argv[2]??"check";
try {
  if(action==="format") run("cargo",["fmt","--manifest-path",manifest,"--all"]);
  else if(action==="contracts") {
    run("cargo",["build","--manifest-path",manifest,"--locked","--offline"]);
    run("node",["scripts/contracts.mjs"]);
  } else if(action==="check") {
    run("cargo",["fmt","--manifest-path",manifest,"--all","--check"]);
    run("cargo",["clippy","--manifest-path",manifest,"--all-targets","--locked","--offline","--","-D","warnings"]);
    run("cargo",["test","--manifest-path",manifest,"--locked","--offline"]);
    run("cargo",["build","--manifest-path",manifest,"--locked","--offline"]);
    run("node",["scripts/contracts.mjs","--check"]);
    run("pnpm",["typecheck"],desktop);
    run("dotnet",["run","--project","native/ContractChecks/ContractChecks.csproj"]);
    run("node",["desktop/scripts/check-api.mjs"]);
    run("pnpm",["exec","expo","install","--check"],desktop);
  } else if(action==="package") {
    if(process.platform!=="win32") throw Error("Only Windows x64 is qualified.");
    run("dotnet",["build","native/RevitAdapter/RevitAdapter.csproj","-c","Release","--ignore-failed-sources"]);
    run("node",["desktop/scripts/stage-native.mjs"]);
    run("cargo",["build","--manifest-path",manifest,"--release","--locked","--offline"]);
    run("pnpm",["export"],desktop);run("pnpm",["bundle"],desktop);
    run("node",["desktop/scripts/manifest.mjs"]);
    run("pnpm",["package"],desktop);
    run("node",["desktop/scripts/check-package.mjs"]);
  } else if(action==="ui") { run("node",["desktop/scripts/check-ui.mjs"]);run("node",["desktop/scripts/check-recovery.mjs"]); }
  else throw Error("Usage: project m1 [check|format|contracts|package|ui]");
} catch(e) { console.error(e.message);process.exitCode=1; }
finally {
  mkdirSync(path.join(root,"artifacts/m1"),{recursive:true});
  const sha=spawnSync("git",["rev-parse","HEAD"],{cwd:root,encoding:"utf8"}).stdout?.trim();
  const clean=spawnSync("git",["status","--porcelain"],{cwd:root,encoding:"utf8"}).stdout?.trim()==="";
  writeFileSync(path.join(root,"artifacts/m1",action+".json"),JSON.stringify({sha,base:"e128a67917e393259b079838c8ac28b88a36f3b2",clean,scope:["ui","package"].includes(action)?"packaged_application":"source_offline",passed:!process.exitCode,checks,actual_revit:false},null,2)+"\n");
}
