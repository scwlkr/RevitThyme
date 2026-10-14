import {contextBridge,ipcRenderer} from "electron";
import {CaptureRequestSchema,SnapshotSchema,PreviewRequestSchema,PreviewSchema,ProposalSchema,ModeSchema,ApplyRequestSchema,OutcomeRequestSchema,MutationResultSchema} from "../src/contracts/generated";
contextBridge.exposeInMainWorld("revitthyme",Object.freeze({
 capture:async(request:unknown)=>SnapshotSchema.parse(await ipcRenderer.invoke("range:capture",CaptureRequestSchema.parse(request))),
 preview:async(request:unknown)=>PreviewSchema.parse(await ipcRenderer.invoke("range:preview",PreviewRequestSchema.parse(request))),
 propose:async(request:unknown)=>ProposalSchema.parse(await ipcRenderer.invoke("range:propose",PreviewRequestSchema.parse(request))),
 mode:async()=>ModeSchema.parse(await ipcRenderer.invoke("range:mode")),
 apply:async(request:unknown)=>MutationResultSchema.parse(await ipcRenderer.invoke("range:apply",ApplyRequestSchema.parse(request))),
 outcome:async(request:unknown)=>MutationResultSchema.parse(await ipcRenderer.invoke("range:outcome",OutcomeRequestSchema.parse(request))),
 cancelApply:async(request:unknown)=>MutationResultSchema.parse(await ipcRenderer.invoke("range:cancel",OutcomeRequestSchema.parse(request))),
 reconnect:async()=>ipcRenderer.invoke("range:reconnect"),
}));
