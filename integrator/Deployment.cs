using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AffineIntegrator {
    internal static class Deployment {
        internal static readonly string[] LegacyMods = { "Mods/AffineHostProbe.dll", "Mods/AffineGameLights.dll" };
        private static readonly string[] Allowed = { "affine_io_single.dll", "segatools.ini", "AquaMai.toml", "start.bat", "Mods/AffineHostProbe.dll", "Mods/AffineGameLights.dll" };
        private const string BackupRoot = "AffineDeployBackups";
        private sealed class TextFile {
            internal string Text; internal Encoding Encoding; internal bool Bom;
            internal TextFile(string path) {
                byte[] b = File.ReadAllBytes(path); int skip = 0;
                if(b.Length >= 3 && b[0]==239 && b[1]==187 && b[2]==191) { Encoding=new UTF8Encoding(true,true); skip=3; Bom=true; }
                else if(b.Length >= 2 && b[0]==255 && b[1]==254) { Encoding=new UnicodeEncoding(false,true,true);skip=2;Bom=true; }
                else if(b.Length >= 2 && b[0]==254 && b[1]==255) { Encoding=new UnicodeEncoding(true,true,true);skip=2;Bom=true; }
                else { try { new UTF8Encoding(false,true).GetString(b); Encoding=new UTF8Encoding(false,true); } catch(DecoderFallbackException) { Encoding=System.Text.Encoding.Default; } }
                Text=Encoding.GetString(b,skip,b.Length-skip);
            }
            internal byte[] Bytes(string text) { var bytes=Encoding.GetBytes(text); return Bom ? Encoding.GetPreamble().Concat(bytes).ToArray() : bytes; }
        }
        internal static string PatchSection(string text, string section, IDictionary<string,string> values) {
            string newline=text.Contains("\r\n") ? "\r\n" : "\n";
            var lines=text.Replace("\r\n","\n").Split('\n').ToList();
            var starts=Enumerable.Range(0,lines.Count).Where(i=>Regex.IsMatch(lines[i],@"^\s*\["+Regex.Escape(section)+@"\]\s*(?:[;#].*)?$",RegexOptions.IgnoreCase)).ToList();
            if(starts.Count>1) throw new InvalidDataException("配置存在重复段："+section);
            if(starts.Count==0) { if(lines.Count>0 && lines.Last()!="") lines.Add(""); lines.Add("["+section+"]");foreach(var pair in values)lines.Add(pair.Key+"="+pair.Value);lines.Add("");return string.Join(newline,lines); }
            int start=starts[0],end=start+1;while(end<lines.Count&&!Regex.IsMatch(lines[end],@"^\s*\["))end++;
            foreach(var pair in values) {
                var indexes=Enumerable.Range(start+1,end-start-1).Where(i=>Regex.IsMatch(lines[i],@"^\s*"+Regex.Escape(pair.Key)+@"\s*=",RegexOptions.IgnoreCase)).ToList();
                if(indexes.Count>1)throw new InvalidDataException("配置键重复："+section+"."+pair.Key);
                if(indexes.Count==1)lines[indexes[0]]=pair.Key+"="+pair.Value;
                else {lines.Insert(end,pair.Key+"="+pair.Value);end++;}
            }
            return string.Join(newline,lines);
        }
        internal static string PatchBat(string text) {
            string nl=text.Contains("\r\n")?"\r\n":"\n";
            var lines=text.Replace("\r\n","\n").Split('\n').ToList();
            var game=new List<int>();var daemon=new List<int>();
            for(int i=0;i<lines.Count;i++) {
                string line=lines[i].TrimStart();
                if(line.StartsWith("::")||Regex.IsMatch(line,@"^@?rem\b",RegexOptions.IgnoreCase)||Regex.IsMatch(line,@"\btaskkill\b",RegexOptions.IgnoreCase))continue;
                if(Regex.IsMatch(line,@"(?:^|\s|"")sinmai\.exe(?:""|\s|$)",RegexOptions.IgnoreCase))game.Add(i);
                if(Regex.IsMatch(line,@"(?:^|\s|"")amdaemon\.exe(?:""|\s|$)",RegexOptions.IgnoreCase)&&!Regex.IsMatch(line,@"\btaskkill\b",RegexOptions.IgnoreCase))daemon.Add(i);
            }
            if(game.Count!=1||daemon.Count!=1)throw new InvalidDataException("start.bat 必须包含一条 Sinmai 和一条 AMDaemon 启动命令。复杂脚本请先整理后部署。");
            foreach(int i in new[]{game[0],daemon[0]}) {
                string exe=i==game[0]?"sinmai.exe":"amdaemon.exe";
                string line=lines[i];
                if(Regex.IsMatch(line,@"\binject(?:\.exe)?\s",RegexOptions.IgnoreCase)) {
                    if(!Regex.IsMatch(line,@"\binject(?:\.exe)?\s+-d\s+-k\s+(?:"")?mai2hook\.dll(?:"")?\s+",RegexOptions.IgnoreCase))
                        throw new InvalidDataException("启动器参数与已验证的 mai2hook 注入方式不同，无法自动修改："+exe);
                } else {
                    // Only the executable token is replaced; all original arguments are preserved.
                    line=Regex.Replace(line,@"(?<![\w\\/:])(?:"")?"+Regex.Escape(exe)+@"(?:"")?(?=\s|$)","inject -d -k mai2hook.dll "+exe,RegexOptions.IgnoreCase);
                    if(!line.Contains("inject -d -k mai2hook.dll "))throw new InvalidDataException("不支持启动命令中的绝对路径："+exe);
                }
                lines[i]=line;
            }
            int di=daemon[0];
            if(game[0]<=di)throw new InvalidDataException("start.bat 应先启动 AMDaemon，再启动 Sinmai。");
            if(!lines.Skip(di+1).Take(game[0]-di-1).Any(l=>Regex.IsMatch(l,@"^\s*@?timeout(?:\.exe)?\s+/t\s+[3-9]\d*\b",RegexOptions.IgnoreCase)))
                lines.Insert(di+1,"timeout /t 3 /nobreak >nul");
            return string.Join(nl,lines);
        }
        private static void EnsureClosed(string root) {
            foreach(string name in new[]{"Sinmai","amdaemon"})foreach(var p in Process.GetProcessesByName(name)) { using(p) { throw new InvalidOperationException("请先退出游戏和 AMDaemon，再部署或恢复。"); } }
        }
        private static string GameRoot(string path) {
            string root=Path.GetFullPath(path);
            foreach(string file in new[]{"Sinmai.exe","amdaemon.exe","inject.exe","mai2hook.dll","segatools.ini","AquaMai.toml","start.bat","Mods/AquaMai.dll","MelonLoader/net35/MelonLoader.dll"})
                if(!File.Exists(Path.Combine(root,file)))throw new FileNotFoundException("游戏目录缺少必需文件："+file);
            return root;
        }
        internal static string Install(string source,string directory) {
            string root=GameRoot(directory); EnsureClosed(root);
            if(string.Equals(Path.GetFullPath(source),Path.Combine(root,"affine_io_single.dll"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请选择另外保存的原版 affine_io.dll；部署位置不能覆盖输入文件。");
            var ini=new TextFile(Path.Combine(root,"segatools.ini"));var aqua=new TextFile(Path.Combine(root,"AquaMai.toml"));var bat=new TextFile(Path.Combine(root,"start.bat"));
            string ip=PatchSection(ini.Text,"mai2io",new Dictionary<string,string>{{"path","affine_io_single.dll"}});
            ip=PatchSection(ip,"io4",new Dictionary<string,string>{{"enable","1"}});
            ip=PatchSection(ip,"button",new Dictionary<string,string>{{"enable","0"}});
            string ap=PatchSection(aqua.Text,"GameSystem.VirtualCoin",new Dictionary<string,string>{{"CoinKey","\"None\""},{"IsUseRemote","false"},{"IsPlaySound","false"}});
            string bp=PatchBat(bat.Text);
            string temp=Path.Combine(Path.GetTempPath(),"AffineDeploy-"+Guid.NewGuid().ToString("N")+".dll");
            byte[] merged;
            try { Program.Build(source,temp);merged=File.ReadAllBytes(temp); } finally { if(File.Exists(temp))File.Delete(temp); }
            var writes=new Dictionary<string,byte[]> { {"affine_io_single.dll",merged},{"segatools.ini",ini.Bytes(ip)},{"AquaMai.toml",aqua.Bytes(ap)},{"start.bat",bat.Bytes(bp)} };
            string backup=Path.Combine(root,BackupRoot,DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
            Directory.CreateDirectory(backup);
            var manifest=new List<string>();
            foreach(string file in Allowed) {
                string target=Path.Combine(root,file);bool exists=File.Exists(target);manifest.Add((exists?"1|":"0|")+file);
                if(exists){string dest=Path.Combine(backup,file);Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(target,dest);}
            }
            File.WriteAllLines(Path.Combine(backup,"manifest.txt"),manifest,Encoding.UTF8);
            EnsureClosed(root);
            try {
                foreach(var pair in writes)File.WriteAllBytes(Path.Combine(root,pair.Key),pair.Value);
                foreach(string file in LegacyMods)if(File.Exists(Path.Combine(root,file)))File.Delete(Path.Combine(root,file));
                File.WriteAllText(Path.Combine(backup,"completed.txt"),"Affine deployed "+DateTime.Now.ToString("O"),Encoding.UTF8);
            } catch { RestoreFiles(root,backup);throw; }
            return "部署完成："+root+Environment.NewLine+"已部署单 DLL，修改 start.bat、segatools.ini 和 AquaMai.toml。旧 Affine Mods 插件已移入备份。"+Environment.NewLine+"备份："+backup+Environment.NewLine+"DLL SHA256："+PeMerge.Hash(merged)+Environment.NewLine+"使用原 start.bat 启动。点数重启清零；更新后请复测投币、扣点和灯光。";
        }
        private static void RestoreFiles(string root,string backup) {
            var entries=File.ReadAllLines(Path.Combine(backup,"manifest.txt"));
            if(entries.Length!=Allowed.Length)throw new InvalidDataException("备份清单不完整。");
            foreach(string file in Allowed) {
                string entry=entries.SingleOrDefault(e=>e=="1|"+file||e=="0|"+file);
                if(entry==null || entry.StartsWith("1|")&&!File.Exists(Path.Combine(backup,file)))throw new InvalidDataException("备份文件缺失："+file);
            }
            foreach(string file in Allowed) {
                string target=Path.Combine(root,file);
                if(entries.Contains("1|"+file)){Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(Path.Combine(backup,file),target,true);}
                else if(File.Exists(target))File.Delete(target);
            }
        }
        internal static string Restore(string directory) {
            string root=GameRoot(directory);EnsureClosed(root);
            string backups=Path.Combine(root,BackupRoot);
            if(!Directory.Exists(backups))throw new InvalidOperationException("该游戏目录没有部署器备份。");
            string backup=Directory.GetDirectories(backups).Where(d=>File.Exists(Path.Combine(d,"completed.txt"))&&!File.Exists(Path.Combine(d,"restored.txt"))).OrderByDescending(d=>Path.GetFileName(d),StringComparer.Ordinal).FirstOrDefault();
            if(backup==null)throw new InvalidOperationException("没有可恢复的部署备份。");
            RestoreFiles(root,backup);File.WriteAllText(Path.Combine(backup,"restored.txt"),DateTime.Now.ToString("O"));
            return "已恢复部署前文件："+backup;
        }
    }
}
