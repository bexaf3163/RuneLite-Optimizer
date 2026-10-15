using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace RuneLiteOptimizer
{
    public static class AutoQuality
    {
        public static readonly string[] Names={"Исходная графика", "Высокое качество", "Баланс", "Плавность", "Минимальная нагрузка", "Проверка результата"};
        public static string Description(string renderer)
        {
            return renderer=="none"?"Стандартная: GPU и 117 HD выключены. Глобальный ограничитель FPS Control отключён. Предел программного рендерера — около 50 FPS.":renderer=="gpu"?"GPU: дальность 40, загрузка карты +1, сглаживание 2×, фильтрация 4×. Unlock FPS включён, VSync выключен, лимит 72. 117 HD выключен.":"117 HD: дальность 30, детали 25, загрузка карты +1; разрешение 100%, без MSAA. Быстрые тени 1024, лёгкое сглаживание теней, немного источников света. Текстуры и normal mapping включены; POM, расширенные тени и прозрачность теней выключены. Фильтрация 4×. Unlock FPS включён, VSync выключен, лимит 72. GPU выключен.";
        }
        public static Dictionary<string,string> Recommended(string renderer,Dictionary<string,string> supported)
        {
            if(!new[]{"none","hd","gpu"}.Contains(renderer))throw new ArgumentException("Unknown renderer");
            var values=renderer=="none"?new Dictionary<string,string>():Values(renderer,renderer=="hd"?3:2,supported);
            if(renderer!="none"){
                values[renderer+".fpsTarget"]="72";
                if(renderer=="gpu"){values["gpu.drawDistance"]="40";values["gpu.expandedMapLoadingChunks"]="1";values["gpu.anisotropicFilteringLevel"]="4";}
                else {
                    var hd=new Dictionary<string,string>{{"hd.drawDistance","30"},{"hd.detailDistance","25"},{"hd.shadowMode","FAST"},{"hd.shadowResolution","RES_1024"},{"hd.shadowFiltering","SMOOTH_LOW"},{"hd.anisotropicFilteringLevel","4"},{"hd.dynamicLights","FEW"},{"hd.sceneResolutionScale","100"}};
                    foreach(var p in hd)if(supported.ContainsKey(p.Key))values[p.Key]=p.Value;
                }
            }
            values["runelite.gpuplugin"]=renderer=="gpu"?"true":"false";values["runelite.hdplugin"]=renderer=="hd"?"true":"false";values["fpscontrol.limitFps"]="false";return values;
        }
        public static Dictionary<string,string> Values(string renderer,int tier,Dictionary<string,string> supported)
        {
            if(tier<1||tier>4)throw new ArgumentOutOfRangeException("tier");
            string g=renderer+".";int i=tier-1;
            var all=new Dictionary<string,string>{{g+"drawDistance",new[]{60,45,30,20}[i].ToString()},{g+"expandedMapLoadingChunks",new[]{3,2,1,0}[i].ToString()},
                {g+"antiAliasingMode",new[]{"MSAA_4","MSAA_2","DISABLED","DISABLED"}[i]},{g+"anisotropicFilteringLevel",new[]{8,4,2,1}[i].ToString()},
                {g+"unlockFps","true"},{g+"fpsTarget","999"},{g+"vsyncMode","OFF"}};
            if(renderer=="hd"){
                all[g+"detailDistance"]=new[]{60,45,30,25}[i].ToString();all[g+"shadowMode"]=new[]{"DETAILED","FAST","OFF","OFF"}[i];all[g+"sceneResolutionScale"]=i==3?"85":"100";
                all[g+"shadowResolution"]=new[]{"RES_4096","RES_2048","RES_1024","RES_1024"}[i];all[g+"shadowFiltering"]=i==0?"SMOOTH_HIGH":i==1?"SMOOTH_LOW":"NEAREST";
                all[g+"dynamicLights"]=new[]{"MANY","SOME","FEW","FEW"}[i];all[g+"normalMapping"]="true";all[g+"groundTextures"]="true";
                all[g+"parallaxOcclusionMappingToggle"]=i==0?"true":"false";all[g+"expandShadowDraw"]="false";all[g+"terrainShadows"]=i<2?"true":"false";all[g+"enableShadowTransparency"]=i==0?"true":"false";
            }
            return all.Where(p=>supported.ContainsKey(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);
        }
        public static bool Valid(string key,string value)
        {
            if(new[]{"runelite.gpuplugin","runelite.hdplugin","fpscontrol.limitFps"}.Contains(key))return value=="true"||value=="false";
            if(key.StartsWith("gpu.",StringComparison.Ordinal)){
                string name=key.Substring(4);int n;
                if(name=="unlockFps")return value=="true"||value=="false";
                if(name=="vsyncMode")return new[]{"OFF","ON","ADAPTIVE"}.Contains(value);
                if(name=="antiAliasingMode")return new[]{"DISABLED","MSAA_2","MSAA_4","MSAA_8","MSAA_16"}.Contains(value);
                if(!int.TryParse(value,out n)||n.ToString()!=value||n<0)return false;
                return name=="drawDistance"?n<=184:name=="expandedMapLoadingChunks"?n<=5:name=="fpsTarget"?n<=999:name=="anisotropicFilteringLevel"&&n<=16;
            }
            var keys=new Dictionary<string,string>{{key,null}};
            return new[]{"gpu","hd"}.Any(g=>Enumerable.Range(1,4).Any(t=>Values(g,t,keys).Any(p=>p.Key==key&&p.Value==value))) || ((key=="gpu.fpsTarget"||key=="hd.fpsTarget")&&value=="72");
        }
        // Judge every phase, not a misleading average of fast and slow locations.
        public static bool Pass(IEnumerable<GuideResult> source,bool final)
        {
            var a=source.ToArray();return a.Length==12&&a.All(r=>r.comparable&&r.metrics.Count>=100&&r.metrics.Seconds>=29&&r.metrics.Fps>=(final?65:72)&&r.metrics.Low1>=60&&r.metrics.P99<=1000.0/60&&r.metrics.Over33==0);
        }
        public static double Loss(IEnumerable<GuideResult> source)
        {
            var a=source.ToArray();return a.Length!=12?double.PositiveInfinity:a.Max(r=>Math.Max(r.metrics.P99,1000/Math.Max(.01,r.metrics.Low1))+r.metrics.Over33*10.0/Math.Max(1,r.metrics.Seconds));
        }
    }
    public sealed class TuneRecovery
    {
        public string Path,Renderer;public bool Completed;
        public Dictionary<string,string> Original,Expected,Pending;
    }
    public static class TuneFiles
    {
        public static string Record {get{return System.IO.Path.Combine(GuideApi.Folder,"active-tune.bin");}}
        public static bool Offline()
        {
            return Diagnostics.Clients().Count==0&&Process.GetProcessesByName("java").Length==0&&Process.GetProcessesByName("javaw").Length==0;
        }
        public static string ValidatePath(string path)
        {
            if(string.IsNullOrEmpty(path))throw new InvalidOperationException("Модуль не определил файл активного профиля RuneLite.");
            string root=System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".runelite","profiles2"))+System.IO.Path.DirectorySeparatorChar;
            string full=System.IO.Path.GetFullPath(path);
            if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!full.EndsWith(".properties",StringComparison.OrdinalIgnoreCase)||!File.Exists(full))throw new InvalidOperationException("Неизвестный файл профиля. Автоматическое изменение отменено.");
            for(var d=new DirectoryInfo(System.IO.Path.GetDirectoryName(full));d!=null;d=d.Parent)
                if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Путь профиля содержит перенаправление.");
            if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Файл профиля содержит перенаправление.");return full;
        }
        public static bool Matches(Dictionary<string,string> actual,Dictionary<string,string> expected)
        {
            return expected.All(p=>{string v;actual.TryGetValue(p.Key,out v);return v==p.Value;});
        }
        public static TuneRecovery Load(){return new JavaScriptSerializer().Deserialize<TuneRecovery>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Record),null,DataProtectionScope.CurrentUser)));}
        public static void Save(TuneRecovery value)
        {
            Directory.CreateDirectory(GuideApi.Folder);string temp=Record+".tmp";File.WriteAllBytes(temp,ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value)),null,DataProtectionScope.CurrentUser));if(File.Exists(Record))File.Replace(temp,Record,null);else File.Move(temp,Record);
        }
        public static TuneRecovery Begin(GameProbe p)
        {
            if(File.Exists(Record)&&!Load().Completed)throw new InvalidOperationException("Сначала верните графику предыдущего незавершённого подбора кнопкой восстановления.");
            var r=new TuneRecovery{Path=ValidatePath(p.configPath),Renderer=p.renderer,Original=new Dictionary<string,string>(p.raw),Expected=new Dictionary<string,string>(p.raw)};Save(r);return r;
        }
        public static void Prepare(TuneRecovery r,Dictionary<string,string> values){r.Pending=new Dictionary<string,string>(r.Expected);foreach(var p in values)r.Pending[p.Key]=p.Value;Save(r);}
        public static void Applied(TuneRecovery r){r.Expected=r.Pending;r.Pending=null;Save(r);}
        public static void ApplyOffline(TuneRecovery r,Dictionary<string,string> values)
        {
            ValidatePath(r.Path);if(!Offline())throw new InvalidOperationException("Закройте RuneLite и другие Java-приложения. Оптимизатор не закрывает их принудительно.");
            byte[] bytes=File.ReadAllBytes(r.Path);if(!Matches(Settings.Read(bytes),r.Expected))throw new InvalidOperationException("Графика в файле изменилась вне подбора. Автоматическая запись остановлена; копия сохранена.");
            Prepare(r,values);Changes.Apply(r.Path,bytes,values);Applied(r);
        }
        public static void Restore(TuneRecovery r)
        {
            ValidatePath(r.Path);if(!Offline())throw new InvalidOperationException("Сначала закройте RuneLite и Java-приложения.");
            byte[] bytes=File.ReadAllBytes(r.Path);var actual=Settings.Read(bytes);
            if(Matches(actual,r.Original)){r.Expected=new Dictionary<string,string>(r.Original);r.Pending=null;r.Completed=true;Save(r);return;}
            if(!Matches(actual,r.Expected)&&(r.Pending==null||!Matches(actual,r.Pending)))throw new InvalidOperationException("Графика изменена вручную. Возврат отменён, чтобы сохранить ваши изменения.");
            Changes.Apply(r.Path,bytes,r.Original);r.Expected=new Dictionary<string,string>(r.Original);r.Pending=null;r.Completed=true;Save(r);
        }
        public static void Tests()
        {
            var supported=OptimizationProfiles.Make(true,true);
            foreach(string mode in new[]{"none","hd","gpu"}){
                var available=Settings.Keys.ToDictionary(k=>k,k=>(string)null);var recommended=AutoQuality.Recommended(mode,available);
                foreach(var p in recommended)if(!AutoQuality.Valid(p.Key,p.Value)&&!HdRules.Valid(p.Key,p.Value))throw new Exception("Recommended preset validation");
                if(recommended["runelite.gpuplugin"]==(recommended["runelite.hdplugin"])&&mode!="none")throw new Exception("Conflicting renderers");
                if(recommended.Any(p=>p.Key.Contains("osrspath")))throw new Exception("OSRS Path isolation");
            }
            foreach(int t in Enumerable.Range(1,4))foreach(var p in AutoQuality.Values("hd",t,supported))if(!AutoQuality.Valid(p.Key,p.Value))throw new Exception("Candidate allowlist");
            var sample=Enumerable.Range(0,12).Select(i=>new GuideResult{comparable=true,metrics=new Statistics{Count=2400,Seconds=30,Fps=80,Low1=70,P99=14}}).ToArray();
            if(!AutoQuality.Pass(sample,false))throw new Exception("Healthy candidate rejected");sample[11].metrics.Over33=1;if(AutoQuality.Pass(sample,false))throw new Exception("Stall hidden by average");sample[11].metrics.Over33=0;sample[11].metrics.Low1=55;if(AutoQuality.Pass(sample,false))throw new Exception("Low percentile ignored");if(AutoQuality.Pass(sample.Take(11),false))throw new Exception("Incomplete round accepted");
            byte[] original=Settings.Bytes.GetBytes("# untouched\r\nplugin.secret=private\r\ngpu.fpsTarget=60\r\n");byte[] changed=Settings.Patch(original,new Dictionary<string,string>{{"gpu.fpsTarget","72"},{"gpu.antiAliasingMode","MSAA_4"}});byte[] restored=Settings.Patch(changed,new Dictionary<string,string>{{"gpu.fpsTarget","60"},{"gpu.antiAliasingMode",null}});
            if(Settings.Hash(original)!=Settings.Hash(restored))throw new Exception("Selective exact unset restore");
            if(TuneFiles.Matches(new Dictionary<string,string>{{"gpu.fpsTarget","72"}},new Dictionary<string,string>{{"gpu.fpsTarget","60"}}))throw new Exception("Conflict guard");
            var engine=new GuidedBenchmark{Renderer="hd",Cycle=1,Running=true};foreach(var r in sample){r.cycle=AutoQuality.Names[1];r.metrics.Low1=70;engine.Results.Add(r);}engine.FinishRound();if(engine.Cycle!=5||engine.SelectedTier!=1||!engine.WaitingForClose)throw new Exception("Passing quality did not request offline verification");
            engine=new GuidedBenchmark{Renderer="hd",Cycle=1,Running=true};engine.FinishRound();if(engine.Cycle!=2||!engine.WaitingForClose)throw new Exception("Incomplete candidate should step down");
            engine=new GuidedBenchmark{Renderer="hd",Cycle=5,Running=true};engine.FinishRound();if(engine.Verified||!engine.Completed)throw new Exception("Unverified final must remain unverified");
            engine=new GuidedBenchmark{Renderer="none",Running=true};engine.FinishRound();if(engine.Verified||!engine.Completed)throw new Exception("Software renderer cannot claim 60+");
            engine=new GuidedBenchmark{Renderer="gpu",QuickApply=true,OriginalRaw=OptimizationProfiles.Make(false,true),Recovery=new TuneRecovery{Path="fixture-profile",Expected=new Dictionary<string,string>{{"gpu.fpsTarget","72"}}}};
            var probe=new GameProbe{timestamp=(long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalMilliseconds,renderer="gpu",configPath="wrong-profile",generation="new-session",raw=new Dictionary<string,string>{{"gpu.fpsTarget","72"}}};
            bool blocked=false;try{engine.Reconnected(probe);}catch(InvalidOperationException){blocked=true;}if(!blocked)throw new Exception("Restart profile guard");
            probe.configPath="fixture-profile";engine.Reconnected(probe);if(engine.Generation!="new-session"||engine.NeedsApply||engine.WaitingForRestart)throw new Exception("Expected restart not accepted");
            sample[11].comparable=false;if(AutoQuality.Pass(sample,false))throw new Exception("Changed crowd accepted");
            string launchLog=System.IO.Path.Combine(Environment.CurrentDirectory,"launcher-fixture.log");
            var dashboard=GuideApi.PathWindowStart(System.IO.Path.Combine(Environment.CurrentDirectory,"fixture.exe"));if(dashboard.UseShellExecute||dashboard.CreateNoWindow||dashboard.WindowStyle!=ProcessWindowStyle.Normal||dashboard.Arguments.Length!=0||dashboard.EnvironmentVariables.ContainsKey("ELECTRON_RUN_AS_NODE")||dashboard.EnvironmentVariables.ContainsKey("RLO_LAUNCH_REQUEST"))throw new Exception("Normal dashboard launch inherited helper mode");
            try{File.WriteAllText(launchLog,"Error opening zip file or JAR manifest missing : fixture.jar\nError occurred during initialization of VM");if(!GuideApi.StartupFailed(launchLog))throw new Exception("Java startup failure not detected");File.WriteAllText(launchLog,"Normal initialization; benchmark has not started.");if(GuideApi.StartupFailed(launchLog))throw new Exception("Normal startup falsely rejected");}finally{if(File.Exists(launchLog))File.Delete(launchLog);}
            File.WriteAllText("autotune-test-result.txt","PASS: three recommended presets; renderer exclusivity; candidate selection and offline transition; incomplete/stalled/low-percentile/crowd rejection; final and software no-false-success; restart profile guard; selective unset and conflict preservation. Synthetic fixtures only.");
        }
    }
}
