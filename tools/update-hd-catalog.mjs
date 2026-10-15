// Explicit maintenance command; never downloads code or settings at app runtime.
import fs from 'node:fs/promises';
const ref='04fd8520a3db9e8c196e987d7164ff9e6c2c701c';
const root=`https://raw.githubusercontent.com/117HD/RLHD/${ref}/src/main/java/rs117/hd/`;
async function get(path){const r=await fetch(root+path);if(!r.ok)throw new Error(`${path}: ${r.status}`);return r.text();}
const source=await get('HdPluginConfig.java'), plugin=await get('HdPlugin.java');
const constants=Object.fromEntries([...source.matchAll(/String\s+(\w+)\s*=\s*"([^"]+)"/g)].map(m=>[m[1],m[2]]));
const enums={SyncMode:['OFF','ON','ADAPTIVE']};
await Promise.all([...source.matchAll(/import rs117\.hd\.config\.(\w+);/g)].map(async m=>{const s=await get(`config/${m[1]}.java`);enums[m[1]]=[...s.matchAll(/^\s*([A-Z][A-Z0-9_]*)\s*(?:\(|[,;])/gm)].map(x=>x[1]);}));
const numerical={MAX_DISTANCE:184,MAX_FOG_DEPTH:Number((plugin.match(/MAX_FOG_DEPTH\s*=\s*(\d+)/)||[])[1])};
function endAnnotation(start){let depth=0,quote=false,escape=false;for(let i=start;i<source.length;i++){const c=source[i];if(escape){escape=false;continue;}if(c==='\\'&&quote){escape=true;continue;}if(c==='"'){quote=!quote;continue;}if(!quote){if(c==='(')depth++;if(c===')'&&--depth===0)return i+1;}}throw new Error('unbalanced annotation');}
function strings(s){return [...s.matchAll(/"((?:\\.|[^"\\])*)"/g)].map(m=>JSON.parse('"'+m[1]+'"')).join('');}
const rows=[];
for(const match of source.matchAll(/@ConfigItem\(/g)){
 const start=match.index,end=endAnnotation(start+11),a=source.slice(start,end);
 if(/hidden\s*=\s*true/.test(a))continue;
 const method=source.slice(end).match(/^[\s\S]*?default\s+(\w+)\s+(\w+)\s*\(/);if(!method)throw new Error('method missing');
 const next=source.indexOf('@ConfigItem(',end);if(next>=0 && end+method[0].length>next)throw new Error('method skipped');
 const key=(a.match(/keyName\s*=\s*"([^"]+)"/)||[])[1]||constants[(a.match(/keyName\s*=\s*(\w+)/)||[])[1]];
 const name=(a.match(/name\s*=\s*"([^"]*)"/)||[])[1];if(!key||!name)continue;
 const desc=strings(((a.split(/description\s*=/)[1]||'').split(/,\s*(?:position|section|hidden|warning)\s*=/)[0])).replace(/<[^>]*>/g,' ').replace(/\s+/g,' ');
 const type=method[1],before=source.slice(source.lastIndexOf('}',start)+1,start),after=source.slice(end,end+method[0].length),range=((before+after).match(/@Range\(([^)]*)\)/)||[])[1]||'';
 let min=0,max=2147483647;for(const field of ['min','max']){let raw=(range.match(new RegExp(field+'\\s*=\\s*([\\w-]+)'))||[])[1];if(raw!==undefined){const value=/^-?\d+$/.test(raw)?Number(raw):numerical[raw];if(!Number.isFinite(value))throw new Error('Unknown range '+raw);if(field==='min')min=value;else max=value;}}
 let options=type==='boolean'?['true','false']:enums[type]||[];if(type!=='int'&&!options.length)throw new Error('Unknown type '+type);
 rows.push({key:'hd.'+key,name,type,desc,min,max,options});
}
if(new Set(rows.map(r=>r.key)).size!==rows.length)throw new Error('duplicate key');
const escape=s=>s.replace(/\\/g,'\\\\').replace(/"/g,'\\"');
const cs=`// Generated from 117 HD ${ref}; see THIRD-PARTY-NOTICES.md.\nusing System;\nusing System.Collections.Generic;\nnamespace RuneLiteOptimizer { public static class HdRules {\npublic static readonly Dictionary<string,string[]> Choices=new Dictionary<string,string[]> {\n${rows.filter(r=>r.type!=='int').map(r=>`{"${r.key}",new[]{${r.options.map(x=>'"'+escape(x)+'"').join(',')}}}`).join(',\n')}\n};\npublic static readonly Dictionary<string,int[]> Ranges=new Dictionary<string,int[]> {\n${rows.filter(r=>r.type==='int').map(r=>`{"${r.key}",new[]{${r.min},${r.max}}}`).join(',\n')}\n};\npublic static bool Valid(string key,string value) { string[] choices;int[] range;int n;return Choices.TryGetValue(key,out choices) ? Array.IndexOf(choices,value)>=0 : Ranges.TryGetValue(key,out range)&&int.TryParse(value,out n)&&n>=range[0]&&n<=range[1]&&n.ToString(System.Globalization.CultureInfo.InvariantCulture)==value; }\n} }\n`;
await fs.writeFile(new URL('../src/HdRules.cs',import.meta.url),cs);
await fs.writeFile(new URL('../hd-settings.tsv',import.meta.url),'Key\tName\tType\tDescription\n'+rows.map(r=>[r.key,r.name,r.type,r.desc].join('\t')).join('\n')+'\n');
console.log(`Generated ${rows.length} verified visible HD settings from ${ref}`);
