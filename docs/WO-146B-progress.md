# WO-146B — progress: the runs, what was touched outside the repo, the saves folder before and after, the privacy sweep

Findings: [`WO-146B-findings.md`](WO-146B-findings.md).
Placeholders: `<RETAIL>` the retail install, `<MT>` the Modding Tools install, `<saves>` the game's Saved Games `saves` folder,
`<scratch>` the session's scratch folder outside the repo, `<repo>` this working tree.

## 1. The runs, in order (2026-09-30, local time)

### Before any code ran
1. **Starting state checked.** The retail game was running with the throwaway world `playline2/autosave028` loaded, `:4600` listening, no launcher,
   agent or relay process anywhere. `<RETAIL>\Mods\kdcmp` was still in place from part A.
2. **Log copied** to `<scratch>` (the single-backup rule: each launch overwrites the game's one backup).
3. **Saves backed up** (standing rule 1): all 318 files of `<saves>` copied to `<scratch>`, a sha256 list written, and the copy verified file by file
   against it — all 318 matched. The hash-of-the-hash-list also matched the value part A recorded at its end, so nothing had changed in between.
4. **Playline gate.** `wh_sys_LastLoadedSave` = `%USER%/saves/playline2/autosave028.whs`. Proceed.
5. **Static work** on the two installs, read-only, before touching the running game: the anchors for steps 1–5 were derived offline from
   `<MT>`'s module DLLs and `<RETAIL>`'s `WHGame.dll` with a small Python PE/RTTI/`.pdata` toolkit and `capstone`, so the probe would have known-good
   answers to check itself against.

### The power cut
6. Mains power was lost mid-session, before any probe code had been built or run. On resume: the machine had rebooted, the game was gone.
   **All 318 saves were re-hashed and were byte-identical to the backup.** Nothing needed restoring.

### First probe process (pid ending 636; started 12:45 by the maintainer, world loaded by the maintainer)
7. **Save locks re-added** over the RemoteConsole (`wo146b_lock`, `wo146_probe`, both `true`), `wh_sys_DebugSaveLock 1` to see them.
8. **v1 injected** — the identity gate passed (sha256 and file size both matched WO-145's record). v1 could not run its discovery because it queued the
   work onto a frame hook that did not exist yet; it was told `bye` and superseded.
9. **v2 injected**: step 1 found its three anchors and installed the frame hook. Ticked once per frame on one thread, 98 fps, for the rest of the run.
10. **Step 2** (gEnv, its members, the entity system, the first RTTR attempt), **step 3** (the C_Actor vtable scan), **step 5** (the class census),
    **step 4** (the two combat vftables, read only).
11. **v3, v4, v5, v6 injected in turn**, each to correct or extend the reflection work; every one of them refused to re-install the frame hook because
    the function was already patched (the fail-closed prologue check doing its job) and said so in the log. Their read-only work ran on the probe's own
    worker thread.
12. **Three capture hooks armed** (C_Actor slot 151, `EnterImpl`, the `C_CombatSoul` hit slot), all with a blanket 18-byte prologue copy.
13. **A fight was started** between two NPCs over the RemoteConsole (the game's own attack interrupt). **The game crashed within seconds** — the probe's
    own bug, diagnosed in the findings §6: 18 bytes is not an instruction boundary in either combat function.
    - The saves were re-hashed immediately: **all 318 unchanged**.
    - The vendor crash reporter (`BsSndRpt64`, window title "Crash Report") opened and was **left alone**: no button was pressed, nothing was sent.
      It is the maintainer's to answer.
    - The game's log and the probe's log were copied to `<scratch>` before anything else.

### Second probe process (pid ending 548)
14. **Relaunched** with `steam.exe -applaunch 1771300 -devmode +wh_sys_AutoLoadLastSave 1`. Steam dropped the extra arguments (the process command line
    shows only the app's own `-devmode`), so part A's untried auto-load cvar is **not** a route to a world without input. The maintainer loaded
    `playline2/autosave028` from the menu and said so; the gate was re-checked and the two save locks re-added.
15. **The window was pushed down once**, without activating it. (One push-down per launch; two in the session.)
16. **v7 injected**: step 1 re-ran from scratch and produced the same three addresses in a fresh process; the frame hook went back in (30 fps unfocused).
17. **Step 4 re-armed with instruction-aligned prologue lengths** (20 and 22), and C_Actor slot 151 with 23. No crash this time.
18. **Two more fight attempts** over the RemoteConsole (a plain NPC, then a guard, each aimed at another NPC). Both were accepted (`sent=true`) and
    neither produced combat within 40 s — the nearest human NPCs were 78–150 m away. `EnterImpl` and the hit slot never fired.
19. **Step 2 and step 5 re-run on the main thread** in the fresh process; the reflection and entity results reproduced. The NPC that had carried a full
    `C_NPC` object earlier had streamed down to `C_AIPuppet`, so the context walk was not repeated there (the earlier reading stands).
20. **Slot 151's capture** collected 34 calls across 9 threads over five minutes before the counters were read out.
21. **Step 4 completed, attended.** With both combat hooks armed, the maintainer drew a weapon, swung four times and hit a cow. `EnterImpl` fired
    four times and the `C_CombatSoul` hit slot once, both on the main thread, each captured `this` confirmed by its own RTTI.
22. **D-095 settled** in the same minute: the player's actor was walked again now that a combat actor existed, and `C_Actor+0x278` holds a
    `C_CombatPlayer`. No save was written; the locks were still held.

## 2. Everything touched outside the repo

| where | what | state at the end |
|---|---|---|
| the running game process | one inline detour on `C_ModulesManager::Update` (the frame hook) and, per process, up to three capture-only detours; a probe DLL loaded (up to seven copies across two processes) | gone with the process; nothing is ever unhooked, so each patch lived until the game exited |
| the game world (`playline2`) | three `crime:attackInitiatedByConcept` messages sent between NPCs (one before the crash, two after); no NPC entered combat and no NPC was harmed — all four involved NPCs read `hp=100` afterwards. Then the maintainer's own four swings and one hit on a cow, at my request, to complete step 4. Two save locks held throughout. | left as found; **no save was written** |
| `<RETAIL>\Mods\kdcmp\` | **not touched**; still the folder part A created | **still there — see §5** |
| `<RETAIL>\kcd.log`, `logbackups\` | written by the game itself across three launches (one pre-existing, two in this session) | the game's own files; a copy of each log state is in `<scratch>` |
| `<saves>` | **nothing written.** Save locks were held whenever the game was in a world. | 318 files, unchanged |
| the game window | one push-down per launch (two in total), `SetWindowPos` to the bottom without activating | no other window action, no input, no screenshots |
| processes | the game was launched once through Steam by me; it was never stopped by me (it crashed once, and was relaunched by me and loaded by the maintainer). No launcher, agent or relay existed at any point, so none was stopped. | one game process running at the end |
| `<scratch>` | the saves backup (556 MB), three sha256 lists, copies of the game log at four points, copies of all seven probe logs, the Python PE/RTTI/`.pdata`/signature toolkit and the eight static-analysis scripts | not committed |
| `<repo>\native\build\wo146b\` | the seven built probe DLLs, their object files, their logs and their command files | gitignored (`native/build/`), not committed |
| `<repo>` | the probe's sources (`native/experiments/wo146b_retail_probe/`) and these two documents | committed |

Nothing was written inside `<MT>`; its module DLLs were only read. Inside `<RETAIL>` nothing was written by me at all.

## 3. The saves folder, before and after

Hashed with `sha256sum` over every file under `<saves>`, sorted by path; the lists are kept in `<scratch>`.

| | files | sha256 of the sorted hash list |
|---|--:|---|
| at the start of the session | 318 | `a1a231337387abee0a2e29a5936855371987f95df558f4316b993c15049935b3` |
| after the power cut and reboot | 318 | identical |
| after the crash | 318 | identical |
| at the end | 318 | identical |

**No save was created, modified or deleted.** The list also matches, byte for byte, the "after" list part A recorded — so the saves folder is unchanged
since part A ended. The backup copy in `<scratch>` was verified file by file against the original when it was made (318/318).

## 4. Tools

* The probe: MSVC 2022 BuildTools, `cl /LD /O2 /EHsc /MT /std:c++17`, built by `native/experiments/wo146b_retail_probe/build.cmd` (a new output name per
  build). Three sources, ~1,700 lines: the anchor toolkit, the capture hook, and the five steps. It links only `bcrypt` (for the identity hash).
* The injector: `native/build/KCDMP_LauncherInjector/KCDMP_LauncherInjector.exe`, unchanged, `--pid <pid> --dll <abs path>`.
* Offline: Python 3.14 (standard library) plus `capstone` for a read-only PE / RTTI / `.pdata` / masked-code-signature toolkit over both installs.
* A RemoteConsole helper on `:4600` (one `'5'` frame per command, then a tail of the game log), as in part A.
* `sha256sum`, `netstat`, `Get-Process`, `Start-Process` for the Steam call.
* No packages were installed; no tool was added to either game install.

## 5. Part A's temporary `Mods` folder — **not removed**

The work order asks for `<RETAIL>\Mods` (created by part A, absent before it) to be removed at the end and the removal recorded. **It is still there.**

Why: the folder holds `mods\kdcmp\data\kdcmp.pak`, the game has it open, and the game is still running with the throwaway world loaded — the work order's
own rule is that the folder can only be deleted with the game closed, and closing the maintainer's game to tidy up is not a call to make unattended
while a follow-up (the one swing that would settle step 4) is still worth doing.

**To remove it**, with the game closed:

```bash
rm -rf "<RETAIL>/Mods"
```

It contained nothing before part A created it, so deleting the whole `Mods` folder restores the install exactly. Its contents are two files, both byte
copies of the repo's own `kdcmp/` (`mod.manifest`, `Data\kdcmp.pak`).

## 6. Privacy sweep of the committed documents

Searched `docs/WO-146B-findings.md`, `docs/WO-146B-progress.md` and every file under `native/experiments/wo146b_retail_probe/` for: the working account
name and e-mail, the Steam account name and id the game log prints, the repo owner's handle, drive-letter and user-profile paths (`X:\`, `X:/`,
`\Users\`, `/Users/`, `Saved Games`), Steam library paths, host and build-machine names (including the build host the game's own log header prints),
the vendor build-agent source path that survives in the retail binary, IPv4 addresses, e-mail-shaped strings, and the names of external mods or projects.
**Zero hits**, apart from the generic Windows folder name "Saved Games" in the placeholder definitions (no path, no user name).

Deliberately **not** used and not reproduced: the vendor build-agent source path. It exists once in the retail binary and would have been a convenient
anchor for `C_Game::Update`; the probe finds what it needs by a code-shape anchor instead, so no build-machine path appears in the repo.

Kept as game content: Steam app ids, class and cvar names, RTTI decorated names, console-command names, NPC entity names (`ttkc_man_5`, `ttkc_man_11`,
`ttkc_man_34`, `ttkc_woman_8`), the player entity name `Dude`, save file names, module-relative addresses (`WHGame+0xRVA`) and byte patterns. Process ids
are written as "pid ending NNN". The playline name `playline2` is the game's own folder name.

## 7. Not done, and why

* **No NPC-vs-NPC fight could be produced from the console**, so step 4's capture was measured only for the player's own action (all of it
  main-thread). Whether an NPC's swing arrives on a job worker is still open.
* **An NPC's health was not read through reflection.** The registry and `type_data` read fine; the property path needs the reimplementation the findings describe.
* **The quest / concept cluster was not probed at all** — out of this work order's five steps.
* **`<RETAIL>\Mods` was not removed** (§5).
