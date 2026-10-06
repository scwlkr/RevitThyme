import {spawnSync} from "node:child_process";
if(process.argv[2]!=="ui")throw Error("Usage: project m3 ui --cdp <loopback URL> --source-sha <installed SHA> --document-name <approved title> --view-name <approved view> --case <name> [--apply-cut-feet <value|current> --approved-writes]");
const result=spawnSync("node",["desktop/scripts/check-revit-ui.mjs",...process.argv.slice(3)],{cwd:new URL("../",import.meta.url),stdio:"inherit",windowsHide:true});
process.exitCode=result.status??1;
