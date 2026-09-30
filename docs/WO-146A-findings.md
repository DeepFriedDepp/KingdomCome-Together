# WO-146A — Retail probe, part A: our Lua, pak, console and saves on the retail game

**Live, no native code.** No DLL built or injected; only the mod's existing Lua, the game console (`-devmode`) and the RemoteConsole were used.
No `VERSION` change, no installer, no tag, no release. Companion: [`WO-146A-progress.md`](WO-146A-progress.md) (everything touched outside the repo, the saves folder before and after, the privacy sweep).
Background: [`WO-145-retail-census.md`](WO-145-retail-census.md) (the static census this part checks live).

Placeholders: `<RETAIL>` = the retail install (Steam app 1771300, game 1.5.6, `Win64MasterMasterSteamPGO`), `<MT>` = the Modding Tools install (app 2429020, 1.5.5),
`<saves>` = the game's Saved Games `saves` folder (shared by both builds), `<scratch>` = the session's scratch folder outside the repo.
Evidence marks: **(observed)** seen live in this run; **(code-verified)** read in our code; **(data-verified)** read in a file or binary; **(inferred)**; **(inconclusive)**.
World: the throwaway playline `playline2`, confirmed by the maintainer before step 1. First `autosave027` (a 1.5.5 save), then `autosave028` (the 1.5.6 save made in step 4).

---

## 0. The answer first

1. **Our pak loads on retail, unchanged.** Retail accepts the manifest as it is: "has no version restrictions" / "is not limited to any game version, it will be enabled". It opens `mods\kdcmp\data\kdcmp.pak`, and `kdcmp.lua` runs by itself at start-up (`=== MOD INIT ===` at t = 10.8 s, `Commands OK`, `Player hooks OK`).
   Table patches work: our `buff__kcdmp.xml` buff id resolves in the world (`AddBuff` returns an instance; a made-up id in our range returns nil), and it was removed again.
   **(observed)** The clothing-preset patch and the two `Libs/Config` overrides (dice keys) are **(inconclusive)**: no Lua read proves them, and proving the keys needs one key press (section 5.3).
2. **The safety switches work.** Yes: our `mp_*` console commands register on retail and run from its console, at the main menu and in a world. `mp_sleep_status` and `mp_quest_status` print their normal status lines, and so do `mp_shared_world`, `mp_friendly_fire`, `mp_summary` and the argument form `mp_entity_id <name>`. **(observed)**
3. **What the agent can use: the RemoteConsole on `:4600`, and that is all it needs for Lua.** It runs `#`-prefixed Lua and plain console commands. A console command's output (including cvar reads) comes back in `kcd.log`, not on the socket. Round trip is 31–69 ms, avg 62, over 10 runs. **(observed)**
   `:1403` does not exist on retail (connection refused). The socket sends back only a banner frame and an autocomplete frame. The only outbound channel is the `kcd.log` tail, as on the Modding Tools build.
4. **Stripped commands fail silently in Lua.** All eight names WO-145 section 4.1 lists (plus `wh_sys_TestSaveGame` and the stock `load`) produce exactly one `[Warning] Unknown command: <name>` line in `kcd.log` and do nothing else.
   `pcall(System.ExecuteCommand, …)` returns `true, nil` for them, **the same as for a real command**. So the mod's own bookkeeping (`_npcPauseExec = "ok"`) would record a pause that never happened. The log warning is the only signal. **(observed; the bookkeeping consequence code-verified)**
5. **Lua state reading works unchanged.** NPC soul, name, faction, health, position and combat mode; the player's health, stamina, money, skills and position; inventory items by class; the calendar time: every read the mod relies on returned real values. **(observed, section 3)**
6. **The save gate is settled.** A real 1.5.6 save says `GameReleaseVersion="10506"`, `BuildInfo="1.5.6-15693-release_1_5"`. It also lists **four save-affecting DLC** (Lion's Crest, Legacy of the Forge, Mysteria Ecclesiae, Brushes with Death), all four reported `active: N` by the Modding Tools build.
   **So the Modding Tools menu refuses a retail-saved world twice over: the version gate fires first, and the DLC gate would fire too.** One save on retail stamps a 1.5.5 world this way (its header before had no `<DLCs>` at all). **(data-verified)**
7. **Two save and load routes behave differently on retail**:
   - `Game.QuickSave()` returns `false` and writes nothing, even with no script lock held (the mod's WO-125 joiner snapshot route). Why is (inconclusive).
   - `Game.SaveGameViaResting()` (= `EnqueueAutoSave`, the host's world save) works when no script lock is held. Under a lock it does nothing, and the error line the Modding Tools build prints is compiled out.
   - `Game.QuickLoad()` returns `false`, and `wh_sys_LoadGame` and the stock `load` are unknown. **No load route without input was found.** The maintainer loaded the world from the menu.
   - An untried candidate is `wh_sys_AutoLoadLastSave` ("Newest game is auto loaded on game start", flag `REQUIRE_APP_RESTART`). **(observed; the cvar's effect inferred)**

**For part B:** retail is running with `-devmode`, the mod loaded from a temporary `<RETAIL>\Mods\kdcmp`, and the throwaway world `playline2/autosave028` loaded, with two script save locks held (section 6).

---

## 1. The channels (step 1)

| channel | retail result | mark |
|---|---|---|
| RemoteConsole `:4600`, `#`-prefixed Lua | runs; multi-statement, `pcall`, tables, loops | observed |
| RemoteConsole, bare Lua (no `#`) | `[Warning] Unknown command: System.LogAlways("...` | observed |
| RemoteConsole, a cvar name (`wh_sys_GameReleaseVersion`) | prints `wh_sys_GameReleaseVersion = ver_01_05_06 []` into `kcd.log` | observed |
| RemoteConsole, a console command (`wh_sys_LastLoadedSave`) | prints its value into `kcd.log` | observed |
| RemoteConsole, setting a cvar (`wh_sys_DebugSaveLock 1`) | applied (the save-lock debug lines appeared) | observed |
| inbound frames on the socket | 16 bytes on every connect: `'1'` banner (empty) + `'6'` autocomplete `map trosecko`; no command output | observed |
| round trip (`#System.LogAlways` → line in `kcd.log`), 10 runs | min 31 / avg 62 / max 69 ms | observed |
| `:1403` (Modding Tools REST) | nothing listening; connection refused | observed |
| `http_startserver` | not run (see Decisions) | — |

The first round-trip measurement read ~355 ms. That came from my helper's 300 ms socket poll, not the game; re-measured with a 5 ms poll. WO-18 measured 30 ms avg at the main menu; the ~62 ms here was in a loaded world. Where the difference comes from is not measured. **(inferred)**

## 2. The stripped commands (step 2)

Each was sent as `#local ok,e=pcall(System.ExecuteCommand,"<cmd>") System.LogAlways(...)` over the RemoteConsole, with the throwaway world loaded. The NPC was `ttkc_man_5` (a guard 18 m from the player).

| command (as sent) | `kcd.log` | Lua sees | mark |
|---|---|---|---|
| `wh_sys_LastLoadedSave` (control, present) | its value | `pcall=true ret=nil` | observed |
| `wh_ai_PauseNPC ttkc_man_5` | `[Warning] Unknown command: wh_ai_PauseNPC` | `pcall=true ret=nil` | observed |
| `wh_ai_ResumeNPC ttkc_man_5` | `[Warning] Unknown command: wh_ai_ResumeNPC` | same | observed |
| `wh_ai_NPCStateResetElement ttkc_man_5 Stance` | `[Warning] Unknown command: wh_ai_NPCStateResetElement` | same | observed |
| `wh_sys_LoadGame` (no args) | `[Warning] Unknown command: wh_sys_LoadGame` | same | observed |
| `wh_concept_HasteTrigger` (no args) | `[Warning] Unknown command: wh_concept_HasteTrigger` | same | observed |
| `wh_rpg_DisablePlayerFallDamage` (query) | `[Warning] Unknown command: wh_rpg_DisablePlayerFallDamage` | same | observed |
| `wh_rpg_AutoDisablePlayerFallDamage` (query) | `[Warning] Unknown command: …` | same | observed |
| `sys_simple_http_base_port` (query) | `[Warning] Unknown command: sys_simple_http_base_port` | same | observed |
| also found: `wh_sys_TestSaveGame`, `load` | `Unknown command` | — | observed |

* **Nothing else happens:** no error, no Lua exception, no crash. `ExecuteCommand` runs synchronously: the warning is logged before the next Lua statement's own line.
* **Consequence (code-verified):** every `pcall(System.ExecuteCommand, "wh_ai_PauseNPC " …)` in `kdcmp.lua` (for example the pause lever at line 4193) takes the success branch. On retail the pause, resume and stance-reset features would believe they worked. A retail build has to either gate them off or watch the log for `Unknown command: wh_ai_` (the agent already tails the log).
* The WO-145 "stripped" list is confirmed exactly: no name on it exists on retail. Two present controls ran normally: `wh_sys_LastLoadedSave` and `wh_sys_DebugSaveLock`.

## 3. What Lua can still read (step 3)

Every read below is the same call the mod makes, sent over the RemoteConsole and wrapped in `pcall`. **(observed)**

| read | value on retail |
|---|---|
| `System.GetEntitiesInSphere(pos, 40)` | 29 NPC entities with souls |
| NPC `System.GetEntityByName("ttkc_man_5")`, `:GetName()` | found; `ttkc_man_5` |
| NPC `.soul` / `soul:GetId()` / `soul:GetNameStringId()` | table / userdata / `soul_ui_name_guard` |
| NPC `actor:GetHealth()`, `soul:GetState("health")`, `actor:IsDead()` | 100 / 100 / false |
| NPC `GetWorldPos()` | a real position (moved between reads) |
| NPC `soul:GetFactionID()`, `soul:IsInCombatMode()` | a guards faction name / false |
| player `GetName()`, `soul:GetNameStringId()` | `Dude` / `char_26_uiName` (Henry) |
| player `actor:GetHealth()`, `soul:GetState("stamina")`, `actor:IsDead()`, `actor:IsUnconscious()` | 31.02 / 89.68 / false / false |
| player `inventory:GetMoney()`, `soul:GetSkillLevel("fencing")` | 15.1 / 7 |
| player `human:GetItemInHand(0)`, `human:IsMounted()`, `human:IsInDialog()` | null handle (empty hand) / false / false |
| `inventory:GetInventoryTable()` → `ItemManager.GetItem(id).class`, `.amount` | 34 entries; class GUID + amount |
| `ItemManager.GetItemName(class)` | `bandage_classic` |
| `Calendar.GetWorldTime()`, `GetWorldTimeRatio()`, `IsWorldTimePaused()`, `GetWorldHourOfDay()`, `GetWorldDay()` | 1009149 / 15 / false / 16.32 / 11; the clock advances |
| `Script.SetTimer`, `System.AddCCommand`, `UIAction.CallFunction`, `XGenAIModule`, `Game.AddSaveLock`/`RemoveSaveLock` | all present |
| `Script.SetTimer(500, <global function>)` sent over the RemoteConsole | fires (a string name instead of a function did not) |

Errors, both outside the mod's shipped path:
* `actor:GetMount()` and `human:GetMount()` raise a Lua error when the player is not mounted. The mod's only call site (line 14781) is a probe; whether they behave the same on the Modding Tools build was not checked. **(inconclusive)**
* `soul:IsInDialog()` does not exist; the mod uses `human:IsInDialog()`, which works.

**What this means:** everything the mod reads through Lua (the WO-28 vitals, the WO-32/60 NPC sampling, the item and money reads, the WO-38/104 clock) works on retail as it is. What does not carry over is the agent's `:1403` RTTR reads (`rpg/SoulList`), which need a Lua or native replacement (WO-145 section 4.2).

## 4. One real 1.5.6 save (step 4)

### 4.1 Making it
| attempt | locks held | result | mark |
|---|---|---|---|
| `Game.QuickSave()` | `wo146_probe` (see Decisions 2) | `false`, no file, nothing in the log | observed |
| `Game.SaveGameViaResting()` | `wo146_probe` | nothing: no file, no log line (the Modding Tools build logs `AutoSave is disabled under a script lock`; retail has no such string) | observed |
| `Game.QuickSave()` | none | `false`, no file, nothing in the log | observed |
| `Game.SaveGameViaResting()` | none | **`autosave028.whs` written** (a new slot, no overwrite), `SaveEntities: 12076 entities saved.` | observed |

Both locks were back about a minute later, and no other file in `<saves>` changed (progress page, section 3).
On the Modding Tools build QuickSave passes script locks and asks only `CanSave(3)` (WO-125, code-verified there). On retail it refuses even without them, for a reason not found here (the player was not in combat and not loading). **(inconclusive)** This breaks WO-125's joiner snapshot (`snapshot=QuickSave`) on retail. The host's world save (`EnqueueAutoSave`) works.

### 4.2 The header (from a copy in `<scratch>`, read with `tools/Read-SaveAnatomy.py` and a direct dump of the description XML)

| field | 1.5.6 save (`autosave028`, retail) | the 1.5.5 save it was loaded from (`autosave027`) |
|---|---|---|
| `BuildInfo` | `1.5.6-15693-release_1_5` | `1.5.5-release_1_5` |
| `AssemblyDate` | `2026-06-17` | empty |
| `GameReleaseVersion` | **10506** | 10505 |
| `NewGameReleaseVersion` | 10505 (the world was started on 1.5.5) | 10505 |
| `<DLCs>` | `QuestForValor`, `ForgeTycoon`, `MysteriaEcclesiae`, `BanditCamps` | none |
| `<UsedMods>` | `KCD2 Multiplayer` 0.3 (carried over from the loaded save: no mod was loaded in this game session) | `KCD2 Multiplayer` 0.3 |
| `SaveType` / `GameMode` | `AutoSave` / `normal` | same |
| stream | version word 23, footer MD5 framing OK, 6,356 soul records, Henry decoded | — |

**Verdict on WO-145 section 5.1 (data-verified):**
* The Modding Tools build reads `wh_sys_GameReleaseVersion` = 10505 (WO-145), and this save carries 10506. So **the version gate blocks it**, and it is tested first.
* **The DLC gate would block it too.** All four listed DLC have `save: Y` (affect saves), and the Modding Tools log on this machine reports every one of them `active: N`.
* The DLC list is every active save-affecting DLC, not what the world used: the source save had none, and one retail save added four.
* A 1.5.5 world saved even once on retail can no longer be picked from the Modding Tools menu. A direct load bypasses both gates (WO-145), but on retail no command for a direct load was found (section 5.4).
* `tools/Read-SaveAnatomy.py` reads the 1.5.6 format unchanged.

## 5. Our pak, our Lua, our switches (step 5)

### 5.1 Launch
* `<RETAIL>\Mods\kdcmp\` was created holding `mod.manifest` and `Data\kdcmp.pak`, byte-identical to the repo's. The pak's five entries match the tracked sources, sha256.
* The game was closed with `System.Quit()` over the RemoteConsole. It was gone in 2 s (`CSystem::Quit invoked … Quit requested by CScriptBind_System::Quit()`), and no exit save was written.
* Relaunched with `steam.exe -applaunch 1771300 -devmode`. **No Steam prompt.** The game process appeared after 2 s and the main menu (`PlayVideoOnly 'main_menu_trosecko'`) after about 14 s. **(observed)**
* The process command line was `KingdomCome.exe -devmode -devmode`, so Steam's own launch options for the app already carry `-devmode` and ours was appended. **(observed; cause inferred)**
* The game window took the foreground on launch; it was pushed down once, without being activated (standing rule 2).

### 5.2 At the main menu (observed, from `kcd.log`)
```
Mods dir is 'mods'
[Mod] 1 mods loaded from mods/
[Mod] 'mods/kdcmp' has no version restrictions in manifest
[Mod] mods/mod_order.txt not found
[Mod] 'mods/kdcmp' is not limited to any game version, it will be enabled
[Mod] Opening paks in mods/kdcmp/data/*.pak
Pak 'mods\kdcmp\data\kdcmp.pak' is opened, root: 'data\'
[KCD2-MP] === MOD INIT ===            (t=10.797)
[KCD2-MP] WO108-BUILD … WO114-BUILD   (every build line, as on the Modding Tools build)
[KCD2-MP] Commands OK
[KCD2-MP] Player hooks OK (OnInit + OnAction x2)
Loading lua init script for mod kdcmp ...
```
* **Manifest accepted; pak opened; `kdcmp.lua` auto-runs from `Scripts/Startup/` inside the mod pak.** It runs *before* the loader's own "Loading lua init script" line.
* `Commands OK` means the whole registration block ran (all 204 `System.AddCCommand` calls in `kdcmp.lua`) without a Lua error (code-verified). `KCD2MP` is a live global at the menu; `player` is nil there, as expected.
* On level load the mod manager also looks for `mods/kdcmp/data/levels/trosecko/*.pak`. There are none, so this is harmless.
* The Modding Tools build prints no `[KCD2-MP] Player loaded!` either, so its absence here is not a retail difference (code-verified hook, data-verified logs). That hook's `OnInit` override is dead on both builds.

### 5.3 The table patches and the config overrides
| file in our pak | test | result | mark |
|---|---|---|---|
| `Libs/Tables/rpg/buff__kcdmp.xml` | in the world: `player.soul:AddBuff("<kcdmp_death_guard id>")`, then `RemoveAllBuffsByGuid` twice | instance returned; removed 1, then 0; a made-up id in our range returns nil. **The patch applied.** | observed |
| `Libs/Tables/item/clothing_preset__kdcmp.xml` | `actor:EquipClothingPreset("kcd2mp_ghost_armor")` on one NPC | returns nil for ours and for a made-up name alike; the effect was not looked at (no screenshots) | inconclusive |
| `Libs/Config/keybindSuperactions.xml`, `Libs/Config/defaultProfile.xml` | `System.IsFileExist` resolves both, but both also exist in `IPL_GameData.pak`; `sys_PakPriority` = 2, the same override path as the Modding Tools build | whether our actions registered needs one key press with `mp_log_actions on` | inconclusive |

Both kinds of override come from the same mod pak through the same mod manager. The buff patch shows the table-patch path works on retail. That the clothing and keybind overrides take the same way is **(inferred)**.

### 5.4 The safety switches, and getting back into a world
**Yes: our `mp_*` commands register and run in retail's console, at the main menu and in a loaded world.** **(observed)** Every command below was sent as a plain console line over the RemoteConsole, the same way a player's console line runs:
* `mp_sleep_status` → `[KCD2-MP] WO140-STATUS session=false … vote=on …`
* `mp_quest_status` → `[KCD2-MP] WO137-STATUS sync=on talk=on …` and `[KCD2-MP] QUEST sync is ON (32 main quests, 53 fireable beats, …)`
* `mp_shared_world` (bare = report) → `WO122-TOGGLE shared_world=on`; `mp_friendly_fire` (bare) → `MP-FF mp_friendly_fire=on`; `mp_summary` → the `MP-SUMMARY-MOD` line; `mp_entity_id nobody_wo146a` → `nobody_wo146a id=not found` (the `%line` argument form works).

Getting back into the world without input:
| route | result | mark |
|---|---|---|
| `Game.QuickLoad()` (Lua over the RemoteConsole, at the menu) | `false`; nothing loaded within 8 s | observed |
| `Game.LoadGame`, `Game.LoadLastSave` | not registered (nil) | observed |
| `wh_sys_LoadGame` | unknown command (section 2) | observed |
| stock CryEngine `load <save>` | `[Warning] Unknown command: load` | observed |
| `wh_sys_AutoLoadLastSave` | exists, 0, `REQUIRE_APP_RESTART`, help "Newest game is auto loaded on game start"; not tried | data-verified / untried |
| what happened | the maintainer loaded `playline2/autosave028` from the menu while this was running | observed |

A load does not survive: script save locks were re-added right after it. Lua timers fire in the loaded world; the world clock runs.

### 5.5 The temporary `Mods` folder
**Left in place** for part B, as the work order asks: part B follows and needs the game in a world with the same mod.
Remove it after part B, with the game closed (it holds the pak open): delete `<RETAIL>\Mods` (it held nothing before this run). **(recorded in the progress page)**

---

## 6. State handed to part B
* Retail running (`-devmode -devmode`), `:4600` listening, mod `kdcmp` loaded from `<RETAIL>\Mods\kdcmp` (temporary).
* The world is `playline2/autosave028` (throwaway; the 1.5.6 save from step 4).
* Script save locks `wo146_probe` and `wo146a_lock` are held. **A load clears them:** re-add both after any load (`Game.AddSaveLock(name, text)`). `wh_sys_DebugSaveLock 1` lists them in `kcd.log` on every change (it resets on restart).
* No launcher, agent or relay was running at the start or at the end.
* NPC `ttkc_man_5` had `EquipClothingPreset` called on it twice (section 5.3), so its outfit may differ; it received two unknown-command pause/resume lines and nothing else.

## 7. What this means for the port (short)
* **Lua tier: ready.** The pak, auto-run, switches, reads, timers and the `:4600` inbound channel all work on retail with no change to `kdcmp.lua`.
* **The first code changes for a retail build**:
  - a retail flag that turns off (or log-verifies) the four stripped-command features: pause/resume, the stance reset, the join auto-load and fall damage;
  - a snapshot route other than `Game.QuickSave` for WO-125;
  - a load route for the join flow: `wh_sys_AutoLoadLastSave` at launch is the candidate to try next, or a native call in part B.
* **Saves: one-way.** A world saved on retail cannot go back to the Modding Tools menu (version and DLC gates). Mixing the two builds in one playline should be treated as a one-way move.

---

## Decisions made unattended
1. **The playline gate.** The work order's starting message did not name the throwaway playline, so I stopped before step 1 and asked. The maintainer confirmed that the loaded one (`playline2`) is the throwaway. Before that I only backed up `<saves>` and took a save lock.
2. **An earlier lock, `wo146_probe`.** `kcd.log` already had a `[WO146] savelock …` line from before this session, and `wh_sys_DebugSaveLock` showed a held script lock `wo146_probe` besides mine. I did not write it.
   For the one manual save in step 4 I lifted it with mine, then re-added it under the same name (description `WO-146 probe`; the original description is unknown). I also re-added both after the maintainer's reload.
3. **Save route.** `Game.QuickSave` refused, so the one manual save was made with `Game.SaveGameViaResting` (an AutoSave). I checked by name, size and sha256 that it took a new slot (`autosave028`) and overwrote nothing. It was left in the playline, because the world handed to part B was loaded from it.
4. **`http_startserver` was not run.** WO-18 already observed it: stock server on port 80, no `/api/`. Starting it would open another unauthenticated listener on the LAN for no new answer.
5. **Stripped commands were called with the least risky arguments.** The load, haste and fall-damage commands had no arguments or were only queried, so that a command that unexpectedly existed would at most print or do its default. None existed.
6. **No screenshots, no input.** Nothing on screen was checked, and the clothing-preset effect and the keybind overrides are left inconclusive rather than looked at. The only window action was one push-down after the relaunch.
7. **The relaunch command** was the work order's `steam -applaunch 1771300 -devmode`, even though Steam's own launch options already add `-devmode`. The doubled flag did no harm.
8. **Evidence of "no prompt"** is the game process appearing 2 s after the Steam call. No Steam window was inspected.
9. **Measurement fix.** My first round-trip numbers (~355 ms) came from the helper's socket poll interval; I fixed the helper and re-measured. Only the second set is reported as the result.
