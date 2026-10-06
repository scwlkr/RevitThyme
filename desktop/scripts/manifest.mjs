import {readdirSync,readFileSync,writeFileSync} from "node:fs";
import {createHash} from "node:crypto";
import {execFileSync} from "node:child_process";
import path from "node:path";
const desktop=path.resolve(import.meta.dirname,".."),root=path.resolve(desktop,"..");
function hash(file){return createHash("sha256").update(readFileSync(file)).digest("hex");}
function files(folder,prefix=""){return readdirSync(folder,{withFileTypes:true}).flatMap(d=>d.isDirectory()?files(path.join(folder,d.name),prefix+d.name+"/"):[prefix+d.name]);}
const assets=Object.fromEntries(files(path.join(desktop,"dist")).map(file=>[file,hash(path.join(desktop,"dist",file))]));
const manifest={schema_version:1,protocol:1,version:"0.1.0",source_sha:execFileSync("git",["rev-parse","HEAD"],{cwd:root,encoding:"utf8"}).trim(),mode:"synthetic",qualified_revit_builds:[],native_adapter_included:false,sidecar_sha256:hash(path.join(root,"replacement/target/release/revitthyme-app.exe")),assets};
writeFileSync(path.join(desktop,"package-manifest.json"),JSON.stringify(manifest,null,2)+"\n");
console.log("Manifest: "+Object.keys(assets).length+" Expo assets. Synthetic only; no native adapter.");

