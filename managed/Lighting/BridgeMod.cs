using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using MelonLoader;

#if AFFINE_NATIVE
// Loaded from memory by the native affine_io entry point; not a Mods plugin.
#else
[assembly: MelonInfo(typeof(AffineGameLights.BridgeMod), "AffineGameLights", "5.0.0", "OpenAI")]
#endif
#if !AFFINE_NATIVE
[assembly: MelonGame("sega-interactive", "Sinmai")]
#endif

namespace AffineGameLights
{
    public sealed class BridgeMod : MelonMod
    {
        private const string Owner = "affine.research.sdgb.game-lights.v5";
        private const string ExpectedHash = "9E15ACC11C3E8865D48D0EEEBE2EFECD88EA7F5B2C97F3BF05C5676B231BB60A";
        private static int brightnessCap = BrightnessConfig.DefaultBrightness;
        private static readonly object Gate = new object();
        private static readonly byte[][] Frames = { new byte[32], new byte[32] };
        private static readonly byte[][] FetStates = { new byte[3], new byte[3] };
        private static readonly bool[] Dirty = new bool[2];
        private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static string logPath;
        private static GsUpdate gsUpdate;
        private static FetUpdate fetUpdate;
        private static long colorCalls, multiCalls, fadeCalls, updateCalls, framesSent, skippedCalls, errors, fetCalls, fetSent;
        private static int samples;
        private static bool enabled;
        private static HarmonyLib.Harmony harmony;
        private long nextSummary;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GsUpdate(byte board, [In] byte[] rgb);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FetUpdate(byte board, [In] byte[] channels);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadLibraryW")]
        private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        private static extern IntPtr GetModuleHandle(string name);

        public override void OnInitializeMelon()
        {
            try
            {
                string gameRoot = AppDomain.CurrentDomain.BaseDirectory;
                string folder = Path.Combine(gameRoot, "AffineGameLights");
                Directory.CreateDirectory(folder);
                logPath = Path.Combine(folder, "bridge-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Process.GetCurrentProcess().Id + ".log");
#if AFFINE_NATIVE
                Log("START version=7.0.0-native process=" + Process.GetCurrentProcess().ProcessName + " pid=" + Process.GetCurrentProcess().Id);
#else
                Log("START version=5.0.0 process=" + Process.GetCurrentProcess().ProcessName + " pid=" + Process.GetCurrentProcess().Id);
#endif
                if (!string.Equals(Process.GetCurrentProcess().ProcessName, "Sinmai", StringComparison.OrdinalIgnoreCase)) { Log("ABORT wrong process"); return; }
                brightnessCap = BrightnessConfig.Load(Path.Combine(folder, "config.ini"), Log);
#if AFFINE_NATIVE
                IntPtr module = NativeEntry.Module;
                if (module == IntPtr.Zero || IntPtr.Size != 8) { Log("ABORT invalid native host"); return; }
#else
                string path = Path.Combine(gameRoot, "affine_io.dll");
                if (!File.Exists(path)) { Log("ABORT original affine_io.dll missing"); return; }
                string hash;
                using (var f = File.OpenRead(path)) using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
                Log("AFFINE path=" + path + " sha256=" + hash);
                if (!string.Equals(hash, ExpectedHash, StringComparison.OrdinalIgnoreCase)) { Log("ABORT original DLL hash mismatch"); return; }
                if (GetModuleHandle("affine_io.dll") != IntPtr.Zero) { Log("ABORT Affine already loaded; duplicate init refused"); return; }
#endif
#if !AFFINE_NATIVE
                IntPtr module = LoadLibrary(path);
#endif
                if (module == IntPtr.Zero) { Log("ABORT LoadLibrary win32=" + Marshal.GetLastWin32Error()); return; }
                Log("LOAD_LIBRARY_OK module=0x" + module.ToInt64().ToString("X"));
                var init = Bind<Init>(module, "mai2_io_init");
                var ledInit = Bind<Init>(module, "mai2_io_led_init");
                gsUpdate = Bind<GsUpdate>(module, "mai2_io_led_gs_update");
                fetUpdate = Bind<FetUpdate>(module, "mai2_io_led_set_fet_output");
                if (init == null || ledInit == null || gsUpdate == null || fetUpdate == null) { Log("ABORT required Affine export missing"); return; }
                Log("INIT_BEGIN");
                int result = init();
                Log("INIT_RESULT 0x" + unchecked((uint)result).ToString("X8"));
                if (result != 0) return;
                result = ledInit();
                Log("LED_INIT_RESULT 0x" + unchecked((uint)result).ToString("X8"));
                if (result != 0) return;

                var game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (game == null) { Log("ABORT Assembly-CSharp not loaded"); return; }
                Type target = game.GetType("Mecha.Bd15070_4IF");
                if (target == null) { Log("ABORT Mecha.Bd15070_4IF missing"); return; }
                harmony = new HarmonyLib.Harmony(Owner);
                string[] names = { "_setColor", "_setColorMulti", "_setColorMultiFade", "_setColorMultiFet", "_setColorFet", "_setLedUpdate", "_setLedAllOff" };
                MethodInfo[] methods = target.GetMethods(Fields | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => names.Contains(m.Name)).ToArray();
                foreach (string name in names)
                    if (methods.Count(m => m.Name == name) != 1) { Log("ABORT unexpected method count for " + name); return; }
                var post = new HarmonyMethod(typeof(BridgeMod).GetMethod(nameof(After), BindingFlags.Static | BindingFlags.NonPublic));
                foreach (MethodInfo method in methods)
                {
                    harmony.Patch(method, postfix: post);
                    Log("PATCH_OK " + method);
                }
                enabled = true;
                nextSummary = TickNow() + 10000;
                Log("READY game button and FET light commands will be forwarded; billboard not handled; no serial opened by plugin");
#if AFFINE_NATIVE
                MelonLogger.Msg("Affine 单 DLL：游戏按键和机身灯转发已启用；日志：" + logPath);
#else
                Melon<BridgeMod>.Logger.Msg("Affine 游戏按键和机身灯转发已启用；日志：" + logPath);
#endif
            }
            catch (Exception ex)
            {
                Log("INIT_EXCEPTION " + ex);
                try { harmony?.UnpatchSelf(); } catch { }
                enabled = false;
#if AFFINE_NATIVE
                MelonLogger.Error(ex.ToString());
#else
                Melon<BridgeMod>.Logger.Error(ex.ToString());
#endif
            }
        }

        private static T Bind<T>(IntPtr module, string name) where T : class
        {
            IntPtr address = GetProcAddress(module, name);
            Log("EXPORT " + name + " " + (address == IntPtr.Zero ? "MISSING" : "OK"));
            return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }

        private static object Field(object instance, string name)
        {
            return instance == null ? null : instance.GetType().GetField(name, Fields)?.GetValue(instance);
        }

        private static int Board(object instance)
        {
            object value = Field(Field(instance, "_initParam"), "index");
            if (value == null) return -1;
            int board = Convert.ToInt32(value);
            return board == 0 || board == 1 ? board : -1;
        }

        private static byte Color(object boxedColor, string channel)
        {
            object value = Field(boxedColor, channel);
            if (value == null) throw new InvalidOperationException("Color32." + channel + " missing");
            return Scale(Convert.ToByte(value));
        }

        private static byte Scale(byte value) { return (byte)((value * brightnessCap + 127) / 255); }

        private static void Set(byte[] frame, int key, byte r, byte g, byte b, byte speed)
        {
            int offset = key * 4;
            frame[offset] = r; frame[offset + 1] = g; frame[offset + 2] = b; frame[offset + 3] = speed;
        }

        private static void After(object __instance, MethodBase __originalMethod, object[] __args, bool __runOriginal)
        {
            if (!enabled) return;
            try
            {
                if (!__runOriginal) { ++skippedCalls; return; }
                int board = Board(__instance);
                if (board < 0) { ++errors; if (errors <= 4) Log("BAD_BOARD " + __originalMethod.Name); return; }
                string name = __originalMethod.Name;
                if (name == "_setLedUpdate")
                {
                    ++updateCalls;
                    SendIfDirty(board);
                    return;
                }
                if (name == "_setLedAllOff")
                {
                    lock (Gate) { Array.Clear(Frames[board], 0, 32); Dirty[board] = true; }
                    SendIfDirty(board);
                    lock (Gate) Array.Clear(FetStates[board], 0, 3);
                    SendFet(board);
                    return;
                }
                if (name == "_setColorMultiFet")
                {
                    byte r = Color(__args[0], "r"), g = Color(__args[0], "g"), b = Color(__args[0], "b");
                    lock (Gate) { FetStates[board][0] = r; FetStates[board][1] = g; FetStates[board][2] = b; }
                    ++fetCalls;
                    SendFet(board);
                    return;
                }
                if (name == "_setColorFet")
                {
                    int index = Convert.ToInt32(__args[0]);
                    if (index < 8 || index > 10) { ++errors; if (errors <= 4) Log("BAD_FET_INDEX " + index); return; }
                    byte value = Scale(Convert.ToByte(__args[1]));
                    lock (Gate) FetStates[board][index - 8] = value;
                    ++fetCalls;
                    SendFet(board);
                    return;
                }
                if (name == "_setColor")
                {
                    int key = Convert.ToInt32(__args[0]);
                    if (key < 0 || key >= 8) { ++errors; if (errors <= 4) Log("BAD_KEY " + key); return; }
                    byte r = Color(__args[1], "r"), g = Color(__args[1], "g"), b = Color(__args[1], "b");
                    lock (Gate) { Set(Frames[board], key, r, g, b, 0); Dirty[board] = true; }
                    ++colorCalls;
                    if (samples++ < 12) Log("COLOR board=" + board + " key=" + key + " rgb=" + r + "," + g + "," + b);
                    return;
                }
                if (name == "_setColorMulti" || name == "_setColorMultiFade")
                {
                    byte r = Color(__args[0], "r"), g = Color(__args[0], "g"), b = Color(__args[0], "b");
                    byte speed = Convert.ToByte(__args[1]);
                    lock (Gate)
                    {
                        for (int key = 0; key < 8; ++key) Set(Frames[board], key, r, g, b, speed);
                        Dirty[board] = true;
                    }
                    if (name == "_setColorMulti") ++multiCalls; else ++fadeCalls;
                    if (samples++ < 12) Log("MULTI board=" + board + " method=" + name + " rgb=" + r + "," + g + "," + b + " speed=" + speed);
                }
            }
            catch (Exception ex)
            {
                ++errors;
                if (errors <= 10) Log("FORWARD_ERROR " + __originalMethod.Name + " " + ex);
            }
        }

        private static void SendIfDirty(int board)
        {
            byte[] copy;
            lock (Gate)
            {
                if (!Dirty[board]) return;
                copy = (byte[])Frames[board].Clone();
                // Transition applies to this submission only. Later partial-key
                // updates must not re-trigger a previous whole-board fade.
                for (int key = 0; key < 8; ++key) Frames[board][key * 4 + 3] = 0;
                Dirty[board] = false;
            }
            gsUpdate((byte)board, copy);
            ++framesSent;
            if (framesSent <= 12) Log("GS_SENT board=" + board + " frame=" + BitConverter.ToString(copy));
        }

        private static void SendFet(int board)
        {
            byte[] copy;
            lock (Gate) copy = (byte[])FetStates[board].Clone();
            fetUpdate((byte)board, copy);
            ++fetSent;
            if (fetSent <= 12) Log("FET_SENT board=" + board + " channels=" + BitConverter.ToString(copy));
        }

        public override void OnUpdate()
        {
            if (!enabled || TickNow() < nextSummary) return;
            nextSummary = TickNow() + 10000;
            Log("SUMMARY color=" + colorCalls + " multi=" + multiCalls + " fade=" + fadeCalls + " updates=" + updateCalls + " gs_sent=" + framesSent + " fet_calls=" + fetCalls + " fet_sent=" + fetSent + " skipped=" + skippedCalls + " errors=" + errors);
        }

        public override void OnApplicationQuit()
        {
            bool wasEnabled = enabled;
            enabled = false;
            try
            {
                if (wasEnabled && gsUpdate != null)
                {
                    gsUpdate(0, new byte[32]);
                    gsUpdate(1, new byte[32]);
                    fetUpdate(0, new byte[3]);
                    fetUpdate(1, new byte[3]);
                }
            }
            catch (Exception ex) { Log("QUIT_BLACKOUT_ERROR " + ex); }
            Log("STOP");
        }

        private static void Log(string message)
        {
            if (logPath == null) return;
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine;
            lock (Gate) File.AppendAllText(logPath, line, new UTF8Encoding(false));
        }

        // Unsigned conversion keeps uptime values positive on .NET Framework 4.7.2.
        private static long TickNow() { return (uint)Environment.TickCount; }
    }
}
