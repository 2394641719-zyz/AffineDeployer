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
        }
    }
}
