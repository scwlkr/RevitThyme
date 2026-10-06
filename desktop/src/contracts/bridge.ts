import type {CaptureRequest,Snapshot,PreviewRequest,Preview,Proposal,Mode,ApplyRequest,OutcomeRequest,MutationResult} from "./generated";
export interface Bridge {
 capture(request:CaptureRequest):Promise<Snapshot>;
 preview(request:PreviewRequest):Promise<Preview>;
 propose(request:PreviewRequest):Promise<Proposal>;
 mode():Promise<Mode>;
 apply(request:ApplyRequest):Promise<MutationResult>;
 outcome(request:OutcomeRequest):Promise<MutationResult>;
 cancelApply(request:OutcomeRequest):Promise<MutationResult>;
 reconnect():Promise<void>;
}
declare global { interface Window { revitthyme?:Bridge } }
export function bridge():Bridge { if(!window.revitthyme)throw Error("Open the packaged RevitThyme desktop app. No desktop connection is available.");return window.revitthyme; }
