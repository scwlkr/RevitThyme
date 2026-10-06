import {readdirSync,readFileSync,writeFileSync} from "node:fs";
import {createHash} from "node:crypto";
import {execFileSync} from "node:child_process";
import path from "node:path";
const desktop=path.resolve(import.meta.dirname,".."),root=path.resolve(desktop,"..");
function hash(file){return createHash("sha256").update(readFileSync(file)).digest("hex");}
function files(folder,prefix=""){return readdirSync(folder,{withFileTypes:true}).flatMap(d=>d.isDirectory()?files(path.join(folder,d.name),prefix+d.name+"/"):[prefix+d.name]);}
const assets=Object.fromEntries(files(path.join(desktop,"dist")).map(file=>[file,hash(path.join(desktop,"dist",file))]));
const packageInfo=JSON.parse(readFileSync(path.join(desktop,"package.json"),"utf8"));
const native=Object.fromEntries(files(path.join(desktop,"native")).map(file=>[file,hash(path.join(desktop,"native",file))]));
const manifest={schema_version:1,protocol: 2,version:packageInfo.version,source_sha:execFileSync("git",["rev-parse","HEAD"],{cwd:root,encoding:"utf8"}).trim(),components:{rust_app:"0.1.1",desktop:packageInfo.version,electron:packageInfo.devDependencies.electron,expo:packageInfo.dependencies.expo,native_adapter:"0.2.1"},mode:"synthetic_or_ribbon_native",build_reference_revit:"27.2.0.39",qualified_revit_builds:[],native_adapter_included:true,native,sidecar_sha256:hash(path.join(root,"replacement/target/release/revitthyme-app.exe")),assets};
writeFileSync(path.join(desktop,"package-manifest.json"),JSON.stringify(manifest,null,2)+"\n");
console.log("Manifest: "+Object.keys(assets).length+" Expo assets and owned native adapter. Actual Revit unqualified; no installation.");
