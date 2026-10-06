using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ArabianWorkbench
{
    public class Fn { public string va { get; set; } public string name { get; set; } public string example { get; set; } public string comment { get; set; } public int ncalls { get; set; } public string area { get; set; } public string bin { get; set; } }

    public partial class MainWindow : Window
    {
        List<Fn> all = new();
        Dictionary<string, Fn> byKey = new();
        string WorkbenchDir = "";
        Dictionary<string, string> editedComments = new(); // key bin|va
        string stagedOrig, stagedNew, stagedRva, stagedFile;
        FunctionStore.FullFn curFull;
        Fn curFn;
        readonly List<(string bin, string va)> navBack = new();
        readonly List<(string bin, string va)> navFwd = new();
        bool navJumping = false;

        static readonly string[] Bins = new[] { "Client", "Game", "Shard", "Agent", "Farm", "Machine", "Gateway", "Download", "Global" };
        static readonly Dictionary<string, string> Titles = new()
        {
            { "Client", "Client — sro_client.exe" }, { "Game", "GameServer — SR_GameServer.exe" }, { "Shard", "ShardManager — SR_ShardManager.exe" },
            { "Agent", "AgentServer — AgentServer.exe" }, { "Farm", "FarmManager — FarmManager.exe" }, { "Machine", "MachineManager — MachineManager.exe" },
            { "Gateway", "GatewayServer — GatewayServer.exe" }, { "Download", "DownloadServer — DownloadServer.exe" }, { "Global", "GlobalManager — GlobalManager.exe" },
        };
        static readonly Dictionary<string, string> DefaultExe = new()
        {
            { "Client", @"C:\Users\hayat\Desktop\VSRO_Client\sro_client.exe" },
            { "Game", @"C:\Users\hayat\Downloads\VSRO_TestIn\SR_GameServer.exe" },
            { "Shard", @"C:\Users\hayat\Downloads\VSRO_TestIn\SR_ShardManager.exe" },
            { "Agent", @"C:\Users\hayat\Downloads\VSRO_TestIn\AgentServer.exe" },
            { "Farm", @"C:\Users\hayat\Downloads\VSRO_TestIn\FarmManager.exe" },
            { "Machine", @"C:\Users\hayat\Downloads\VSRO_TestIn\MachineManager.exe" },
            { "Gateway", @"C:\Users\hayat\Downloads\VSRO_TestIn\GatewayServer.exe" },
            { "Download", @"C:\Users\hayat\Downloads\VSRO_TestIn\DownloadServer.exe" },
            { "Global", @"C:\Users\hayat\Downloads\VSRO_TestIn\GlobalManager.exe" },
        };

        public MainWindow()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("/selftest") || args.Contains("/selftest2")) { RunSelfTestHeadless(args); Application.Current.Shutdown(0); return; }
            InitializeComponent();
            InitCombos();
            LoadDb();
            FunctionStore.ResolveRoots(WorkbenchDir);
            LoadComments();
            LoadSeeds();
            RefreshHistory();
            BuildTree("");
            SearchBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) BuildTree(SearchBox.Text); };
            AsmFilterBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) OnAsmFilter(s, null); };
            JumpBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) OnJump(s, null); };
            DeepBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) OnDeepSearch(s, null); };
            DeepScope.Items.Add("All binaries");
            foreach (var b in Bins) DeepScope.Items.Add(b);
            DeepScope.SelectedIndex = 0;
            StrBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) OnStrSearch(s, null); };
            RttiScope.Items.Add("All binaries");
            foreach (var b in Bins) RttiScope.Items.Add(b);
            RttiScope.SelectedIndex = 2; // Game first (Ref/world classes live here)
            RttiBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) OnRttiList(s, null); };
            LoadPins();
        }

        void RunSelfTestHeadless(string[] args)
        {
            WorkbenchDir = FindWorkbenchDir();
            // headless: no UI touches
            all.Clear();
            foreach (var f in Directory.GetFiles(WorkbenchDir, "*.jsonl"))
            {
                var bin = Path.GetFileNameWithoutExtension(f);
                foreach (var line in File.ReadLines(f))
                {
                    try { var fn = JsonSerializer.Deserialize<Fn>(line); fn.bin = bin; all.Add(fn); } catch { }
                }
            }
            Console.WriteLine($"SELFTEST funcs={all.Count}");
            if (all.Count < 10000) Environment.Exit(1);
            if (args.Contains("/selftest2"))
            {
                FunctionStore.ResolveRoots(WorkbenchDir);
                var full = FunctionStore.GetFull("Game", "0x59c4e0");
                Console.WriteLine($"SELFTEST2 full Game 0x59c4e0 insns={full.Insns.Count} chunk={full.Chunk}");
                var full2 = FunctionStore.GetFull("Client", "0x430dda");
                Console.WriteLine($"SELFTEST2 full Client 0x430dda insns={full2.Insns.Count} chunk={full2.Chunk}");
                Console.WriteLine($"SELFTEST2 patches={PatchesPath()}");
                try
                {
                    var doc = JsonDocument.Parse(File.ReadAllText(PatchesPath()));
                    Console.WriteLine($"SELFTEST2 seeds={doc.RootElement.GetProperty("patches").GetArrayLength()}");
                }
                catch (Exception ex) { Console.WriteLine("SELFTEST2 seeds ERR " + ex.Message); Environment.Exit(1); }
                if (full.Insns.Count == 0) Environment.Exit(1);
                var (nt, cl) = FunctionStore.GetCallers("Game", "0x59c4e0");
                Console.WriteLine($"SELFTEST2 callers 59c4e0 total={nt} shown={cl.Count}");
                var (nt2, cl2) = FunctionStore.GetCallers("Client", "0x430a50");
                Console.WriteLine($"SELFTEST2 callers 430a50 total={nt2} shown={cl2.Count}");
                var iat = FunctionStore.GetIat("Game");
                Console.WriteLine($"SELFTEST2 iat Game={iat.Count}");
                foreach (var kv in iat.Take(3)) Console.WriteLine($"SELFTEST2 iat {kv.Key:X8} -> {kv.Value}");
                var asc = FunctionStore.TryReadAscii("Game", 0xB88438, 72);
                Console.WriteLine($"SELFTEST2 ascii B88438={(asc == null ? "(null)" : asc.Substring(0, Math.Min(60, asc.Length)))}");
                var hits = FunctionStore.DeepSearch("Game", "cmp al, 2", 5);
                Console.WriteLine($"SELFTEST2 deep cmp-al-2 hits={hits.Count}");
                foreach (var h in hits.Take(3)) Console.WriteLine($"SELFTEST2 hit {h.bin} {h.va} {h.line.Trim()}");
                var hot = FunctionStore.GetHot("Game");
                Console.WriteLine($"SELFTEST2 hot Game={hot.Count}");
                foreach (var kv in hot.OrderByDescending(k => k.Value).Take(3)) Console.WriteLine($"SELFTEST2 hot {kv.Key} x{kv.Value}");
                var (strs, sfuncs) = FunctionStore.InverseStringSearch("Game", "Siege", 200, 5);
                Console.WriteLine($"SELFTEST2 str Siege strings={strs.Count} funcs={sfuncs.Count}");
                foreach (var h in sfuncs.Take(3)) Console.WriteLine($"SELFTEST2 strhit {h.bin} {h.funcVa} \"{h.strText}\"");
                Console.WriteLine($"SELFTEST2 rtti Game={FunctionStore.GetRtti("Game").Count} Client={FunctionStore.GetRtti("Client").Count} Shard={FunctionStore.GetRtti("Shard").Count}");
                var rr = FunctionStore.GetRttiRefs("Game", "0xc6e9c8", 5);
                Console.WriteLine($"SELFTEST2 rttirefs RefObjCommon={rr.Count}");
                foreach (var h in rr.Take(3)) Console.WriteLine($"SELFTEST2 rttihit {h.funcVa} {h.line.Trim()}");
            }
        }

        void RunSelfTest()
        {
            InitCombos();
            LoadDb();
            Console.WriteLine($"SELFTEST funcs={all.Count}");
            if (all.Count < 10000) Environment.Exit(1);
            // extended: full-disasm + verify dry-run when /selftest2
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("/selftest2"))
            {
                FunctionStore.ResolveRoots(WorkbenchDir);
                var full = FunctionStore.GetFull("Game", "0x59c4e0");
                Console.WriteLine($"SELFTEST2 full Game 0x59c4e0 insns={full.Insns.Count} chunk={full.Chunk}");
                var full2 = FunctionStore.GetFull("Client", "0x430dda");
                Console.WriteLine($"SELFTEST2 full Client 0x430dda insns={full2.Insns.Count} chunk={full2.Chunk}");
                Console.WriteLine($"SELFTEST2 patches={PatchesPath()}");
                try
                {
                    var doc = JsonDocument.Parse(File.ReadAllText(PatchesPath()));
                    Console.WriteLine($"SELFTEST2 seeds={doc.RootElement.GetProperty("patches").GetArrayLength()}");
                }
                catch (Exception ex) { Console.WriteLine("SELFTEST2 seeds ERR " + ex.Message); Environment.Exit(1); }
                if (full.Insns.Count == 0) Environment.Exit(1);
            }
        }

        string PatchesPath()
        {
            var c1 = Path.Combine(WorkbenchDir, "..", "patches.json");
            if (File.Exists(Path.GetFullPath(c1))) return Path.GetFullPath(c1);
            var c2 = Path.Combine(WorkbenchDir, "patches.json");
            if (File.Exists(c2)) return c2;
            var c3 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "patches.json");
            if (File.Exists(c3)) return c3;
            return Path.GetFullPath(c1);
        }

        string FindWorkbenchDir()
        {
            var cands = new[]
            {
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..")),
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..")),
                AppDomain.CurrentDomain.BaseDirectory,
                @"C:\Users\hayat\Documents\Default Project\ArabianRace\Workbench",
            };
            foreach (var c in cands)
            {
                try { if (Directory.Exists(c) && Directory.GetFiles(c, "*.jsonl").Length > 0) return Path.GetFullPath(c); }
                catch { }
            }
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
        }

        void InitCombos()
        {
            BinCombo.Items.Add("All binaries");
            foreach (var b in Bins) BinCombo.Items.Add(Titles[b]);
            BinCombo.SelectedIndex = 0;
        }

        void LoadDb()
        {
            all.Clear(); byKey.Clear();
            WorkbenchDir = FindWorkbenchDir();
            foreach (var f in Directory.GetFiles(WorkbenchDir, "*.jsonl"))
            {
                var bin = Path.GetFileNameWithoutExtension(f);
                foreach (var line in File.ReadLines(f))
                {
                    try { var fn = JsonSerializer.Deserialize<Fn>(line); fn.bin = bin; all.Add(fn); byKey[bin + "|" + fn.va.ToLower()] = fn; } catch { }
                }
            }
            // area combo from data
            var areas = all.Select(x => x.area ?? "General").Distinct().OrderBy(a => a == "General" ? "~~~" : a).ToList();
            AreaCombo.Items.Clear();
            AreaCombo.Items.Add("All areas");
            foreach (var a in areas) AreaCombo.Items.Add(a);
            AreaCombo.SelectedIndex = 0;
            var per = string.Join(" ", Bins.Select(b => $"{b}={all.Count(x => x.bin == b)}"));
            try { CountsBlock.Text = $"total {all.Count} · {per}"; } catch { }
        }

        string SelBin() // null = all
        {
            if (BinCombo.SelectedIndex <= 0) return null;
            return Bins[BinCombo.SelectedIndex - 1];
        }
        string SelArea()
        {
            if (AreaCombo.SelectedIndex <= 0) return null;
            return AreaCombo.SelectedItem as string;
        }

        void OnHotToggle(object s, RoutedEventArgs e) => BuildTree(SearchBox.Text);

        string HotBadge(string bin, string va)
        {
            var hot = FunctionStore.GetHot(bin);
            if (hot.TryGetValue(va.ToLower(), out var n)) return $" 🔥{n}";
            return "";
        }

        void AddFuncNode(TreeViewItem parent, Fn g)
        {
            var ti = new TreeViewItem { Header = g.name + HotBadge(g.bin, g.va), Tag = g, IsExpanded = false };
            ti.Items.Add(new TreeViewItem { Header = "(open to inspect)" });
            ti.Expanded += (s, e) =>
            {
                var t = (TreeViewItem)s;
                if (t.Items.Count == 1 && ((TreeViewItem)t.Items[0]).Header as string == "(open to inspect)")
                {
                    t.Items.Clear();
                    var fn = (Fn)t.Tag;
                    t.Items.Add(new TreeViewItem { Header = "example: " + fn.example });
                    t.Items.Add(new TreeViewItem { Header = "comment: " + (fn.comment ?? "") });
                }
            };
            parent.Items.Add(ti);
        }

        void BuildTree(string q)
        {
            FuncTree.Items.Clear();
            q = (q ?? "").ToLower();
            string fb = SelBin(), fa = SelArea();
            bool hotFirst = HotFirstBox != null && HotFirstBox.IsChecked == true;
            var bins = fb == null ? Bins : new[] { fb };
            foreach (var b in bins)
            {
                var bnode = new TreeViewItem { Header = $"{Titles[b]} ({all.Count(x => x.bin == b)})" };
                // hot node: top xref targets that are real functions, sorted
                if (fa == null)
                {
                    var hot = FunctionStore.GetHot(b);
                    if (hot.Count > 0)
                    {
                        var hnode = new TreeViewItem { Header = $"🔥 Hot (top {Math.Min(30, hot.Count)} by xrefs)" };
                        foreach (var kv in hot.OrderByDescending(k => k.Value).Take(30))
                        {
                            if (byKey.TryGetValue(b + "|" + kv.Key, out var hf)) AddFuncNode(hnode, hf);
                        }
                        if (hnode.Items.Count > 0) bnode.Items.Add(hnode);
                    }
                }
                var groups = all.Where(x => x.bin == b
                    && (fa == null || (x.area ?? "General") == fa)
                    && (string.IsNullOrEmpty(q) || x.name.ToLower().Contains(q) || x.va.ToLower().Contains(q) || (x.comment ?? "").ToLower().Contains(q) || (x.area ?? "").ToLower().Contains(q)));
                bool anyQ = !string.IsNullOrEmpty(q);
                foreach (var area in groups.GroupBy(x => x.area ?? "General").OrderBy(g => g.Key == "General" ? "~~~" : g.Key))
                {
                    var anode = new TreeViewItem { Header = area.Key + " (" + area.Count() + ")" };
                    System.Collections.Generic.IEnumerable<Fn> ordered = area;
                    if (hotFirst)
                    {
                        var hot = FunctionStore.GetHot(b);
                        ordered = area.OrderByDescending(g => hot.TryGetValue(g.va.ToLower(), out var n) ? n : 0).ThenBy(g => g.name);
                    }
                    foreach (var g in ordered.Take(anyQ ? 4000 : 600)) AddFuncNode(anode, g);
                    bnode.Items.Add(anode);
                }
                FuncTree.Items.Add(bnode);
            }
            Status($"9 binaries × areas. {(string.IsNullOrEmpty(q) ? "Top 600/area shown — search to widen." : "Filtered view.")}{(hotFirst ? " Hot-first ON." : "")} FullReverse: {FunctionStore.FullReverseRoot ?? "(not found)"}");
        }

        // ---------- selection + full disassembly + xrefs ----------
        void OnSelect(object s, RoutedPropertyChangedEventArgs<object> e)
        {
            if (FuncTree.SelectedItem is TreeViewItem ti && ti.Tag is Fn fn)
            {
                if (!navJumping && curFn != null && (curFn.bin != fn.bin || curFn.va != fn.va))
                { navBack.Add((curFn.bin, curFn.va)); if (navBack.Count > 100) navBack.RemoveAt(0); navFwd.Clear(); }
                ShowFn(fn);
            }
        }

        void ShowFn(Fn fn)
        {
            curFn = fn;
            NameBox.Text = fn.name; VaBox.Text = fn.va;
            MetaBlock.Text = $"{fn.bin} · {fn.area ?? "General"} · {fn.va}";
            ExampleBlock.Text = (fn.example ?? "").Replace("; ", "\n");
            CommentBox.Text = GetComment(fn);
            BinInfoBlock.Text = FunctionStore.ReportSummary(fn.bin);
            LoadFull(fn);
            if (string.IsNullOrEmpty(ExeBox.Text) && DefaultExe.TryGetValue(fn.bin, out var d)) ExeBox.Text = d;
        }

        Fn Lookup(string bin, string va)
        {
            if (byKey.TryGetValue(bin + "|" + va.ToLower(), out var f)) return f;
            // mid-function VA (patch site / crash site): keep EXACT va so VaBox stays
            // patch-ready; LoadFull falls back to the container for display.
            return new Fn { bin = bin, va = va.ToLower(), name = $"Jump_{va.ToLower()} [{va.ToLower()}]", area = "General", example = "", comment = "" };
        }

        void JumpTo(string bin, string va)
        {
            navJumping = true;
            try
            {
                if (curFn != null) { navBack.Add((curFn.bin, curFn.va)); navFwd.Clear(); }
                ShowFn(Lookup(bin, va));
            }
            finally { navJumping = false; }
        }

        void OnNavBack(object s, RoutedEventArgs e)
        {
            if (navBack.Count == 0 || curFn == null) return;
            navFwd.Add((curFn.bin, curFn.va));
            var prev = navBack[navBack.Count - 1]; navBack.RemoveAt(navBack.Count - 1);
            navJumping = true; try { ShowFn(Lookup(prev.bin, prev.va)); } finally { navJumping = false; }
        }
        void OnNavFwd(object s, RoutedEventArgs e)
        {
            if (navFwd.Count == 0 || curFn == null) return;
            navBack.Add((curFn.bin, curFn.va));
            var nxt = navFwd[navFwd.Count - 1]; navFwd.RemoveAt(navFwd.Count - 1);
            navJumping = true; try { ShowFn(Lookup(nxt.bin, nxt.va)); } finally { navJumping = false; }
        }
        void OnJump(object s, RoutedEventArgs e)
        {
            var t = (JumpBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(t) || curFn == null) return;
            if (!t.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)) t = "0x" + t;
            JumpTo(curFn.bin, t.ToLower());
        }
        void OnCallerJump(object s, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (CallerList.SelectedItem is string line && curFn != null)
            {
                var va = line.TrimStart().Split(' ')[0].Trim();
                if (va.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)) JumpTo(curFn.bin, va.ToLower());
            }
        }
        void OnExpand2(object s, RoutedEventArgs e)
        {
            if (curFn == null || curFull == null) return;
            string bin = curFn.bin, va = VaBox.Text.ToLower();
            // if container fallback active, use container VA from AsmSrcBlock? keep curFull.Va
            if (curFull.Va != null && curFull.Insns.Count > 0) va = curFull.Va.ToLower();
            CallerList.Items.Clear();
            var (total, callers) = FunctionStore.GetCallers(bin, va);
            CallerHdr.Text = $"{total} xrefs — 2-level tree (top 20 callers × top 8 grand-callers)";
            int shown = 0;
            foreach (var c in callers.Take(20))
            {
                string nm = FnName(bin, c);
                CallerList.Items.Add(string.IsNullOrEmpty(nm) ? c : $"{c}  {nm}");
                shown++;
                var (gt, gc) = FunctionStore.GetCallers(bin, c);
                foreach (var g in gc.Take(8))
                {
                    string gn = FnName(bin, g);
                    CallerList.Items.Add(string.IsNullOrEmpty(gn) ? $"  └─ {g}  (via {c})" : $"  └─ {g}  {gn}  (via {c})");
                    shown++;
                }
            }
            if (shown == 0) CallerHdr.Text = "no indexed callers to expand";
        }
        void OnCollapseCallers(object s, RoutedEventArgs e)
        {
            if (curFn == null || curFull == null) return;
            FillXrefs(curFn.bin, curFull.Va ?? curFn.va, curFull);
        }
        void OnCalleeJump(object s, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (CalleeList.SelectedItem is string line && curFn != null)
            {
                var va = line.Split(' ')[0].Trim();
                if (va.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)) JumpTo(curFn.bin, va);
            }
        }
        void OnDeepJump(object s, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DeepList.SelectedItem is string line)
            {
                // format: [Bin] va  sample
                try
                {
                    int b1 = line.IndexOf('[') + 1, b2 = line.IndexOf(']');
                    string bin = line.Substring(b1, b2 - b1);
                    string rest = line.Substring(b2 + 1).Trim();
                    string va = rest.Split(' ')[0].Trim().ToLower();
                    JumpTo(bin, va);
                }
                catch { }
            }
        }

        void LoadFull(Fn fn)
        {
            curFull = null;
            AsmBox.Text = "";
            AsmInfoBlock.Text = "loading…";
            CallerList.Items.Clear(); CalleeList.Items.Clear(); RefList.Items.Clear();
            LiveBlock.Text = "";
            try
            {
                var full = FunctionStore.GetFull(fn.bin, fn.va);
                if (full.Insns.Count == 0)
                {
                    uint fv2;
                    if (uint.TryParse(fn.va.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out fv2))
                    {
                        var cont = FunctionStore.FindContaining(fn.bin, fv2);
                        if (cont != null && !cont.Equals(fn.va, System.StringComparison.OrdinalIgnoreCase))
                        {
                            var cf = FunctionStore.GetFull(fn.bin, cont);
                            if (cf.Insns.Count > 0)
                            {
                                curFull = cf;
                                uint cv = System.Convert.ToUInt32(cont.Replace("0x", ""), 16);
                                AsmSrcBlock.Text = "[" + fn.bin + " " + fn.va + "] inside " + cont + " +0x" + (fv2 - cv).ToString("X") + " · " + cf.Chunk + " #" + cf.Idx + " · " + cf.Insns.Count + " insns";
                                AsmBox.Text = string.Join("\n", cf.Insns);
                                AsmInfoBlock.Text = cf.Insns.Count + " instructions (container " + cont + ")";
                                FillXrefs(fn.bin, cont, cf);
                                FillLive(fn.bin, fn.va);
                                return;
                            }
                        }
                    }
                    curFull = full;
                    AsmSrcBlock.Text = $"[{fn.bin} {fn.va}] not in va_index (synthetic entry?) — preview only.";
                    AsmBox.Text = (fn.example ?? "").Replace("; ", "\n");
                    AsmInfoBlock.Text = "0 instructions (index preview)";
                    return;
                }
                curFull = full;
                AsmSrcBlock.Text = $"[{fn.bin} {fn.va}] {full.Chunk} #{full.Idx} · {full.Insns.Count} insns · {full.Calls.Count} callees";
                AsmBox.Text = string.Join("\n", full.Insns);
                AsmInfoBlock.Text = $"{full.Insns.Count} instructions";
                FillXrefs(fn.bin, fn.va, full);
                FillLive(fn.bin, fn.va);
            }
            catch (Exception ex) { AsmInfoBlock.Text = "load fail: " + ex.Message; }
        }

        string FnName(string bin, string va)
        {
            if (byKey.TryGetValue(bin + "|" + va.ToLower(), out var f))
            {
                var n = f.name ?? "";
                int b = n.IndexOf(" [0x");
                return b > 0 ? n.Substring(0, b) : n;
            }
            return "";
        }

        void FillXrefs(string bin, string va, FunctionStore.FullFn full)
        {
            try
            {
                // callers (who calls me)
                var (total, callers) = FunctionStore.GetCallers(bin, va);
                CallerHdr.Text = total == 0 ? "no indexed callers" : $"{total} xrefs (showing {callers.Count}) — double-click to jump";
                CallerList.Items.Clear();
                foreach (var c in callers)
                {
                    string nm = FnName(bin, c);
                    CallerList.Items.Add(string.IsNullOrEmpty(nm) ? c : $"{c}  {nm}");
                }
                // callees (who I call) with import resolve
                var iat = FunctionStore.GetIat(bin);
                CalleeList.Items.Clear();
                foreach (var t in full.Calls.Distinct().Take(60))
                {
                    uint tv;
                    string extra = "";
                    if (uint.TryParse(t.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out tv) && iat.TryGetValue(tv, out var imp))
                        extra = $"  ← {imp}";
                    else
                    {
                        string nm = FnName(bin, t);
                        if (!string.IsNullOrEmpty(nm)) extra = $"  {nm}";
                    }
                    CalleeList.Items.Add(t + extra);
                }
                int nc = full.Calls.Distinct().Count();
                CalleeHdr.Text = nc == 0 ? "leaf (no calls)" : $"{nc} callees" + (iat.Count > 0 ? $" · IAT {iat.Count}" : "");
                // string refs: push/mov <imgVA> that reads as ASCII in target exe
                RefList.Items.Clear();
                int shown = 0;
                var seen = new System.Collections.Generic.HashSet<uint>();
                foreach (var line in full.Insns)
                {
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(line, @"0x[0-9a-fA-F]{6,8}"))
                    {
                        uint v;
                        if (!uint.TryParse(m.Value.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out v)) continue;
                        if (v < 0x400000 || v > 0xC00000) continue;
                        if (!seen.Add(v)) continue;
                        var s = FunctionStore.TryReadAscii(bin, v, 72);
                        if (s != null)
                        {
                            RefList.Items.Add($"{m.Value}  \"{s}\"");
                            if (++shown >= 40) break;
                        }
                    }
                    if (shown >= 40) break;
                }
                RefHdr.Text = shown == 0 ? "no ASCII refs resolved in target exe" : $"{shown} string refs (live from {System.IO.Path.GetFileName(FunctionStore.ExePathForBin(bin) ?? "?")})";
                AsmInfoBlock.Text = $"{full.Insns.Count} instructions · {nc} callees · {total} xrefs";
            }
            catch (System.Exception ex) { CallerHdr.Text = "xref fail: " + ex.Message; }
        }

        void FillLive(string bin, string va)
        {
            try
            {
                var exe = FunctionStore.ExePathForBin(bin);
                if (exe == null || !File.Exists(exe)) { LiveBlock.Text = "(target exe not found — set ExeBox path for live bytes)"; return; }
                uint v = System.Convert.ToUInt32(va.Replace("0x", ""), 16);
                uint img = PatchEngine.ImageBase(exe);
                uint rva = v >= img ? v - img : v;
                uint off = 0;
                try { off = PatchEngine.Verify(exe, va, "00").Off; } catch { }
                // read 16 live bytes for orientation
                var d = File.ReadAllBytes(exe);
                int n = (int)System.Math.Min(16, d.Length - off);
                var lb = new byte[n]; System.Array.Copy(d, off, lb, 0, n);
                LiveBlock.Text = $"live {va}: rva=0x{rva:X} fileoff=0x{off:X} ({off}) bytes={PatchEngine.ToHex(lb)}  cksum={PatchEngine.PeChecksum(exe):X8}  [{System.IO.Path.GetFileName(exe)}]";
            }
            catch (System.Exception ex) { LiveBlock.Text = "live: " + ex.Message; }
        }

        void OnDeepSearch(object s, RoutedEventArgs e)
        {
            var term = (DeepBox.Text ?? "").Trim();
            if (term.Length < 2) { DeepStatus.Text = "type 2+ chars"; return; }
            string scope = DeepScope.SelectedItem as string ?? "All binaries";
            DeepStatus.Text = $"searching '{term}' in {scope} bodies…";
            DeepList.Items.Clear();
            var task = System.Threading.Tasks.Task.Run(() => FunctionStore.DeepSearch(scope == "All binaries" ? null : scope, term, 300));
            task.ContinueWith(t =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (t.Exception != null) { DeepStatus.Text = "search fail: " + t.Exception.InnerException?.Message; return; }
                    var hits = t.Result;
                    DeepStatus.Text = $"{hits.Count} functions contain '{term}'{(hits.Count >= 300 ? " (capped 300)" : "")}";
                    foreach (var h in hits.Take(300))
                    {
                        string nm = FnName(h.bin, h.va);
                        DeepList.Items.Add($"[{h.bin}] {h.va}  {(string.IsNullOrEmpty(nm) ? "" : nm + "  ")}// {h.line.Trim()}");
                    }
                });
            });
        }

        void OnAsmFilter(object s, RoutedEventArgs e)
        {
            if (curFull == null || curFull.Insns.Count == 0) return;
            var f = (AsmFilterBox.Text ?? "").ToLower();
            if (string.IsNullOrWhiteSpace(f)) { AsmBox.Text = string.Join("\n", curFull.Insns); AsmInfoBlock.Text = $"{curFull.Insns.Count} instructions"; return; }
            var hits = curFull.Insns.Where(l => l.ToLower().Contains(f)).ToList();
            AsmBox.Text = string.Join("\n", hits);
            AsmInfoBlock.Text = $"{hits.Count}/{curFull.Insns.Count} match '{AsmFilterBox.Text}'";
        }
        void OnAsmClear(object s, RoutedEventArgs e)
        {
            AsmFilterBox.Text = "";
            if (curFull != null) { AsmBox.Text = string.Join("\n", curFull.Insns); AsmInfoBlock.Text = $"{curFull.Insns.Count} instructions"; }
        }
        void OnCopyAsm(object s, RoutedEventArgs e) { try { Clipboard.SetText(AsmBox.Text); Status("disassembly copied"); } catch { } }
        void OnCopyVa(object s, RoutedEventArgs e) { try { Clipboard.SetText(VaBox.Text); } catch { } }
        void OnCopyName(object s, RoutedEventArgs e) { try { Clipboard.SetText(NameBox.Text); } catch { } }

        // ---------- 4: hex-at-cursor → Patch ----------
        string CurrentAsmLine()
        {
            try
            {
                string t = AsmBox.Text ?? "";
                if (string.IsNullOrEmpty(t)) return null;
                int caret = AsmBox.CaretIndex;
                int start = t.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
                int end = t.IndexOf('\n', caret);
                if (end < 0) end = t.Length;
                return t.Substring(start, Math.Max(0, end - start)).Trim();
            }
            catch { return null; }
        }
        void OnSendPatch(object s, RoutedEventArgs e)
        {
            try
            {
                if (curFn == null) { Status("open a function first"); return; }
                string line = null;
                try { line = AsmBox.SelectedText; } catch { }
                if (string.IsNullOrWhiteSpace(line)) line = CurrentAsmLine();
                if (string.IsNullOrWhiteSpace(line)) { Status("no line under cursor"); return; }
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^([0-9A-Fa-f]{8})\s+([0-9A-Fa-f]+)\s");
                if (!m.Success) { Status("line has no addr+bytes: " + line.Trim()); return; }
                string va = "0x" + m.Groups[1].Value.ToLower();
                string raw = m.Groups[2].Value;
                string spaced = string.Join(" ", Enumerable.Range(0, raw.Length / 2).Select(i => raw.Substring(i * 2, 2).ToUpper()));
                VaBox.Text = va;
                OrigBox.Text = spaced;
                if (string.IsNullOrEmpty(ExeBox.Text) && DefaultExe.TryGetValue(curFn.bin, out var d)) ExeBox.Text = d;
                PatchInfoBlock.Text = $"from {curFn.bin} {line.Trim()} → staged {va} orig={spaced}. Edit New hex, then Verify + Apply.";
                try { MainTabs.SelectedItem = PatchTab; } catch { }
                Status($"staged {va} from disassembly");
            }
            catch (Exception ex) { Status("send→patch: " + ex.Message); }
        }

        // ---------- 1: inverse string search ----------
        void OnStrSearch(object s, RoutedEventArgs e)
        {
            var q = (StrBox.Text ?? "").Trim();
            if (q.Length < 2) { StrStatus.Text = "type 2+ chars"; return; }
            string scope = (DeepScope.SelectedItem as string) ?? "All binaries";
            StrStatus.Text = $"strings '{q}' in {scope}…";
            StrList.Items.Clear();
            var task = System.Threading.Tasks.Task.Run(() => FunctionStore.InverseStringSearch(scope == "All binaries" ? null : scope, q, 200, 300));
            task.ContinueWith(t =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (t.Exception != null) { StrStatus.Text = "search fail: " + t.Exception.InnerException?.Message; return; }
                    var (strings, funcs) = t.Result;
                    StrStatus.Text = $"{strings.Count} strings, {funcs.Count} functions{(funcs.Count >= 300 ? " (capped)" : "")}";
                    if (strings.Count > 0)
                    {
                        StrList.Items.Add($"── {Math.Min(10, strings.Count)} sample strings ──");
                        foreach (var se in strings.Take(10)) StrList.Items.Add($"\"{se.text}\" @ {se.va} [{se.bin}]");
                        StrList.Items.Add($"── functions referencing them ──");
                    }
                    foreach (var h in funcs.Take(300))
                    {
                        string nm = FnName(h.bin, h.funcVa);
                        StrList.Items.Add($"[{h.bin}] {h.funcVa}  {(string.IsNullOrEmpty(nm) ? "" : nm + "  ")}// \"{h.strText}\" @ {h.strVa}");
                    }
                });
            });
        }
        void OnStrFuncJump(object s, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (StrList.SelectedItem is string line && line.StartsWith("["))
            {
                try
                {
                    int b1 = line.IndexOf('[') + 1, b2 = line.IndexOf(']');
                    string bin = line.Substring(b1, b2 - b1);
                    string va = line.Substring(b2 + 1).Trim().Split(' ')[0].Trim().ToLower();
                    if (va.StartsWith("0x")) JumpTo(bin, va);
                }
                catch { }
            }
        }

        // ---------- RTTI class browser ----------
        System.Collections.Generic.List<FunctionStore.RttiClass> rttiShown = new();
        string RttiScopeBin()
        {
            var s = RttiScope.SelectedItem as string;
            return (s == null || s == "All binaries") ? null : s;
        }
        void OnRttiList(object s, RoutedEventArgs e)
        {
            try
            {
                rttiShown.Clear(); RttiList.Items.Clear(); RttiRefList.Items.Clear();
                string q = (RttiBox.Text ?? "").Trim().ToLower();
                string scope = RttiScopeBin();
                string[] bins = scope == null ? Bins : new[] { scope };
                foreach (var b in bins)
                    foreach (var c in FunctionStore.GetRtti(b))
                        if (string.IsNullOrEmpty(q) || c.name.ToLower().Contains(q))
                        { rttiShown.Add(new FunctionStore.RttiClass { name = $"[{b}] {c.name}", kind = c.kind, mangled = c.mangled, va = c.va }); }
                rttiShown = rttiShown.OrderBy(c => c.name).Take(2000).ToList();
                foreach (var c in rttiShown.Take(2000)) RttiList.Items.Add($"{c.name}  {c.va}");
                RttiHdr.Text = $"{rttiShown.Count} classes{(rttiShown.Count >= 2000 ? " (capped)" : "")}";
                RttiStatus.Text = string.Join(" ", bins.Select(b => $"{b}={FunctionStore.GetRtti(b).Count}"));
            }
            catch (System.Exception ex) { RttiHdr.Text = "rtti fail: " + ex.Message; }
        }
        void OnRttiRefs(object s, RoutedEventArgs e) => RttiRefsCore();
        void OnRttiRefsGo(object s, System.Windows.Input.MouseButtonEventArgs e) => RttiRefsCore();
        void RttiRefsCore()
        {
            try
            {
                int i = RttiList.SelectedIndex;
                if (i < 0 || i >= rttiShown.Count) { RttiRefHdr.Text = "select a class first (or double-click it)"; return; }
                var c = rttiShown[i];
                int b1 = c.name.IndexOf('[') + 1, b2 = c.name.IndexOf(']');
                string bin = c.name.Substring(b1, b2 - b1);
                string shortName = c.name.Substring(b2 + 1).Trim();
                RttiRefHdr.Text = $"refs to {shortName} ({c.va})…";
                RttiRefList.Items.Clear();
                var task = System.Threading.Tasks.Task.Run(() => FunctionStore.GetRttiRefs(bin, c.va, 200));
                task.ContinueWith(t =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (t.Exception != null) { RttiRefHdr.Text = "refs fail"; return; }
                        var hits = t.Result;
                        if (hits.Count == 0)
                        {
                            RttiRefHdr.Text = $"no direct code refs to {shortName} (RTTI names are referenced via descriptors, not code) — try String → functions on '{shortName}' or its error strings";
                            return;
                        }
                        RttiRefHdr.Text = $"{hits.Count} functions reference {shortName} — double-click to jump";
                        foreach (var h in hits)
                        {
                            string nm = FnName(bin, h.funcVa);
                            RttiRefList.Items.Add($"[{bin}] {h.funcVa}  {(string.IsNullOrEmpty(nm) ? "" : nm + "  ")}// {h.line.Trim()}");
                        }
                    });
                });
            }
            catch (System.Exception ex) { RttiRefHdr.Text = "refs fail: " + ex.Message; }
        }
        void OnRttiJump(object s, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (RttiRefList.SelectedItem is string line && line.StartsWith("["))
            {
                try
                {
                    int b1 = line.IndexOf('[') + 1, b2 = line.IndexOf(']');
                    string bin = line.Substring(b1, b2 - b1);
                    string va = line.Substring(b2 + 1).Trim().Split(' ')[0].Trim().ToLower();
                    if (va.StartsWith("0x")) JumpTo(bin, va);
                }
                catch { }
            }
        }

        // ---------- 5: pins / workspace ----------
        public class Pin { public string bin { get; set; } public string va { get; set; } public string label { get; set; } }
        List<Pin> pins = new();
        void LoadPins()
        {
            try
            {
                PinsList.Items.Clear();
                var p = Path.Combine(WorkbenchDir, "pins.json");
                if (File.Exists(p)) pins = JsonSerializer.Deserialize<List<Pin>>(File.ReadAllText(p)) ?? new List<Pin>();
                foreach (var pin in pins) PinsList.Items.Add(PinText(pin));
                PinsStatus.Text = pins.Count == 0 ? "no pins — Pin current, or load Arabian preset" : $"{pins.Count} pins (pins.json)";
            }
            catch { }
        }
        string PinText(Pin p)
        {
            string nm = FnName(p.bin, p.va);
            return $"[{p.bin}] {p.va}  {(string.IsNullOrEmpty(nm) ? "" : nm + "  ")}// {p.label}";
        }
        void SavePins()
        {
            try { File.WriteAllText(Path.Combine(WorkbenchDir, "pins.json"), JsonSerializer.Serialize(pins, new JsonSerializerOptions { WriteIndented = true })); } catch { }
        }
        void OnPinCurrent(object s, RoutedEventArgs e)
        {
            if (curFn == null) return;
            string label = string.IsNullOrWhiteSpace(CommentBox.Text) ? FnName(curFn.bin, curFn.va) : CommentBox.Text.Split('\n')[0];
            if (pins.Any(p => p.bin == curFn.bin && p.va.ToLower() == curFn.va.ToLower())) { PinsStatus.Text = "already pinned"; return; }
            var pin = new Pin { bin = curFn.bin, va = curFn.va.ToLower(), label = label ?? "" };
            pins.Add(pin); PinsList.Items.Add(PinText(pin)); SavePins();
            PinsStatus.Text = $"{pins.Count} pins saved → pins.json";
        }
        void OnPinRemove(object s, RoutedEventArgs e)
        {
            int i = PinsList.SelectedIndex;
            if (i < 0 || i >= pins.Count) return;
            pins.RemoveAt(i); PinsList.Items.RemoveAt(i); SavePins();
            PinsStatus.Text = $"{pins.Count} pins";
        }
        void OnPinJump(object s, System.Windows.Input.MouseButtonEventArgs e)
        {
            int i = PinsList.SelectedIndex;
            if (i < 0 || i >= pins.Count) return;
            JumpTo(pins[i].bin, pins[i].va);
        }
        void OnPinsPreset(object s, RoutedEventArgs e)
        {
            var preset = new List<Pin>
            {
                new Pin { bin = "Game", va = "0x6a3da5", label = "Country validator 02→FF (LIVE patched)" },
                new Pin { bin = "Game", va = "0x6a2400", label = "Game_RefValidator (Country vectors)" },
                new Pin { bin = "Game", va = "0x6a2a48", label = "CountryVector_Bounds16 (prime)" },
                new Pin { bin = "Game", va = "0x6a2b70", label = "per-element gate cmp al,2 (DRY-RUN seed: imm 02->03 @0x6A2B71)" },
                new Pin { bin = "Game", va = "0x6a2a2a", label = "AR-ready reference gate cmp al,3 (do not touch)" },
                new Pin { bin = "Game", va = "0x6a2b99", label = "deep-stack proof site" },
                new Pin { bin = "Game", va = "0x59c4e0", label = "Mastery_Level_Check 330/240" },
                new Pin { bin = "Game", va = "0x59e7c0", label = "Mastery_Sum_Levels" },
                new Pin { bin = "Client", va = "0x430a50", label = "list crash container (fault 0x430DDA +0x38A)" },
                new Pin { bin = "Shard", va = "0x42a238", label = "Shard_CreateChar_SQL_Format (_AddNewChar)" },
                new Pin { bin = "Client", va = "0x85eb10", label = "Select_Create_Screen" },
            };
            foreach (var pr in preset)
                if (!pins.Any(p => p.bin == pr.bin && p.va == pr.va)) { pins.Add(pr); PinsList.Items.Add(PinText(pr)); }
            SavePins();
            PinsStatus.Text = $"{pins.Count} pins (preset merged) → pins.json";
        }

        // ---------- comments ----------
        string CKey(Fn f) => f.bin + "|" + f.va.ToLower();
        string GetComment(Fn f) => editedComments.TryGetValue(CKey(f), out var c) ? c : (f.comment ?? "");
        void LoadComments()
        {
            try
            {
                var p = Path.Combine(WorkbenchDir, "comments.json");
                if (!File.Exists(p)) return;
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p));
                foreach (var kv in d) editedComments[kv.Key] = kv.Value;
            }
            catch { }
        }
        void OnSaveComment(object s, RoutedEventArgs e)
        {
            if (curFn == null) return;
            editedComments[CKey(curFn)] = CommentBox.Text;
            curFn.comment = CommentBox.Text;
            try { File.WriteAllText(Path.Combine(WorkbenchDir, "comments.json"), JsonSerializer.Serialize(editedComments, new JsonSerializerOptions { WriteIndented = true })); Status("comment saved → comments.json"); }
            catch (Exception ex) { Status("comment save fail: " + ex.Message); }
        }

        // ---------- search / samples ----------
        void OnSearch(object s, RoutedEventArgs e) => BuildTree(SearchBox.Text);
        void OnSamples(object s, RoutedEventArgs e)
        {
            try
            {
                var patches = JsonDocument.Parse(File.ReadAllText(PatchesPath()));
                Status(patches.RootElement.GetProperty("patches").GetArrayLength() + " seed patches in " + PatchesPath());
            }
            catch (Exception ex) { Status("samples: " + ex.Message); }
        }

        // ---------- patch v2 ----------
        void OnBrowse(object s, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Executables|*.exe;*.dll" };
            if (d.ShowDialog() == true) { ExeBox.Text = d.FileName; OnBackupRefresh(s, e); }
        }
        void OnUseDefault(object s, RoutedEventArgs e)
        {
            string b = curFn != null ? curFn.bin : (SelBin() ?? "Game");
            if (DefaultExe.TryGetValue(b, out var d)) { ExeBox.Text = d; OnBackupRefresh(s, e); Status("target = " + d); }
        }
        void OnStage(object s, RoutedEventArgs e)
        {
            stagedFile = ExeBox.Text; stagedRva = VaBox.Text;
            stagedOrig = OrigBox.Text.Replace(" ", ""); stagedNew = NewBox.Text.Replace(" ", "");
            Status($"staged {stagedRva}: {stagedOrig} -> {stagedNew} (Verify first, then Apply)");
        }
        void OnVerify(object s, RoutedEventArgs e)
        {
            try
            {
                string va = !string.IsNullOrWhiteSpace(VaBox.Text) ? VaBox.Text : stagedRva;
                var r = PatchEngine.Verify(ExeBox.Text, va, OrigBox.Text);
                PatchInfoBlock.Text = (r.Match ? "✓ " : "✗ ") + r.Msg + $" · cksum={PatchEngine.PeChecksum(ExeBox.Text):X8}";
                Status(r.Match ? "verify MATCH — safe to Apply" : "verify MISMATCH — will refuse Apply");
            }
            catch (Exception ex) { PatchInfoBlock.Text = "VERIFY: " + ex.Message; Status("verify fail: " + ex.Message); }
        }
        void OnApply(object s, RoutedEventArgs e)
        {
            try
            {
                string f = stagedFile ?? ExeBox.Text, va = stagedRva ?? VaBox.Text;
                string o = stagedOrig ?? OrigBox.Text.Replace(" ", ""), n = stagedNew ?? NewBox.Text.Replace(" ", "");
                Status(PatchEngine.Apply(f, va, o, n));
                OnBackupRefresh(s, e); RefreshHistory();
            }
            catch (Exception ex) { Status("APPLY REFUSED: " + ex.Message); PatchInfoBlock.Text = "APPLY REFUSED: " + ex.Message; }
        }
        void OnRollback(object s, RoutedEventArgs e)
        {
            try { Status(PatchEngine.Rollback(ExeBox.Text)); OnBackupRefresh(s, e); RefreshHistory(); }
            catch (Exception ex) { Status("ROLLBACK: " + ex.Message); }
        }
        void OnBackupRefresh(object s, RoutedEventArgs e)
        {
            try
            {
                BackupList.Items.Clear();
                foreach (var b in PatchEngine.ListBackups(ExeBox.Text)) BackupList.Items.Add(b);
                if (BackupList.Items.Count == 0) BackupList.Items.Add("(no backups beside target yet)");
            }
            catch { }
        }
        void OnRollbackTo(object s, RoutedEventArgs e)
        {
            try
            {
                var sel = BackupList.SelectedItem as string;
                if (string.IsNullOrEmpty(sel) || sel.StartsWith("(no")) { Status("pick a backup first"); return; }
                Status(PatchEngine.RollbackTo(ExeBox.Text, sel));
                RefreshHistory();
            }
            catch (Exception ex) { Status("ROLLBACK: " + ex.Message); }
        }

        List<JsonElement> seeds = new();
        void LoadSeeds()
        {
            try
            {
                seeds.Clear(); SeedList.Items.Clear();
                var doc = JsonDocument.Parse(File.ReadAllText(PatchesPath()));
                foreach (var p in doc.RootElement.GetProperty("patches").EnumerateArray())
                {
                    seeds.Add(p.Clone());
                    SeedList.Items.Add($"{p.GetProperty("name").GetString()}  {p.GetProperty("va").GetString()}  [{p.GetProperty("file").GetString()}]");
                }
            }
            catch { }
        }
        void OnSeedVerifyAll(object s, RoutedEventArgs e)
        {
            try
            {
                if (SeedList.Items.Count == 0) { SeedInfoBlock.Text = "no seeds"; return; }
                var lines = new List<string>();
                foreach (var p in seeds)
                {
                    string file = p.GetProperty("file").GetString();
                    string va = p.GetProperty("va").GetString();
                    string orig = p.GetProperty("orig_bytes").GetString();
                    // only verify seeds whose file matches target basename
                    if (!string.IsNullOrEmpty(ExeBox.Text) && !ExeBox.Text.EndsWith(file, StringComparison.OrdinalIgnoreCase)) { lines.Add($"skip {va} ({file})"); continue; }
                    try
                    {
                        var r = PatchEngine.Verify(ExeBox.Text, va, orig);
                        lines.Add($"{(r.Match ? "MATCH " : "DIFF   ")} {va} off=0x{r.Off:X} live={r.LiveHex}");
                    }
                    catch (Exception ex) { lines.Add($"ERR {va}: {ex.Message}"); }
                }
                SeedInfoBlock.Text = string.Join("\n", lines);
            }
            catch (Exception ex) { SeedInfoBlock.Text = ex.Message; }
        }
        void OnSeedApply(object s, RoutedEventArgs e)
        {
            try
            {
                if (SeedList.SelectedIndex < 0) { SeedInfoBlock.Text = "select a seed first"; return; }
                var p = seeds[SeedList.SelectedIndex];
                string va = p.GetProperty("va").GetString();
                string orig = p.GetProperty("orig_bytes").GetString();
                VaBox.Text = va; OrigBox.Text = orig;
                // new bytes: ask via NewBox (user edits imm/branch there)
                SeedInfoBlock.Text = $"seed staged: {va} orig={orig} → edit New hex, then Verify + Apply staged. Note: {p.GetProperty("note").GetString()}";
                OnVerify(s, e);
            }
            catch (Exception ex) { SeedInfoBlock.Text = ex.Message; }
        }
        void RefreshHistory()
        {
            try
            {
                var p = Path.Combine(WorkbenchDir, "patches_applied.log");
                HistBlock.Text = File.Exists(p) ? string.Join("\n", File.ReadLines(p).Reverse().Take(30)) : "(no patches applied yet this workbench)";
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (curFn != null && !string.IsNullOrEmpty(CommentBox.Text) && CommentBox.Text != curFn.comment)
                    editedComments[CKey(curFn)] = CommentBox.Text;
                if (editedComments.Count > 0)
                    File.WriteAllText(Path.Combine(WorkbenchDir, "comments.json"), JsonSerializer.Serialize(editedComments, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
            base.OnClosing(e);
        }

        void Status(string m) => StatusBlock.Text = m;
    }
}
