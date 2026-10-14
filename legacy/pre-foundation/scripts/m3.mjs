import {spawnSync} from "node:child_process";
const drivers={ui:"check-revit-ui.mjs",transport:"check-revit-transport.mjs",geometry:"check-revit-geometry.mjs"};
const driver=drivers[process.argv[2]];
if(!driver)throw Error("Usage: project m3 <ui|transport|geometry> --cdp <loopback URL> --source-sha <installed SHA> --case <name> [driver options]");
const result=spawnSync("node",["desktop/scripts/"+driver,...process.argv.slice(3)],{cwd:new URL("../",import.meta.url),stdio:"inherit",windowsHide:true});
process.exitCode=result.status??1;
