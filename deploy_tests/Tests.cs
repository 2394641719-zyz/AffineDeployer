using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using AffineIntegrator;
namespace AffineDeploymentTests {
    internal static class Tests {
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
        static int Main(string[] args){
            string source=Path.GetFullPath(args[0]),root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
            string originalBat="@echo off\r\n::中文备注\r\nstart /min inject -d -k mai2hook.dll amdaemon.exe -f -c config_common.json config_server.json config_client.json\r\nsinmai.exe -screen-width 4320 -screen-height 3840\r\ntaskkill /f /im amdaemon.exe\r\n";
            string patched=Deployment.PatchBat(originalBat);
            Check(patched.Contains("inject -d -k mai2hook.dll sinmai.exe -screen-width 4320 -screen-height 3840"),"game arguments preserved and hook injected");
            Check(patched.Contains("timeout /t 3 /nobreak >nul"),"daemon startup delay inserted");
            Check(Deployment.PatchBat(patched)==patched,"batch patch idempotent");
            string ini="[dns]\r\npath=keep.dll\r\n[mai2io]\r\n;path=disabled.dll\r\n path = old.dll\r\n[io4]\r\nenable=0\r\n";
            string changed=Deployment.PatchSection(ini,"mai2io",new Dictionary<string,string>{{"path","affine_io_single.dll"}});
            Check(changed.Contains("[dns]\r\npath=keep.dll")&&changed.Contains(";path=disabled.dll")&&changed.Contains("path=affine_io_single.dll"),"only active target-section path modified");
            try{Deployment.PatchSection("[mai2io]\npath=a\n[mai2io]\npath=b","mai2io",new Dictionary<string,string>{{"path","x"}});throw new Exception("duplicate accepted");}catch(InvalidDataException){Console.WriteLine("PASS duplicate sections rejected");}
            foreach(string f in new[]{"Sinmai.exe","amdaemon.exe","inject.exe","mai2hook.dll","Mods/AquaMai.dll","MelonLoader/net35/MelonLoader.dll","Mods/AffineHostProbe.dll"}){string p=Path.Combine(root,f);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllBytes(p,new byte[]{1,2,3});}
            File.WriteAllText(Path.Combine(root,"segatools.ini"),ini,new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(root,"start.bat"),originalBat,Encoding.GetEncoding(936));
            File.WriteAllText(Path.Combine(root,"AquaMai.toml"),"#中文备注\r\n[GameSystem.VirtualCoin]\r\nCoinKey = \"F3\"\r\nIsUseRemote = true\r\n[Other]\r\nValue = 7\r\n",new UnicodeEncoding(false,true));
            var files=new[]{"segatools.ini","start.bat","AquaMai.toml","mai2hook.dll","Mods/AffineHostProbe.dll"};
            var originals=files.ToDictionary(f=>f,f=>File.ReadAllBytes(Path.Combine(root,f)));
            Console.WriteLine(Deployment.Install(source,root));
            Check(File.Exists(Path.Combine(root,"affine_io_single.dll"))&&!File.Exists(Path.Combine(root,"Mods/AffineHostProbe.dll")),"merged DLL deployed and old plugin disabled");
            Check(File.ReadAllText(Path.Combine(root,"segatools.ini")).Contains("path=affine_io_single.dll"),"Segatools configured");
            string aqua=File.ReadAllText(Path.Combine(root,"AquaMai.toml"));
            Check(aqua.Contains("CoinKey=\"None\"")&&aqua.Contains("IsUseRemote=false")&&aqua.Contains("[Other]\r\nValue = 7"),"existing AquaMai section updated without changing other settings");
            Check(File.ReadAllText(Path.Combine(root,"start.bat"),Encoding.GetEncoding(936)).Contains("::中文备注"),"GBK batch encoding preserved");
            Check(File.ReadAllBytes(Path.Combine(root,"AquaMai.toml"))[0]==255&&File.ReadAllBytes(Path.Combine(root,"segatools.ini"))[0]==239,"UTF16 and UTF8 BOM preserved");
            Check(File.ReadAllBytes(Path.Combine(root,"mai2hook.dll")).SequenceEqual(originals["mai2hook.dll"]),"original hook remains byte-identical");
            byte[] deployed=File.ReadAllBytes(Path.Combine(root,"affine_io_single.dll"));
            Console.WriteLine(Deployment.Install(source,root));Console.WriteLine(Deployment.Restore(root));
            Check(File.ReadAllBytes(Path.Combine(root,"affine_io_single.dll")).SequenceEqual(deployed),"updating and restoring returns previous integrated version");
            Console.WriteLine(Deployment.Restore(root));
            Check(files.All(f=>File.ReadAllBytes(Path.Combine(root,f)).SequenceEqual(originals[f]))&&!File.Exists(Path.Combine(root,"affine_io_single.dll")),"restore recovers exact originals including old plugin");
            File.WriteAllText(Path.Combine(root,"start.bat"),"unrecognized launcher");byte[] before=File.ReadAllBytes(Path.Combine(root,"segatools.ini"));
            try{Deployment.Install(source,root);throw new Exception("unsupported accepted");}catch(InvalidDataException){Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"segatools.ini")))&&!File.Exists(Path.Combine(root,"affine_io_single.dll")),"unsupported startup script rejected before writes");}
            return 0;
        }
    }
}
