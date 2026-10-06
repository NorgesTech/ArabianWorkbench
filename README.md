Arabian Workbench

A reverse-engineering workbench for VSRO 1.188–class Silkroad server binaries (sro_client.exe, SR_GameServer.exe, SR_ShardManager.exe, AgentServer.exe, FarmManager.exe, MachineManager.exe, GatewayServer.exe, DownloadServer.exe, GlobalManager.exe). Browse ~21k functions with human-readable names, full disassembly, cross-references, string/RTTI browsers — and apply hash-guarded binary patches with verify-before-write.

Features

9 binaries × areas — functions grouped under Client / GameServer / ShardManager / Agent / Farm / Machine / Gateway / Download / Global, then areas (Character, Skill, Quest, Teleport, Network, Database, World, Security, …). Search across names, VAs, comments, areas.
Readable headers, not sub_* — names come from referenced strings, imports, and code shape (Mastery_Level_Check [0x59C4E0]), with curated entries for key sites.
Full disassembly viewer — lazy-loaded bodies (up to cap) from the FullReverse/ corpus, in-function filter, live-bytes line (rva / file offset / 16 bytes / checksum).
Xrefs — callers (who calls me) with 2-level expansion, callees with import resolution (call … ← KERNEL32.dll!FormatMessageA), string refs resolved live from the target exe.
Deep search — full-body scan (cmp al, 2, push 0xB88438, …) plus String → functions inverse search (strings containing text → functions referencing them).
RTTI class browser — ~2000 Game / ~1200 Client / ~1000 Shard classes (CRefTactics, VCRefHive, 292 CIF* windows, CRTSkeleton, …) with referencing-function lookup.
Hot functions — 🔥 Hot node per binary from call-count data, inline 🔥count badges, Hot-first sorting.
Patch v2 (hash-guarded) — Verify (no write) → Stage → Apply, automatic timestamped backups, rollback (latest or selected), batch seeds from patches.json with per-seed Verify, history log.
Hex-at-cursor — send the disassembly line under the caret straight into the patch composer (VA + orig bytes prefilled).
Pins workspace — pin key sites, persisted to pins.json, one-click preset for the active work area.
Requirements
Windows 10/11, .NET 9 SDK (WPF)
Python 3.10+ with capstone + pefile — only needed to (re)generate the FullReverse/ corpus, not to run the app
