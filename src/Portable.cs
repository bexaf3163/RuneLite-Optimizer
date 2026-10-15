using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

[assembly: AssemblyVersion(RuneLiteOptimizer.Portable.Version+".0")]
[assembly: AssemblyFileVersion(RuneLiteOptimizer.Portable.Version+".0")]

namespace RuneLiteOptimizer
{
    public static class Portable
    {
        public const string Version="0.4.0";
        public static byte[] Resource(string name)
        {
            using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("bundle."+name))
            {if(source==null)throw new InvalidOperationException("Отсутствует встроенный компонент: "+name);using(var buffer=new MemoryStream()){source.CopyTo(buffer);return buffer.ToArray();}}
        }
        public static string Text(string name){return Encoding.UTF8.GetString(Resource(name)).TrimStart('\ufeff');}
        public static string RuntimeFolder {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RuneLiteOptimizer","Runtime");}}
        public static string CapturePath {get{return Path.Combine(RuntimeFolder,"PresentMon-2.6.0-"+Capture.Digest.Substring(0,12)+".exe");}}
        public static string EnsureCapture(){return ExtractCapture(RuntimeFolder);}
        static string ExtractCapture(string folder)
        {
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,Path.GetFileName(CapturePath));
            if(File.Exists(path) && string.Equals(Settings.Hash(File.ReadAllBytes(path)),Capture.Digest,StringComparison.OrdinalIgnoreCase))return path;
            byte[] bytes=Resource("PresentMon.exe");if(!string.Equals(Settings.Hash(bytes),Capture.Digest,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Встроенный измеритель не прошёл SHA-256.");
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllBytes(temporary,bytes);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
            return path;
        }
        public static void Tests()
        {
            if(!string.Equals(Settings.Hash(Resource("PresentMon.exe")),Capture.Digest,StringComparison.OrdinalIgnoreCase))throw new Exception("Embedded tool hash");
            if(Text("hd-settings.tsv").Split('\n').Length<101)throw new Exception("Embedded HD catalogue");
            if(!Text("THIRD-PARTY-NOTICES.md").Contains("Intel Corporation"))throw new Exception("Missing embedded license");
            string fixture=Path.Combine(Environment.CurrentDirectory,"portable-fixtures-"+Guid.NewGuid().ToString("N"));string extracted=ExtractCapture(fixture);
            if(!string.Equals(Settings.Hash(File.ReadAllBytes(extracted)),Capture.Digest,StringComparison.OrdinalIgnoreCase))throw new Exception("Extracted resource hash");
            File.WriteAllText(extracted,"invalid cached file");ExtractCapture(fixture);
            if(!string.Equals(Settings.Hash(File.ReadAllBytes(extracted)),Capture.Digest,StringComparison.OrdinalIgnoreCase))throw new Exception("Damaged resource cache repair");
        }
    }
}
