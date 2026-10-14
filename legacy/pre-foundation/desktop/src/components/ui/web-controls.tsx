import type {ComponentProps,ReactNode} from "react";

// Desktop form adapters retain browser keyboard, focus and form semantics.
export function Input({className="",...props}:ComponentProps<"input">){
 return <input {...props} className={"ui-control "+className}/>;
}
export function Select({className="",...props}:ComponentProps<"select">){
 return <select {...props} className={"ui-control "+className}/>;
}
export function Checkbox({label,...props}:Omit<ComponentProps<"input">,"type"> & {label?:string}){
 const input=<input {...props} type="checkbox" className="ui-checkbox"/>;
 return label?<label className="ui-check-label">{input}<span>{label}</span></label>:input;
}
export function Slider(props:Omit<ComponentProps<"input">,"type">){
 return <input {...props} type="range" className="ui-slider"/>;
}
export function Details({title,children}:{title:string;children:ReactNode}){
 return <details className="ui-details"><summary>{title}</summary><div>{children}</div></details>;
}
