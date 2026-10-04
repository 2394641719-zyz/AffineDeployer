using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace AffineIntegrator
{
    internal static class Program
    {
        private static byte[] Resource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AffineIntegrator." + name))
            {
                if (stream == null) throw new InvalidDataException("整合程序缺少内置模板：" + name);
                using (var memory = new MemoryStream()) { stream.CopyTo(memory); return memory.ToArray(); }
            }
        }
        internal static string Build(string source, string output)
        {
            string a = Path.GetFullPath(source), b = Path.GetFullPath(output);
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("输出位置不能覆盖输入 DLL。");
            if (!File.Exists(a)) throw new FileNotFoundException("找不到新版 affine_io.dll。", a);
            if (File.Exists(b)) throw new IOException("输出文件已存在，请换一个文件名以保留旧版本：" + b);
            byte[] original = File.ReadAllBytes(a);
            byte[] result = PeMerge.Merge(original, Resource("bootstrap.dll"), Resource("managed.dll"));
            Directory.CreateDirectory(Path.GetDirectoryName(b));
            File.WriteAllBytes(b, result);
            return "已生成单 DLL：" + b + Environment.NewLine
                + "输入 SHA256：" + PeMerge.Hash(original) + Environment.NewLine
                + "输出 SHA256：" + PeMerge.Hash(result) + Environment.NewLine
                + "原生导出接口保留；仅内嵌实体投币桥接，灯光由现有 hook 处理。投币桥接需启用 AquaMai VirtualCoin，CoinKey=None、IsUseRemote=false；点数重启清零。" + Environment.NewLine
                + "接口名称检查不能保证任意新版 affine_io 的参数与设备协议兼容。";
        }
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--build")
            {
                try
                {
                    string report = Build(args[1], args[2]);
                    File.WriteAllText(Path.GetFullPath(args[2]) + ".build.txt", report + Environment.NewLine, new UTF8Encoding(true));
                    Console.WriteLine(report);
                    return 0;
                }
                catch (Exception ex)
                {
                    string report = ex.ToString();
                    File.WriteAllText(Path.GetFullPath(args[2]) + ".error.txt", report, new UTF8Encoding(true));
                    Console.Error.WriteLine(report);
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
        private sealed class MainForm : Form
        {
            private readonly TextBox source = new TextBox { Left=22, Top=64, Width=526, Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
            private readonly TextBox game = new TextBox { Left=22, Top=135, Width=526, Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
            private readonly TextBox status = new TextBox { Left=22, Top=340, Width=656, Height=185, Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right };
            private readonly Button deploy = new Button { Left=22, Top=281, Width=170, Height=38, Text="一键整合并部署" };
            private readonly Button restore = new Button { Left=207, Top=281, Width=170, Height=38, Text="恢复上次部署" };
            private readonly Button build = new Button { Left=392, Top=281, Width=170, Height=38, Text="仅生成单 DLL" };
            internal MainForm()
            {
                Text="Affine 一键整合部署器"; ClientSize=new Size(700,548); MinimumSize=new Size(716,587);
                StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",9);AutoScaleMode=AutoScaleMode.Dpi;
                Controls.Add(new Label { Left=22,Top=16,Width=656,Height=26,Text="选择原版 Affine DLL 和游戏目录，整合后自动部署。" });
                Controls.Add(new Label { Left=22,Top=44,Width=250,Text="原版 affine_io.dll（未经整合）" });Controls.Add(source);
                var browse=new Button { Left=560,Top=61,Width=118,Height=28,Text="选择 DLL",Anchor=AnchorStyles.Top|AnchorStyles.Right };
                browse.Click+=(s,e)=>{using(var d=new OpenFileDialog { Filter="原版 Affine DLL|*.dll",CheckFileExists=true })if(d.ShowDialog(this)==DialogResult.OK)source.Text=d.FileName;};Controls.Add(browse);
                Controls.Add(new Label { Left=22,Top=115,Width=400,Text="游戏根目录（包含 Sinmai.exe 和 start.bat）" });Controls.Add(game);
                var folder=new Button { Left=560,Top=132,Width=118,Height=28,Text="选择游戏目录",Anchor=AnchorStyles.Top|AnchorStyles.Right };
                folder.Click+=(s,e)=>{using(var d=new FolderBrowserDialog { Description="选择 Sinmai.exe 所在的游戏目录",ShowNewFolderButton=false })if(d.ShowDialog(this)==DialogResult.OK)game.Text=d.SelectedPath;};Controls.Add(folder);
                Controls.Add(new Label { Left=22,Top=180,Width=656,Height=91,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,
                    Text="自动处理：生成 affine_io_single.dll；修改 start.bat、Segatools 路径和 AquaMai 投币设置；备份并移走旧 Affine Mods 插件。\r\n\r\n请先退出游戏。需要已有 mai2hook、MelonLoader 和 AquaMai。点数重启清零。" });
                Controls.Add(deploy);Controls.Add(restore);Controls.Add(build);Controls.Add(status);
                deploy.Click+=async(s,e)=>{string input=source.Text,directory=game.Text;await Run(()=>Deployment.Install(input,directory));};
                restore.Click+=async(s,e)=>{string directory=game.Text;await Run(()=>Deployment.Restore(directory));};
                build.Click+=async(s,e)=>{using(var d=new SaveFileDialog { Filter="DLL 文件|*.dll",FileName="affine_io_single.dll",OverwritePrompt=false })if(d.ShowDialog(this)==DialogResult.OK){string input=source.Text,output=d.FileName;await Run(()=>Build(input,output));}};
                string nearby=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"affine_io.dll");if(File.Exists(nearby))source.Text=nearby;
                string here=AppDomain.CurrentDomain.BaseDirectory;if(File.Exists(Path.Combine(here,"Sinmai.exe")))game.Text=here;
                status.Text="部署文件会备份到游戏目录的 AffineDeployBackups 文件夹。\r\n支持本机已验证的 SDGB1.56 / MelonLoader net35 / AquaMai 环境。";
            }
            private async System.Threading.Tasks.Task Run(Func<string> action)
            {
                // Take UI values before dispatch; worker never reads Windows Forms controls.
                deploy.Enabled=restore.Enabled=build.Enabled=false;
                status.Text="正在处理，请稍候…";
                try { status.Text=await System.Threading.Tasks.Task.Run(action); }
                catch(Exception ex) { status.Text="操作失败："+ex.Message+"\r\n"+(ex is UnauthorizedAccessException?"目录写入权限不足，请以管理员身份运行部署器。":""); }
                finally { deploy.Enabled=restore.Enabled=build.Enabled=true; }
            }
        }
    }
}