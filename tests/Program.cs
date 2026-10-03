using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using AffineIntegrator;
internal static class Program
{
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibrary(string p);
    [DllImport("kernel32", CharSet=CharSet.Ansi, SetLastError=true)] static extern IntPtr GetProcAddress(IntPtr h,string n);
    [DllImport("kernel32", SetLastError=true)] static extern bool FreeLibrary(IntPtr h);
    static int Main(string[] a)
    {
        if(a.Length==4 && a[0]=="--inspect")
        {
            var original=new Pe(File.ReadAllBytes(a[1]));
            var merged=new Pe(File.ReadAllBytes(a[2]),false);
            var aExports=original.Exports(); var bExports=merged.Exports();
            if(aExports.Count!=bExports.Count || aExports.Any(kv=>!bExports.TryGetValue(kv.Key,out uint value)||value!=kv.Value))
                throw new Exception("Original export table changed");
            for(int i=0;i<original.Count;i++)
            {
                var left=original.List[i]; var right=merged.List[i];
                if(left.Va!=right.Va||left.Raw!=right.Raw||left.RawSize!=right.RawSize)
                    throw new Exception("Original section moved: "+left.Name);
                for(int k=0;k<left.RawSize;k++) if(original.B[(int)left.Raw+k]!=merged.B[(int)right.Raw+k])
                    throw new Exception("Original section bytes changed: "+left.Name);
            }
            foreach(string name in new[]{"mai2_io_init","mai2_io_led_init","mai2_io_led_gs_update","mai2_io_led_set_fet_output"})
                if(!bExports.ContainsKey(name)) throw new Exception("Missing required export "+name);
            var meta=merged.List.Single(s=>s.Name==".affmeta");
            int payload=(int)meta.Raw;
            if(!merged.B.AsSpan(payload,(int)meta.VirtualSize).SequenceEqual(File.ReadAllBytes(a[3])))
                throw new Exception("Embedded managed image changed");
            Console.WriteLine($"PASS {aExports.Count} original exports, {original.Count} original sections, managed payload {meta.VirtualSize} bytes");
            return 0;
        }
        string p=Path.GetFullPath(a[0]); Console.WriteLine("Loading "+p);
        IntPtr h=LoadLibrary(p); Console.WriteLine("Handle="+h+" error="+Marshal.GetLastWin32Error());
        if(h==IntPtr.Zero)return 1;
        Console.WriteLine("GS="+GetProcAddress(h,"mai2_io_led_gs_update"));
        if(a.Length>1 && a[1]=="--wait") System.Threading.Thread.Sleep(1000);
        FreeLibrary(h); return 0;
    }
}
