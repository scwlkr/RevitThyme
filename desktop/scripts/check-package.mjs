import {readdirSync,readFileSync,existsSync} from "node:fs";
import {createHash} from "node:crypto";
import assert from "node:assert/strict";
import path from "node:path";
import {listPackage,extractFile} from "@electron/asar";
const desktop=path.resolve(import.meta.dirname,".."),folder=path.join(desktop,"out/RevitThyme-win32-x64/resources");
function hash(file){return createHash("sha256").update(readFileSync(file)).digest("hex");}
assert.ok(existsSync(path.join(folder,"app.asar")));
assert.deepEqual(listPackage(path.join(folder,"app.asar")).map(file=>file.replaceAll("\\","/")).sort(),["/build","/build/main.cjs","/build/preload.cjs","/package.json"]);
assert.equal(JSON.parse(extractFile(path.join(folder,"app.asar"),"package.json").toString()).main,"build/main.cjs");
const manifest=JSON.parse(readFileSync(path.join(folder,"package-manifest.json")));
assert.equal(manifest.protocol,2);
assert.deepEqual(manifest.components,{rust_app:"0.1.1",desktop:"0.1.1",electron:"44.5.1",expo:"57.0.27",native_adapter:"0.2.1"});
assert.equal(hash(path.join(folder,"revitthyme-app.exe")),manifest.sidecar_sha256);
for(const [file,expected]of Object.entries(manifest.assets))assert.equal(hash(path.join(folder,"dist",file)),expected);
assert.equal(manifest.native_adapter_included,true);
assert.equal(manifest.build_reference_revit,"27.2.0.39");
for(const [file,expected]of Object.entries(manifest.native))assert.equal(hash(path.join(folder,"native",file)),expected);
assert.deepEqual(readdirSync(path.join(folder,"native")).sort(),["Adapter.Core.dll","RevitThyme.RevitAdapter.deps.json","RevitThyme.RevitAdapter.dll","RevitThyme.addin.template"].sort());
assert.deepEqual(manifest.qualified_revit_builds,[]);
function walk(folder){return readdirSync(folder,{withFileTypes:true}).flatMap(d=>d.isDirectory()?walk(path.join(folder,d.name)):[path.join(folder,d.name)]);}
const names=walk(folder);
assert.ok(names.every(file=>!/(RevitAPI|TimberFold|\.rvt$|\.rfa$|\.env|local\.json)/i.test(file)));
assert.deepEqual(readdirSync(folder).sort(),["app.asar","dist","native","package-manifest.json","revitthyme-app.exe"]);
console.log("Packaged allowlist and every sidecar/asset/adapter checksum pass; no Autodesk assemblies/models/TimberFold; no automatic installation.");
