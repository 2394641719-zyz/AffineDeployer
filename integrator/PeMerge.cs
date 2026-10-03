using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AffineIntegrator
{
    internal sealed class Pe
    {
        internal readonly byte[] B;
        internal readonly int PeAt, Optional, Sections, Count;
        internal readonly uint SectionAlign, FileAlign, SizeImage, SizeHeaders, Entry;
        internal readonly ulong Base;
        internal struct Section { internal string Name; internal uint Va, VirtualSize, Raw, RawSize, Flags; }
        internal readonly Section[] List;
        internal Pe(byte[] bytes, bool rejectIntegrated = true)
        {
            B = bytes;
            if (bytes.Length < 64 || U16(0) != 0x5A4D) throw new InvalidDataException("不是 PE DLL。");
            PeAt = checked((int)U32(60)); Check(PeAt, 24);
            if (U32(PeAt) != 0x4550 || U16(PeAt + 4) != 0x8664 || (U16(PeAt + 22) & 0x2000) == 0)
                throw new InvalidDataException("只支持 x64 原生 DLL。");
            Count = U16(PeAt + 6); Optional = PeAt + 24;
            int olen = U16(PeAt + 20); Check(Optional, olen);
            if (olen < 240 || U16(Optional) != 0x20b || U32(Optional + 108) < 16 || Dir(14) != 0)
                throw new InvalidDataException("不是标准 x64 原生 PE DLL。");
            Sections = Optional + olen; Check(Sections, Count * 40L);
            SectionAlign = U32(Optional + 32); FileAlign = U32(Optional + 36);
            SizeImage = U32(Optional + 56); SizeHeaders = U32(Optional + 60); Entry = U32(Optional + 16);
            Base = U64(Optional + 24);
            if (Count < 1 || Count > 50 || SectionAlign != 4096 || FileAlign != 512 || SizeHeaders > bytes.Length)
                throw new InvalidDataException("不支持此 DLL 的分区对齐或 PE 头。");
            List = new Section[Count];
            long rawEnd = 0;
            for (int i = 0; i < Count; i++)
            {
                int p = Sections + i * 40;
                Section s = new Section { Name = Encoding.ASCII.GetString(bytes, p, 8).TrimEnd('\0'),
                    VirtualSize = U32(p + 8), Va = U32(p + 12), RawSize = U32(p + 16), Raw = U32(p + 20), Flags = U32(p + 36) };
                if (s.RawSize != 0) Check(s.Raw, s.RawSize);
                rawEnd = Math.Max(rawEnd, (long)s.Raw + s.RawSize);
                List[i] = s;
            }
            if (rawEnd != bytes.Length) throw new InvalidDataException("DLL 末尾有额外数据，暂不修改此文件。");
            if (Dir(4) != 0 || Dir(11) != 0 || Dir(13) != 0 || U32(PeAt + 12) != 0)
                throw new InvalidDataException("暂不支持签名、Bound/Delay Import 或 COFF 符号表。");
            if ((U16(Optional + 70) & 0x4000) != 0)
                throw new InvalidDataException("暂不支持启用 CFG 的 DLL。");
            if (rejectIntegrated && List.Any(s => s.Name.StartsWith(".aff", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("这已经是整合版；请选择未经整合的新版 affine_io.dll。");
        }
        internal void Check(long p, long n) { if (p < 0 || n < 0 || p > B.Length || n > B.Length - p) throw new InvalidDataException("PE 文件截断或指针越界。"); }
        internal ushort U16(long p) { Check(p, 2); return BitConverter.ToUInt16(B, (int)p); }
        internal uint U32(long p) { Check(p, 4); return BitConverter.ToUInt32(B, (int)p); }
        internal ulong U64(long p) { Check(p, 8); return BitConverter.ToUInt64(B, (int)p); }
        internal uint Dir(int index) { return U32(Optional + 112 + index * 8); }
        internal uint DirSize(int index) { return U32(Optional + 116 + index * 8); }
        internal int At(uint rva, int len)
        {
            foreach (Section s in List)
                if (rva >= s.Va && (ulong)rva - s.Va + (ulong)len <= s.RawSize)
                { long p = (long)s.Raw + rva - s.Va; Check(p, len); return (int)p; }
            throw new InvalidDataException("PE RVA 不在已初始化的分区中。");
        }
        internal string CStr(int p)
        {
            int e = p; Check(p, 1);
            while (e < B.Length && e - p < 512 && B[e] != 0) e++;
            if (e == B.Length || e - p == 512) throw new InvalidDataException("无效的 PE 字符串。");
            return Encoding.ASCII.GetString(B, p, e - p);
        }
        internal Dictionary<string, uint> Exports()
        {
            if (Dir(0) == 0) throw new InvalidDataException("DLL 没有导出接口。");
            int p = At(Dir(0), 40);
            uint fnCount = U32(p + 20), count = U32(p + 24);
            if (fnCount > 65536 || count > 65536) throw new InvalidDataException("导出表数量异常。");
            int f = At(U32(p + 28), checked((int)fnCount * 4)), n = At(U32(p + 32), checked((int)count * 4));
            int o = At(U32(p + 36), checked((int)count * 2));
            var result = new Dictionary<string, uint>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                ushort ordinal = U16(o + i * 2);
                if (ordinal >= fnCount) throw new InvalidDataException("导出序号异常。");
                result.Add(CStr(At(U32(n + i * 4), 1)), U32(f + ordinal * 4));
            }
            return result;
        }
    }

    internal static class PeMerge
    {
        private static uint Align(uint n, uint a) { return checked((n + a - 1) / a * a); }
        private static void Put(byte[] b, int p, uint v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, p, 4); }
        private static void Put64(byte[] b, int p, ulong v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, p, 8); }
        internal static string Hash(byte[] b) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", ""); }
        internal static byte[] Merge(byte[] nativeBytes, byte[] bootBytes, byte[] managedBytes)
        {
            Pe n = new Pe(nativeBytes), boot = new Pe(bootBytes);
            if (n.Base != boot.Base || n.Dir(0) == 0 || boot.Dir(1) == 0 || boot.Dir(3) == 0)
                throw new InvalidDataException("原生 DLL 与启动模板不兼容。");
            string[] required = { "mai2_io_init", "mai2_io_led_init", "mai2_io_led_gs_update", "mai2_io_led_set_fet_output" };
            var originals = n.Exports();
            foreach (string symbol in required)
                if (!originals.ContainsKey(symbol) || originals[symbol] == 0)
                    throw new InvalidDataException("新版缺少必要接口：" + symbol);
            var bex = boot.Exports();
            if (!bex.ContainsKey("affine_boot_config") || boot.Entry == 0)
                throw new InvalidDataException("启动模板缺少入口。");
            if (n.U32(n.Optional + 64) != 0 || n.Dir(9) != 0 && n.DirSize(9) != 40)
                throw new InvalidDataException("原生 DLL 使用尚未验证的校验和或 TLS 格式。");
            if (n.Sections + (n.Count + boot.Count + 2) * 40 > n.List.Min(s => s.Raw))
                throw new InvalidDataException("PE 头空间不足，无法安全增加分区。");
            uint delta = checked(Align(n.SizeImage, n.SectionAlign) - 4096);
            uint raw = Align((uint)n.B.Length, n.FileAlign), va = Align(n.SizeImage, n.SectionAlign);
            var output = new List<byte>(n.B);
            var added = new List<Pe.Section>();
            foreach (Pe.Section s in boot.List)
            {
                uint newRaw = raw, newVa = checked(s.Va + delta);
                if (newVa < va) throw new InvalidDataException("启动模板分区重叠。");
                Append(output, newRaw, boot.B, (int)s.Raw, (int)s.RawSize);
                uint flags = s.Flags;
                if (boot.Dir(12) >= s.Va && boot.Dir(12) < s.Va + s.RawSize) flags |= 0x80000000;
                added.Add(new Pe.Section { Name = ".aff" + added.Count, Va = newVa, VirtualSize = s.VirtualSize,
                    Raw = newRaw, RawSize = s.RawSize, Flags = flags });
                raw = Align(checked(newRaw + s.RawSize), n.FileAlign);
                va = Align(checked(newVa + Math.Max(s.VirtualSize, s.RawSize)), n.SectionAlign);
            }
            // Fix all bootstrap import descriptor RVAs and lookup/IAT thunk RVAs.
            int bootImports = boot.At(boot.Dir(1), (int)boot.DirSize(1));
            for (int p = bootImports; boot.U32(p) != 0; p += 20)
            {
                foreach (int field in new[] { 0, 12, 16 })
                    PutMapped(output, boot, added, p + field, checked(boot.U32(p + field) + delta));
                foreach (uint table in new[] { boot.U32(p), boot.U32(p + 16) }.Distinct())
                {
                    for (int i = 0; ; i++)
                    {
                        int t = boot.At(checked(table + (uint)(i * 8)), 8);
                        ulong val = boot.U64(t);
                        if (val == 0) break;
                        if ((val & 0x8000000000000000) == 0)
                            Put64Mapped(output, boot, added, t, checked(val + delta));
                    }
                }
            }
            // The bootstrap is linked at the same preferred base as the original.
            // Its relocations adjust the referenced RVA when it moves to the new sections.
            byte[] bootRelocs = RelocateBoot(output, boot, added, delta);
            uint metaVa = va, metaRaw = raw;
            Append(output, metaRaw, managedBytes, 0, managedBytes.Length);
            uint metaSize = Align((uint)managedBytes.Length, n.FileAlign);
            added.Add(new Pe.Section { Name = ".affmeta", Va = metaVa, VirtualSize = (uint)managedBytes.Length,
                Raw = metaRaw, RawSize = metaSize, Flags = 0x40000040 });
            raw = Align(checked(metaRaw + metaSize), n.FileAlign);
            va = Align(checked(metaVa + (uint)managedBytes.Length), n.SectionAlign);
            int config = boot.At(bex["affine_boot_config"], 16);
            if (boot.U32(config) != 0x37464641) throw new InvalidDataException("启动模板配置签名不符。");
            PutMapped(output, boot, added, config + 4, n.Entry);
            PutMapped(output, boot, added, config + 8, metaVa);
            PutMapped(output, boot, added, config + 12, (uint)managedBytes.Length);
            // New directory section combines the existing and moved import, exception and relocation tables.
            var dir = new List<byte>();
            uint importOffset = (uint)dir.Count;
            if (n.Dir(1) != 0) CopyImportDescriptors(n, dir);
            int countNew = CopyImportDescriptors(boot, dir, delta);
            dir.AddRange(new byte[20]);
            uint importLength = (uint)(dir.Count - importOffset);
            Pad(dir, 4);
            uint exceptionOffset = (uint)dir.Count;
            if (n.Dir(3) != 0) { int p = n.At(n.Dir(3), (int)n.DirSize(3)); AddRange(dir, n.B, p, (int)n.DirSize(3)); }
            if (boot.DirSize(3) % 12 != 0 || n.DirSize(3) % 12 != 0) throw new InvalidDataException("x64 异常表长度无效。");
            for (int i = 0; i < boot.DirSize(3); i += 12)
            {
                int p = boot.At(boot.Dir(3) + (uint)i, 12);
                AddU32(dir, checked(boot.U32(p) + delta)); AddU32(dir, checked(boot.U32(p + 4) + delta));
                AddU32(dir, checked(boot.U32(p + 8) + delta));
            }
            uint exceptionLength = (uint)(dir.Count - exceptionOffset);
            Pad(dir, 4);
            uint relocOffset = (uint)dir.Count;
            if (n.Dir(5) != 0) { int p = n.At(n.Dir(5), (int)n.DirSize(5)); AddRange(dir, n.B, p, (int)n.DirSize(5)); }
            dir.AddRange(bootRelocs);
            uint relocLength = (uint)(dir.Count - relocOffset);
            uint dirVa = va, dirRaw = raw;
            Append(output, dirRaw, dir.ToArray(), 0, dir.Count);
            added.Add(new Pe.Section { Name = ".affdir", Va = dirVa, VirtualSize = (uint)dir.Count,
                Raw = dirRaw, RawSize = Align((uint)dir.Count, n.FileAlign), Flags = 0x40000040 });
            while (output.Count < (long)dirRaw + Align((uint)dir.Count, n.FileAlign)) output.Add(0);
            byte[] result = output.ToArray();
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(n.Count + added.Count)), 0, result, n.PeAt + 6, 2);
            Put(result, n.Optional + 16, checked(boot.Entry + delta));
            Put(result, n.Optional + 56, Align(checked(dirVa + (uint)dir.Count), n.SectionAlign));
            Put(result, n.Optional + 64, 0); // image checksum invalid after modification
            PutDir(result, n, 1, dirVa + importOffset, importLength);
            PutDir(result, n, 3, dirVa + exceptionOffset, exceptionLength);
            PutDir(result, n, 5, dirVa + relocOffset, relocLength);
            // Keep the original IAT directory. Its pages include runtime locks
            // that the Windows loader must leave writable after import snapping.
            uint code = n.U32(n.Optional + 4), data = n.U32(n.Optional + 8);
            foreach (Pe.Section s in added)
                if ((s.Flags & 0x20) != 0) code = checked(code + s.RawSize); else data = checked(data + s.RawSize);
            Put(result, n.Optional + 4, code); Put(result, n.Optional + 8, data);
            for (int i = 0; i < added.Count; i++) WriteSection(result, n.Sections + (n.Count + i) * 40, added[i]);
            // The merged import directory resides in another section. Windows can
            // revisit the original IAT after its first snap, so keep that page writable.
            foreach (var section in n.List.Select((value, index) => new { value, index }))
                if (n.Dir(12) >= section.value.Va && n.Dir(12) < section.value.Va + section.value.RawSize)
                {
                    int header = n.Sections + section.index * 40;
                    Put(result, header + 36, section.value.Flags | 0x80000000);
                }
            var merged = new Pe(result, false);
            var finalExports = merged.Exports();
            if (finalExports.Count != originals.Count || originals.Any(kv => !finalExports.ContainsKey(kv.Key) || finalExports[kv.Key] != kv.Value))
                throw new InvalidDataException("合并后原始导出接口发生变化。");
            if (countNew == 0) throw new InvalidDataException("启动模板没有导入函数。");
            return result;
        }
        private static void Append(List<byte> list, uint raw, byte[] source, int start, int len)
        {
            while (list.Count < raw) list.Add(0);
            if (list.Count != raw) throw new InvalidDataException("PE 分区文件位置重叠。");
            AddRange(list, source, start, len);
        }
        private static void AddRange(List<byte> list, byte[] source, int start, int len)
        { for (int i = 0; i < len; i++) list.Add(source[start + i]); }
        private static void Pad(List<byte> list, int alignment) { while (list.Count % alignment != 0) list.Add(0); }
        private static void AddU32(List<byte> list, uint value) { list.AddRange(BitConverter.GetBytes(value)); }
        private static void WriteSection(byte[] b, int p, Pe.Section s)
        {
            var name = Encoding.ASCII.GetBytes(s.Name);
            Buffer.BlockCopy(name, 0, b, p, name.Length);
            Put(b, p + 8, s.VirtualSize); Put(b, p + 12, s.Va); Put(b, p + 16, s.RawSize);
            Put(b, p + 20, s.Raw); Put(b, p + 36, s.Flags);
        }
        private static void PutDir(byte[] b, Pe pe, int i, uint rva, uint size)
        { Put(b, pe.Optional + 112 + i * 8, rva); Put(b, pe.Optional + 116 + i * 8, size); }
        private static int Mapped(Pe boot, List<Pe.Section> sections, int sourceOffset)
        {
            for (int i = 0; i < boot.Count; i++)
            {
                Pe.Section s = boot.List[i], dest = sections[i];
                if (sourceOffset >= s.Raw && (long)sourceOffset - s.Raw < s.RawSize)
                    return checked((int)(dest.Raw + sourceOffset - s.Raw));
            }
            throw new InvalidDataException("启动模板位置未映射。");
        }
        private static void PutMapped(List<byte> output, Pe boot, List<Pe.Section> sections, int sourceOffset, uint value)
        { int p = Mapped(boot, sections, sourceOffset); byte[] b = BitConverter.GetBytes(value); for (int i = 0; i < 4; i++) output[p + i] = b[i]; }
        private static void Put64Mapped(List<byte> output, Pe boot, List<Pe.Section> sections, int sourceOffset, ulong value)
        { int p = Mapped(boot, sections, sourceOffset); byte[] b = BitConverter.GetBytes(value); for (int i = 0; i < 8; i++) output[p + i] = b[i]; }
        private static int CopyImportDescriptors(Pe pe, List<byte> dest, uint delta = 0)
        {
            if (pe.Dir(1) == 0) return 0;
            int p = pe.At(pe.Dir(1), (int)pe.DirSize(1)), count = 0;
            while (pe.U32(p) != 0 || pe.U32(p + 12) != 0 || pe.U32(p + 16) != 0)
            {
                if (++count > 1024) throw new InvalidDataException("导入表异常。");
                for (int i = 0; i < 20; i += 4)
                    AddU32(dest, (i == 0 || i == 12 || i == 16) ? checked(pe.U32(p + i) + delta) : pe.U32(p + i));
                p += 20; pe.Check(p, 20);
            }
            return count;
        }
        private static byte[] RelocateBoot(List<byte> output, Pe boot, List<Pe.Section> sections, uint delta)
        {
            var list = new List<byte>();
            if (boot.Dir(5) == 0) return list.ToArray();
            uint consumed = 0;
            while (consumed < boot.DirSize(5))
            {
                int p = boot.At(boot.Dir(5) + consumed, 8);
                uint page = boot.U32(p), len = boot.U32(p + 4);
                if (len < 8 || len % 4 != 0 || consumed + len > boot.DirSize(5)) throw new InvalidDataException("启动模板重定位表异常。");
                AddU32(list, checked(page + delta)); AddU32(list, len);
                for (int i = 8; i < len; i += 2)
                {
                    ushort item = boot.U16(p + i), kind = (ushort)(item >> 12);
                    list.AddRange(BitConverter.GetBytes(item));
                    if (kind == 0) continue;
                    if (kind != 10) throw new InvalidDataException("启动模板使用不支持的重定位类型。");
                    int target = boot.At(checked(page + (uint)(item & 0xfff)), 8);
                    Put64Mapped(output, boot, sections, target, checked(boot.U64(target) + delta));
                }
                consumed += len;
            }
            return list.ToArray();
        }
    }
}
