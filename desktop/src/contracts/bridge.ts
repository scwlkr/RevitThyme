import type {CaptureRequest,Snapshot,PreviewRequest,Preview,Proposal} from "./generated";
export interface Bridge {
 capture(request:CaptureRequest):Promise<Snapshot>;
 preview(request:PreviewRequest):Promise<Preview>;
 propose(request:PreviewRequest):Promise<Proposal>;
 reconnect():Promise<void>;
}
declare global { interface Window { revitthyme?:Bridge } }
export function bridge():Bridge { if(!window.revitthyme)throw Error("Open the packaged RevitThyme desktop app. No desktop connection is available.");return window.revitthyme; }
