# Arabian Workbench

A reverse-engineering workbench for **VSRO 1.188–class Silkroad server binaries**
(`sro_client.exe`, `SR_GameServer.exe`, `SR_ShardManager.exe`, `AgentServer.exe`,
`FarmManager.exe`, `MachineManager.exe`, `GatewayServer.exe`, `DownloadServer.exe`,
`GlobalManager.exe`). Browse ~21k functions with human-readable names, full
disassembly, cross-references, string/RTTI browsers — and apply hash-guarded
binary patches with verify-before-write.

![icon](App/icon_preview.png)

> **Test environments only.** Only point this at binaries and servers you own or
> have explicit permission to study. Every patch path requires backup +
> byte-guard match before it writes anything.

## Features

- **9 binaries × areas** — functions grouped under Client / GameServer /
  ShardManager / Agent / Farm / Machine / Gateway / Download / Global, then areas
  (Character, Skill, Quest, Teleport, Network, Database, World, Security, …).
  Search across names, VAs, comments, areas.
- **Readable headers, not `sub_*`** — names come from referenced strings,
  imports, and code shape (`Mastery_Level_Check [0x59C4E0]`), with curated
  entries for key sites.
- **Full disassembly viewer** — lazy-loaded bodies (up to cap) from the
  `FullReverse/` corpus, in-function filter, live-bytes line
  (`rva / file offset / 16 bytes / checksum`).
- **Xrefs** — callers (who calls me) with **2-level expansion**, callees with
  **import resolution** (`call … ← KERNEL32.dll!FormatMessageA`), string refs
  resolved live from the target exe.
- **Deep search** — full-body scan (`cmp al, 2`, `push 0xB88438`, …) plus
  **String → functions** inverse search (strings containing text → functions
  referencing them).
- **RTTI class browser** — ~2000 Game / ~1200 Client / ~1000 Shard classes
  (`CRefTactics`, `VCRefHive`, 292 `CIF*` windows, `CRTSkeleton`, …) with
  referencing-function lookup.
- **Hot functions** — `🔥 Hot` node per binary from call-count data, inline
  `🔥count` badges, Hot-first sorting.
- **Patch v2 (hash-guarded)** — Verify (no write) → Stage → Apply, automatic
  timestamped backups, rollback (latest or selected), batch seeds from
  `patches.json` with per-seed Verify, history log.
- **Hex-at-cursor** — send the disassembly line under the caret straight into
  the patch composer (VA + orig bytes prefilled).
- **Pins workspace** — pin key sites, persisted to `pins.json`, one-click
  preset for the active work area.

## Requirements

- Windows 10/11, [.NET 9 SDK](https://dotnet.microsoft.com/) (WPF)
- Python 3.10+ with `capstone` + `pefile` — only needed to (re)generate the
  `FullReverse/` corpus, not to run the app

## Layout

```text
ArabianRace/
├─ Workbench/
│  ├─ App/                      # WPF source (this README's project)
│  │  ├─ ArabianWorkbench.csproj
│  │  ├─ MainWindow.xaml(.cs)   # tree + tabs
│  │  ├─ FunctionStore.cs       # lazy disasm / xrefs / search
│  │  └─ PatchEngine.cs         # guarded patcher + checksum fix
│  ├─ *.jsonl                   # light index per binary (name/va/example/comment/area)
│  ├─ comments.json             # your saved notes (created on first save)
│  ├─ pins.json                 # your workspace pins (created on first pin)
│  └─ patches_applied.log       # patch history (created on first apply)
├─ FullReverse/<Bin>/           # heavy corpus (generated, not committed*)
│  ├─ funcs_*.json              # full bodies
│  ├─ va_index.json             # va → chunk lookup
│  ├─ callers_index.json        # reverse call map
│  ├─ call_top200.json          # hot functions
│  ├─ strings_map.json          # string VA map
│  ├─ rtti_map.json             # demangled classes
│  └─ report.json               # imports / sections / counts
└─ patches.json                 # seed patches (VA + guarded orig bytes + notes)
```

\* Commit the `*.jsonl` index + `patches.json`; generate or ship `FullReverse/`
separately (it's ~190 MB). Point the app at it via `Workbench/fullreverse.path`
(a one-line override file) or keep the default relative layout
(`Workbench/../FullReverse`).

## Build & run

```powershell
dotnet build Workbench/App/ArabianWorkbench.csproj -c Release
.\Workbench\App\bin\Release\net9.0-windows\ArabianWorkbench.exe
# headless checks:
.\Workbench\App\bin\Release\net9.0-windows\ArabianWorkbench.exe /selftest
.\Workbench\App\bin\Release\net9.0-windows\ArabianWorkbench.exe /selftest2
```

## (Re)generating the corpus

```powershell
python full_svc.py Game        # disassemble (repeat per binary)
python gen_namedb2.py          # human-readable headers
python gen_areas.py            # area classification
python gen_strings_map.py      # string VA map
# callers_index / rtti_map builders live beside these scripts
```

## Safe patching workflow

1. Select a function → Disassembly → `→ Patch` (or fill VA/hex by hand).
2. **Verify (no write)** — must read MATCH (live bytes = guard bytes).
3. Stage → Apply (backup is written automatically beside the target).
4. Restart the service, test one thing, check logs/dumps.
5. Regress on unrelated behavior; **Rollback** at the first surprise.

Rules we follow: one patch at a time, reversible DB/file changes, checksums
fixed by the engine, nothing applied on a mismatch, test server first.

## Roadmap

- Loader/fill analysis for Country-indexed Ref vectors (gate side proven)
- Spawn/nest SQL helpers, PK2 client↔server sync checker
- IDA / x64dbg bookmark export, minidump triage tab

## Contributing

PRs welcome: curated function names, area rules, seed patches with verified
guards and notes. Please include the target file hash and before/after bytes
for any seed.

## License

MIT — see [LICENSE](LICENSE).Swap in your preferred license if needed.
