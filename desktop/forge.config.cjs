const path=require("node:path");
module.exports={
  packagerConfig:{
    name:"RevitThyme",asar:true,executableName:"RevitThyme",
    afterCopy:[({buildPath})=>{
      const fs=require("node:fs");const file=path.join(buildPath,"package.json");
      const data=JSON.parse(fs.readFileSync(file,"utf8"));data.main="build/main.cjs";
      fs.writeFileSync(file,JSON.stringify(data));
    }],
    extraResource:[path.resolve("../replacement/target/release/revitthyme-app.exe"),path.resolve("dist"),path.resolve("package-manifest.json")],
    ignore:(file)=>file!=="" && !/^\/(build(?:\/|$)|package\.json$)/.test(file),
  },
  makers:[{name:"@electron-forge/maker-zip",platforms:["win32"]}],
};
