using System;
using System.IO;
using System.Linq;
using System.Threading;
using MelonLoader;

namespace AffineGameLights
{
    public static class NativeEntry
    {
        internal static IntPtr Module;
        private static int started;
        private static BridgeMod bridge;
        private static PhysicalCoinBridge coinBridge;
        public static void Start(IntPtr module)
        {
            if (Interlocked.Exchange(ref started, 1) != 0) return;
            Module = module;
            // Register only; all game reflection, Harmony patching and device init run on the game thread.
            MelonEvents.OnUpdate.Subscribe(InitializeOnGameThread, -10000, true);
        }

        private static void InitializeOnGameThread()
        {
            coinBridge = new PhysicalCoinBridge();
            MelonEvents.OnUpdate.Subscribe(coinBridge.Update);
            try
            {
                if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a != typeof(NativeEntry).Assembly &&
                    (a.GetName().Name == "AffineHostProbe" || a.GetName().Name == "AffineGameLights")))
                    throw new InvalidOperationException("检测到旧 Affine Mods 插件。请退出游戏，移走旧插件后重启，避免重复转发。");
                bridge = new BridgeMod();
                bridge.OnInitializeMelon();
                MelonEvents.OnUpdate.Subscribe(bridge.OnUpdate);
                MelonEvents.OnApplicationQuit.Subscribe(bridge.OnApplicationQuit);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("AffineSingle bootstrap: " + ex);
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "affine-single-managed-error.log"), ex + Environment.NewLine); }
                catch { }
            }
        }
    }
}
