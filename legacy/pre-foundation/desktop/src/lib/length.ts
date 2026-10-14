import type {Unit} from "../contracts/generated";

const fractions=["","1/8","1/4","3/8","1/2","5/8","3/4","7/8"];
const decimal=/^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$/;
export const eighthInchFeet=1/96;
export function roundEighth(feet:number){return Math.sign(feet)*Math.round(Math.abs(feet)*96)/96;}
export function imperial(feet:number){
 const eighths=Math.round(Math.abs(feet)*96),wholeFeet=Math.floor(eighths/96);
 const inches=Math.floor(eighths%96/8),fraction=fractions[eighths%8];
 return (feet<0&&eighths?"-":"")+wholeFeet+"' "+inches+(fraction?" "+fraction:"")+'"';
}
export function offsetText(value:number,unit:Unit){
 return unit==="ft"?imperial(value):String(Number(value.toFixed(unit==="mm"?1:3)));
}
export function lengthText(feet:number,unit:Unit="ft"){
 return unit==="ft"?imperial(feet):offsetText(feet*(unit==="mm"?304.8:0.3048),unit)+" "+unit;
}
function inches(text:string):number|undefined{
 if(decimal.test(text))return Number(text);
 const match=/^(?:(\d+)\s+)?(\d+)\/(\d+)$/.exec(text);
 if(!match||Number(match[3])===0)return;
 return Number(match[1]??0)+Number(match[2])/Number(match[3]);
}
export function parseOffset(text:string,unit:Unit):number|undefined{
 let value:number|undefined;
 text=text.trim().replace(/[′’]/g,"'").replace(/[″“”]/g,'"');
 if(decimal.test(text))value=Number(text);
 else if(unit==="ft"){
  const sign=text.startsWith("-")?-1:1;text=text.replace(/^[+-]/,"").trim();
  const feet=/^(\d+(?:\.\d+)?)\s*'\s*(?:-\s*)?(.*?)$/.exec(text);
  const remainder=(feet?feet[2]:text).replace(/"$/,"").trim();
  const amount=remainder?inches(remainder):feet?0:undefined;
  if(amount!==undefined&&amount>=0)value=sign*((feet?Number(feet[1]):0)+amount/12);
 }
 if(value===undefined||!Number.isFinite(value))return;
 return unit==="ft"?roundEighth(value):value;
}
