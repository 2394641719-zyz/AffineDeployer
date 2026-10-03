using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace AffineGameLights {
    // Game-thread-only fallback. Uses USB coin counters; never keyboard events.
    internal sealed class PhysicalCoinBridge {
        private Type usb, credit, virtualCoin;
        private FieldInfo buffer;
        private bool baseline, active, nativeRestored, failed;
        private ushort previous;
        private uint nativeCoins;
        private int pending, remainder;
        private long due;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "affine-physical-coin.log");
        private static object Get(object obj, string name) {
            var t = obj as Type ?? obj.GetType();
            return t.GetProperty(name).GetValue(obj is Type ? null : obj, null);
        }
        private static object Item(object list, int index) { return list.GetType().GetProperty("Item").GetValue(list, new object[] { index }); }
        private void Log(string message) { File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine); }
        internal void Update() {
            if (failed || nativeRestored) return;
            try {
                if (!active) {
                    usb = AccessTools.TypeByName("AMDaemon.UsbIO");
                    credit = AccessTools.TypeByName("AMDaemon.Credit");
                    virtualCoin = AccessTools.TypeByName("AquaMai.Mods.GameSystem.VirtualCoin");
                    if (usb == null || credit == null || virtualCoin == null) return;
                    buffer = AccessTools.Field(virtualCoin, "_bufferCredit");
                    var unit = AccessTools.TypeByName("AMDaemon.CreditUnit");
                    var patches = HarmonyLib.Harmony.GetPatchInfo(AccessTools.PropertyGetter(unit, "Credit"));
                    if (buffer == null || patches == null) return;
                    bool installed = false;
                    foreach (var p in patches.Postfixes) if (p.PatchMethod.DeclaringType == virtualCoin) installed = true;
                    if (!installed) return;
                    active = true;
                    Log("READY source=AMDaemon.UsbIO coin slot0; AquaMai VirtualCoin; keyboard=none; persistence=session-only");
                }
                if (!(bool)Get(usb, "IsAvailable") || !(bool)Get(credit, "IsAvailable") || (int)Get(usb, "NodeCount") == 0) { baseline = false; pending = 0; return; }
                var coin = Get(Item(Get(usb, "Nodes"), 0), "CoinInput");
                if ((int)Get(coin, "SlotCount") == 0) return;
                ushort current = Convert.ToUInt16(Item(Get(coin, "Values"), 0));
                uint total = Convert.ToUInt32(Get(Get(credit, "Bookkeeping"), "TotalCoin"));
                if (!baseline) { previous = current; nativeCoins = total; baseline = true; Log("BASELINE usb=" + current + " native=" + total); return; }
                int delta = (current - previous) & 255;
                previous = current;
                if (total != nativeCoins) {
                    nativeRestored = true; pending = 0;
                    Log("STOP native coin accounting changed from " + nativeCoins + " to " + total + "; stop fallback to avoid duplicate credit");
                    return;
                }
                var condition = Item(Get(coin, "Conditions"), 0);
                bool blocked = (bool)Get(credit, "CoinInIgnored") || (bool)Get(condition, "IsJam") || (bool)Get(condition, "IsDisconnected") || (bool)Get(condition, "IsBusy");
                if (blocked) { pending = 0; return; }
                if (delta > 16) { pending = 0; Log("RESYNC usb jump=" + delta); return; }
                if (delta > 0) { pending += delta; due = clock.ElapsedMilliseconds + 350; Log("PHYSICAL delta=" + delta + " usb=" + current); }
                if (pending == 0 || clock.ElapsedMilliseconds < due) return;
                var config = Get(credit, "Config");
                uint rate = Convert.ToUInt32(Get(config, "CoinToCredit"));
                uint multiplier = Convert.ToUInt32(Item(Get(config, "CoinMultipliers"), 0));
                if (rate == 0 || (bool)Get(config, "IsFreePlay")) { pending = 0; return; }
                remainder += pending * (int)multiplier;
                pending = 0;
                int added = remainder / (int)rate;
                remainder %= (int)rate;
                int shown = Convert.ToInt32(Get(Item(Get(credit, "Players"), 0), "Credit"));
                added = Math.Min(added, Math.Max(0, 24 - shown));
                if (added > 0) {
                    int old = (int)buffer.GetValue(null);
                    buffer.SetValue(null, checked(old + added));
                    Log("ADD credits=" + added + " buffer=" + (old + added));
                }
            } catch (Exception ex) { failed = true; Log("ERROR stopped: " + ex.GetBaseException()); MelonLogger.Error("Affine physical coin: " + ex.GetBaseException().Message); }
        }
    }
}
