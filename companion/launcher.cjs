'use strict';
// Calls the installed application's existing launcher unchanged. No source/ASAR edits.
const fs=require('node:fs'),path=require('node:path');
const input=JSON.parse(fs.readFileSync(process.env.RLO_LAUNCH_REQUEST,'utf8').replace(/^\uFEFF/,''));
function finish(value){fs.writeFileSync(input.result,JSON.stringify(value));}
(async()=>{
 try{
  if(Date.now()>input.deadline)throw Error('Время запуска истекло; повторите попытку.');
  const resources=path.join(path.dirname(process.execPath),'resources');
  const location=path.join(resources,'app.asar','electron','runelite-launcher.cjs');
  if(!fs.existsSync(location))throw Error('В установленном приложении не найден совместимый запуск RuneLite.');
  if(path.resolve(process.resourcesPath||'')!==path.resolve(resources))throw Error('Несовместимый каталог ресурсов установленного приложения.');
  const launcher=require(location);
  if(typeof launcher.createLauncher!=='function')throw Error('Версия запуска OSRS Path несовместима.');
  if(input.inspect){finish({ok:true,compatible:true});return;}
  if(!/^[a-fA-F0-9]{64}$/.test(input.token)||!fs.existsSync(input.jar))throw Error('Нет модуля телеметрии.');
  process.env.JAVA_TOOL_OPTIONS=(process.env.JAVA_TOOL_OPTIONS||'')+' "-javaagent:'+input.jar+'" -Drunelite.optimizer.token='+input.token;
  const run=launcher.createLauncher({logFile:input.log});
  const result=await run.launch();
  finish({ok:result.ok===true,state:String(result.state||'unknown')});
 }catch(error){finish({ok:false,message:String(error.message||error)});}
})().finally(()=>setTimeout(()=>process.exit(0),100));
