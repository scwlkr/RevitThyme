import {useState,useEffect,useRef,useCallback} from "react";
import {bridge} from "../../contracts/bridge";
import type {Snapshot,PreviewRequest,Preview,Proposal,Unit,Axis,ViewKind,Edits} from "../../contracts/generated";
export const keys=["top","cut","bottom","depth"] as const;
export type PlaneKey=typeof keys[number];
function originals(s:Snapshot):Edits {
 return Object.fromEntries(keys.map(k=>[k,{value:s.original[k].offset_feet,unit:"ft",unlimited:s.original[k].unlimited}])) as Edits;
}
export function useEditor() {
 const [snapshot,setSnapshot]=useState<Snapshot>();
 const [request,setRequest]=useState<PreviewRequest>();
 const [preview,setPreview]=useState<Preview>();
 const [proposal,setProposal]=useState<Proposal>();
 const [error,setError]=useState("");
 const [busy,setBusy]=useState(false);
 const [unit,setUnit]=useState<Unit>("mm");
 const [kind,setKind]=useState<ViewKind>("floor");
 const [partial,setPartial]=useState(false);
 const [cancelled,setCancelled]=useState(false);
 const [invalidInputs,setInvalidInputs]=useState<Set<PlaneKey>>(new Set());
 const inputInvalid=invalidInputs.size>0;
 function setInputInvalid(key:PlaneKey,invalid:boolean){setInvalidInputs(previous=>{if(previous.has(key)===invalid)return previous;const next=new Set(previous);if(invalid)next.add(key);else next.delete(key);return next;});if(invalid){revision.current++;setProposal(undefined);}}
 const [inputReset,setInputReset]=useState(0);
 const revision=useRef(0);
 const epoch=useRef(0);
 const update=useCallback((patch:Partial<PreviewRequest>)=>{
   revision.current++;setProposal(undefined);setError("");setCancelled(false);
   setRequest(r=>r?{...r,...patch,input_revision:revision.current}:r);
 },[]);
 const reset=useCallback(()=>{
   if(snapshot) {setInvalidInputs(new Set());setInputReset(n=>n+1);update({edits:originals(snapshot)});}
 },[snapshot,update]);
 const cancel=useCallback(()=>{
   reset();setCancelled(true);setProposal(undefined);
 },[reset]);
 async function capture(reconnect=false) {
   const token=++epoch.current;revision.current++;setBusy(true);setError("");setProposal(undefined);setPreview(undefined);setRequest(undefined);setSnapshot(undefined);setInvalidInputs(new Set());
   try {
     if(reconnect)await bridge().reconnect();
     const s=await bridge().capture({protocol:1,view_kind:kind,partial_fixture:partial});
     if(token!==epoch.current)return;
     setSnapshot(s);setCancelled(false);
     setRequest({protocol:1,snapshot_id:s.snapshot_id,target:s.target,input_revision:revision.current,axis:"y",fraction:0.015625,unit,edits:originals(s)});
   }catch(e){if(token===epoch.current)setError(String(e));}
   finally {if(token===epoch.current)setBusy(false);}
 }
 useEffect(()=>{void capture();return()=>{epoch.current++;};},[]);
 useEffect(()=>{
   if(!request)return;
   let abandoned=false;const token=epoch.current;
   const timer=setTimeout(async()=>{
     try{
       const p=await bridge().preview(request);
       if(!abandoned && token===epoch.current && p.input_revision===revision.current && p.snapshot_id===request.snapshot_id){setPreview(p);setError("");}
     }catch(e){if(!abandoned && token===epoch.current && request.input_revision===revision.current){setError(String(e));}}
   },45);
   return()=>{abandoned=true;clearTimeout(timer);};
 },[request]);
 useEffect(()=>{
   const escape=(e:KeyboardEvent)=>{if(e.key==="Escape"){e.preventDefault();cancel();}};
   window.addEventListener("keydown",escape);return()=>window.removeEventListener("keydown",escape);
 },[cancel]);
 function edit(key:PlaneKey,value:number,unlimited?:boolean,valueUnit:Unit=unit) {
   if(!request)return;
   update({edits:{...request.edits,[key]:{...request.edits[key],value,unit:valueUnit,unlimited:unlimited??request.edits[key].unlimited}}});
 }
 async function review() {
   if(!request || inputInvalid)return;
   const token=epoch.current,rev=request.input_revision;setBusy(true);
   try {const p=await bridge().propose(request);if(token===epoch.current && rev===revision.current)setProposal(p);}
   catch(e){if(token===epoch.current && rev===revision.current)setError(String(e));}
   finally{if(token===epoch.current)setBusy(false);}
 }
 function changeUnit(next:Unit){setInvalidInputs(new Set());setInputReset(n=>n+1);setUnit(next);update({unit:next});}
 return {snapshot,request,preview,proposal,error,busy,unit,kind,partial,cancelled,inputInvalid,inputReset,
   setKind,setPartial,setInputInvalid,capture,reset,cancel,edit,review,changeUnit,update,
   ready:!!preview && preview.input_revision===request?.input_revision && !error && !inputInvalid};
}
