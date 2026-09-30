# WO-146A — progress: what was touched outside the repo, the saves folder before and after, the privacy sweep

Findings: [`WO-146A-findings.md`](WO-146A-findings.md).
Placeholders: `<RETAIL>` retail install, `<MT>` Modding Tools install, `<saves>` the game's Saved Games `saves` folder (both builds share it; retail has Steam Cloud), `<scratch>` the session's scratch folder outside the repo, `<repo>` the working tree.

## 1. What was done, in order (2026-09-30, local time)
1. **Checked the starting state.**
   - Retail `KingdomCome.exe -devmode` was running, `:4600` was listening, and `:1403`, `:7778` and `:5273` had no listener. No launcher, agent or relay process existed.
   - `<RETAIL>` had no `Mods` folder.
   - `kcd.log` showed the last load as `playline2/autosave027.whs`.
2. **Copied the logs:** `kcd.log` and `kcd_launcher.log` went to `<scratch>` (the single-backup rule).
3. **Backed up the saves** (standing rule 1): all of `<saves>` (317 files, 555 MB) copied to `<scratch>` with timestamps kept, a sha256 list written, and the copy verified against it (all 317 OK). `steam_autocloud.vdf` was recorded.
4. **Took a save lock** over the RemoteConsole: `Game.AddSaveLock("wo146a_lock", …)` returned true, and a second add returned false (the lock was held).
5. **Stopped on the playline gate:** the throwaway playline had not been named. The maintainer then confirmed the loaded `playline2` is the throwaway.
6. **Steps 1–3** over the RemoteConsole (channels, stripped commands, Lua reads). Reads only, apart from two `pcall`-wrapped unknown commands aimed at NPC `ttkc_man_5`.
7. **Step 4, the one manual save:**
   - Set `wh_sys_DebugSaveLock 1`, which listed a second held lock, `wo146_probe` (findings, Decisions 2).
   - `Game.QuickSave` was refused twice (with the locks and without); `Game.SaveGameViaResting` with no locks wrote `playline2/autosave028.whs`.
   - Both locks were re-added about a minute after they were lifted.
   - The save was copied to `<scratch>` (sha256 matched the original) and its header read on the copy.
8. **Step 5, the relaunch:**
   - Closed the game with `System.Quit()`; its `kcd.log` was copied first.
   - Re-verified all 317 original saves (unchanged).
   - Created `<RETAIL>\Mods\kdcmp\mod.manifest` and `<RETAIL>\Mods\kdcmp\Data\kdcmp.pak`.
   - Launched `steam.exe -applaunch 1771300 -devmode` and pushed the window down once.
   - Menu checks, then the switches.
   - `Game.QuickLoad` and the stock `load` both failed. The maintainer loaded `playline2/autosave028` from the menu.
   - Re-added both save locks.
   - In-world checks: timers, the buff patch (added and removed on the player), the clothing preset (called on one NPC), and the switches again.
   - Final saves snapshot; `kcd.log` copied.

## 2. Everything touched outside the repo
| where | what | state at the end |
|---|---|---|
| `<RETAIL>\Mods\kdcmp\` | **created**: `mod.manifest` (sha256 `ccf34908…8ee5b`) and `Data\kdcmp.pak` (sha256 `d9ebf261…f21bd89f`), byte-copies of `<repo>\kdcmp\` | **left in place for part B.** Remove the whole `<RETAIL>\Mods` folder after part B, with the game closed; it did not exist before this run |
| `<RETAIL>\kcd.log`, `kcd_launcher.log`, `logbackups\` | written by the game itself: two launches (the maintainer's, and my relaunch, which rotated the earlier log into `logbackups\kcd Build(0) 30 Sep 26 (09 29 33).log`) | the game's own files; copies of each log state are in `<scratch>` |
| `<saves>\playline2\autosave028.whs` | **created** by the step-4 save (1,416,658 B, sha256 `70595a82…6d5775ff`) | left in place: part B's world is loaded from it |
| `<saves>\steam_autocloud.vdf` | modified time changed at the relaunch (Steam's doing) | content unchanged (sha256 `33d27e3c…a5fd0e`, before and after) |
| the running game | save locks `wo146a_lock` and `wo146_probe` (re-added); cvar `wh_sys_DebugSaveLock 1` (gone at restart); one `kcdmp_death_guard` buff added to and removed from the player; `EquipClothingPreset` called twice on NPC `ttkc_man_5`; a global Lua function `WO146A_T` (a timer test) | the game is still running for part B |
| the game window | one push-down (`SetWindowPos` to the bottom without activating it) after the relaunch | no other window action, no input, no screenshots |
| processes | the game was closed once with `System.Quit()` and relaunched through Steam | no launcher, agent or relay existed, so none was stopped |
| `<scratch>` | the saves backup (580 MB), the two sha256 lists, log copies, the 1.5.6 save copy, the RemoteConsole helper (`rc.py`), the probe scripts and their outputs, an extracted copy of the script-bind docs, and vanilla `defaultProfile.xml`/`keybindSuperactions.xml` extracted from `IPL_GameData.pak` for a diff | not committed |
| `<repo>` | two docs added | committed (docs only) |

Nothing was written inside `<MT>`. Its `kcd.log` and the script-bind docs zip were only read. Inside `<RETAIL>` the only write was the `Mods` folder.

## 3. The saves folder, before and after
Hashed with `sha256sum` over every file under `<saves>`, sorted by path. The two list files are kept in `<scratch>`.

| | before (start of the run) | after (end of the run, game running in the world) |
|---|---|---|
| files | 317 | 318 |
| sha256 of the sorted hash list | `6d9f45f7c74d30691044a97619a95f23d070e862941881cd9a311ec77bdd802b` | `a1a231337387abee0a2e29a5936855371987f95df558f4316b993c15049935b3` |
| `playline0` | 143 | 143 |
| `playline1` / `playline1 - Copy` | 115 / 18 | 115 / 18 |
| `playline2` | 26 | **27** |
| `playline2_wo16backup` / `_wo24backup` / `_wo25backup` / `_wo26backup` | 2 / 3 / 3 / 3 | 2 / 3 / 3 / 3 |
| `playline3` | 3 | 3 |
| `steam_autocloud.vdf` | `33d27e3c…a5fd0e` | same |

**The whole difference:** one added line, `70595a824962f9ea06cc049cdeb2cfca0188a5fed798b8524a8ce22c6d5775ff  ./playline2/autosave028.whs`.
All 317 original files matched their "before" hash three times: after the quit, after the relaunch and load, and at the end. No real save was touched, so nothing needed restoring.
The loaded source save, `playline2/autosave027.whs`, is unchanged (sha256 `3dd42887…57f67`).
Steam Cloud: the folder has no other Steam file and no file changed. Whether the cloud received `autosave028` was not checked. **(inconclusive)**

## 4. Tools
* Python 3.14 (standard library): a RemoteConsole helper (one `'5'` frame per command, then a tail of `kcd.log`), a string scanner for `WHGame.dll`, and `zipfile` for the pak comparisons.
* `tools/Read-SaveAnatomy.py` (`verify`, `map --depth 1`, `henry`) on the save copy.
* `sha256sum`, `find`, `netstat`, `tasklist`, and `Start-Process` for the Steam call.
* No packages were installed.

## 5. Not done, and why
* The clothing-preset effect and the keybind/profile overrides were not confirmed: that needs a screenshot or a key press, and both are ruled out.
  Morning step for the maintainer (30 s): in the loaded world, open the console, type `mp_log_actions on`, press F9 and F11 once, then `mp_log_actions off`. A `kcd2mp_dice_*` action name in `kcd.log` confirms the overrides.
* `wh_sys_AutoLoadLastSave` was not tried: it needs a restart, and the world was already loaded by then. It is the next candidate for a load without input.
* Why retail's `Game.QuickSave` refuses was not investigated (native; part B).
* `http_startserver` was not run (findings, Decisions 4).

## 6. Privacy sweep of the committed documents
Searched both documents for: the working account name and email, the Steam account name and id that `kcd.log` prints, the repo owner's handle, drive-letter and user-profile paths (`X:\`, `X:/`, `\Users\`, `/Users/`, `Saved Games`), Steam library paths, host and build-machine names, IPv4 addresses, email-shaped strings, and the names of external mods and projects. **Zero hits**, apart from the generic Windows folder name "Saved Games" in the placeholder definitions (no path, no user name).
Kept as game content: Steam app ids, cvar/command/scriptbind names, DLC ids, NPC entity names (`ttkc_man_5`), the player entity name `Dude`, save file names, and the game's own short log lines (each under 15 words).
The `Mods` folder paths are written relative to `<RETAIL>`. The playline name `playline2` is the game's own folder name. The helper scripts contain local paths and were not committed.
