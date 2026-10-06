using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ArabianWorkbench
{
    // Lazy full-disassembly loader backed by FullReverse/<Bin>/funcs_*.json + va_index.json.
    // Light index (*.jsonl) stays in memory; heavy bodies load on demand and cache one chunk.
    public static class FunctionStore
    {
        public static string FullReverseRoot { get; private set; }

        static readonly Dictionary<string, Dictionary<string, JsonElement>> IndexCache = new();
        static readonly Dictionary<string, List<string>> SortedVas = new();
        static string lastChunkPath;
        static JsonDocument lastChunkDoc;

        static readonly Dictionary<string, JsonDocument> ReportCache = new();

        public static string ResolveRoots(string workbenchDir)
        {
            var cands = new List<string>();
            try
            {
                var cfg = Path.Combine(workbenchDir, "fullreverse.path");
                if (File.Exists(cfg))
                {
                    var p = File.ReadAllText(cfg).Trim();
                    if (!string.IsNullOrEmpty(p)) cands.Add(p);
                }
            }
            catch { }
            cands.Add(Path.Combine(workbenchDir, "..", "FullReverse"));
            cands.Add(@"C:\Users\hayat\Documents\Default Project\ArabianRace\FullReverse");
            cands.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FullReverse"));
            foreach (var c in cands)
            {
                try
                {
                    var full = Path.GetFullPath(c);
                    if (Directory.Exists(full) && File.Exists(Path.Combine(full, "Game", "va_index.json")))
                    {
                        FullReverseRoot = full;
                        return full;
                    }
                }
                catch { }
            }
            return null;
        }

        public static int IndexCount(string bin)
        {
            var idx = GetIndex(bin);
            return idx == null ? 0 : idx.Count;
        }

        static Dictionary<string, JsonElement> GetIndex(string bin)
        {
            if (IndexCache.TryGetValue(bin, out var d)) return d;
            if (FullReverseRoot == null) return null;
            try
            {
                var p = Path.Combine(FullReverseRoot, bin, "va_index.json");
                if (!File.Exists(p)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(p));
                d = new Dictionary<string, JsonElement>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                    d[prop.Name] = prop.Value.Clone();
                IndexCache[bin] = d;
                SortedVas[bin] = d.Keys.OrderBy(k => Convert.ToUInt32(k.Replace("0x", ""), 16)).ToList();
                return d;
            }
            catch { return null; }
        }

        public class FullFn
        {
            public string Va; public string Chunk; public int Idx; public int Total;
            public List<string> Insns = new();
            public List<string> Calls = new();
            public List<string> Strings = new();
        }

        public static FullFn GetFull(string bin, string va)
        {
            var res = new FullFn { Va = va };
            if (FullReverseRoot == null || string.IsNullOrEmpty(va)) return res;
            var key = va.ToLower();
            var idx = GetIndex(bin);
            string chunk = null; int at = -1, total = 0;
            if (idx != null && idx.TryGetValue(key, out var ent))
            {
                // stored as [chunkFile, index, total]
                var arr = ent.EnumerateArray().ToArray();
                chunk = arr[0].GetString(); at = arr[1].GetInt32(); total = arr[2].GetInt32();
                res.Chunk = chunk; res.Idx = at; res.Total = total;
            }
            else return res; // not indexed (e.g. synthetic base entry)
            try
            {
                var path = Path.Combine(FullReverseRoot, bin, chunk);
                if (lastChunkPath != path)
                {
                    lastChunkDoc?.Dispose();
                    lastChunkDoc = JsonDocument.Parse(File.ReadAllText(path));
                    lastChunkPath = path;
                }
                var el = lastChunkDoc.RootElement[at];
                foreach (var s in el.GetProperty("insns").EnumerateArray())
                {
                    var line = s.GetString();
                    res.Insns.Add(line);
                }
                // derive calls + string-ish refs (cheap, display-only)
                foreach (var line in res.Insns)
                {
                    var ci = line.IndexOf("call 0x");
                    if (ci >= 0)
                    {
                        var tok = new string(line.Skip(ci + 5).TakeWhile(c => Uri.IsHexDigit(c) || c == 'x').ToArray());
                        if (tok.Length > 3) res.Calls.Add(tok);
                    }
                }
                res.Calls = res.Calls.Distinct().Take(40).ToList();
            }
            catch { }
            return res;
        }

        public static string FindContaining(string bin, uint faultVa)
        {
            var idx = GetIndex(bin);
            if (idx == null) return null;
            var sorted = SortedVas[bin];
            string best = null;
            // binary search over sorted VAs
            int lo = 0, hi = sorted.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                uint v = Convert.ToUInt32(sorted[mid].Replace("0x", ""), 16);
                if (v == faultVa) return sorted[mid];
                if (v < faultVa) { best = sorted[mid]; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best;
        }

        public static string ReportSummary(string bin)
        {
            try
            {
                if (FullReverseRoot == null) return "";
                if (!ReportCache.TryGetValue(bin, out var doc))
                {
                    var p = Path.Combine(FullReverseRoot, bin, "report.json");
                    if (!File.Exists(p)) return "";
                    doc = JsonDocument.Parse(File.ReadAllText(p));
                    ReportCache[bin] = doc;
                }
                var r = doc.RootElement;
                string sha = r.TryGetProperty("sha256", out var s) ? s.GetString().Substring(0, 12) : "?";
                string ib = r.TryGetProperty("imagebase", out var b) ? b.GetString() : "?";
                int funcs = r.TryGetProperty("funcs", out var f) ? f.GetInt32() : 0;
                int ins = r.TryGetProperty("insns_total", out var ii) ? ii.GetInt32() : 0;
                return $"funcs={funcs} insns={ins} base={ib} sha={sha}…";
            }
            catch { return ""; }
        }

        // ---------- deeper viewer: callers / imports / strings / deep search ----------
        static readonly Dictionary<string, JsonDocument> CallersCache = new();

        public static (int total, List<string> callers) GetCallers(string bin, string va)
        {
            try
            {
                if (FullReverseRoot == null) return (0, new List<string>());
                if (!CallersCache.TryGetValue(bin, out var doc))
                {
                    var p = Path.Combine(FullReverseRoot, bin, "callers_index.json");
                    if (!File.Exists(p)) return (0, new List<string>());
                    doc = JsonDocument.Parse(File.ReadAllText(p));
                    CallersCache[bin] = doc;
                }
                var key = va.ToLower();
                if (!doc.RootElement.TryGetProperty(key, out var ent)) return (0, new List<string>());
                int n = ent.GetProperty("n").GetInt32();
                var list = ent.GetProperty("callers").EnumerateArray().Select(x => x.GetString()).ToList();
                return (n, list);
            }
            catch { return (0, new List<string>()); }
        }

        public static string ExePathForBin(string bin) => bin switch
        {
            "Client" => @"C:\Users\hayat\Desktop\VSRO_Client\sro_client.exe",
            "Game" => @"C:\Users\hayat\Downloads\VSRO_TestIn\SR_GameServer.exe",
            "Shard" => @"C:\Users\hayat\Downloads\VSRO_TestIn\SR_ShardManager.exe",
            "Agent" => @"C:\Users\hayat\Downloads\VSRO_TestIn\AgentServer.exe",
            "Farm" => @"C:\Users\hayat\Downloads\VSRO_TestIn\FarmManager.exe",
            "Machine" => @"C:\Users\hayat\Downloads\VSRO_TestIn\MachineManager.exe",
            "Gateway" => @"C:\Users\hayat\Downloads\VSRO_TestIn\GatewayServer.exe",
            "Download" => @"C:\Users\hayat\Downloads\VSRO_TestIn\DownloadServer.exe",
            "Global" => @"C:\Users\hayat\Downloads\VSRO_TestIn\GlobalManager.exe",
            _ => null,
        };

        // IAT map: VA -> "dll!func", parsed from target exe import directory (32-bit PE).
        static readonly Dictionary<string, Dictionary<uint, string>> IatCache = new();
        public static Dictionary<uint, string> GetIat(string bin)
        {
            if (IatCache.TryGetValue(bin, out var m)) return m;
            m = new Dictionary<uint, string>();
            IatCache[bin] = m;
            try
            {
                var path = ExePathForBin(bin);
                if (path == null || !File.Exists(path)) return m;
                var d = File.ReadAllBytes(path);
                int pe = BitConverter.ToInt32(d, 0x3C);
                ushort magic = BitConverter.ToUInt16(d, pe + 24);
                if (magic != 0x10b) return m; // expect PE32
                uint img = BitConverter.ToUInt32(d, pe + 24 + 28);
                int nsec = BitConverter.ToUInt16(d, pe + 6);
                int optsz = BitConverter.ToUInt16(d, pe + 20);
                int secs = pe + 24 + optsz;
                uint impRva = BitConverter.ToUInt32(d, pe + 24 + 104);
                //uint impSz = BitConverter.ToUInt32(d, pe + 24 + 108);
                System.Func<uint, int> rva2off = (rva) =>
                {
                    for (int i = 0; i < nsec; i++)
                    {
                        int o = secs + i * 40;
                        uint va = BitConverter.ToUInt32(d, o + 12);
                        uint vsz = BitConverter.ToUInt32(d, o + 8);
                        uint rsz = BitConverter.ToUInt32(d, o + 16);
                        uint raw = BitConverter.ToUInt32(d, o + 20);
                        if (rva >= va && rva < va + System.Math.Max(vsz, rsz)) return (int)(rva - va + raw);
                    }
                    return -1;
                };
                for (int di = 0; ; di++)
                {
                    int doff = rva2off(impRva + (uint)(di * 20));
                    if (doff < 0 || doff + 20 > d.Length) break;
                    uint ilt = BitConverter.ToUInt32(d, doff + 0);
                    uint nameRva = BitConverter.ToUInt32(d, doff + 12);
                    uint iat = BitConverter.ToUInt32(d, doff + 16);
                    if (ilt == 0 && nameRva == 0 && iat == 0) break;
                    int noff = rva2off(nameRva);
                    if (noff < 0) continue;
                    int zn = System.Array.IndexOf(d, (byte)0, noff);
                    string dll = System.Text.Encoding.ASCII.GetString(d, noff, (zn < 0 ? 32 : zn - noff));
                    uint thunkRva = ilt != 0 ? ilt : iat;
                    for (int k = 0; k < 512; k++)
                    {
                        int toff = rva2off(thunkRva + (uint)(k * 4));
                        if (toff < 0 || toff + 4 > d.Length) break;
                        uint entry = BitConverter.ToUInt32(d, toff);
                        if (entry == 0) break;
                        if ((entry & 0x80000000) != 0) { m[img + iat + (uint)(k * 4)] = $"{dll}!ord_{entry & 0xFFFF}"; continue; }
                        int hoff = rva2off(entry);
                        if (hoff < 0 || hoff + 3 > d.Length) continue;
                        int s = hoff + 2; int e = s;
                        while (e < d.Length && d[e] != 0 && e - s < 128) e++;
                        string fn = System.Text.Encoding.ASCII.GetString(d, s, e - s);
                        m[img + iat + (uint)(k * 4)] = $"{dll}!{fn}";
                    }
                    if (di > 64) break;
                }
            }
            catch { }
            return m;
        }

        public static string TryReadAscii(string bin, uint va, int max = 64)
        {
            try
            {
                var path = ExePathForBin(bin);
                if (path == null || !File.Exists(path)) return null;
                var d = File.ReadAllBytes(path);
                int pe = BitConverter.ToInt32(d, 0x3C);
                uint img = BitConverter.ToUInt32(d, pe + 24 + 28);
                ushort nsec = BitConverter.ToUInt16(d, pe + 6);
                int optsz = BitConverter.ToUInt16(d, pe + 20);
                int secs = pe + 24 + optsz;
                uint rva = va >= img ? va - img : va;
                foreach (var _ in new int[1])
                {
                    for (int i = 0; i < nsec; i++)
                    {
                        int o = secs + i * 40;
                        uint v = BitConverter.ToUInt32(d, o + 12);
                        uint vsz = BitConverter.ToUInt32(d, o + 8);
                        uint rsz = BitConverter.ToUInt32(d, o + 16);
                        uint raw = BitConverter.ToUInt32(d, o + 20);
                        if (rva >= v && rva < v + System.Math.Max(vsz, rsz))
                        {
                            int off = (int)(rva - v + raw);
                            if (off < 0 || off >= d.Length) return null;
                            int e = off;
                            while (e < d.Length && d[e] != 0 && e - off < max) e++;
                            if (e - off < 4) return null;
                            var bytes = new byte[e - off];
                            System.Array.Copy(d, off, bytes, 0, bytes.Length);
                            if (bytes.All(b => b >= 0x20 && b < 0x7F || b == 0x0A || b == 0x0D)) return System.Text.Encoding.ASCII.GetString(bytes);
                            return null;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // ---------- RTTI class browser ----------
        public class RttiClass { public string name; public string kind; public string mangled; public string va; }
        static readonly Dictionary<string, List<RttiClass>> RttiCache = new();
        public static List<RttiClass> GetRtti(string bin)
        {
            if (RttiCache.TryGetValue(bin, out var l)) return l;
            l = new List<RttiClass>();
            RttiCache[bin] = l;
            try
            {
                if (FullReverseRoot == null) return l;
                var p = Path.Combine(FullReverseRoot, bin, "rtti_map.json");
                if (!File.Exists(p)) return l;
                using var doc = JsonDocument.Parse(File.ReadAllText(p));
                foreach (var e in doc.RootElement.EnumerateArray())
                    l.Add(new RttiClass
                    {
                        name = e.GetProperty("name").GetString(),
                        kind = e.GetProperty("kind").GetString(),
                        mangled = e.GetProperty("mangled").GetString(),
                        va = e.GetProperty("va").GetString(),
                    });
            }
            catch { }
            return l;
        }

        public class RttiHit { public string funcVa; public string line; }
        public static List<RttiHit> GetRttiRefs(string bin, string strVaHex, int cap = 200)
        {
            var hits = new List<RttiHit>();
            if (FullReverseRoot == null) return hits;
            uint target;
            try { target = Convert.ToUInt32(strVaHex.Replace("0x", ""), 16); }
            catch { return hits; }
            var dir = Path.Combine(FullReverseRoot, bin);
            if (!Directory.Exists(dir)) return hits;
            foreach (var chunk in Directory.GetFiles(dir, "funcs_*.json").OrderBy(x => x))
            {
                JsonDocument doc = null;
                try { doc = JsonDocument.Parse(File.ReadAllText(chunk)); }
                catch { continue; }
                using (doc)
                {
                    foreach (var f in doc.RootElement.EnumerateArray())
                    {
                        string fva = f.GetProperty("va").GetString();
                        foreach (var ins in f.GetProperty("insns").EnumerateArray())
                        {
                            string line = ins.GetString();
                            if (line == null || line.IndexOf("0x") < 0) continue;
                            bool found = false;
                            foreach (System.Text.RegularExpressions.Match mt in System.Text.RegularExpressions.Regex.Matches(line, @"0x[0-9a-fA-F]+"))
                            {
                                try { if (Convert.ToUInt32(mt.Value.Replace("0x", ""), 16) == target) { found = true; break; } }
                                catch { }
                            }
                            if (found)
                            {
                                hits.Add(new RttiHit { funcVa = fva, line = line });
                                if (hits.Count >= cap) return hits;
                                break;
                            }
                        }
                    }
                }
            }
            return hits;
        }

        public class DeepHit { public string bin; public string va; public string line; }
        public static List<DeepHit> DeepSearch(string scopeBinOrAll, string term, int cap = 300)
        {
            var hits = new List<DeepHit>();
            if (FullReverseRoot == null || string.IsNullOrWhiteSpace(term)) return hits;
            term = term.ToLower();
            string[] bins = scopeBinOrAll == "All binaries" || scopeBinOrAll == null
                ? new[] { "Client", "Game", "Shard", "Agent", "Farm", "Machine", "Gateway", "Download", "Global" }
                : new[] { scopeBinOrAll };
            foreach (var bin in bins)
            {
                var dir = Path.Combine(FullReverseRoot, bin);
                if (!Directory.Exists(dir)) continue;
                foreach (var chunk in Directory.GetFiles(dir, "funcs_*.json").OrderBy(x => x))
                {
                    JsonDocument doc = null;
                    try { doc = JsonDocument.Parse(File.ReadAllText(chunk)); }
                    catch { continue; }
                    using (doc)
                    {
                        foreach (var f in doc.RootElement.EnumerateArray())
                        {
                            string va = f.GetProperty("va").GetString();
                            foreach (var s in f.GetProperty("insns").EnumerateArray())
                            {
                                string line = s.GetString();
                                if (line != null && line.ToLower().Contains(term))
                                {
                                    hits.Add(new DeepHit { bin = bin, va = va, line = line });
                                    if (hits.Count >= cap) return hits;
                                    break; // one hit per function keeps list scannable
                                }
                            }
                        }
                    }
                }
            }
            return hits;
        }

        // ---------- 3: hot functions (call_top200) ----------
        static readonly Dictionary<string, Dictionary<string, int>> HotCache = new();
        public static Dictionary<string, int> GetHot(string bin)
        {
            if (HotCache.TryGetValue(bin, out var m)) return m;
            m = new Dictionary<string, int>();
            HotCache[bin] = m;
            try
            {
                if (FullReverseRoot == null) return m;
                var p = Path.Combine(FullReverseRoot, bin, "call_top200.json");
                if (!File.Exists(p)) return m;
                using var doc = JsonDocument.Parse(File.ReadAllText(p));
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    string va = row[0].GetString().ToLower();
                    int n = row[1].GetInt32();
                    m[va] = n;
                }
            }
            catch { }
            return m;
        }

        // ---------- 1: inverse string search ----------
        public class StrEntry { public string bin; public string va; public string text; }
        static readonly Dictionary<string, List<StrEntry>> StrCache = new();
        static List<StrEntry> GetStrings(string bin)
        {
            if (StrCache.TryGetValue(bin, out var l)) return l;
            l = new List<StrEntry>();
            StrCache[bin] = l;
            try
            {
                if (FullReverseRoot == null) return l;
                var p = Path.Combine(FullReverseRoot, bin, "strings_map.json");
                if (!File.Exists(p)) return l;
                using var doc = JsonDocument.Parse(File.ReadAllText(p));
                foreach (var e in doc.RootElement.EnumerateArray())
                    l.Add(new StrEntry { bin = bin, va = e.GetProperty("va").GetString(), text = e.GetProperty("text").GetString() });
            }
            catch { }
            return l;
        }

        public class StrHit { public string bin; public string funcVa; public string strVa; public string strText; public string line; }
        public static (List<StrEntry> strings, List<StrHit> funcs) InverseStringSearch(string scopeBinOrAll, string substr, int capStr = 200, int capFunc = 300)
        {
            var matched = new List<StrEntry>();
            var hits = new List<StrHit>();
            if (FullReverseRoot == null || string.IsNullOrWhiteSpace(substr) || substr.Length < 2) return (matched, hits);
            string[] bins = scopeBinOrAll == "All binaries" || scopeBinOrAll == null
                ? new[] { "Client", "Game", "Shard", "Agent", "Farm", "Machine", "Gateway", "Download", "Global" }
                : new[] { scopeBinOrAll };
            // step 1: strings containing substr
            var want = new Dictionary<string, HashSet<uint>>(); // bin -> set of string VAs
            foreach (var bin in bins)
            {
                foreach (var s in GetStrings(bin))
                {
                    if (s.text.IndexOf(substr, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matched.Add(s);
                        if (matched.Count >= capStr) break;
                    }
                }
                if (matched.Count >= capStr) break;
            }
            foreach (var s in matched)
            {
                if (!want.TryGetValue(s.bin, out var set)) { set = new HashSet<uint>(); want[s.bin] = set; }
                try { set.Add(Convert.ToUInt32(s.va.Replace("0x", ""), 16)); } catch { }
            }
            if (matched.Count == 0) return (matched, hits);
            var strByVa = new Dictionary<string, StrEntry>();
            foreach (var s in matched) strByVa[s.bin + "|" + s.va.ToLower()] = s;
            // step 2: functions whose body references any matched string VA
            foreach (var kv in want)
            {
                string bin = kv.Key; var set = kv.Value;
                var dir = Path.Combine(FullReverseRoot, bin);
                if (!Directory.Exists(dir)) continue;
                foreach (var chunk in Directory.GetFiles(dir, "funcs_*.json").OrderBy(x => x))
                {
                    JsonDocument doc = null;
                    try { doc = JsonDocument.Parse(File.ReadAllText(chunk)); }
                    catch { continue; }
                    using (doc)
                    {
                        foreach (var f in doc.RootElement.EnumerateArray())
                        {
                            string fva = f.GetProperty("va").GetString();
                            foreach (var ins in f.GetProperty("insns").EnumerateArray())
                            {
                                string line = ins.GetString();
                                if (line == null) continue;
                                int ci = line.IndexOf("0x");
                                if (ci < 0) continue;
                                bool found = false; string hitVa = null;
                                foreach (System.Text.RegularExpressions.Match mt in System.Text.RegularExpressions.Regex.Matches(line.Substring(ci), @"0x[0-9a-fA-F]+"))
                                {
                                    try
                                    {
                                        uint v = Convert.ToUInt32(mt.Value.Replace("0x", ""), 16);
                                        if (set.Contains(v)) { found = true; hitVa = "0x" + v.ToString("x"); break; }
                                    }
                                    catch { }
                                }
                                if (found)
                                {
                                    strByVa.TryGetValue(bin + "|" + hitVa, out var se);
                                    hits.Add(new StrHit { bin = bin, funcVa = fva, strVa = hitVa, strText = se?.text ?? "", line = line });
                                    if (hits.Count >= capFunc) return (matched, hits);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            return (matched, hits);
        }
    }
}
