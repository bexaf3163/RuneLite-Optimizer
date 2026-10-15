using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RuneLiteOptimizer
{
    public sealed class GameProbe
    {
        public int schema,pid,world,yaw,pitch,viewportWidth,viewportHeight,clientFps,nearbyPlayers;
        public int? x,y,plane;
        public long timestamp,cameraTravel,walkTravel;
        public bool inGame;
        public string generation,renderer,configPath;public string[] availableRenderers;
        public Dictionary<string,string> raw,effective;
        public bool Fresh {get{return Math.Abs((DateTime.UtcNow-new DateTime(1970,1,1)).TotalMilliseconds-timestamp)<4000;}}
    }
    public sealed class GuidePreferences {public string osrsPath="",mode="gpu";}
    public static class GuideApi
    {
        public static string Folder {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RuneLiteOptimizer","Guided");}}
        public static string Token()
        {
            Directory.CreateDirectory(Folder);string path=Path.Combine(Folder,"token.bin");
            if(File.Exists(path))return Encoding.ASCII.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser));
            byte[] bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);string value=Settings.Hash(bytes);File.WriteAllBytes(path,ProtectedData.Protect(Encoding.ASCII.GetBytes(value),null,DataProtectionScope.CurrentUser));return value;
        }
        public static GuidePreferences Preferences(){try{return new JavaScriptSerializer().Deserialize<GuidePreferences>(File.ReadAllText(Path.Combine(Folder,"preferences.json")));}catch{return new GuidePreferences();}}
        public static void Save(GuidePreferences p){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"preferences.json"),new JavaScriptSerializer().Serialize(p));}
        static string Call(string path,object body)
        {
            var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:38283"+path);request.Proxy=null;request.Timeout=2500;request.ReadWriteTimeout=2500;request.Headers["X-RuneLite-Optimizer"]=Token();
            if(body!=null){request.Method="POST";request.ContentType="application/json";byte[] bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));request.ContentLength=bytes.Length;using(var output=request.GetRequestStream())output.Write(bytes,0,bytes.Length);}
            using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){string text=reader.ReadToEnd();if(text.Length>65536)throw new InvalidOperationException("Слишком большой ответ телеметрии.");return text;}
        }
        public static Task<GameProbe> Read(){return Task.Run(()=>{var p=new JavaScriptSerializer().Deserialize<GameProbe>(Call("/telemetry",null));if(p.schema!=2 || p.pid<=0 || string.IsNullOrEmpty(p.generation) || p.raw==null || p.effective==null || p.availableRenderers==null || string.IsNullOrEmpty(p.configPath))throw new InvalidOperationException("Перезапустите RuneLite из новой версии оптимизатора: нужен обновлённый модуль телеметрии.");return p;});}
        public static Task Apply(GameProbe p,Dictionary<string,string> values){return Task.Run(()=>{if(!p.Fresh)throw new InvalidOperationException("Данные игры устарели.");foreach(var v in values)if(!p.raw.ContainsKey(v.Key) || !v.Key.StartsWith(p.renderer+".",StringComparison.Ordinal))throw new InvalidOperationException("Рендерер изменился.");Call("/configure",new{generation=p.generation,values=values,expected=p.raw});});}
        public static async Task<string> Launch(string application,bool inspect=false)
        {
            if(!File.Exists(application) || !application.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Выберите установленный OSRS Path EXE.");
            if(!inspect)try{var existing=await Read();if(existing.Fresh)return "RuneLite уже открыт, модуль оптимизатора подключён.";}catch(Exception){}
            string stage=Path.Combine(Folder,"launch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            string jar=Path.Combine(stage,"OptimizerTelemetry.jar"),script=Path.Combine(stage,"launcher.cjs"),result=Path.Combine(stage,"result.json"),request=Path.Combine(stage,"request.json");
            File.WriteAllBytes(jar,Portable.Resource("OptimizerTelemetry.jar"));File.WriteAllBytes(script,Portable.Resource("launcher.cjs"));
            File.WriteAllText(request,new JavaScriptSerializer().Serialize(new{jar=jar,result=result,token=Token(),log=Path.Combine(stage,"private-launch.log"),inspect=inspect,deadline=(long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalMilliseconds+40000}));
            var info=new ProcessStartInfo(application,"\""+script+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(application)};info.EnvironmentVariables["ELECTRON_RUN_AS_NODE"]="1";info.EnvironmentVariables["RLO_LAUNCH_REQUEST"]=request;
            using(var process=Process.Start(info))
            {
                var watch=Stopwatch.StartNew();while(!File.Exists(result)&&watch.Elapsed.TotalSeconds<40){await Task.Delay(200);if(process.HasExited)break;}
                if(!File.Exists(result))throw new InvalidOperationException("OSRS Path не подтвердил совместимый запуск. Если выбран portable EXE, попробуйте установленную/распакованную сборку. Игру можно запустить вручную.");
            }
            var reply=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(result));File.Delete(request);
            if(!reply.ContainsKey("ok")||!(bool)reply["ok"])throw new InvalidOperationException(reply.ContainsKey("message")?Convert.ToString(reply["message"]):"OSRS Path не смог запустить клиент. Проверьте его обычную кнопку запуска.");
            if(inspect)return "Совместимый запуск подтверждён";
            string state=reply.ContainsKey("state")?Convert.ToString(reply["state"]):"unknown";
            if(!new[]{"started","starting","running"}.Contains(state))throw new InvalidOperationException("Модуль запуска не подтвердил старт клиента.");
            var handshake=Stopwatch.StartNew();
            while(handshake.Elapsed.TotalSeconds<(state=="running"?4:75))
            {
                try{var probe=await Read();if(probe.Fresh)return "RuneLite и модуль оптимизатора подключены. Нажмите Play, если ещё не вошли в игру.";}catch(Exception){}
                if(StartupFailed(Path.Combine(stage,"private-launch.log")))throw new InvalidOperationException("Java завершилась при загрузке модуля оптимизатора. Игра не запущена. Обновите оптимизатор и повторите запуск.");
                await Task.Delay(700);
            }
            throw new InvalidOperationException(state=="running"?"RuneLite уже открыт без совместимого модуля оптимизатора. Закройте RuneLite и нажмите «Запустить игру» снова.":"Подключение к RuneLite не подтверждено. Если клиент открыт, дождитесь его загрузки. При повторной попытке сначала закройте клиент. Настройки графики не подтверждены и замеры не начаты.");
        }
        public static bool StartupFailed(string log)
        {
            try{using(var input=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(input)){char[] buffer=new char[32768];int count=reader.Read(buffer,0,buffer.Length);string value=new string(buffer,0,count);return value.Contains("Error occurred during initialization of VM")||value.Contains("Error opening zip file or JAR manifest missing");}}catch{return false;}
        }
    }
    public sealed class BenchmarkPlace
    {
        public string Name,Instruction;public int X,Y;
        public bool Contains(GameProbe p,int radius){return p.x.HasValue&&p.y.HasValue&&p.plane==0&&Math.Abs(p.x.Value-X)<=radius&&Math.Abs(p.y.Value-Y)<=radius;}
        public static BenchmarkPlace[] All(){return new[]{
            new BenchmarkPlace{Name="Lumbridge",X=3222,Y=3218,Instruction="Телепортируйтесь в Lumbridge и выйдите к точке появления перед замком."},
            new BenchmarkPlace{Name="Varrock",X=3210,Y=3424,Instruction="Придите на центральную площадь Varrock, к фонтану."},
            new BenchmarkPlace{Name="Grand Exchange",X=3165,Y=3487,Instruction="Придите к центральным стойкам Grand Exchange."},
            new BenchmarkPlace{Name="Falador",X=2965,Y=3380,Instruction="Придите к фонтану в центре Falador."}};}
    }
    public sealed class GuideResult
    {
        public string cycle,scene,phase;public int world,width,height,nearbyPlayers;public long cameraTravel,walkTravel;public Statistics metrics;public int conditionPauses;public bool comparable;
    }
    public sealed class GuidedBenchmark
    {
        public bool Running,NeedsApply,WaitingForClose,WaitingForRestart,RestoreRequested,Completed,Verified,QuickApply;public int SelectedTier;public TuneRecovery Recovery;
        public int Cycle,Place,Phase;public string Instruction="Выберите OSRS Path и запустите игру с модулем измерений.",Status="Тест ещё не начат",ReportPath="",Error="";
        public readonly List<GuideResult> Results=new List<GuideResult>();
        public readonly List<Statistics> RawSegments=new List<Statistics>();
        public Dictionary<string,string> OriginalRaw,OriginalEffective,Desired;public string Generation,Renderer;
        public double PhaseSeconds,Warmup;public bool Arrived;
        int world,width,height,pauses;double cursor;long cameraStart,walkStart;bool phaseStarted;string stream;
        readonly List<Frame> frames=new List<Frame>();
        public static readonly string[] Cycles=AutoQuality.Names;
        public static readonly string[] Phases={"Стойте спокойно, не двигайте камеру.","Плавно вращайте камеру, оставаясь на месте. Сделайте хотя бы пол-оборота.","Пройдите примерно 12 клеток и вернитесь обратно. Достаточно двух перемещений."};
        public Dictionary<string,string> Plan(int cycle)
        {
            if(QuickApply)return AutoQuality.Recommended(Renderer,OriginalRaw);
            int tier=cycle==5?SelectedTier:cycle;
            var values=tier==0?OriginalRaw.Where(p=>p.Key.StartsWith(Renderer+".",StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value):AutoQuality.Values(Renderer,tier,OriginalRaw);
            if(Renderer=="none")values.Clear();
            else {values[Renderer+".unlockFps"]="true";values[Renderer+".vsyncMode"]="OFF";values[Renderer+".fpsTarget"]=cycle==5?"72":"999";}
            values["runelite.gpuplugin"]=Renderer=="gpu"?"true":"false";values["runelite.hdplugin"]=Renderer=="hd"?"true":"false";values["fpscontrol.limitFps"]="false";
            return values;
        }
        public void RequireClose(){NeedsApply=true;WaitingForClose=true;WaitingForRestart=false;Status="Закройте игру · следующий профиль готов";Instruction="Выйдите из игры и закройте RuneLite. После полного закрытия программа изменит графику и снова запустит клиент.";}
        public void Reconnected(GameProbe p)
        {
            if(!p.Fresh||(!p.inGame&&!QuickApply)||p.renderer!=Renderer||!string.Equals(p.configPath,Recovery.Path,StringComparison.OrdinalIgnoreCase)||!TuneFiles.Matches(p.raw,Recovery.Expected))throw new InvalidOperationException("Ожидание исходного профиля RuneLite и выбранного рендерера. Настройки не подтверждены.");
            Generation=p.generation;WaitingForRestart=false;Applied();
        }
        IEnumerable<GuideResult> Round(int round){return Results.Where(r=>r.cycle==Cycles[round]);}
        public void FinishRound()
        {
            if(Renderer=="none"){Completed=true;Verified=false;Running=false;Status="Стандартный режим измерен";Instruction="Без GPU/117 HD стандартный рендерер ограничен примерно 50 FPS. Для цели 60+ выберите GPU или 117 HD.";return;}
            if(Cycle==5){Verified=AutoQuality.Pass(Round(5),true);Completed=true;Running=false;Status=Verified?"Готово · цель подтверждена на тесте":"Готово · стабильные 60+ не подтверждены";Instruction=Verified?"Оставлен самый красивый прошедший профиль из проверенной лестницы качества. Отчёт сохранён.":"Выбранный профиль не прошёл повторную проверку. Результат сохранён без обещания отсутствия просадок.";return;}
            if(Cycle>0&&AutoQuality.Pass(Round(Cycle),false)){SelectedTier=Cycle;Cycle=5;}
            else if(Cycle==4){SelectedTier=Enumerable.Range(0,5).OrderBy(t=>AutoQuality.Loss(Round(t))).First();Cycle=5;}
            else Cycle++;
            RequireClose();
        }
        public void Begin(GameProbe p,string reportFolder,string target=null,bool quick=false)
        {
            if(!p.Fresh||(!p.inGame&&!quick)||p.raw==null||p.effective==null||!(new[]{"none","hd","gpu"}.Contains(target??p.renderer))||p.availableRenderers==null||!p.availableRenderers.Contains(target??p.renderer))throw new InvalidOperationException("Войдите в игру для замеров. Для 117 HD плагин должен быть установлен через Plugin Hub.");
            QuickApply=quick;
            Recovery=TuneFiles.Begin(p);Generation=p.generation;Renderer=target??p.renderer;world=p.world;width=p.viewportWidth;height=p.viewportHeight;OriginalRaw=new Dictionary<string,string>(p.raw);OriginalEffective=new Dictionary<string,string>(p.effective);Results.Clear();RawSegments.Clear();Cycle=Place=Phase=0;Error="";Running=true;Completed=Verified=RestoreRequested=false;SelectedTier=0;NeedsApply=true;ResetPhase();RequireClose();
            Directory.CreateDirectory(reportFolder);ReportPath=Path.Combine(reportFolder,"benchmark-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".json");
            File.WriteAllBytes(ReportPath+".backup",ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new{generation=Generation,renderer=Renderer,raw=OriginalRaw})),null,DataProtectionScope.CurrentUser));
        }
        public void Applied(){Desired=Plan(Cycle);NeedsApply=false;Arrived=false;ResetPhase();}
        void ResetPhase(){frames.Clear();PhaseSeconds=Warmup=0;phaseStarted=false;stream=null;cursor=0;pauses=0;}
        public void Pause(string reason){Instruction=reason;Status="Пауза · условия не выполнены";Warmup=0;cursor=double.NaN;pauses++;}
        bool SettingsMatch(GameProbe p)
        {
            foreach(var pair in Desired){string value;if(!p.effective.TryGetValue(pair.Key,out value))return false;string expected=pair.Value??(OriginalEffective.ContainsKey(pair.Key)?OriginalEffective[pair.Key]:null);if(expected!=value)return false;}return true;
        }
        public void Observe(GameProbe p,FrameSession source,bool focused)
        {
            if(!Running||NeedsApply)return;
            if(!phaseStarted && source.StoredSpan>120)Archive(source);
            if(!p.Fresh){Pause("Нет свежей телеметрии игры. Запись приостановлена.");return;}
            if(p.generation!=Generation){Running=false;Error="Сессия или профиль RuneLite изменились. Тест остановлен; настройки другого профиля не меняются.";Save(source);return;}
            if(!p.inGame){Pause("Войдите в игру и нажмите Play.");return;}
            if(p.renderer!=Renderer||!SettingsMatch(p)){Pause("Дождитесь применения профиля. Если меняли графику вручную, остановите тест и начните заново.");return;}
            if(p.world!=world||p.viewportWidth!=width||p.viewportHeight!=height){Pause("Верните исходный мир "+world+" и размер игровой области "+width+"×"+height+".");return;}
            var place=BenchmarkPlace.All()[Place];if(!place.Contains(p,Arrived?55:14)){Arrived=false;Pause(place.Instruction+" Цель: "+place.X+", "+place.Y+"; вы: "+p.x+", "+p.y+".");return;}Arrived=true;
            if(!focused){Pause("Переключитесь в окно игры — запись продолжится автоматически.");return;}
            var candidates=source.Chains().Select(c=>new{Chain=c,Stats=Statistics.From(source.Select(c,Math.Max(0,source.LastTime-5),true))}).Where(c=>c.Stats.Count>=10).OrderBy(c=>Math.Abs(c.Stats.Fps-Math.Max(1,p.clientFps))).ToArray();
            if(candidates.Length==0||p.clientFps<=0){Pause("Ожидание кадров. Если ETW запрещён, запустите оптимизатор с разрешением на сбор кадров.");return;}
            string selected=candidates[0].Chain;if(Math.Abs(candidates[0].Stats.Fps-p.clientFps)>Math.Max(10,p.clientFps*.35)){Pause("Источники FPS расходятся. Нельзя автоматически подтвердить игровой поток.");return;}
            if(stream!=null&&stream!=selected){ResetPhase();}stream=selected;
            if(Warmup<10){Warmup++;cursor=source.LastTime;Instruction="Вы на месте. Оставайтесь здесь: прогрев сцены "+Warmup.ToString("F0")+" / 10 с.";Status=Cycles[Cycle]+" · "+place.Name;return;}
            if(!phaseStarted){cameraStart=p.cameraTravel;walkStart=p.walkTravel;phaseStarted=true;cursor=source.LastTime;}
            if((Phase==0&&(p.cameraTravel-cameraStart>24||p.walkTravel-walkStart>2))||(Phase==1&&p.walkTravel-walkStart>2)){ResetPhase();Instruction="Для этого этапа остановитесь. Начинаю его заново.";return;}
            if(double.IsNaN(cursor)){cursor=source.LastTime;return;}
            var next=source.Select(stream,cursor+.000001,true);cursor=source.LastTime;
            frames.AddRange(next);PhaseSeconds=frames.Sum(f=>f.Milliseconds)/1000;
            double goal=Phase==2?60:30;Instruction=Phases[Phase];Status=Cycles[Cycle]+" · "+place.Name+" · "+Math.Min(goal,PhaseSeconds).ToString("F0")+" / "+goal+" с";
            bool action=Phase==0||Phase==1&&p.cameraTravel-cameraStart>=1024||Phase==2&&p.walkTravel-walkStart>=24;
            if(PhaseSeconds>180&&!action){Archive(source);ResetPhase();Instruction="Действие не зафиксировано. Повторим этот этап: "+Phases[Phase];return;}
            if(PhaseSeconds<goal||!action)return;
            var reference=Results.FirstOrDefault(r=>r.cycle==Cycles[0]&&r.scene==place.Name&&r.phase==Phases[Phase]);
            bool comparable=reference==null||Math.Abs(reference.nearbyPlayers-p.nearbyPlayers)<=Math.Max(10,reference.nearbyPlayers*.5);
            Results.Add(new GuideResult{cycle=Cycles[Cycle],scene=place.Name,phase=Phases[Phase],world=world,width=width,height=height,nearbyPlayers=p.nearbyPlayers,cameraTravel=p.cameraTravel-cameraStart,walkTravel=p.walkTravel-walkStart,metrics=Statistics.From(frames),conditionPauses=pauses,comparable=comparable});
            Phase++;ResetPhase();if(Phase==3){Phase=0;Place++;Arrived=false;}
            if(Place==4){Place=0;FinishRound();if(Completed&&Recovery!=null){Recovery.Completed=true;TuneFiles.Save(Recovery);}}
            Archive(source);Save(source);
        }
        void Archive(FrameSession source){var raw=Statistics.From(source.All());if(raw.Count>0)RawSegments.Add(raw);source.Clear();}
        public void Save(FrameSession source)
        {
            if(string.IsNullOrEmpty(ReportPath))return;
            var serializer=new JavaScriptSerializer();string json=serializer.Serialize(new{version=Portable.Version,completed=Completed,unmeasuredPreset=QuickApply,verified60plus=Verified,selectedTier=SelectedTier,qualityLadder=Cycles,renderer=Renderer,results=Results,rawSegments=RawSegments,rawSession=Statistics.From(QuickApply?new Frame[0]:source.All()),warning="Present intervals, not network latency. Best among tested presets, not a global optimum or future no-stall guarantee. Paused/invalid intervals remain in raw segments. World/viewport fixed; player count can differ between passes. No account identity or private configuration path recorded.",error=Error.Length>0?"Operation failed; details remain in the local UI.":""});
            string temp=ReportPath+".tmp";File.WriteAllText(temp,json,new UTF8Encoding(false));if(File.Exists(ReportPath))File.Replace(temp,ReportPath,null);else File.Move(temp,ReportPath);
        }
        public static void Tests()
        {
            var place=BenchmarkPlace.All()[0];var p=new GameProbe{x=3222,y=3218,plane=0};if(!place.Contains(p,14))throw new Exception("Scene arrival");p.plane=1;if(place.Contains(p,14))throw new Exception("Scene floor isolation");
            var engine=new GuidedBenchmark{Renderer="hd",OriginalRaw=new Dictionary<string,string>{{"hd.fpsTarget",null},{"hd.shadowMode","DETAILED"}}};if(engine.Plan(0)["hd.shadowMode"]!="DETAILED"||engine.Plan(0)["hd.fpsTarget"]!="999"||engine.Plan(5)["hd.fpsTarget"]!="72")throw new Exception("Guide profile plan");
            TuneFiles.Tests();
            File.WriteAllText("guide-test-result.txt","PASS: scene bounds/floor and non-destructive profile planning. Java companion tests exercise authorization and input allowlist.");
        }
    }
    public sealed class GuideHud : Form
    {
        [StructLayout(LayoutKind.Sequential)] struct WindowBounds {public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle,out WindowBounds bounds);
        readonly Label text=new Label{Dock=DockStyle.Fill,Padding=new Padding(12),ForeColor=Color.White,BackColor=Color.FromArgb(20,24,31),Font=new Font("Segoe UI",10)};
        public GuideHud(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;Size=new Size(330,130);Opacity=.93;Controls.Add(text);StartPosition=FormStartPosition.Manual;Location=new Point(24,90);}
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000020|0x00000080;return p;}}
        public void UpdateText(string title,string instruction){text.Text=title+"\n\n"+instruction;}
        public void PlaceFor(int pid){try{using(var p=Process.GetProcessById(pid)){WindowBounds r;if(GetWindowRect(p.MainWindowHandle,out r))Location=new Point(r.Left+18,r.Top+90);}}catch{}}
    }
}
