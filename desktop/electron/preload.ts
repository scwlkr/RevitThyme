import {contextBridge,ipcRenderer} from "electron";
import {CaptureRequestSchema,SnapshotSchema,PreviewRequestSchema,PreviewSchema,ProposalSchema} from "../src/contracts/generated";
contextBridge.exposeInMainWorld("revitthyme",Object.freeze({
 capture:async(request:unknown)=>SnapshotSchema.parse(await ipcRenderer.invoke("range:capture",CaptureRequestSchema.parse(request))),
 preview:async(request:unknown)=>PreviewSchema.parse(await ipcRenderer.invoke("range:preview",PreviewRequestSchema.parse(request))),
 propose:async(request:unknown)=>ProposalSchema.parse(await ipcRenderer.invoke("range:propose",PreviewRequestSchema.parse(request))),
 reconnect:async()=>ipcRenderer.invoke("range:reconnect"),
}));
