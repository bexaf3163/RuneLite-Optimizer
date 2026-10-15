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
  if(input.inspect){const launcher=require(location);if(typeof launcher.createLauncher!=='function')throw Error('Версия запуска OSRS Path несовместима.');finish({ok:true,compatible:true});return;}
  if(!/^[a-fA-F0-9]{64}$/.test(input.token)||!fs.existsSync(input.jar))throw Error('Нет модуля телеметрии.');
  const child=require('node:child_process'),nativeSpawn=child.spawn;
  const {adapt}=require('./launch-adapter.cjs');let adapted=false;
  child.spawn=function(command,args,options){const next=adapt(command,args,input.jar,input.token);if(next)adapted=true;return nativeSpawn.call(this,command,next||args,options);};
  const launcher=require(location);
  if(typeof launcher.createLauncher!=='function')throw Error('Версия запуска OSRS Path несовместима.');
  const run=launcher.createLauncher({logFile:input.log});
  const result=await run.launch();
  if(result.ok&&result.state==='started'&&!adapted)throw Error('Не удалось подключить независимый загрузчик оптимизатора.');
  finish({ok:result.ok===true,state:String(result.state||'unknown')});
  // Keep a portable wrapper's resources alive while its client is running.
  if(result.ok&&adapted){
   const http=require('node:http');let connected=false,misses=0;const until=Date.now()+90000;
   while(true){
    const alive=await new Promise(resolve=>{const r=http.get({hostname:'127.0.0.1',port:38283,path:'/telemetry',headers:{'X-RuneLite-Optimizer':input.token},timeout:2000},res=>{res.resume();resolve(res.statusCode===200);});r.on('error',()=>resolve(false));r.on('timeout',()=>{r.destroy();resolve(false);});});
    if(alive){connected=true;misses=0;}else misses++;
    if((!connected&&Date.now()>until)||(connected&&misses>=4))break;
    await new Promise(resolve=>setTimeout(resolve,1500));
   }
  }
 }catch(error){finish({ok:false,message:String(error.message||error)});}
})().finally(()=>setTimeout(()=>process.exit(0),100));
