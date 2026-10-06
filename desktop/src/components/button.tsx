import {Pressable,Text} from "react-native";
import type {StyleProp,ViewStyle} from "react-native";
export function Button({children,onPress,disabled=false,primary=false,style}:{children:string;onPress:()=>void;disabled?:boolean;primary?:boolean;style?:StyleProp<ViewStyle>}) {
 return <Pressable accessibilityRole="button" accessibilityLabel={children} accessibilityState={{disabled}} disabled={disabled} onPress={onPress}
 className={"rounded-md px-4 py-3 border "+(primary?"bg-thyme border-thyme":"bg-surface border-line")}
 style={({pressed})=>[{opacity:disabled?.4:pressed?.7:1},style]}>
 <Text className={primary?"text-on-thyme font-semibold":"text-ink font-semibold"}>{children}</Text></Pressable>;
}
