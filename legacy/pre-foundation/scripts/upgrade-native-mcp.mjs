// Upgrade only the external read-only MCP client; preserve the exact running Revit host and document.
import {spawnSync} from "node:child_process";
import {readFileSync,writeFileSync,copyFileSync,mkdirSync,existsSync} from "node:fs";
import {createHash} from "node:crypto";
import path from "node:path";
import assert from "node:assert/strict";

const root=path.resolve(import.meta.dirname,".."),folder=path.resolve(process.argv[2]??"");
assert.equal(process.argv.length,3);assert.equal(path.dirname(folder),path.join(root,"artifacts/revit-lab"));
const plan=JSON.parse(readFileSync(path.join(folder,"plan.json"))),binding=JSON.parse(readFileSync(path.join(folder,"binding.json")));
assert.equal(plan.folder,folder);assert.equal(binding.fixture_path,plan.config.fixture_path);assert.equal(binding.allow_writes,false);
assert.ok(Number.isSafeInteger(binding.process_id)&&binding.process_id>0);
const manifest=readFileSync(path.join(folder,"RevitThyme.Lab.addin"));
assert.ok(readFileSync(plan.registration).equals(manifest),"Installed lab registration differs from this run");
const run=(exe,args)=>{
 const r=spawnSync(exe,args,{cwd:root,encoding:"utf8",windowsHide:true,timeout:240000,maxBuffer:8*1024*1024});
 if(r.status!==0)throw Error((r.stdout??"")+(r.stderr??"")+"Failed: "+exe);return r.stdout.trim();
};
const sha=run("git",["rev-parse","HEAD"]);assert.match(sha,/^[a-f0-9]{40}$/);
run("git",["diff","--quiet","HEAD"]);
const changes=run("git",["diff","--name-only",plan.sha,"HEAD","--","native/Adapter.Core","native/Lab.Core","native/LabAdapter","native/RevitAdapter","replacement/app/src/native.rs","replacement/app/src/contract.rs","replacement/app/src/inspection_contract.rs"]);
assert.equal(changes,"","Native adapter/wire changes require a fresh host bundle");
const active=JSON.parse(run("pwsh",["-NoProfile","-Command",`$p=Get-Process -Id ${Number(binding.process_id)} -ErrorAction Stop; @{ticks=$p.StartTime.ToUniversalTime().Ticks.ToString()} | ConvertTo-Json -Compress`]));
assert.equal(active.ticks,binding.process_start_ticks);
const hash=file=>createHash("sha256").update(readFileSync(file)).digest("hex");
for(const [file,expected] of Object.entries(plan.hashes))assert.equal(hash(path.join(folder,"bundle",file)),expected);
run("cargo",["build","--manifest-path","replacement/Cargo.toml","--offline","--locked","-p","revitthyme-app","--release"]);
const candidate=path.join(folder,"mcp-upgrades",sha),binary=path.join(candidate,"bundle/revitthyme-test.exe");
mkdirSync(path.dirname(binary),{recursive:true});
const source=path.join(root,"replacement/target/release/revitthyme-app.exe"),binaryHash=hash(source);
if(existsSync(binary))assert.equal(hash(binary),binaryHash);else copyFileSync(source,binary);
const alias=path.join(candidate,"binding.json"),bindingBytes=readFileSync(path.join(folder,"binding.json"));
if(existsSync(alias))assert.ok(readFileSync(alias).equals(bindingBytes));else writeFileSync(alias,bindingBytes);
assert.equal(hash(binary),binaryHash);
const receipt=path.join(folder,"mcp-upgrade.json");
assert.ok(!existsSync(receipt),"Inspect an existing upgrade before another rotation");
const configuration=JSON.parse(run("python",["-X","utf8","scripts/native-mcp-config.py","upgrade",folder,candidate]));
assert.ok(readFileSync(plan.registration).equals(manifest),"Native registration changed during MCP upgrade");
writeFileSync(receipt,JSON.stringify({source_sha:sha,native_sha:plan.sha,binary,binary_sha256:binaryHash,binding_path:alias,binding_sha256:hash(alias),
 process_id:binding.process_id,process_start_ticks:binding.process_start_ticks,session_id:binding.session_id,configuration,
 native_registration_changed:false,revit_restarted:false},null,2)+"\n");
console.log("External MCP upgraded; native session preserved: "+receipt);
