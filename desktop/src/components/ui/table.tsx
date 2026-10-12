import type {ComponentProps} from "react";

export function Table({className="",...props}:ComponentProps<"table">){
 return <table {...props} className={"ui-table "+className}/>;
}
export function TableRow(props:ComponentProps<"tr">){return <tr {...props}/>;}
export function TableHead(props:ComponentProps<"th">){return <th {...props} scope="col"/>;}
export function TableCell(props:ComponentProps<"td">){return <td {...props}/>;}
