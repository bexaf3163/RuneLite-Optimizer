'use strict';
const assert=require('node:assert/strict'),path=require('node:path');
const {adapt}=require('../companion/launch-adapter.cjs');
const token='a'.repeat(64),base=['-ea','-Xmx768m','-cp','bridge.jar'+path.delimiter+'client.jar','com.osrspath.bridge.OsrsPathLauncher','--noupdate'];
const before=base.slice(),result=adapt('javaw.exe',base,'own module.jar',token);
assert.deepEqual(base,before);assert.ok(result.includes('-ea'));assert.ok(result.includes('-Xmx768m'));assert.equal(result[result.indexOf('-cp')+1],'own module.jar'+path.delimiter+before[3]);assert.ok(result.includes('com.runeliteoptimizer.OptimizerLauncher'));assert.equal(result.at(-1),'--noupdate');assert.equal(result.filter(x=>x.startsWith('-Drunelite.optimizer.token=')).length,1);
assert.equal(adapt('other.exe',['unrelated'],'own.jar',token),null);
assert.throws(()=>adapt('other.exe',base,'own.jar',token));assert.throws(()=>adapt('java.exe',['com.osrspath.bridge.OsrsPathLauncher'],'own.jar',token));assert.throws(()=>adapt('java.exe',base,'own.jar','bad'));
console.log('PASS: own launch adapter preserves JVM flags, original bridge classpath and arguments; unrelated spawns unaffected; malformed launch rejected.');
