import type {ComponentProps,ReactNode} from "react";
import {useEffect,useRef} from "react";
import {View,Text,ScrollView} from "react-native";
import type {StyleProp,ViewStyle} from "react-native";

export function Label({className="text-ink",style,...props}:ComponentProps<typeof Text>){
 return <Text {...props} className={className} style={[{fontFamily:"Segoe UI",fontSize:13,lineHeight:18},style]}/>;
}
export function DialogLayout({toolbar,children,footer,revealKey}:{toolbar:ReactNode;children:ReactNode;footer:ReactNode;revealKey?:string}){
 const body=useRef<ScrollView>(null);
 useEffect(()=>{
  if(!revealKey)return;
  const frame=requestAnimationFrame(()=>body.current?.scrollToEnd({animated:false}));
  return()=>cancelAnimationFrame(frame);
 },[revealKey]);
 return <View className="flex-1 bg-background">
  <View className="border-b border-line p-2 gap-2">{toolbar}</View>
  <ScrollView ref={body} className="flex-1" contentContainerStyle={{padding:8,gap:8}}>{children}</ScrollView>
  <View className="flex-row flex-wrap items-center gap-2 border-t border-line p-2">{footer}</View>
 </View>;
}
export function GroupBox({title,children,style}:{title:string;children:ReactNode;style?:StyleProp<ViewStyle>}){
 return <View className="border border-line px-2 pb-2 pt-3 mt-2 gap-2" style={style}>
  <View className="absolute -top-2 left-2 bg-background px-1"><Label>{title}</Label></View>
  {children}
 </View>;
}
export function Notice({children,tone="warning"}:{children:ReactNode;tone?:"warning"|"info"}){
 return <View accessibilityRole="alert" accessibilityLiveRegion="polite" className={"bg-surface border-l-2 p-2 gap-1 "+(tone==="warning"?"border-timber":"border-thyme")}>{children}</View>;
}
