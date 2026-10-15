using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace RuneLiteOptimizer
{
    public static class OptimizationProfiles
    {
        public static Dictionary<string,string> Make(bool hd, bool uncapped)
        {
            var v = new Dictionary<string,string>(Preset.All()[0].Values.ToDictionary(p => hd ? p.Key.Replace("gpu.", "hd.") : p.Key, p => p.Value));
            v[(hd ? "hd." : "gpu.") + "fpsTarget"] = uncapped ? "999" : "60";
            if (hd) { v["hd.detailDistance"] = "25"; v["hd.shadowMode"] = "OFF"; v["hd.sceneResolutionScale"] = "100"; }
            return v;
        }
        public static string[] Keys { get { return Make(false,true).Keys.Concat(Make(true,true).Keys).Distinct().ToArray(); } }
        public static bool Valid(string key, string value)
        {
            return new[] { Make(false,false), Make(false,true), Make(true,false), Make(true,true) }.Any(v => v.ContainsKey(key) && v[key] == value);
        }
    }
    public sealed class Frame
    {
        public double Time, Milliseconds, QpcSeconds;
        public string Chain;
        public bool Active;
    }
    public sealed class CsvFrames
    {
        string[] columns;
        public int Invalid;
        public static string[] Split(string line)
        {
            var result = new List<string>(); var s = new StringBuilder(); bool quoted = false;
            for(int i=0;i<line.Length;i++) { char c=line[i]; if(c=='"') { if(quoted && i+1<line.Length && line[i+1]=='"') { s.Append('"');i++; } else quoted=!quoted; } else if(c==',' && !quoted) { result.Add(s.ToString());s.Clear(); } else s.Append(c); }
            result.Add(s.ToString());return result.ToArray();
        }
        public Frame Parse(string line, int pid, bool active)
        {
            var values=Split(line);
            if(values.Any(v=>string.Equals(v,"ProcessID",StringComparison.OrdinalIgnoreCase)) && values.Any(v=>string.Equals(v,"MsBetweenPresents",StringComparison.OrdinalIgnoreCase))) { columns=values;return null; }
            if(columns==null)return null;
            try {
                Func<string,string> get=k=> { int n=Array.FindIndex(columns,c=>string.Equals(c,k,StringComparison.OrdinalIgnoreCase));return n<0 || n>=values.Length ? "" : values[n]; };
                if(get("ProcessID")!=pid.ToString(CultureInfo.InvariantCulture))return null;
                double ms,t;
                if(!double.TryParse(get("MsBetweenPresents"),NumberStyles.Float,CultureInfo.InvariantCulture,out ms) || !double.TryParse(get("TimeInSeconds"),NumberStyles.Float,CultureInfo.InvariantCulture,out t) || ms<=0 || double.IsNaN(ms) || double.IsInfinity(ms) || t<0 || double.IsNaN(t) || double.IsInfinity(t)) {Invalid++;return null;}
                double qpc;double.TryParse(get("QPCTime"),NumberStyles.Float,CultureInfo.InvariantCulture,out qpc);
                return new Frame { Time=t,Milliseconds=ms,QpcSeconds=qpc/Stopwatch.Frequency,Chain=get("SwapChainAddress"),Active=active };
            } catch {Invalid++;return null;}
        }
    }
    public sealed class FocusHistory
    {
        readonly List<KeyValuePair<double,bool>> changes=new List<KeyValuePair<double,bool>>();
        readonly object gate=new object();
        public void Add(double time,bool active){lock(gate){if(changes.Count==0 || changes[changes.Count-1].Value!=active)changes.Add(new KeyValuePair<double,bool>(time,active));}}
        public bool During(double start,double end){lock(gate){int i=changes.FindLastIndex(p=>p.Key<=start);if(i<0 || !changes[i].Value)return false;return !changes.Skip(i+1).Any(p=>p.Key<=end && !p.Value);}}
    }
    public sealed class Statistics
    {
        public int Count, Over33, Over50, Over100;
        public double Fps, Low1, P50, P95, P99, BudgetPercent, Seconds;
        public static Statistics From(IEnumerable<Frame> frames)
        {
            double[] a=frames.Select(f=>f.Milliseconds).OrderBy(x=>x).ToArray(); var r=new Statistics {Count=a.Length};if(a.Length==0)return r;
            r.Seconds=a.Sum()/1000; r.Fps=a.Length/r.Seconds;
            r.Low1=1000/a.Skip(a.Length-Math.Max(1,(int)Math.Ceiling(a.Length*.01))).Average();
            r.P50=Percentile(a,.50);r.P95=Percentile(a,.95);r.P99=Percentile(a,.99);
            r.Over33=a.Count(x=>x>1000.0/30);r.Over50=a.Count(x=>x>50);r.Over100=a.Count(x=>x>100);
            r.BudgetPercent=100.0*a.Count(x=>x<=1000.0/60)/a.Length;return r;
        }
        static double Percentile(double[] a,double p) { return a[Math.Max(0,(int)Math.Ceiling(a.Length*p)-1)]; }
        public string Summary() { return Count==0 ? "Нет кадров для анализа." : string.Format(CultureInfo.InvariantCulture,"Frames: {0}\r\nObserved frame time: {1:F1} s\r\nAverage present FPS: {2:F1}\r\n1% low (mean worst 1%): {3:F1}\r\np50 / p95 / p99: {4:F2} / {5:F2} / {6:F2} ms\r\nFrames within 16.67 ms: {7:F1}%\r\nIntervals >33.33 / >50 / >100 ms: {8} / {9} / {10}",Count,Seconds,Fps,Low1,P50,P95,P99,BudgetPercent,Over33,Over50,Over100); }
    }
    public sealed class FrameSession
    {
        readonly List<Frame> frames=new List<Frame>(); readonly object gate=new object();
        readonly Dictionary<string,int> chains=new Dictionary<string,int>();double lastTime;
        public bool Truncated;
        public double LastTime {get{lock(gate)return lastTime;}}
        public void Add(Frame f) {lock(gate) {if(frames.Count>=1000000) {Truncated=true;return;}frames.Add(f);lastTime=Math.Max(lastTime,f.Time);int count;chains.TryGetValue(f.Chain,out count);chains[f.Chain]=count+1;}}
        public Frame[] All() {lock(gate)return frames.ToArray();}
        public string[] Chains() {lock(gate)return chains.OrderByDescending(g=>g.Value).Select(g=>g.Key).ToArray();}
        public Frame[] Select(string chain,double start,bool activeOnly) {lock(gate)return frames.Where(f=>f.Chain==chain && f.Time>=start && (!activeOnly || f.Active)).ToArray();}
        public double Duration(string chain,double start,bool activeOnly){lock(gate){double sum=0;foreach(var f in frames)if(f.Chain==chain && f.Time>=start && (!activeOnly || f.Active))sum+=f.Milliseconds;return sum/1000;}}
        public void Clear(){lock(gate) {frames.Clear();chains.Clear();lastTime=0;Truncated=false;}}
    }
    public sealed class Capture : IDisposable
    {
        public const string Digest="b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af";
        public static string Tool {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools","PresentMon.exe");}}
        public readonly FrameSession Session=new FrameSession();
        public string Status="Ожидание RuneLite", Error="";
        public int Pid; public DateTime LastFrame=DateTime.MinValue;
        Process process;string sessionName; readonly object errorGate=new object();System.Threading.Timer focusTimer;FocusHistory focus;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
        public static bool Foreground(int pid) {uint n;GetWindowThreadProcessId(GetForegroundWindow(),out n);return n==pid;}
        public bool Running {get {try{return process!=null && !process.HasExited;}catch{return false;}}}
        public void Start(int pid)
        {
            Dispose(); if(!File.Exists(Tool))throw new InvalidOperationException("Нет tools/PresentMon.exe. Используйте полную portable-сборку.");
            if(!string.Equals(Settings.Hash(File.ReadAllBytes(Tool)),Digest,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("PresentMon не прошёл проверку SHA-256.");
            Pid=pid;Error="";Session.Clear();LastFrame=DateTime.MinValue;var parser=new CsvFrames();sessionName="RLO_"+Guid.NewGuid().ToString("N");
            focus=new FocusHistory();var history=focus;history.Add((double)Stopwatch.GetTimestamp()/Stopwatch.Frequency,Foreground(pid));
            focusTimer=new System.Threading.Timer(s=>history.Add((double)Stopwatch.GetTimestamp()/Stopwatch.Frequency,Foreground(pid)),null,100,100);
            var info=new ProcessStartInfo(Tool,"--process_id "+pid+" --session_name "+sessionName+" --output_stdout --no_console_stats --v1_metrics --qpc_time --no_track_gpu --no_track_input --no_track_display --terminate_on_proc_exit") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            process=new Process {StartInfo=info};
            process.OutputDataReceived+=(s,e)=> {if(e.Data==null)return;var f=parser.Parse(e.Data,pid,false);if(f!=null) {f.Active=history.During(f.QpcSeconds-f.Milliseconds/1000,f.QpcSeconds);Session.Add(f);LastFrame=DateTime.UtcNow;}};
            process.ErrorDataReceived+=(s,e)=> {if(e.Data==null)return;lock(errorGate) {if(Error.Length<6000)Error+=e.Data+"\r\n";}};
            try {process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();Status="Измерение · PID "+pid;}catch{Dispose();throw;}
        }
        public void Dispose()
        {
            if(focusTimer!=null){focusTimer.Dispose();focusTimer=null;}
            if(process==null)return;
            // Stop only the trace session created by this instance, never another recorder.
            try {if(!process.HasExited) {using(var stop=Process.Start(new ProcessStartInfo(Tool,"--session_name "+sessionName+" --terminate_existing_session") {UseShellExecute=false,CreateNoWindow=true})) {if(stop!=null)stop.WaitForExit(2000);}if(!process.WaitForExit(2000))process.Kill();}}catch{}
            process.Dispose();process=null;
        }
    }
    public static class TelemetryTests
    {
        static void Check(bool ok,string label){if(!ok)throw new Exception("Telemetry: "+label);}
        public static void Run()
        {
            var p=new CsvFrames();p.Parse("Application,ProcessID,SwapChainAddress,TimeInSeconds,MsBetweenPresents",123,true);
            Check(p.Parse("\"java,w.exe\",123,A,1.25,16.5",123,true).Milliseconds==16.5,"quoted CSV");
            Check(p.Parse("java,124,A,1,20",123,true)==null,"PID isolation");
            Check(p.Parse("java,123,A,1,NaN",123,true)==null,"NaN rejected");
            Check(p.Parse("java,123,A,1,0",123,true)==null,"zero rejected");
            Check(p.Parse("java,123,A,1,5000",123,true).Milliseconds==5000,"stalls retained");
            var s=Statistics.From(Enumerable.Range(0,100).Select(i=>new Frame {Milliseconds=i==99?100:10}));
            Check(Math.Abs(s.Fps-100000.0/1090)<.001 && s.Low1==10 && s.P99==10 && s.Over50==1,"time-weighted FPS, percentile and 1% low");
            var session=new FrameSession();session.Add(new Frame {Chain="A",Active=true,Time=1});session.Add(new Frame {Chain="B",Active=true,Time=2});session.Add(new Frame {Chain="A",Active=false,Time=3});
            Check(session.Select("A",0,true).Length==1 && session.Select("A",2,false).Length==1,"focus, chain and checkpoint isolation");
            foreach(bool hd in new[]{false,true})foreach(bool uncapped in new[]{false,true}) {var v=OptimizationProfiles.Make(hd,uncapped);var data=Settings.Patch(Encoding.UTF8.GetBytes("# untouched\nosrspath.enabled=true\n"),v);Check(Encoding.UTF8.GetString(data).Contains("osrspath.enabled=true"),"preserve plugin");Check(Settings.Read(data)[(hd?"hd.":"gpu.")+"fpsTarget"]==(uncapped?"999":"60"),"renderer FPS profile");}
            Check(!OptimizationProfiles.Valid("hd.shadowMode","INVALID"),"invalid enum rejected");
            Check(CsvFrames.Split("\"a\"\"b\",c")[0]=="a\"b","CSV escaped quotes");
            Check(Statistics.From(new Frame[0]).Count==0,"empty is unknown");
            var history=new FocusHistory();history.Add(0,true);history.Add(2,false);history.Add(3,true);
            Check(history.During(.1,1) && !history.During(1,4) && history.During(3.1,4),"focus at frame time, including intervals crossing background");
            p=new CsvFrames();p.Parse("Application,ProcessID,SwapChainAddress,TimeInSeconds,msBetweenPresents,QPCTime",123,false);
            Check(p.Parse("java,123,A,1,16,100000",123,false).QpcSeconds>0,"legacy metric casing and QPC");
            var hdValues=new Dictionary<string,string>();foreach(var choice in HdRules.Choices)hdValues[choice.Key]=choice.Value[0];foreach(var range in HdRules.Ranges)hdValues[range.Key]=range.Value[0].ToString(CultureInfo.InvariantCulture);
            Check(hdValues.Count==100 && Settings.Read(Settings.Patch(Encoding.UTF8.GetBytes("other=keep\n"),hdValues)).Count==100,"all visible HD settings patch roundtrip");
            Check(!HdRules.Valid("hd.shadowMode","FAKE") && !HdRules.Valid("hd.drawDistance","185") && !HdRules.Valid("hd.drawDistance","-1") && !HdRules.Valid("hd.unlockFps","True"),"enum, range and boolean boundaries");
            File.WriteAllText("telemetry-test-result.txt","PASS: telemetry parser, exact metrics, isolation, stall retention and four renderer profiles. Synthetic fixtures only.");
        }
    }
}
