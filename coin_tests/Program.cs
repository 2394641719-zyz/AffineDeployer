using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using AffineGameLights;
using V = AquaMai.Mods.GameSystem.VirtualCoin;
namespace AMDaemon {
 public static class UsbIO { public static bool IsAvailable => true; public static int NodeCount => 1; public static List<Node> Nodes {get;} = new List<Node>{new Node()}; }
 public sealed class Node { public Coin CoinInput {get;}=new Coin(); }
 public sealed class Coin { public int SlotCount=>1; public List<ushort> Values{get;}=new List<ushort>{0}; public List<Condition> Conditions{get;}=new List<Condition>{new Condition()}; }
 public sealed class Condition { public bool IsJam=>false;public bool IsDisconnected=>false;public bool IsBusy=>false; }
 public static class Credit { public static bool IsAvailable=>true; public static bool CoinInIgnored {get;set;} public static Book Bookkeeping{get;}=new Book(); public static Config Config{get;}=new Config();public static List<CreditUnit> Players{get;}=new List<CreditUnit>{new CreditUnit()}; }
 public sealed class Book { public uint TotalCoin{get;set;} }
 public sealed class Config {public uint CoinToCredit{get;set;}=1;public bool IsFreePlay=>false;public List<uint> CoinMultipliers{get;}=new List<uint>{1};}
 public sealed class CreditUnit {public uint Credit=>(uint)V._bufferCredit;}
}
namespace AquaMai.Mods.GameSystem {
 public static class VirtualCoin { public static volatile int _bufferCredit;
  [HarmonyPostfix,HarmonyPatch(typeof(AMDaemon.CreditUnit),"get_Credit")]
  public static void CreditPatch(ref uint __result){__result+=(uint)_bufferCredit;}
 }
}
class Program {
 static PhysicalCoinBridge Fresh(ushort count=0) {V._bufferCredit=0;AMDaemon.Credit.Bookkeeping.TotalCoin=0;AMDaemon.Credit.CoinInIgnored=false;AMDaemon.Credit.Config.CoinToCredit=1;AMDaemon.UsbIO.Nodes[0].CoinInput.Values[0]=count;var b=new PhysicalCoinBridge();var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;foreach(var pair in new[]{new object[]{"usb",typeof(AMDaemon.UsbIO)},new object[]{"credit",typeof(AMDaemon.Credit)},new object[]{"virtualCoin",typeof(V)},new object[]{"buffer",typeof(V).GetField("_bufferCredit")},new object[]{"active",true}})typeof(PhysicalCoinBridge).GetField((string)pair[0],flags).SetValue(b,pair[1]);b.Update();return b;}
 static void Press(PhysicalCoinBridge b,ushort count){AMDaemon.UsbIO.Nodes[0].CoinInput.Values[0]=count;b.Update();Thread.Sleep(400);b.Update();}
 static void Expect(int expected,string name){if(V._bufferCredit!=expected)throw new Exception(name+": expected "+expected+", got "+V._bufferCredit);Console.WriteLine("PASS "+name);}
 static void Main(){
  var b=Fresh();Press(b,3);Expect(3,"three physical counter increments");b.Update();Expect(3,"repeated polling does not add credits");
  b=Fresh(255);Press(b,0);Expect(1,"8-bit rollover");
  b=Fresh(10);Press(b,0);Expect(0,"daemon counter reset does not create credits");
  b=Fresh();AMDaemon.Credit.Config.CoinToCredit=2;Press(b,3);Expect(1,"two coins per credit");Press(b,4);Expect(2,"conversion remainder retained");
  b=Fresh();V._bufferCredit=22;Press(b,3);Expect(24,"credit limit");
  b=Fresh();AMDaemon.Credit.CoinInIgnored=true;Press(b,1);Expect(0,"blocked coin ignored");
  b=Fresh();AMDaemon.UsbIO.Nodes[0].CoinInput.Values[0]=1;b.Update();AMDaemon.Credit.Bookkeeping.TotalCoin=1;Thread.Sleep(400);b.Update();Expect(0,"native accounting cancels pending fallback");
 }
}
