# WO-145 — progress: what was read, the tools, what was touched, the privacy sweep

Read-only work order. Findings: [`WO-145-retail-census.md`](WO-145-retail-census.md). Census table: [`WO-145-native-dependencies.md`](WO-145-native-dependencies.md) and [`.csv`](WO-145-native-dependencies.csv).
Placeholders: `<MT>` Modding Tools install, `<RETAIL>` retail install, `<repo>` the working tree, `<saves>` the game's Saved Games folder, `<scratch>` the session's scratch folder (outside the repo).

## 1. What was done, in order
1. Phase 0: `git fetch` and divergence check; found both installs through `libraryfolders.vdf` and the two app manifests; hashed every executable and DLL in the two build folders; read the PE layout of the retail exe and `WHGame.dll`; compared `system.cfg`, `whdlversions.json` and the build headers of both `kcd.log` files.
2. Wrote a small read-only toolkit (PE parser, RTTI → vftable, `.pdata` function boundaries, RIP-relative string cross-references, byte-pattern scans, a capstone displacement check) and checked it against the Modding Tools modules as a control.
3. Dispatched nine read-only readers under one written brief (hard rules, evidence marks, row schema): four census readers over `native/KCDMP/*`, one for hooks/threads/injector, one for Lua/console/cvar/pak dependencies, one for saves and DLC, one for the engine questions, one for retail combat/animation threading and protection markers.
4. Merged 599 raw census rows into 579, assigned stable ids, probed every row against the retail image and its Modding Tools module, classified A/B/C/D, reviewed and corrected the rules (section 4), wrote the three documents.

## 2. What was read
* **Native DLL:** every line of `native/KCDMP/*.cpp|h` (29,277 lines, about 93 files) plus `native/KCDMP_LauncherInjector/main.cpp`. Rows per reader: 185 + 111 + 125 + 178 = 599 raw rows.
* **Launcher, installer, agent, Lua:** the parts naming the game, the Steam app id, module names, the Mods folder, console/REST channels, and every Lua payload; the shipped `kdcmp.lua` (20,375 lines) scanned for engine names with a Lua-aware scanner (comments and strings stripped); `tools/*.ps1`, `tools/*.lua` probes and `native/experiments/*` for the dev-tool rows. The 40+ `Test-*Synthetic.lua` files run against stubbed tables and were skipped.
* **Work-order pages** (read in full or by section): WO-18, 40, 42, 44, 53, 67, 99.5, 100, 105, 112, 115, 116, 118, 119, 121, 122, 126A, 127, 129, 135–144 and others as cited in the rows; `docs/kcd2_lua_api.md`. Not all read through: WO-32, 60, 63, 75, 107–109, 114, 128, 141A were listed only (they add no engine anchor to the census files).
* **Game files, read-only:** every executable and DLL in both build folders (hashes, PE layout, exports, imports, RTTI, strings); the Modding Tools module DLLs for the control and the thread/job analysis; `Scripts.pak`, `Tables.pak`, `IPL_GameData.pak`, `Engine.pak`, `English_xml.pak` (zip central directories compared entry by entry; selected XML/Lua read in memory); `system.cfg`, `whdlversions.json`, `dlc.xml`, `pak.cfg`, both `kcd.log` files (grepped for build info, DLC list, mod-manager and load lines), the Modding Tools `ConsoleHTMLHelp` and script-bind docs, both Steam app manifests, the two `Mods` folder listings.
* **Saves:** six save files copied to `<scratch>` (each copy re-hashed and matched to its original afterwards); all decoding was done on the copies.

## 3. Tools used (and not available)
* **Used:** Python 3.14 (standard library), `capstone` (already installed; used for the TLS callbacks, the save-menu gate, the threading functions and the struct-offset check), the repo's own `Read-SaveAnatomy.py` imported as a module for TLV walking on the save copies (no bytecode was written; the `tools/__pycache__` files predate this work: 2026-09-27), `git` read-only plus one `git fetch origin`.
* **Not available, and what was done instead:** `pefile` and `lief` (not installed; wrote a minimal PE parser), Ghidra (not installed; no decompilation was done, so all retail findings are string/RTTI/`.pdata`/byte-scan level plus a few capstone listings), `dumpbin`. No packages were installed.

## 4. Method notes that matter for trusting the numbers
* A row is one independently validated engine fact; recurring facts were merged (20 duplicates removed). 14 rows are game-data or working-directory names (class D) and 22 more were shown to be data names present in the paks (class D): 36 D rows in all.
* **Class rules** are in section 3.3 of the findings. Changes made after a first pass, all toward being stricter: a string row is A only if the same literal is found and every listed string exists; a retail string that merely *extends* ours is B; a struct-field row is A only if the retail function reads all tested offsets (none did); rows whose recorded anchor also fails on the Modding Tools control are flagged inconclusive (10 rows); the relaxed string-boundary rule (padding instead of NUL) produced one B.
* **Control:** 264 of the 295 rows that carry a string or RTTI anchor resolve on the Modding Tools build; the 31 that do not are 21 data names and 10 imprecise anchors.
* The probe can establish presence, absence and uniqueness, never meaning. Claims about what a retail function does were made only where it was disassembled (the save-menu gate on the Modding Tools build, the retail TLS callbacks, the retail `denuvo` string branch, the job-kick and main-thread-compare sites).

## 5. Everything touched outside the repo
* **`<scratch>` (outside the repo, session-specific):** reader outputs (census rows, notes), the toolkit and its caches (string/RTTI/xref indexes of both builds), six save copies, extracted Lua and XML from the paks, text dumps of the script-bind docs, per-file string indexes. None of it is committed.
* **`git`:** one `git fetch origin` (updates the remote-tracking refs inside `.git`); nothing else. No commit other than the final docs commit, no tags, no branch, no `VERSION` change, no release, no installer built.
* **Game installs:** nothing written. Binaries, paks, logs and configs were opened read-only (`rb`, read-only memory maps, `zipfile` read mode).
* **Saves:** nothing written or moved. Originals were opened only to copy and to re-hash afterwards.
* **One slip, corrected:** a reader's first extraction call used a POSIX-style path inside Windows Python and wrote 415 of its own extracted Lua files into a new folder at the root of a drive, outside both installs and outside the repo. It deleted exactly that folder (only its own files in it) and confirmed it was gone; I re-checked the drive root afterwards and it is. Nothing inside either game install was written.
* No game, launcher, installer or injector was started; no process was opened.

## 6. Not done, and why
* No decompilation; no claim about what retail functions do beyond the listed disassemblies.
* No live run of anything: stripped commands were not called, the pak was not loaded on retail, no retail 1.5.6 save exists on this machine (the version-gate case is inferred).
* The nine engine questions were answered from our docs, the game data and strings; five are "partly", none "needs him" outright, and the follow-up list is in the findings.
* The Steam entitlement call, the `C_NPCContext` function addresses in retail, the call graph from the frame loop to the combat and actor groups, and the start/sync roles of the two parallel-updater events were not traced.

## 7. Privacy sweep of the committed documents
Searched the three findings/table documents and the CSV for: the working account's name and email, the repo owner's handle, drive-letter and user-profile paths (`X:\`, `X:/`, `/Users/`, `\Users\`), Steam library paths, vendor build-host and build-agent names and the PDB path embedded in the binaries, IPv4 addresses, email-shaped strings, and the names of the external retail-side projects, libraries and developers that older pages name. **Zero hits.**
Judgement calls: install folders are `<MT>` and `<RETAIL>` everywhere; the build-host user name inside a save's debug block and the vendor build-agent path are described, not quoted; the one third-party mod in the Modding Tools `Mods` folder is not named; another developer who works on retail is "an external retail developer", and an external offset/header reference is "an external retail-side reference". Steam app and DLC ids, game class, cvar, console and pak names and the game's own short strings are game content and are kept (quotes under 15 words). The probe scripts and string dumps contain local paths and large extractions from the game binaries and were not committed.
