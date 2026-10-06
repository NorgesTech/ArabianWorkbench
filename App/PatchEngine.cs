using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ArabianWorkbench
{
    // Hash-guarded raw patcher: RVA->file offset via section table, backup, PE checksum fix.
    // v2: Verify (no write), offset/RVA helpers, backup list, batch apply, history log.
    public static class PatchEngine
    {
        struct Sec { public uint VAddr, VSize, Raw, RawSize; }

        public static byte[] Hex(string s)
        {
            if (s == null) throw new Exception("empty hex");
            s = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (s.Length == 0) throw new Exception("empty hex");
            if (s.Length % 2 != 0) throw new Exception("odd hex length");
            return Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray();
        }

        public static string ToHex(byte[] d) => BitConverter.ToString(d).Replace("-", " ");

        static System.Collections.Generic.List<Sec> Sections(string path)
        {
            var d = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(d, 0x3C);
            ushort nsec = BitConverter.ToUInt16(d, pe + 6);
            int opt = pe + 24, secs = opt + BitConverter.ToUInt16(d, pe + 20);
            var list = new System.Collections.Generic.List<Sec>();
            for (int i = 0; i < nsec; i++)
            {
                int o = secs + i * 40;
                list.Add(new Sec
                {
                    VSize = BitConverter.ToUInt32(d, o + 8),
                    VAddr = BitConverter.ToUInt32(d, o + 12),
                    RawSize = BitConverter.ToUInt32(d, o + 16),
                    Raw = BitConverter.ToUInt32(d, o + 20)
                });
            }
            return list;
        }

        public static uint ImageBase(string path)
        {
            var d = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(d, 0x3C);
            return BitConverter.ToUInt32(d, pe + 24 + 28);
        }

        public static uint VaToRva(string path, uint va)
        {
            uint img = ImageBase(path);
            return va >= img ? va - img : va;
        }

        public static uint RvaToOff(string path, uint rva, uint imgBase)
        {
            var secs = Sections(path);
            uint r = rva >= imgBase ? rva - imgBase : rva;
            foreach (var s in secs)
                if (r >= s.VAddr && r < s.VAddr + Math.Max(s.VSize, s.RawSize))
                    return (r - s.VAddr) + s.Raw;
            throw new Exception("RVA not in sections");
        }

        public static uint PeChecksum(string path)
        {
            var d = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(d, 0x3C);
            int csumOff = pe + 24 + 64;
            uint sum = 0;
            for (int i = 0; i < d.Length; i += 2)
            {
                if (i == csumOff || i == csumOff + 2) continue;
                ushort w = (ushort)(d[i] | ((i + 1 < d.Length ? d[i + 1] : 0) << 8));
                sum += w; sum = (sum & 0xFFFF) + (sum >> 16);
            }
            sum += (uint)d.Length;
            return sum;
        }

        static void FixChecksum(string path)
        {
            var d = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(d, 0x3C);
            int csumOff = pe + 24 + 64;
            uint sum = 0;
            for (int i = 0; i < d.Length; i += 2)
            {
                if (i == csumOff || i == csumOff + 2) continue;
                ushort w = (ushort)(d[i] | ((i + 1 < d.Length ? d[i + 1] : 0) << 8));
                sum += w; sum = (sum & 0xFFFF) + (sum >> 16);
            }
            sum += (uint)d.Length;
            var b = BitConverter.GetBytes(sum);
            Array.Copy(b, 0, d, csumOff, 4);
            File.WriteAllBytes(path, d);
        }

        static void Log(string workbenchDir, string line)
        {
            try { File.AppendAllText(Path.Combine(workbenchDir, "patches_applied.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + line + "\n"); } catch { }
        }

        static string WbDir()
        {
            try
            {
                var d = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
                if (Directory.GetFiles(d, "*.jsonl").Length > 0) return d;
            }
            catch { }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        public class VerifyResult
        {
            public uint Va; public uint Rva; public uint Off;
            public string LiveHex; public bool Match; public string Msg;
        }

        public static VerifyResult Verify(string file, string vaHex, string origHex)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) throw new Exception("pick target exe first");
            var orig = Hex(origHex);
            uint va = Convert.ToUInt32(vaHex.Replace("0x", ""), 16);
            uint img = ImageBase(file);
            uint rva = va >= img ? va - img : va;
            uint off = RvaToOff(file, va, img);
            var data = File.ReadAllBytes(file);
            if (off + orig.Length > data.Length) throw new Exception("patch runs past EOF");
            var live = new byte[orig.Length];
            Array.Copy(data, off, live, 0, live.Length);
            bool match = live.SequenceEqual(orig);
            return new VerifyResult
            {
                Va = va, Rva = rva, Off = off,
                LiveHex = ToHex(live), Match = match,
                Msg = match ? $"MATCH rva=0x{rva:X} off=0x{off:X} ({off}) live={ToHex(live)}"
                            : $"MISMATCH rva=0x{rva:X} off=0x{off:X} live={ToHex(live)} != guard={ToHex(orig)} — refusing"
            };
        }

        public static string Apply(string file, string vaHex, string origHex, string newHex)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) throw new Exception("pick target exe first");
            var orig = Hex(origHex); var nw = Hex(newHex);
            if (orig.Length != nw.Length || orig.Length == 0) throw new Exception("orig/new same-size non-empty hex required");
            uint va = Convert.ToUInt32(vaHex.Replace("0x", ""), 16);
            uint img = ImageBase(file);
            uint off = RvaToOff(file, va, img);
            var data = File.ReadAllBytes(file);
            for (int i = 0; i < orig.Length; i++)
                if (data[off + i] != orig[i])
                    throw new Exception($"GUARD FAIL at +{i}: live {data[off + i]:X2} != {orig[i]:X2}. Refusing.");
            string bak = file + DateTime.Now.ToString(".bak_yyyyMMdd_HHmmss");
            File.Copy(file, bak, true);
            Array.Copy(nw, 0, data, off, nw.Length);
            File.WriteAllBytes(file, data);
            FixChecksum(file);
            string msg = $"applied {vaHex}: {origHex} -> {newHex}, checksum {PeChecksum(file):X8}, backup {Path.GetFileName(bak)}";
            Log(WbDir(), msg + " :: " + Path.GetFileName(file));
            return msg;
        }

        public static string[] ListBackups(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return new string[0];
            try
            {
                var dir = Path.GetDirectoryName(file); var name = Path.GetFileName(file);
                if (!Directory.Exists(dir)) return new string[0];
                return Directory.GetFiles(dir, name + ".bak_*").OrderBy(x => x).ToArray();
            }
            catch { return new string[0]; }
        }

        public static string Rollback(string file)
        {
            var cands = ListBackups(file);
            if (cands.Length == 0) throw new Exception("no backups");
            File.Copy(cands.Last(), file, true);
            FixChecksum(file);
            string msg = "rolled back from " + Path.GetFileName(cands.Last());
            Log(WbDir(), msg + " :: " + Path.GetFileName(file));
            return msg;
        }

        public static string RollbackTo(string file, string backupPath)
        {
            if (!File.Exists(backupPath)) throw new Exception("backup missing");
            File.Copy(backupPath, file, true);
            FixChecksum(file);
            return "rolled back from " + Path.GetFileName(backupPath);
        }
    }
}
