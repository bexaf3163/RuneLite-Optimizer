'use strict';
// Own process-local adapter. Installed OSRS Path files are only read, never edited.
const path=require('node:path');
const originalMain='com.osrspath.bridge.OsrsPathLauncher';
function adapt(command,args,jar,token){
 if(!Array.isArray(args)||!args.includes(originalMain))return null;
 if(!/^javaw?(?:\.exe)?$/i.test(path.basename(command)))throw Error('Unsupported Java launcher');
 if(!/^[a-fA-F0-9]{64}$/.test(token))throw Error('Invalid telemetry token');
 const copy=args.slice(),cp=copy.indexOf('-cp'),main=copy.indexOf(originalMain);
 if(cp<0||cp+1>=main||main!==copy.lastIndexOf(originalMain))throw Error('Unsupported RuneLite arguments');
 copy[cp+1]=jar+path.delimiter+copy[cp+1];
 copy[main]='com.runeliteoptimizer.OptimizerLauncher';
 copy.splice(cp,0,'-Drunelite.optimizer.token='+token);
 return copy;
}
module.exports={adapt};
