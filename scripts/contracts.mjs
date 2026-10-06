import {spawnSync} from "node:child_process";
import {readFileSync,writeFileSync,mkdirSync} from "node:fs";
import path from "node:path";
const root=path.resolve(import.meta.dirname,"..");
const exe=path.join(root,"replacement/target/debug/revitthyme-app"+(process.platform==="win32"?".exe":""));
const r=spawnSync(exe,["--openapi"],{encoding:"utf8"});
if(r.status!==0) throw Error(r.stderr);
const document=JSON.parse(r.stdout);
const schemas=document.components.schemas;
const resolved=new Map();
function schema(s) {
  if(s.$ref) return s.$ref.split("/").at(-1)+"Schema";
  if(s.enum) return "z.enum("+JSON.stringify(s.enum)+")";
  if(s.type==="object") {
    const props=Object.entries(s.properties??{}).map(([n,v])=>JSON.stringify(n)+":"+schema(v)+((s.required??[]).includes(n)?"":".optional()"));
    return "z.object({"+props.join(",")+"}).strict()";
  }
  if(s.type==="array") {
    let result="z.array("+schema(s.items)+")";
    if(s.minItems!==undefined)result+=".min("+s.minItems+")";
    if(s.maxItems!==undefined)result+=".max("+s.maxItems+")";
    return result;
  }
  if(s.type==="string")return "z.string()";
  if(s.type==="boolean")return "z.boolean()";
  if(s.type==="number" || s.type==="integer"){
    let result="z.number().finite()"+(s.type==="integer"?".int()":"");
    if(s.minimum!==undefined)result+=".min("+s.minimum+")";
    if(s.maximum!==undefined)result+=".max("+s.maximum+")";
    return result;
  }
  throw Error("Unsupported OpenAPI shape: "+JSON.stringify(s));
}
function emit(name) {
  if(resolved.has(name))return;
  function deps(s) { if(s.$ref)emit(s.$ref.split("/").at(-1));for(const [key,v]of Object.entries(s)){if(key!=="$ref" && typeof v==="object" && v) { if(Array.isArray(v))v.forEach(x=>x && typeof x==="object"&&deps(x));else deps(v); } } }
  deps(schemas[name]);resolved.set(name,"export const "+name+"Schema="+schema(schemas[name])+";\nexport type "+name+"=z.infer<typeof "+name+"Schema>;");
}
Object.keys(schemas).sort().forEach(emit);
const generated="// Generated from Rust Utoipa OpenAPI. Run project m1 contracts.\nimport {z} from 'zod';\n"+[...resolved.values()].join("\n")+"\n";
for(const [relative,content]of [["contracts/openapi.json",JSON.stringify(document,null,2)+"\n"],["desktop/src/contracts/generated.ts",generated]]) {
 const destination=path.join(root,relative);
 if(process.argv.includes("--check")) { if(readFileSync(destination,"utf8")!==content) throw Error("Contract drift: "+relative); }
 else { mkdirSync(path.dirname(destination),{recursive:true});writeFileSync(destination,content); }
}
console.log("Rust/OpenAPI/Zod generation is aligned.");

