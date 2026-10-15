using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RuneLiteOptimizer
{
    public sealed class UpdateInfo { public string Version,Url,Digest;public long Size; }
    public sealed class UpdatePreferences { public bool Automatic=true; }
    public sealed class Updates
    {
        [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
        public const string Repository="bexaf3163/RuneLite-Optimizer";
        public const string Asset="RuneLite-Optimizer.exe";
        public string Status="Проверка обновлений…",Candidate="",Digest="",AvailableVersion="";
        public bool Busy,Ready;
        public static string Folder {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RuneLiteOptimizer","Updates");}}
        public static bool Automatic()
        {
            try {return new JavaScriptSerializer().Deserialize<UpdatePreferences>(File.ReadAllText(Path.Combine(Folder,"preferences.json"))).Automatic;}catch{return true;}
        }
        public static void SavePreference(bool automatic){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"preferences.json"),new JavaScriptSerializer().Serialize(new UpdatePreferences {Automatic=automatic}));}
        static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="RuneLite-Optimizer/"+Portable.Version;request.Accept="application/vnd.github+json";request.Timeout=15000;request.ReadWriteTimeout=15000;request.AllowAutoRedirect=true;return request;
        }
        public static UpdateInfo Parse(string json,string current)
        {
            var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
            if((bool)root["draft"] || (bool)root["prerelease"])return null;
            string tag=(string)root["tag_name"];if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$"))throw new InvalidOperationException("Неподдерживаемая версия обновления.");
            if(new Version(tag.Substring(1))<=new Version(current))return null;
            foreach(var item in (IEnumerable)root["assets"])
            {
                var asset=(Dictionary<string,object>)item;if((string)asset["name"]!=Asset)continue;
                string url=(string)asset["browser_download_url"],digest=Convert.ToString(asset.ContainsKey("digest")?asset["digest"]:null);
                string expected="https://github.com/"+Repository+"/releases/download/"+tag+"/"+Asset;
                if(url!=expected || !Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))throw new InvalidOperationException("Неверный источник или отсутствует SHA-256 обновления.");
                long size=Convert.ToInt64(asset["size"]);if(size<1024 || size>64*1024*1024)throw new InvalidOperationException("Неподдерживаемый размер обновления.");
                return new UpdateInfo {Version=tag.Substring(1),Url=url,Digest=digest.Substring(7),Size=size};
            }
            throw new InvalidOperationException("В выпуске нет portable EXE. Текущая версия продолжит работать.");
        }
        public async Task Check(string currentVersion=null)
        {
            if(Busy)return;Busy=true;
            try
            {
                await Task.Run(()=>{
                    Status="Проверка обновлений…";
                    string json;using(var response=Request("https://api.github.com/repos/"+Repository+"/releases/latest").GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))json=reader.ReadToEnd();
                    var info=Parse(json,currentVersion??Portable.Version);if(info==null){Status="Установлена последняя версия";return;}
                    Status="Загрузка версии "+info.Version+"…";string stage=Path.Combine(Folder,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);string candidate=Path.Combine(stage,"candidate.exe");
                    try
                    {
                        using(var response=Request(info.Url).GetResponse())using(var source=response.GetResponseStream())using(var target=new FileStream(candidate,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                        {var bytes=new byte[65536];int n;long count=0;while((n=source.Read(bytes,0,bytes.Length))>0){count+=n;if(count>info.Size)throw new InvalidOperationException("Размер загрузки отличается от выпуска.");target.Write(bytes,0,n);}if(count!=info.Size)throw new InvalidOperationException("Загрузка не завершена.");}
                        VerifyCandidate(candidate,info.Digest);if(FileVersionInfo.GetVersionInfo(candidate).FileVersion!=info.Version+".0")throw new InvalidOperationException("Версия EXE отличается от выпуска.");Candidate=candidate;Digest=info.Digest;AvailableVersion=info.Version;Ready=true;Status="Версия "+info.Version+" готова к установке";
                    }
                    catch {if(File.Exists(candidate))File.Delete(candidate);throw;}
                });
            }
            catch(Exception ex){Status="Не удалось проверить обновление: "+ex.Message;}
            finally{Busy=false;}
        }
        public static void VerifyCandidate(string path,string digest)
        {
            if(!Regex.IsMatch(digest??"",@"^[a-fA-F0-9]{64}$"))throw new InvalidOperationException("Отсутствует SHA-256.");
            byte[] bytes=File.ReadAllBytes(path);
            if(bytes.Length<1024 || bytes[0]!='M' || bytes[1]!='Z' || !string.Equals(Settings.Hash(bytes),digest,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Обновление не прошло проверку целостности.");
        }
        static string Encode(string value){return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));}
        static string Decode(string value){return Encoding.UTF8.GetString(Convert.FromBase64String(value));}
        public void Install()
        {
            if(!Ready)throw new InvalidOperationException("Обновление ещё не загружено.");VerifyCandidate(Candidate,Digest);
            string current=Process.GetCurrentProcess().MainModule.FileName,helper=Path.Combine(Path.GetDirectoryName(Candidate),"Updater-"+Guid.NewGuid().ToString("N")+".exe");
            File.Copy(current,helper,false);
            var process=Process.GetCurrentProcess();string arguments="--apply-update "+process.Id+" "+process.StartTime.ToUniversalTime().Ticks+" "+Encode(current)+" "+Encode(Candidate)+" "+Digest;
            Process.Start(new ProcessStartInfo(helper,arguments) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(helper)});
        }
        public static void ReplaceVerified(string target,string candidate,string digest,string expectedOriginal)
        {
            VerifyCandidate(candidate,digest);
            if(!string.Equals(Settings.Hash(File.ReadAllBytes(target)),expectedOriginal,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Текущий EXE изменился; обновление отменено.");
            string temporary=target+".update-"+Guid.NewGuid().ToString("N")+".tmp";
            try {File.Copy(candidate,temporary,false);VerifyCandidate(temporary,digest);if(Settings.Hash(File.ReadAllBytes(target))!=expectedOriginal)throw new InvalidOperationException("EXE изменился перед заменой.");File.Replace(temporary,target,null);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public static int Apply(string[] args,bool test=false)
        {
            SetErrorMode(0x8003); // Loader failures must return errors, not leave an invisible system dialog.
            string target=null;bool replaced=false;
            try
            {
                if(args.Length!=6)throw new InvalidOperationException("Неверные параметры обновления.");
                int pid=int.Parse(args[1]);long ticks=long.Parse(args[2]);target=Path.GetFullPath(Decode(args[3]));string candidate=Path.GetFullPath(Decode(args[4]));
                string helper=Process.GetCurrentProcess().MainModule.FileName,stage=Path.GetDirectoryName(helper);
                if((!test && !stage.StartsWith(Path.GetFullPath(Folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) || (test && !string.Equals(Path.GetDirectoryName(target),stage,StringComparison.OrdinalIgnoreCase)) || !string.Equals(Path.GetDirectoryName(candidate),stage,StringComparison.OrdinalIgnoreCase) || Path.GetFileName(candidate)!="candidate.exe" || !target.EndsWith(".exe",StringComparison.OrdinalIgnoreCase) || string.Equals(target,helper,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Неверный путь обновления.");
                string originalHash=Settings.Hash(File.ReadAllBytes(helper));VerifyCandidate(candidate,args[5]);
                try {using(var parent=Process.GetProcessById(pid)){if(parent.StartTime.ToUniversalTime().Ticks!=ticks)throw new InvalidOperationException("Процесс был заменён.");if(!parent.WaitForExit(30000))throw new InvalidOperationException("Оптимизатор ещё работает. Обновление отменено.");}}catch(ArgumentException){}
                ReplaceVerified(target,candidate,args[5],originalHash);replaced=true;
                try {if(test){using(var child=Process.Start(new ProcessStartInfo(target,"--self-test") {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage})){if(!child.WaitForExit(20000)){child.Kill();throw new Exception("Updated fixture timed out");}if(child.ExitCode!=0)throw new Exception("Updated fixture self-test failed");}}else Process.Start(new ProcessStartInfo(target) {UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(target)});}
                catch {ReplaceVerified(target,helper,originalHash,args[5]);replaced=false;throw;}
                try{File.Delete(candidate);}catch(IOException){}return 0;
            }
            catch(Exception ex){if(test)File.WriteAllText("update-helper-failure.txt",ex.ToString());else MessageBox.Show("Обновление не установлено. "+(replaced?"Новый EXE сохранён.":"Текущий EXE сохранён.")+"\n"+ex.Message,"RuneLite Optimizer",MessageBoxButtons.OK,MessageBoxIcon.Information);return 1;}
        }
        public static void IntegrationTest()
        {
            string stage=Path.Combine(Environment.CurrentDirectory,"update-integration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            string original=Process.GetCurrentProcess().MainModule.FileName,helper=Path.Combine(stage,"Updater.exe"),target=Path.Combine(stage,"Portable fixture.exe"),candidate=Path.Combine(stage,"candidate.exe");
            File.Copy(original,helper);File.Copy(original,target);byte[] bytes=File.ReadAllBytes(original);Array.Resize(ref bytes,bytes.Length+1);File.WriteAllBytes(candidate,bytes);string hash=Settings.Hash(bytes);
            Action<string,bool> run=(candidateHash,success)=>
            {
                using(var parent=Process.Start(new ProcessStartInfo(original,"--update-wait-fixture") {UseShellExecute=false,CreateNoWindow=true}))
                {
                    string args="--apply-update-test "+parent.Id+" "+parent.StartTime.ToUniversalTime().Ticks+" "+Encode(target)+" "+Encode(candidate)+" "+candidateHash;
                    using(var updater=Process.Start(new ProcessStartInfo(helper,args) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage}))
                    {if(!updater.WaitForExit(25000)){updater.Kill();throw new Exception("Updater helper timed out");}if((updater.ExitCode==0)!=success)throw new Exception("Updater helper failed: "+stage);}
                }
            };
            run(hash,true);
            if(Settings.Hash(File.ReadAllBytes(target))!=hash || !File.ReadAllText(Path.Combine(stage,"self-test-result.txt")).StartsWith("PASS"))throw new Exception("Replacement/restart not verified");
            File.Copy(original,target,true);var invalid=new byte[2048];invalid[0]=(byte)'M';invalid[1]=(byte)'Z';File.WriteAllBytes(candidate,invalid);run(Settings.Hash(invalid),false);
            if(Settings.Hash(File.ReadAllBytes(target))!=Settings.Hash(File.ReadAllBytes(original)))throw new Exception("Failed launch rollback not verified");
            File.WriteAllText("update-integration-result.txt","PASS: helper parent wait, atomic EXE replacement, restart/self-tests and rollback after launch failure. Isolated application copies; installed EXE untouched.");
        }
        public static void Tests()
        {
            string digest=new string('a',64);string json="{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v9.0.0\",\"assets\":[{\"name\":\"RuneLite-Optimizer.exe\",\"browser_download_url\":\"https://github.com/"+Repository+"/releases/download/v9.0.0/RuneLite-Optimizer.exe\",\"digest\":\"sha256:"+digest+"\",\"size\":2048}]}";
            if(Parse(json,"0.3.0").Version!="9.0.0" || Parse(json,"9.0.0")!=null || Parse(json.Replace("\"prerelease\":false","\"prerelease\":true"),"0.3.0")!=null)throw new Exception("Update version gates");
            bool rejected=false;try{Parse(json.Replace("https://github.com/","https://example.com/"),"0.3.0");}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Update source gate");
            rejected=false;try{Parse(json.Replace("sha256:","bad:"),"0.3.0");}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Update missing digest gate");
            string folder=Path.Combine(Environment.CurrentDirectory,"update-fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string target=Path.Combine(folder,"app.exe"),candidate=Path.Combine(folder,"candidate.exe");
            byte[] old=new byte[2048],next=new byte[3072];old[0]=next[0]=(byte)'M';old[1]=next[1]=(byte)'Z';File.WriteAllBytes(target,old);File.WriteAllBytes(candidate,next);
            string oldHash=Settings.Hash(old),nextHash=Settings.Hash(next);ReplaceVerified(target,candidate,nextHash,oldHash);if(Settings.Hash(File.ReadAllBytes(target))!=nextHash)throw new Exception("Atomic update fixture");
            rejected=false;try{ReplaceVerified(target,candidate,nextHash,oldHash);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Update conflict gate");
            rejected=false;try{VerifyCandidate(candidate,oldHash);}catch(InvalidOperationException){rejected=true;}if(!rejected || Settings.Hash(File.ReadAllBytes(target))!=nextHash)throw new Exception("Update tamper gate");
            Portable.Tests();File.WriteAllText("update-test-result.txt","PASS: embedded resources, version/source/digest gates, atomic replacement and conflicts. Isolated fixtures; no installed EXE modified.");
        }
    }
}
