# WO-149 — progress

Session 2026-10-01, unattended, one pass. Findings: `docs/WO-149-cutscene-census.md`; one row per cutscene or set piece:
`docs/WO-149-cutscene-census.csv`. This file: what was read, the tools, what was touched, the checks, and what is still open.

## 1. Constraints kept

| constraint | how |
|---|---|
| never launch the game, no injection, no relay/agent/REST console, nothing that takes focus | nothing was launched or connected; the install's own files show the last game/log activity at 14:38 local, before this work order started (first scratchpad file 16:15), and none after |
| no code change, no build, no `VERSION`, no installer, no tag | only the three docs were written in the repo; `git status` shows them as the only new files (the six modified files in the status were already modified before the session and were not touched) |
| game paks and binaries read-only, from the Modding Tools install only; never write inside it; retail out of scope | paks are ordinary ZIP files read with `zipfile`; binaries read as bytes and parsed (PE headers, strings, exports, RTTI, capstone disassembly); the install was only read; the retail install was never opened |
| never touch a save | no `.whs` file was opened; save facts come from the committed docs |
| helpers write only in the scratchpad or the repo | the six helpers wrote only in the scratchpad; the lead wrote the three repo docs |
| unattended-safe | no question was asked; judgement calls are in section 9 of the census |
| privacy | logs stayed in the scratchpad; machines are "the host" and "the joiner"; no name, path or address is quoted (sweep below) |
| game text | cited by ID only; the CSV carries node, cutscene, dialogue and holder IDs and generated descriptions, no dialogue, quest or book text |
| port-aware | no native code written; §3.8 and §7 of the census say how each engine piece is found (RTTI, string, export, structure) and whether WO-145 lists it |

## 2. What was read

* **Evidence pack** (copied/extracted to the scratchpad first): seven zips from the maintainer's desktop. `HOSTINGPLAYER--…232810`
  is byte-identical to `HOST1-…232810` (same SHA-256), so six distinct sets, 13 game launches, 18 agent logs, native logs, the host's
  relay logs, leash CSVs. Sessions S1 (29 Sep, 0.42.0), S2 (30 Sep early, 0.42.0), S3 and S4 (30 Sep evening, 0.42.2).
* **Game data** (`<MT>`): `Scripts.pak` (21,648 quest XML files under `Quests/Final/Barbora`, plus `Scripts/**`, `AI/**`), `Tables.pak` (all
  1,738 entries; `ui/cutscene.xml`, `skiptime.xml`, `rpg/soul__cutscene.xml`, `rpg/buff.xml`, `ai/*`), the three levels' `level.pak`
  (`objects_mission0.xml`, 4,310 layer files, `moviedata.xml`), `Cinematics.pak`, `Animations.pak`, `Music.pak`, `Sounds.pak` (names),
  `Videos-part0..5.pak` and `IPL_Videos-part0..2.pak` (names, sizes, Bink 2 headers), `GeomCaches.pak`, `IPL_GameData.pak`,
  `English_xml.pak` (string-ID existence only), the scriptbind reference pages.
* **Binaries** (`Bin/Win64ReleaseSteamLTO_DLL`): GUIModule, DialogModule, ConceptModule, QuestModule, PlayerModule, Framework,
  CrySystem, AnimationModule, CryMovie and others, for strings, exports, RTTI/vftables and the scene-manager, cutscene-player,
  handler, dialogue-state and State-setter bodies. Ghidra projects were not opened.
* **Repo docs**: WO-78/80/90/92/95/96/97/98/99/99.5/105/109/111/112/113/114/118/122/123/124/125/126A/127/133/136/137/138/139/140/141A/
  144/145/147/148, the tester pages TEST-0.42.x, and the code that handles scenes (`kdcmp.lua`, `dotnet/`, `native/KCDMP/`).

## 3. How the work was split

Six read-only helpers (sub-agents), each with a written brief (scratchpad `BRIEF.md`) carrying the rules, the evidence marks, the kind
taxonomy and the CSV schema; the lead read every result and wrote the three docs.

| helper | job | outputs (scratchpad) |
|---|---|---|
| docs digest | every cutscene-relevant fact in our docs and code, with doc and section | `digest_docs.md` |
| D1 | cutscene table, placed holders, definitions, orphans, WO-141A verification | `census/D1_definitions.md`, `cutscene_table.csv`, `level_holders.csv` |
| D2 | the whole quest corpus: one row per scene-like node, resolution of holders, totals, per-quest tables, coverage, player switches | `census/D2_quests.md`, `D2_quests_nonmain.md`, `quest_scenes.csv`, `quest_scenes.py` |
| D3 | the first two hours scene by scene; dialogue twins in data | `census/D3_first_two_hours.md`, `first_two_hours.csv` |
| D4 | how the engine runs scenes; what sets a State; port-aware anchors | `census/D4_engine.md`, `d4/*` |
| field | every scene event in the pack, the five known cases, the Phase 5 numbers | `field_evidence.md`, `field/*` |

## 4. Tools

Python 3.14 (`zipfile`, `csv`, `xml.etree`, `struct`, `re`, `capstone` already installed since 13 Sep), Git Bash and PowerShell for
listing and hashing. Every Python process set itself to BELOW_NORMAL priority first. Nothing was installed. The repo docs were
assembled by `census/build_csv.py`, `gen_tables.py` and `assemble.py` (scratchpad; rerunnable from the other outputs).

## 5. Everything touched outside the repo

Only the session scratchpad: the extracted logs (`logs/`), the extracted quest and table data (`data/`, about 380 MB), the helpers'
scripts, pickles, CSVs and reports, and Python bytecode caches next to the scripts. Nothing was written inside the Modding Tools
install, the retail install, the game's user folders or the maintainer's desktop (the zips were read only). No package, setting,
hook or scheduled task was created.

## 6. Checks run on the numbers

* WO-126A's 337 handlers and 141 background sequences are reproduced exactly; its 29 unresolved holders all resolve now.
* D1 and D2 agree on the holder entities quests reach: 602 of D1's 608; the other 6 are DLC holders resolved differently (listed in
  D2's report).
* WO-141A §5: `AnimChar` 6,068 and `IngameCutsceneData` 391 exact; `MusicCutscene` is 3,063 (corrected).
* Rendered durations are exact (Bink header frames over fps; header size equals the stored size minus 8 for all 83 videos).
* The field clock alignment holds to 0.02 s (worst 0.06 s) per launch; a cross-check (the contraband cutscene start on both machines)
  agrees to 0.02 s.
* The maintainer's 473 / 87 / 403 State figures were not reproduced under any counting definition tried (census §5.3); the
  load-burst structure that explains the "inactive" drops was.
* D2's per-quest ordering is the editor graph order and reproduces WO-126A's order for M01; elsewhere it is a reading, not a trace.
* The CSV's 6,453 rows: 5,785 auto-extracted primary rows (all classes; M30, M01, M02, M03 and S14 excluded), 142 curated
  first-two-hours rows, 526 definition-only rows for cutscene-table entries no handler plays.

## 7. Privacy sweep

Before commit, the three docs were searched for: the user name and home path, drive-letter paths, the install path, IPv4 addresses,
account and Steam identifiers, the maintainer's mail address, and non-ASCII letters from the game's language (game text). **No hit** in
`WO-149-cutscene-census.md` or `WO-149-cutscene-census.csv`; the markdown's only non-ASCII characters are typographic (dashes, §, ×, ≥).
Quoted engine log lines are format strings with `<name>`/`<soul>`/`<h>` placeholders. No external project is named. The log pack
stayed in the scratchpad and is not committed.

## 8. What is still open

The census's section 8 lists 14 items only a live run can settle. The three known cases that are not in the pack (the lake massacre
timings, "Henry falls" queued again, the opening siege on two machines) need the game logs of the opening (session S2 before 02:12
host-local) or a new two-machine run of M01. Retail was not probed. The per-quest scene order in the CSV is graph order.

## 9. Not done, on purpose

No code, no build, no `VERSION`, no installer, no tag, no push. The policy for each kind is the maintainer's: the census recommends
and says why.
