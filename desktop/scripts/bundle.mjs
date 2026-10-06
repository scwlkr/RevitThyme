import {build} from "esbuild";
await build({entryPoints:["electron/main.ts","electron/preload.ts"],bundle:true,outdir:"build",outExtension:{".js":".cjs"},platform:"node",target:"node22",external:["electron"],format:"cjs"});

