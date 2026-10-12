import {Pressable} from "react-native";
import type {StyleProp,ViewStyle} from "react-native";
import {Label} from "./layout";

export function Button({children,onPress,disabled=false,variant="default",style}:{
 children:string;onPress:()=>void;disabled?:boolean;variant?:"default"|"primary";style?:StyleProp<ViewStyle>;
}){
 return <Pressable accessibilityRole="button" accessibilityLabel={children} accessibilityState={{disabled}}
  disabled={disabled} onPress={onPress}
  className={"h-7 min-w-[76px] items-center justify-center rounded-[3px] border bg-surface px-3 "+(variant==="primary"?"border-thyme":"border-line")}
  style={({pressed})=>[{opacity:disabled?.45:pressed?.7:1},style]}>
  <Label>{children}</Label>
 </Pressable>;
}
