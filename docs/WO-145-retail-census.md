# WO-145 — Retail census: what it would take to run on the retail game

**Read-only research. No code changed, no port, no game launched, no save touched, no install written.**
Companion files: [`WO-145-native-dependencies.md`](WO-145-native-dependencies.md) and
[`WO-145-native-dependencies.csv`](WO-145-native-dependencies.csv) (the census table, 579 rows), [`WO-145-progress.md`](WO-145-progress.md)
(what was read, the tools, everything touched outside the repo, the privacy sweep).

Placeholders: `<MT>` = the Modding Tools install (Steam app 2429020, game 1.5.5), `<RETAIL>` = the retail install (app 1771300, game 1.5.6),
`<repo>` = this working tree, `<saves>` = the game's Saved Games folder.
Evidence marks: **(code-verified)** read in our code; **(data-verified)** read in game data or a binary (or a log); **(inferred)**; **(inconclusive)**.
Anything said about the retail binary comes from static reading only (strings, RTTI, `.pdata`, byte scans, capstone on a few functions). Nothing was executed.

---

## 0. The answer first

**Recommendation: probe first, then decide. The port is large but it is not a wall, and the probe session can be small and decisive.**

1. **Size.** The native DLL has 565 engine dependencies (579 census rows, 14 of them game-data or working-directory names). 543 are anchored in engine binaries
   and were probed statically against the retail `WHGame.dll`: **89 (16%) class A** (the same anchor works), **254 (47%) class B** (a new anchor is clear),
   **200 (37%) class C** (needs real reverse engineering). The other 36 rows are data names that live in the game's paks, which are byte-identical in both builds.
   The A/B/C call is about *finding* the thing; B rows still need their value (slot, offset, layout) re-verified. **(data-verified, rules in section 3.3)**
2. **Why it is not a recompile.** Every line of native code that touches the engine assumes the Modding Tools build is 40-odd module DLLs with
   about 7,400 exported C++ symbols. Retail is one 89 MB monolith that exports **two** C++ symbols. All 92 of our `export` rows, all 17 split-module
   `GetModuleHandle` lookups, the `CrySystem.dll` start-up gate and the frame hook (an import-table swap against `Framework.dll`) have no retail equivalent as written.
   **The plugin would wait 60 seconds and exit silently on retail.** **(code-verified + data-verified)**
3. **What is on our side.** Retail keeps RTTI for real classes (about 58,400 type descriptors; 46 of the 54 class names our code hard-codes are present),
   keeps `.pdata` (291,812 function entries, so function boundaries are readable), keeps cvar names and help text, keeps RTTR registration names, and is not packed.
   **Game data is identical:** Tables, Scripts, `IPL_GameData` and Engine paks have the same hashes in both installs, so every Lua, table, quest and concept-graph fact in the mod is already proven against retail data.
   The work is native: find ~450 engine facts again, in a PGO build.
4. **The hardest part.** Three things, in order: (a) **the foundation**: a new start-up gate, a frame hook with no import to swap, `gEnv`, and the RTTR reflection ABI (36 rows; retail has no exports to call);
   (b) **struct and vtable layout drift**: the one offset we can check both ways differs (`C_Actor` combat-actor field: +0x300 here, +0x278 retail), so 142 struct-field and 95 vtable-slot rows are hypotheses until re-checked;
   (c) **the NPC-state context** that activity/stance puppets depend on: its class has no RTTI in retail and none of its log strings survive. Combat is *not* the wall its reputation suggests (section 2.4): there is no combat thread, but actor update and animation run in job windows that hooks must respect, and that is already true on the Modding Tools build.
5. **Ongoing cost with retail stable.** Low per patch, if we build the verification in. The retail `WHGame.dll` is the build dated June 19 2026 and has the same MD5 as the one recorded in WO-67. Our anchors are largely RTTI and string based, so a future retail patch would move RVAs but mostly keep the anchors;
   what breaks is slot numbers, offsets and prologue bytes. The fail-closed pattern we already use (validate, then arm the feature) carries over. The real recurring cost is that we would support **two builds** (Modding Tools 1.5.5 and retail 1.5.6) until we drop one.
6. **What the probe session should prove first** (section 7.3; 15 rows chosen in section 3.6): the frame hook and `gEnv`, the RTTR entry points, the `C_Actor` layout shift, `UpdateMannequinTags`, the NPC-state context, and whether our pak and Lua load on retail at all.
   If the first three succeed, the port is a long but well-defined run of work orders; if the NPC-state context or the layout drift turns out wholesale, the activity/stance features become a separate decision.
7. **Saves and DLC (why the maintainer sees greyed saves).** The Modding Tools load menu has **two independent gates**: a **DLC gate** (saves that name a DLC that is not active and affects saves; the retail-written saves on this machine are stamped with The Lion's Crest, which the Modding Tools build reports as not owned) and a **version gate** (a save whose release number is higher than the running build's; 10506 retail vs 10505 here). Both are menu-only; loading a save without the menu, as the mod already does, bypasses them. **(data-verified mechanism; a genuine 1.5.6 save was not available, so the version case is inferred)**
   On retail the DLC content is inside the base paks and entitlement alone switches it on, so DLC co-op is an entitlement-matching problem, not a content-mounting problem (section 5.2).

---

## 1. Phase 0 — origin and inventory

### 1.1 Origin
* `origin` is the project's GitHub repository; `main` was clean and level with `origin/main` (fast-forward, no divergence); WO-144's findings were the latest commit on `origin/main`. **(code-verified, `git` read-only)**

### 1.2 Both installs, found through Steam's library folders
Both are in the second Steam library folder on this machine. **(data-verified, `libraryfolders.vdf` and the two app manifests)**

| | Modding Tools | Retail |
|---|---|---|
| Steam app | 2429020 | 1771300 |
| install directory name | `KCD2Mod` | `KingdomComeDeliverance2` |
| game version | 1.5.5 (`system.cfg`, exe resource 1.5.5.0) | 1.5.6 (`system.cfg`, exe resource 1.5.6.0) |
| build | `release_1_5_1166656_117`, configuration `ReleaseSteamLTO_DLL`, built 2026-04-16 | `release_1_5_1308617_856`, configuration `MasterMasterSteamPGO`, built 2026-06-19 |
| binaries folder | `Bin\Win64ReleaseSteamLTO_DLL` (49 executables and DLLs, including editor files) | `Bin\Win64MasterMasterSteamPGO` (9 files) plus `Bin\Win64Shared` |
| one monolithic executable? | **No.** `KingdomCome.exe` (1.5 MB) plus about 40 module DLLs; `WHGame.dll` is a 4.5 MB glue module that imports about 25 modules | **No single exe, but one monolith:** `KingdomCome.exe` (1.5 MB, launcher only, no game logic) loads one 89 MB `WHGame.dll` that holds every module |
| RTTI | per module; 90,766 unique type names across all modules (includes editor and test modules) | one image; about 58,400 type descriptors (58,533 by a looser count) |
| C++ exports | about 7,400 mangled C++ exports over the game modules (RPGModule 119, EntityModule 798, XGenAIModule 1,782, CrySystem 1,063, Framework 997, ConceptModule 402, PlayerModule 229, ...) | **2 C++ exports** (`CreateGameStartup`, `C_Game::CreateInstance`), 40 vendor SDK exports (NVIDIA NGX, AMD FSR) |
| saves folder | one shared `<saves>` folder for both builds | same |
| game data paks | 42 paks | the same 42 paks (same names and sizes), plus `pak.cfg` and a `Facials` folder |
| `system.cfg` | differs from retail in exactly one line | `wh_sys_version = "1.5.6"` |

Retail also ships `WHGameArm.dll` (107 MB; by its name an ARM build, not examined and not expected to load on x64). A full per-file list with sizes and sha256 is in Appendix A; the per-module export and RTTI counts are in Appendix B.

The retail `WHGame.dll` MD5 (`170a55fe…`) is the same as the one recorded in WO-67, which matches "stable since June". **(data-verified)**

---

## 2. Phase 1 and 2 — the native dependency census, hooks and threads

### 2.1 How the census was built
Four readers each read every line of a share of `native/KCDMP/*.cpp|h` (29,277 lines, about 93 source files; nothing left unread) plus the work-order pages that name anchors, and produced one row per engine dependency
(schema in the table page). A fifth reader covered hooks, threads, the injector and the launcher's assumptions. 599 raw rows merged to 579 by combining facts that recur in several files
(for example `GetGameIface` was used in 8 files and is one row). The merged rows were then probed statically against the retail image (section 3).

**Totals** (full breakdowns by kind, module, thread and hook type are in the table page):

| kind | rows | A | B | C | D |
|---|--:|--:|--:|--:|--:|
| export (resolved by name from a split module) | 92 | 0 | 52 | 40 | 0 |
| string anchor | 104 | 41 | 15 | 28 | 20 |
| RTTI class | 52 | 43 | 0 | 9 | 0 |
| vtable slot | 95 | 2 | 80 | 13 | 0 |
| fixed module+RVA | 24 | 0 | 7 | 17 | 0 |
| prologue bytes | 37 | 3 | 15 | 19 | 0 |
| struct-field offset | 142 | 0 | 82 | 58 | 2 |
| global / static | 19 | 0 | 3 | 16 | 0 |
| data / working-directory | 14 | 0 | 0 | 0 | 14 |
| **total** | **579** | **89** | **254** | **200** | **36** |

By module (top): RPGModule 90 rows, XGenAIModule 83, CrySystem 61 (the RTTR ABI), EntityModule 55, CryEntitySystem 47, ConceptModule 34, CombatModule 30, WHGame 22, PlayerModule 20, GUIModule 18, Framework 16, Shared 13, QuestModule 12; the rest are small. **(code-verified)**
Threads of the code that uses each row: 531 `main`, 32 `engine-any` (inside hook callbacks), 14 `worker`, 2 `pipe`.

### 2.2 Modding Tools-only assumptions (what a reader of the code must know)
* **Named exports.** 92 rows resolve mangled C++ symbols from split modules by name (37 from CrySystem, 36 of the rows in total being RTTR entry points; 12 ConceptModule; 11 Framework; 11 QuestModule; 9 EntityModule). **(code-verified)** Retail exports none of them. **(data-verified)**
* **Split-module lookups.** The code calls `GetModuleHandle` on 18 module names (17 split modules plus `WHGame.dll`, which does exist on retail). Retail has only `WHGame.dll` (plus the exe and third-party DLLs). **(code-verified; retail module list inferred from the file set and import table)**
* **The plugin's first gate.** `dllmain.cpp` waits up to 60 s for `CrySystem.dll`, then `probe_rttr` (whose own failure text says retail "is monolithic and exports nothing"), then `rttr::validate`, then an import-table swap against `Framework.dll`. On retail every step before the last fails. **(code-verified)**
* **`__FUNCTION__`-style strings.** The Modding Tools modules carry 5,630 of them (`wh::ns::Class::Function`); retail keeps almost none. Of our 18 anchors of that kind, 16 are absent from retail and 2 would need widening. The `wh::ns::Name` strings that survive are RTTR registration and type names (`wh::rpgmodule::StopFight`, `wh::rpgmodule::Soul.Guid`, ...). **(data-verified)**
* **The anchor helper itself.** `anchors.cpp` requires a NUL byte before a string and exactly one referencing function. Retail's `CSystem::Render` string is preceded by padding, not NUL, and is referenced by two functions, so the helper misses it. Both rules need loosening. **(code-verified + data-verified)**
* **Fixed layout constants.** About 40 fixed `module+0xRVA` rows and 237 struct/vtable rows were derived on the 1.5.5 modules. `C_Actor`'s combat-actor field is the one checked both ways: +0x300 here, +0x278 retail (WO-67; different source snapshot and compile configuration). **(code-verified)**
* **Console and log surface** (Lua side): section 4.

### 2.3 Hooks
22 hooks are installed by the DLL (12 vtable swaps, 5 inline gates, 3 detours, 1 import-table swap, 1 proxy callback). The full table with what hooks what, how, from which thread it is installed, which thread runs it, and its static retail check is in Appendix C. **(code-verified)**
The 19 repeating per-frame ticks (plus one added when the pipe starts) are not hooks: they are our functions riding the frame hook.

Ranked by what they put at risk:
* **The frame hook** (`C_ModulesManager::Update` via `WHGame.dll`'s import of `Framework.dll`) is the keystone: every `main` row depends on it and it is the marshalling point from the pipe thread to the engine thread. Retail has no `framework.dll` import and no RTTI for `C_ModulesManager`; the only surviving anchor found is the string `wh::framework::C_ModulesManager::ProcessMessage` (one referencing function), so the function is reachable from its neighbour. **(data-verified; the route is B)**
* **Anchored by `__FUNCTION__` strings that are gone in retail**: dialogue start gate, `CCryAction::PauseGame`, `C_SkipTime::ShowDialog`, `RequestStateChange`, `C_GameOver::Start` (and the `C_GameOver@playermodule` RTTI itself), `C_Calendar::SetWorldTime`. About 8 of the 22 hooks need a new structural anchor. **(data-verified)**
* **Anchored by RTTI that survives**: `C_CombatSoul` hit slots, the four `C_CombatActorAction*` classes, `C_ActorStateExpansion`, `C_Actor`, `C_StateVariable`, `C_Function`, `C_UIHudEvents`. **(data-verified)**

### 2.4 Threads and the combat question
**Threads the DLL creates or relies on** (`threads.md` of the reader, summarised; **code-verified**):

| thread | created where | does | engine objects touched |
|---|---|---|---|
| engine main ("frame") thread | the game | runs the frame hook, then our queue and all repeating ticks | everything the mod does to the world |
| plugin worker | `DllMain` → `CreateThread` | waits for modules, runs the RTTR probe and validation, installs the frame hook, then **installs seven more hooks itself**, arms ticks, starts the pipe, exits | RTTR registry calls; inline patches and vtable swaps (see below) |
| pipe listener | `pipe::start` | reads frames, dispatches; every engine-touching command goes through `run_sync_bounded` | none by design; one exception (lazy `CSystem::Render` patch for trace tooling) |
| run_sync helpers | one short-lived thread per synchronous command | wait only | none |
| injector remote thread | launcher's injector | runs `LoadLibrary`, gone when `DllMain` returns | none |

There are **no timers, no `std::async`, no thread pool**; all periodic work rides the frame hook. Locks are listed in the reader's notes (about a dozen mutexes; the log mutex is taken on any thread that logs).

**Engine objects touched off the main thread — the porting risk list** (all **code-verified**):
1. RTTR registry calls on the plugin worker (`rttr::validate`, the NPC-state `bind_rttr`). Works on 1.5.5; on retail the registry has to be found first.
2. Code patches written from the worker (five inline gates and one raw dice patch): each suspends every other thread in the process, retries while a suspended thread is inside the patch range. The dice patch does **not** suspend threads and does not check the original bytes. Quest-graph vtable swaps are aligned 8-byte exchanges from the worker with no suspension.
3. One inline patch from the pipe thread (`CSystem::Render`, debug tooling only).
4. **Twenty of the 22 hook callbacks run on whatever thread the engine calls the hooked function from.** From those callbacks the DLL writes actor mannequin tag inputs, reads quest nodes through exported getters, captures combat actions, reads NPC-state change requests, and reads hit structs.
5. Main-thread blocking: the main thread performs blocking overlapped pipe writes; every `logf` flushes two files under one mutex on any thread.

**Thread ids in our logs — the gap.** Our native log prints `[time] text` only; there is no thread id in `log.h`, and neither mirror log has one. **(data-verified)** So no hook's thread has been *observed* in our own logs. The only measured thread census is WO-116's research probe (8 minutes, 35,774 frames): `C_ModulesManager::Update`, `CAIProxy::Update`, `CAnimatedCharacter::Update`, `CSystem::Update/Render`, `CEntitySystem::Update` on the main thread only; `C_ActorMovementController::Update`, `FinalizeMovementRequest`, the animation finish step and (per WO-129) `UpdateMannequinTags` on main plus **8 job workers**; animation command-buffer execution on workers only; NPC brain order queueing on workers only; `CLivingEntity::Step` on its own physics thread. **(observed, WO-116; WO-129 for `UpdateMannequinTags`)** The cheapest future evidence is to add the thread id to `logf` and to the first lines of each hook callback.

**The combat hooks named in the work order.** `C_CombatActor::SetCombatZone`, `RequestGuard`, `C_CombatPlayerController::BeginAttack/PerformAttack`, `C_ShootingSystem::FireWeapon` are **not hooked, called or mentioned anywhere in `native/KCDMP`** (grepped). **(code-verified)** WO-119 records them as "retail lead names" that have no string anchors in our build; their equivalents were found by the test harness, RTTI and live capture. `C_ShootingSystem` does not exist in either build (shooting is `C_ActorActionShooting*`, `C_ActorShootingExpansion`, `C_BattleShootingAction`). **(data-verified)** What we actually hook in combat:

| our hook | mechanism | anchor | runs on (evidence) |
|---|---|---|---|
| `C_Actor::UpdateMannequinTags` (writes gait/mannequin tag inputs) | detour, `C_Actor` vtable slot 0xC98, 18-byte prologue | RTTI `C_Actor` (present in retail, 3 vftables) + prologue (0 matches in retail) | main **and 8 job workers** (WO-116/129, observed) |
| melee and missile hit (`C_CombatSoul` slots 0x150/0x158) | vtable swap | RTTI `C_CombatSoul@rpgmodule` (present, 1 vftable) + a history-writer prologue | unknown (inferred: any thread that resolves hits) |
| `EnterImpl` of Attack/Dodge/PerfectBlock/Block actions (slot 0x1C8) | vtable swap with a register-preserving thunk | RTTI of the four `C_CombatActorAction*` classes (all present) | unknown (inferred: the combat actor state machine) |
| `RequestJump` (`C_ActorStateExpansion` slot 0x100) | vtable swap | RTTI (present) | unknown |
| `C_GameOver::Start` (death/respawn) | vtable swap, slot 1 | RTTI `C_GameOver@playermodule` (**absent in retail**) + two strings (absent) | unknown |
| WO-131/132 hit gates | **no hook of their own** (no install or patch code in `wo131.cpp`/`wo132.cpp`, **code-verified**): they apply buffs and read combat state through the engine services and consume the hit slots above | as above | main (called through `run_sync`/ticks) |

We *call* (never hook) `C_Actor::GetOrCreateCombatActor` (slot 0x970), `C_Player::PlayAnim` and the `CombatModule` `QueueAction` (`C_CombatAnimActionManager`, module+0xF3C00; not the AnimationModule one of the same name). **(code-verified)**

**What the retail binary shows about combat and animation threading** (static; **data-verified** unless marked):
* **The threading design is the same in both builds.** Retail keeps the full CryEngine job system: RTTI for `CJobManager`, `CWorkerThread`, `CThreadManager`, worker names `JobWorker_%02u (Regular/Helper/Blocking)`, 71 distinct job functions (Modding Tools: 70). The shipped `engine_core.thread_config` in `Engine.pak` is byte-identical in both builds and lists no combat, AI-logic, animation or mannequin thread.
* **There is no "combat thread".** The only combat-named job is the combat spatial-grid build. `C_CombatModule` is not a parallel module: only `RPGModule` and `EnvironmentModule` derive `C_ParallelModuleUpdater` (which starts a `ModuleUpdateJob` on one frame event and syncs on another). Which thread calls `C_CombatActor` logic could not be settled statically. **(inferred / inconclusive)**
* **Actor, entity and animation update is parallel by default** and split: `es_ParallelEntityUpdate`, `es_ParallelComponentUpdate`, `wh_actor_ParallelPrePhysicsUpdate` default to 1 (decoded on the Modding Tools build); entity components update in named groups via `EntityUpdateJob`; `C_AnimationControllerManager::DoParallelUpdate` is a job with separate start and wait functions; the animation computation jobs have explicit wait points (`SyncAllAnimations`). The mannequin action controller has no job of its own and runs on the caller's thread.
* **What that means for "combat threads are the hard part":** the hazard is *when* a call lands relative to the start and sync of those jobs (a write between a job's start and its wait races with a worker), not a separate combat worker. Retail keeps the same windows, so our current frame-hook timing assumptions carry over to the extent they held on 1.5.5. **(inferred)**
* **The game modules never assert main thread** (0 sites in Combat, Animation, Entity, RPG, XGenAI), so a mis-threaded call will not trip an assert; it will race silently. `gEnv` is a fixed static struct in retail (main-thread id, job manager and console at offsets consistent with base RVA 0x492d800; derived from three offsets, not seen as a symbol). **(data-verified / inferred)**
* **PGO has removed at least one vtable:** `C_AnimationControllerManager` has RTTI only inside its job template in retail, no standalone vftable. Ordinary classes keep theirs. **(data-verified; cause inferred)**
* Two perception job types exist only in retail (`C_JobBatch<...>`); both `system.cfg` files switch the two AI perception/hearing parallel paths **off** ("crash too often"). **(data-verified)**

---

## 3. Phase 3 — the retail static probe

### 3.1 Retail PE layout, and what is visible about protection (**data-verified**)
| | `KingdomCome.exe` | `WHGame.dll` |
|---|---|---|
| sections | `.text .rdata .data .pdata .rsrc .reloc` | `.text .rdata .data .pdata _RDATA .rsrc .reloc` |
| image base, size | 0x140000000, 1.5 MB | 0x180000000, 96 MB in memory |
| `.text` | 34 KB, entropy 6.22 | 60.5 MB, entropy 6.49 |
| exception directory | present (2.7 KB) | present, 3.5 MB = **291,812 function entries** (chained unwind info splits hot/cold fragments) |
| exports | `NvOptimusEnablement`, `AmdPowerXpressRequestHighPerformance` | 42: `CreateGameStartup`, `C_Game::CreateInstance`, 40 vendor SDK exports |
| imports | 16 DLLs, none of the game's own | 55 DLLs; **no `framework.dll`, no `crysystem.dll`**; `steam_api64` (12 functions incl. `SteamAPI_RestartAppIfNecessary`), a publisher account SDK, BugSplat, fmod, d3d12, upscaler SDK loaders |
| TLS callbacks | none | two, both **compiler-generated `thread_local` init/destroy code** (disassembled; no code reads, no hashing) |
| debug directory | PDB reference present (a vendor build-agent path; not reproduced) | same |
| RTTI | — | present, about 58,400 type descriptors |

* **No packing, no protector layout, no integrity strings.** Zero hits (both ASCII, case-insensitive) for Arxan, VMProtect, Themida, WinLicense, EasyAntiCheat, BattlEye in `WHGame.dll` or the exe, and in every Modding Tools module. Sections are ordinary, `.pdata` is readable, RTTI is readable. `INTEGRITY_CHECK_FAILED` exists in the Modding Tools modules as an enum-name list and is stripped in retail; `sys_PakValidateFileHash` ("validate file hashes in pak files for collisions") exists in both; no code-hash or signature string.
* **Things retail has that Modding Tools does not** (only reported, not followed into logic): a Steam launch gate (`steam_appId` cvar default 1771300 + `SteamAPI_RestartAppIfNecessary`; on true it logs "not started through Steam" and quits); a publisher account SDK (`pros.sdk.x64.dll`, 13 imports); the stock CryEngine network anti-cheat classes (`CClientDefence`, `CServerDefence`, `CProbeThread`), constructed from code paths not followed, so whether anything runs in single player is **(inconclusive)** (a co-op mod that never opens a CryEngine network session should not reach it, **inferred**).
* The string `denuvo` appears in both `KingdomCome.exe` files. It is a substring test on the game-DLL *name* that guards a `steam_appid.txt` write; it is not a protector. **(code-verified by disassembly)**
* **Nothing was probed, patched or bypassed.** This is a list of what is visible.

### 3.2 What survives in retail, and what does not (summary of the probe)
| what we anchor on | in retail |
|---|---|
| RTTI class names | 46 of the 54 literals in our code exist. Absent: `C_GameOver@playermodule`, `C_EnableAutomation`, `C_NPCContext@NPCState`, `C_NPCLookTarget`, `C_SetBlockMode`, and three `combattests` classes (test module). `C_AnimationControllerManager` has no standalone vftable. |
| RTTI overall | 52,393 of the Modding Tools modules' 90,766 names are also in retail (58%; the rest includes editor and test modules); retail has 6,014 names the tools build lacks (1.5.6 changes, PGO template instantiation). |
| mangled C++ exports | none (2 C++ exports only) |
| `wh::ns::Class::Function` strings (`__FUNCTION__`) | almost all gone (16 of our 18 absent) |
| RTTR registration and type names (`StopFight`, `Soul`, `SoulList`, `SoulState`, `AdvanceWorldTime`, `PassLongTime`, ...) | present (all 21 class names one reader used; all reflected type names the RTTR layer passes except `C_Dice`) |
| cvar names and help text | present (except the stripped debug ones in section 4) |
| console-command literals | the five debug commands gone (section 4) |
| engine log-line fragments | 22 of the 34 the agent parses are gone (section 4) |
| byte patterns (prologues) | 19 of 37 match nothing; PGO makes the rest ambiguous (hundreds of matches for short patterns); only patterns combined with a string or RTTI cross-reference identify a function |
| fixed module+RVA, struct offsets, vtable slot numbers | not portable; per-row verdicts in the table |
| job-system and thread-name strings | present (same design) |
| `gEnv` | a fixed static struct (base RVA 0x492d800 derived from three consistent offsets) |

### 3.3 Method and the A/B/C rules
Per row the probe takes the anchor alternatives the readers recorded (`str:`, `rtti:`, `exp:`, `pat:`, `via-str:`, `via-rtti:`, `offset-of:`), looks for them in the retail image with a small read-only PE/RTTI/`.pdata` toolkit (strings exact and relaxed-boundary, RTTI type descriptor → complete-object-locator → vftable, RIP-relative cross-references to find the referencing function, byte patterns, function boundaries from `.pdata`), and for struct-field rows disassembles the retail function found by the same string (capstone) to test whether it reads the same displacement. Then:

* **A (found as-is, 89 rows):** a string found and referenced by exactly one function; an RTTI class whose vftable is located; a vtable slot whose retail function passes the same structural byte checks our code applies (2 rows); a pattern matching once (3 rows); a struct-field offset read by the anchored retail function (0 rows passed this strict test).
* **B (a new anchor is clear, 254 rows):** the class, string, registration name or function is reachable, but the literal differs or is ambiguous (for example `CEntitySystem::SpawnEntity %s %s 0x%x` extends the literal we look for), the export has an RTTR-registration or string route (52 export rows), or the slot/offset/layout exists only as a hypothesis on an RTTI-present owner class (162 slot/field rows).
* **C (needs reverse engineering, 200 rows):** the recorded anchor reaches nothing in retail (absent string, absent class RTTI, absent export with no route, raw RVA, pattern with 0 matches), or the owner has no RTTI and no other anchor.
* **D (36 rows):** names that live in game data (20 shown to sit in the Scripts/Tables paks, which are identical in both builds) or our own working-directory files.

**Validity of the method.** (1) A Modding Tools control: each row's anchor was also searched in the Modding Tools modules; 264 of the 295 rows that carry a string or RTTI anchor resolve there. Of the 31 that do not, 21 are game-data names and 10 are rows whose recorded anchor also fails on the tools build (for example a truncated class name or a code-comment-level anchor); those 10 carry an "inconclusive" flag in the table. (2) The probe can only say *present/absent/unique*, never *same meaning*; A is a minimum, not a guarantee. (3) "Absent" is ASCII and UTF-16 exact-literal absence; tail-shared strings would be reported as a substring hit and flagged inconclusive (1 such case). (4) The owner-class rule for struct/slot rows is a heuristic (**inferred**): an RTTI-present owner makes the layout re-derivable from its methods, which is a B, not a verification.

### 3.4 How engine functions can be found on retail (the usable anchors)
1. **RTTI → vftable → slot.** 58,400 type descriptors; the complete-object-locator and vftable are readable (our `find_vftable` logic ports). Slot numbers must be re-verified (declaration order in public headers is scrambled; WO-40/42), by structural checks like the two that already pass.
2. **RTTR registration names** (`StopFight`, `AdvanceWorldTime`, `HealBleeding`, `SetWorldTime`, `Trigger`, `GetParent`): the string is referenced by the registration function, which leads to the method wrapper; this is the route for most of the 52 B export rows (**inferred**; not yet walked).
3. **Cvar names and help strings**, **job-name strings**, **`ProcessMessage`-style labels**: present and mostly uniquely referenced.
4. **`gEnv`-relative structure**: the static `gEnv` struct and main-thread compare give engine globals without exports.
5. **`.pdata`**: function boundaries and chained fragments are exact, so "the function containing this string reference" is well defined even with PGO splitting.

**What resists hooking** (static observations, **data-verified** unless marked): PGO duplicates short prologues (hundreds of matches), so raw prologue patterns need a second anchor; hot/cold fragmentation means a function may start in one place and continue elsewhere (our inline hooks overwrite the first 12–24 bytes and validate exact bytes, which is correct only if the same entry is found); a string referenced by more than one function (our "exactly one function" rule fails: `CSystem::Render` has two); removed vtables and RTTI (`C_NPCContext`, `C_GameOver@playermodule`); a control-flow-guard indirect-call dispatcher in the TLS init path (**inferred**; only affects hooks on indirect calls); a Steam launch gate that must be satisfied before anything else runs.

### 3.5 Phase 3 results worth singling out
* **Frame hook and start-up gate:** retail has no `Framework.dll` import and no `C_ModulesManager` RTTI; `ProcessMessage` has one referencing function. The gate `CrySystem.dll` cannot pass. (class B route)
* **RTTR ABI (36 rows; 30 B, 6 C):** the entry points are not exported; reflected type and member names survive; the 24-byte `variant`/`argument` layouts need re-proving.
* **Engine services:** `CEntitySystem`/`CEntity`/`CXConsole` RTTI present; the `CEntitySystem` vftable slots 0x60 and 0x98 still reference the SpawnEntity and RemoveEntity strings (longer literals). `gEnv` lift through `ValidateAlcoTeleportPoints`/`HangoverSpotsHub` does **not** port: both strings are absent (ASCII and UTF-16).
* **Script contexts (crime isolation, gait and relation contexts):** retail has the `C_ScriptContextManager` vftable (under `@game@wh@@`, not `@wh@@`); our five hard-coded RVAs and four prologues all fail, so the feature would disarm (it is fail-closed).
* **Concept tree / quest layer:** the structural detector for the State setter still selects exactly one candidate function in retail; the time-gate method-name proof (`4C 8B 43 68`) did not match, so that offset is inconclusive. Port-class table (WO-99.5) must be re-proven.
* **NPC-state context (WO-141/143):** class RTTI `C_NPCContext` is absent, `C_NPCLookTarget` absent, all `C_NPCContext::…` strings absent; the element classes (Stance, HandContent, Unstance, their `Required` variants) and the owner classes (`C_NPC`, `C_SkipTime`, `C_Calendar`, `C_UIHudStates`, `CCryAction`) have RTTI. About 25 fixed RVAs are tied to the 1.5.5 images and five WO-143 functions have only an RVA and a prologue.
* **Death and respawn:** `C_GameOver@playermodule` has no RTTI; the Game Over guard has nothing to attach to by its current anchor.
* **Combat:** `C_Actor`, `C_CombatSoul`, `C_ActorStateExpansion`, the four action classes, `C_CombatPlayerController` (3 vftables), `C_CombatActor` (2) have RTTI; the five combat test-command classes and the property names the combat model reader uses (`RequestedAtkZone`, `RequestedInputClass`, ...) are absent; the byte patterns for `UpdateMannequinTags`, `QueueAction`, `RequestAction`, `SetFlag` and the history writer match nothing.

### 3.6 The probe set (15 rows for a later hands-on session)
A mix of A, B and C; combat and the NPC-state context included. Chosen because each either unlocks a large cluster or decides how hard a whole class of rows is.

| id | class | kind | anchor | what the probe session must prove |
|---|---|---|---|---|
| D-007 | B | export | ?Update@C_ModulesManager@framework@wh@@QEAAXM@Z | Frame hook: find `C_ModulesManager::Update` (no import to swap); prove a detour fires once per frame on the main thread. Everything marked `main` depends on it. |
| D-010 | B | export | ?get_by_name@type@rttr@@ | RTTR registry entry points (`get_by_name` and 29 siblings): locate from registration strings and prove a `Soul` lookup. About 36 rows and most reads/writes depend on it. |
| D-183 | B | global | CryScriptSystem.dll+0x8E560 (private SSystemGlobalEnvironment* gEnv slot) | `gEnv` location (engine globals): confirm the derived base and the main-thread-id, job-manager and console offsets; several rows lift it from a stripped string today. |
| D-208 | B | string-anchor | CEntitySystem::SpawnEntity | Entity-system vtable slots 0x60/0x98 validated through the SpawnEntity/RemoveEntity strings: confirm the widened literal and the slot numbers. |
| D-343 | A | vtable-slot | IEntity vtbl+0x278 GetPhysics (GetProxy(1)-&gt;vtbl[0xB0](), rope proxy 12 fallback) | IEntity vtable slot (GetPhysics): already passes the same byte checks statically; confirm live that the same slot does the same thing (validates the IEntity vtable approach). |
| D-365 | A | rtti | .?AVC_CombatActorActionAttack@combatmodule@wh@@ \| .?AVC_CombatActorActionDodge@combatmodu… | `C_CombatActorActionAttack` RTTI (present) and its EnterImpl slot 0x1C8: confirm the slot and that the capture thunk works; combat. |
| D-415 | B | vtable-slot | C_Actor vtbl+0xC98 (C_Actor::UpdateMannequinTags) | `C_Actor::UpdateMannequinTags` (vtable slot 0xC98, 18-byte prologue): find it in retail, check prologue/slot, and log which threads call it; combat and threading. |
| D-095 | B | struct-field | C_Actor+0x300 (m_pCombatActor, I_CombatActor*) | `C_Actor+0x300` combat-actor field (retail is known to differ: +0x278): measure how many other `C_Actor` offsets shifted; decides how hard every struct-field row is. |
| D-369 | B | vtable-slot | C_Actor vtbl+0x970 (GetOrCreateCombatActor, what every combat test command calls) | `C_Actor` vtable slot 0x970 (GetOrCreateCombatActor): confirm or re-find; combat. |
| D-444 | B | vtable-slot | C_StateVariable vtbl[33] (+0x108) ExecuteNode (called by C_Node::Execute) -- HOOKED to re… | `C_StateVariable` slots 33/42 and the setter the detector selects (one candidate found statically): confirm; quest layer. |
| D-553 | C | string-anchor | "wh::xgenaimodule::NPCState::C_NPCContext::RequestStateChange" | NPC-state context (`RequestStateChange`): string and context RTTI are absent in retail; find the function through the element classes that do exist; NPC-state. |
| D-516 | C | struct-field | XGenAI object +0x9C0 = embedded C_NPCContext | The embedded NPC-state context inside the XGenAI object (+0x9C0): confirm the offset and the object; NPC-state (activity/stance puppets depend on it). |
| D-232 | C | rtti | .?AVC_GameOver@playermodule@wh@@ | Game Over hook: `C_GameOver@playermodule` has no RTTI in retail; find what replaced it; death/respawn depends on it. |
| D-123 | B | fixed-offset | WHGame+0x32AAD0 (C_ScriptContextManager::vftable) | Script-context manager: retail has the vftable (different namespace) but every hard-coded RVA and prologue fails; prove slots 2/4/7/8. |
| D-351 | B | string-anchor | CSystem::Render (the profiler label: the one function that references it, via anchor::fun… | `CSystem::Render` label found only by a relaxed string-boundary rule: proves the anchor helper needs the loosened rule. |

---

## 4. Phase 4 — what our Lua, agent and data rely on

Every console command, cvar, scriptbind and log line the shipped Lua (`kdcmp.lua`, 20,375 lines), the C# agent/launcher and the native DLL rely on: **304 names** (276 on the shipped path, 28 dev-tool or comment-only). Per name: class, who uses it, present in Modding Tools, present in retail, and what breaks. The reader's machine-readable table has all rows; the decisive ones are below. **(code-verified use; data-verified presence: each name searched as an exact ASCII and UTF-16 literal in every Modding Tools binary and in retail `WHGame.dll`; "suffix" and "infix" hits counted as inconclusive/no with the containing string recorded)**

| class | names | in Modding Tools | in retail | retail: no | retail: inconclusive |
|---|--:|--:|--:|--:|--:|
| entity methods (`actor:`, `soul:` ...) | 99 | 86 | 86 | 12 | 1 |
| scriptbind table functions (`Game.*`, `XGenAIModule.*`, `System.*` ...) | 72 | 69 | 69 | 3 | 0 |
| engine log lines the agent parses | 34 | 30 | 8 | 22 | 4 |
| cvars | 26 | 26 | 24 | 2 | 0 |
| Lua class-table functions (in `Scripts.pak`) | 21 | 21 | 21 | 0 | 0 |
| native `__FUNCTION__` anchors | 10 | 10 | 1 | 9 | 0 |
| console commands | 9 | 9 | 4 | 5 | 0 |
| others (input action names, globals, brain messages, REST routes, entity sub-tables) | 33 | — | — | — | — |

Totals: present in Modding Tools 271, present in retail 229; shipped-path names missing in retail 53, inconclusive 13.

### 4.1 Missing in retail — what breaks
| name | used by | retail | what breaks without it |
|---|---|---|---|
| `wh_ai_PauseNPC` (console command) | shared-world NPC authority pause (WO-107/108/109), 13 call sites | **absent** (bare fragment `PauseNPC` also absent; `C_NPCStateDebug` class string absent) | hard: paused copies keep running their own brain and fight the host copy |
| `wh_ai_ResumeNPC` | release of paused NPCs | **absent** | hard: a paused NPC can never be released |
| `wh_ai_NPCStateResetElement` | activity mirroring (WO-141/143) | **absent** | degrades: a stance element cannot be cleared; NPCs stay in the wrong pose |
| `wh_sys_LoadGame` | join auto-load of the transferred save (WO-124) | **absent** (only the unrelated `wh_sys_LoadGameFilter` contains it) | hard: no command to call for the join flow |
| `wh_concept_HasteTrigger` | shared-quest catch-up | **absent** (class `C_HasteTrigger` exists) | hard for catch-up; the quest layer is already off in shared world (WO-133) |
| `wh_rpg_DisablePlayerFallDamage`, `wh_rpg_AutoDisablePlayerFallDamage` | wake/join teleports | **absent** | native `cvar_set_int` returns false, so teleports run with fall damage live |
| `sys_simple_http_base_port` (and the listener path string) | the agent's REST input channel on `:1403` | **absent** (`http_startserver` exists) | the agent's input channel; whether retail serves the routes on any port is inconclusive |
| 22 of 34 engine log fragments (`FastTravel:`, `Gameplay started`, `CutscenePlayer::`, the dialogue lines, `Switching to player`, ...) | the agent's log tail (load detection, leash, cutscene/dialogue holds, sleep and inventory gating) | compiled out; only 8 survive (`LoadGame`, `Loading saved game`, `CSystem::Quit`, `Loading level`, `Binary saveload`, `User folder is`, `PlayVideoOnly`, `TeleportPlayer`) | those features would go blind |
| `HasBuffDebug` (Lua name and native anchor) | one native anchor | absent | that row only |
| 12 entity methods, 3 scriptbinds | all also absent in Modding Tools; every call site checked is `pcall`-guarded | absent | none (probes) |

Not missing (good news): every sync-critical entity method (`GetWorldPos`, `IsDead`, `AddBuff`, `CreateItem`, `StartAnimation`, `PlayAnim`, ...), `Script.SetTimer`, `System.AddCCommand`, `System.LogAlways`, `System.ExecuteCommand`, `UIAction.CallFunction`, `Calendar.*`, `Game.*SaveLock*`, `wh_ui_ApsePauseRatio`, `wh_pl_FastTravelEnabled`, `wh_sys_LastLoadedSave`. The Lua class tables we wrap live in `Scripts.pak` (identical). **A stripped command called through `ExecuteCommand` most likely prints "unknown command" and does nothing (inferred, unobserved): the first probe-session step should call one to confirm the silent-failure assumption.**
`wh_ai_PauseNPC` is not simply dead weight: it calls `C_IntelligentObject::Suspend` (WO-107), and that class exists in retail RTTI, so the lever could be rebuilt natively. **(data-verified class; inferred route)**

### 4.2 The agent's channels on retail
* **Input** (Lua payloads, SoulList reads): the debug REST API on `localhost:1403`, Modding-Tools-only (WO-18 disproved it on retail; `sys_simple_http_base_port` absent). Retail with `-devmode` opens the RemoteConsole on `:4600` and accepts `#`-prefixed Lua in about 30 ms (WO-18; project notes). **(observed in earlier WOs; not re-verified here)** The `rpg/SoulList` RTTR reads would need a Lua or native replacement. **(inferred)**
* **Output** (`kcd.log` tail): `System.LogAlways` exists; our own lines survive; the engine's event lines mostly do not (table above).

### 4.3 Mods folder, manifest, pak
* **Same mod manager.** 31 mod-manager strings compared (`mod.manifest`, `mod.cfg`, `mod_order.txt`, `mod_report.xml`, `Mods dir is '%s'`, game-version checks, localization patches, `wh_mod_GenerateReport`, `sys_PakLoadModePaks`, ...): all exist in both builds. A vanilla retail launch log on disk shows `Mods dir is 'mods'`, `0 mods loaded from mods/`. `<RETAIL>` has no `Mods` folder today. **(data-verified)**
* **Our manifest has no `<supports>` element**, so the loader's "not limited to any game version, it will be enabled" path applies on both builds; `wh_sys_version` needs no change. **(data-verified strings and manifest; behaviour inferred)**
* **Our pak** (`mod.manifest` + `Data/kdcmp.pak`, a stored zip with five entries): the shape should be accepted unchanged. **Unproven on retail** until a run: auto-exec of `Scripts/Startup/kdcmp.lua` from a mod pak (the `Scripts\Startup\` string exists); the two table-patch files (`*__kdcmp.xml`; retail lacks the "Patched database" *log* line but has the patch code's other strings); the two `Libs/Config` full-file overrides (`keybindSuperactions.xml`, `defaultProfile.xml`; same kind of shadowing as today, precedence governed by `sys_PakPriority`, which exists in both).
* **Modding Tools-specific in our tooling:** `Build-And-Install-Mod.ps1` probes `:1403` to decide the game is closed and locates `KCD2Mod` first; the installer and launcher encode "`Framework.dll` and `CrySystem.dll` next to the exe" as *the* Modding Tools test, `SteamAppId` default 2429020 (1771300 is selectable), `steam_appid.txt` at the install root as the way to make a directly started process belong to app 2429020.
* **Launching retail:** retail's exe started directly exits with "not started through Steam" (WO-53, observed there); a retail launch must go through Steam (`-applaunch`), and the launcher would find the process by name and hand its pid to the unchanged injector. The injector needs only a pid and a DLL path and makes no assumption about the exe, modules or working directory. **(code-verified; retail launch behaviour from WO-53)**
* Our diagnostic files (`kcdmp-*.txt`, traces) are plain file writes into the process working directory; on retail they would appear in `<RETAIL>` root if Steam's working directory is the root. **(inferred)**

---

## 5. Phase 5 — saves and DLC

### 5.1 Why retail saves are greyed out in the Modding Tools load menu — **both gates exist; which one fires depends on the save**
Method: six saves were copied to the scratchpad (each copy hash-matched to its original), headers decoded from the copies, the menu's gate logic read from `GUIModule.dll` and `Framework.dll` (disassembly of four functions), the retail binary searched for the same keys. **(data-verified)**

* **Gate 1, DLC (`ui_load_disabled_by_dlc`, "This savegame requires DLC:").** Takes the save's DLC list and, for each DLC that is not active **and** has `affects_savegame="true"` in `dlc.xml`, blocks the load. The retail-written saves on this machine (143 in one playline, written by retail 1.1.1 in 2025) are stamped with *The Lion's Crest* (id 1, affects saves); the Modding Tools build logs that DLC as `active: N` (Steam entitlement runs under app 2429020, which does not own it) while the retail log on the same machine says `active: Y`. **This is what greys out the retail saves that exist here.** The four DLC that affect saves are Lion's Crest (1), Legacy of the Forge (5), Mysteria Ecclesiae (7), Brushes with Death (9); Season Pass (10) and Gold Edition (11) are written into saves but never block; 2/3/4 are free.
* **Gate 2, version (`ui_load_disabled_by_version`, "requires a later version of the game").** Blocks when the save's `GameReleaseVersion` is greater than the cvar `wh_sys_GameReleaseVersion`: **10505 in Modding Tools, 10506 in retail**. It is tested *before* the DLC gate. A save written by the retail 1.5.6 build therefore fails the version gate first. **Mechanism and both constants data-verified; "a retail 1.5.6 save" is inferred because none exists on this machine.** One header read of a real 1.5.6 save would settle it.
* **What it is not:** `wh_sys_version` from `system.cfg` (mod enabling only), the `BuildInfo` string, the mod list (tooltip only), the MD5 footer (WO-115 already loaded a file with a flipped footer byte from the main menu).
* **Where enforced:** only in the menu. The three description functions are imported by `GUIModule.dll` and by no other module. A direct load (console, script, the mod's own transient-world load) is not gated by them. **(data-verified imports; the engine's own load command not exercised, inferred)**

Header comparison (retail 1.1.1 save vs Modding Tools 1.5.5 save, **data-verified**): description XML `BuildInfo` `1.1.1-11377-release_1_1` vs `1.5.5-release_1_5`; `GameReleaseVersion` 10101 vs 10505; `NewGameReleaseVersion` absent vs 10505; `<DLCs>` three entries (Lion's Crest, Season Pass, Gold Edition) vs none; `<UsedMods>` none vs ours; a debug-info history block only in the Modding Tools save. The release version is stored three times (XML attribute, the binary description's first word, the `_SaveGameVersion` game variable). Stream version word (23), CryAction save version (0x23) and game rules (`SinglePlayer`) are the same. Further levers that exist in the tools build (`wh_sys_FakeLoadedGameVersion`, `wh_dlc_FakeLicense`) are absent or read-only and were **not** tried; they are reported, not recommended.
**Retail reading Modding Tools saves:** 10505 ≤ 10506 and an empty DLC list pass both gates, so those saves should appear enabled in the retail menu; whether they *load correctly* across 1.5.5 → 1.5.6 is unmeasured. **(inferred)**

### 5.2 DLC
* **What is restricted in Modding Tools is entitlement, not content.** `<MT>\Data` and `<RETAIL>\Data` hold the same 42 paks with identical sizes, and every DLC row in `dlc.xml` has `need_mount="false"`: DLC content (`item__dlc.xml`, `buff__dlc.xml`, `dlc2_*` quests, `dlc3_*` Storm roles, UI textures) is inside the base paks, and entitlement alone activates it. Retail's app manifest lists three depots (binaries, main data, speech/video) and **no DLC depot**, yet all nine DLC show `active: Y` there. Two optional DLC-9 cinematic/video paks named in `pak.cfg` are on neither install and are not opened.
* **Entitlement path:** `wh::game::dlc::C_DLCManager_PC` (RTTI in both builds) asks the Steam layer per DLC app id (`ISteamApps` through the context interface; there is no `BIsDlcInstalled` import). Retail hard-codes app 1771300, Modding Tools 2429020.
* **`RequiredDLC` quest roots.** 18 quest files carry a `RequiredDLC` attribute; three are `C_Quest` roots, the three WO-137 excluded: `navstevaLekare` (Mysteria Ecclesiae, side quest S301; **inactive** in Modding Tools), `katuvSleh` and `zavodniPodkovy` (Horse Racing, a **free** DLC, active in Modding Tools too). Two of the three were excluded by name, not by entitlement; **(inferred)** harmless. The other 15 are modules or gameplay nodes (Barber, Forge, Hardcore mode, Brushes with Death, Horse Racing, Mysteria Ecclesiae level switch, preorder bonus).
* **What DLC co-op on retail would need, at a high level (all inferred design; nothing built):**
  1. A way for a save to reach the world without the menu (already how the mod loads transient worlds, WO-115/124), or a build filter that keys on `GameReleaseVersion` 10506 / `1.5.6-15693-release_1_5` instead of `1.5.5-release_1_5`.
  2. **DLC is per player.** Two retail players with different ownership have different DLC sets. The host should advertise its active DLC ids (the `dlc.xml` ids) and the joiner should refuse or degrade when an `affects_savegame` id is missing; a direct load skips the menu gate, but a joiner without the DLC would still have those quest graphs hibernated.
  3. What must match: the game build (release number), the same save content (the spliced joiner save keeps the host's description header byte for byte, WO-115, so the listed DLC set is the host's), the DLC set the save lists.
  4. Steam: a launcher must start retail through Steam (`-applaunch`) so entitlement lookups work.
* **Game data vs binaries:** the version gap between the two builds is binary-side plus config, not data-side. `Tables.pak`, `Scripts.pak`, `IPL_GameData.pak`, `Characters.pak`, `GeomCaches.pak`, `IPL_GeomCaches.pak`, `Engine.pak`, `Shaders.pak` and `English_xml.pak` are identical (full sha256). This is the most useful fact for the port: the mod's table names, quest graph and script functions are already proven against the retail data; only native layout differs. **(data-verified)** The Modding Tools data is probably copied from retail (its manifest says 16.6 GB, the tree is 91.5 GB, no GameData rows in its `whdlversions.json`). **(inferred)**

---

## 6. Phase 6 — the engine questions

Status per question: **answered**, **partly**, **needs him**. Evidence marks as above. All data facts hold for retail because the Scripts, Tables and `IPL_GameData` paks are identical entry by entry (name, CRC, size). Game strings are short fragments.

**1. Ending an interactive scene the `InteractiveSceneManager` is still running — partly.**
`wh::guimodule::C_InteractiveSceneManager` has `EnqueueScene`, `EndScene`, `CleanupScene`, `Interrupt` (from its `__FUNCTION__` strings; none is exported, none is a scriptbind); the warning for exactly our case exists ("Scene '%s' is being enqueued after the previous scene is already finished. Please use OnQueued signal."), so an enqueue after finish is a known authoring error the engine tolerates by queuing. A stuck scene is usually waiting for one of three inputs: a `PlayCutscene` signal, a `FinishCutscene` signal (both In ports on the `CutsceneHandler` node; the latter is refused when AutoFinish is on), or positioning of NPCs. `wh_ui_StopCutscene` stops only what `wh_ui_PlayCutscene` started; `Movie.StopAllCutScenes` is trackview-only. `human:InterruptDialogs()` closes a running dialogue (observed, WO-112); that it also runs `EndScene` for a dialogue scene is inferred. **Still open:** which native call the quest graph uses to end a scene cleanly; whether `Interrupt` raises the handler's `OnFinished` (quest proceeds) or swallows it; whether `EndScene` is reachable from outside the scene player. *Needs him for these three.*

**2. `WAITING_FOR_TWINS`; forced dialogue with a brain suspended — answered (part 2), partly (part 1).**
`WAITING_FOR_TWINS` is a state of `wh::dialogmodule::C_DialogInstance` (24 states in the string table, inferred to be enum order; between `WAITING_FOR_NPC_FREEZE_EDDA` and `GENERATING_DECISIONS`). Meaning (inferred from strings): the dialogue has frozen its participants and waits for `C_DialogueTwinController::MakeTwin` to replace them with `DialogTwin_<name>` stand-ins; it can also wait on clothing ray-casts (`wh_dlg_WaitForCloth`), horse-dismount positioning and player/twin overlap queries. The real log line carries the state (`Dialog interrupted. [... state: <STATE> ...]`). Interrupters we know: `C_DialogManager::InterruptDialog(s)`, priority clashes, and our own `mp_ghost_isolate`, which calls `InterruptDialogs()` on ghost bodies — a suspect for field interruptions, not proven.
**Suspended brain: answered (observed, WO-112 D2, one NPC).** `wh_ai_PauseNPC` then `DialogModule.ForceDialog(npc, player)` reached `WAITING_FOR_INTERACTION` with world time paused, identical to the unsuspended control; that state sits after the freeze and twin steps, so both completed (inferred from order). Organic (player-started) dialogue does **not** run on a suspended brain (request times out; WO-112 D3/D4). Retail caveat: the command is absent; the owning class exists. *Needs him:* what "Edda" is, what makes a twin stall for a hidden or externally driven participant, what ends a wait by timeout.

**3. Quest States without a port — answered: none found; nearest lever named.**
A `State` node has `Exec`, `Value`, `DefaultValue` (hidden) and generated `Set<Value>` / `On<Value>` ports per enum value (not in `definitions.xml`). The native surface (ConceptModule, 404 exports) is `FindNode` → `GetPort` → `I_Port::Read` (slot 16) and `Trigger` (slot 15); no `Write`/`SetValue`/`Assign` exists; RTTR `set_value` would repoint a typed reference and fire no `On<Value>` edge; exported mutators (`ActivateNode`, `Reset`, `Hibernate`, `Wake`) have unknown semantics. **Nearest lever:** pulse an In trigger port (`Set<Value>`, or `Exec` when `Value` is wired) through slot 15: that runs exactly the graph's own consumers of that edge. Never fired live (WO-126A §6.2). Loading a save is the only proven way to set an exact value. *Needs him:* is there an internal setter behind `State::Exec`; is pulsing `Exec` with an unwired `Value` defined.

**4. Random encounters — partly.**
Structure (data): `RandomEventPlace` (trigger area, spawn points), `RandomEvent`/`Variant`, `GenericRandomEvent`, and **`ManualRandomEvent` with `Spawn` and `Despawn` In ports**. Activation paths (PlayerModule strings): the player entering a place's trigger area (cooldowns, difficulty check, `wh_pl_RandomEventsAutoSpawnEnabled`); generic events (an enemy NPC or one with `allowGenericRandomEventParticipation` sends a perception message, then `TrySpawnGenericEvent`); fast travel; a `ManualRandomEvent` `Spawn` port from a quest graph. Test overrides: `wh_pl_RandomEventsSelectEventPlaceOverride`, `wh_pl_RandomEventsSelectVariantOverride` (present in retail). NPCs are not level-parked entities by design: spawns are scheduled from a soul pool and orphans despawned; saved started events are restored. What we *observed* (WO-144): on the joiner the host's encounter bodies exist as copies with no physics, hidden. **Remote activation:** nothing in our code does it; the `Spawn` In port (native pulse, inferred) and the override cvars are the levers. *Needs him:* what "parked" means for an encounter NPC (`HorseParkedCondition` exists), what un-parks it, whether a named variant can be started at a position without a player in the trigger area.

**5. Dedicated souls and appearance — partly.**
(a) Souls are table rows (`Libs/Tables/rpg/soul.xml` plus per-area `soul__<suffix>.xml`, each with a compiled `.tbl`); the engine patches tables from extra part files, and we already ship two (`clothing_preset__kdcmp.xml`, `buff__kcdmp.xml`), so a `soul__kcdmp.xml` is very likely the same route (inferred; never tried). Spawn: `XGenAIModule.SpawnEntity{SharedSoulGuid=...}`, which our roster avatars already use. Risks: souls persist in every save that met them; random events refuse unique souls on load. (b) **Storm rule:** Henry's own rule is `appearance_player_henry` in `appearance_unique.xml` (selector `<isPlayerHenry/>`; operations `setHead`, `setHair`, `setUnderwear`, `setBody`, `setBeard`); a rule for a dedicated soul is the same operations with `<hasName name="..."/>` (1,387 selectors use `hasName`). Storm loads from `Libs\Storm\storm.xml`; the code has a *mod storm root* path, but its file convention was not found. (c) **Haircut/beard:** the barber flow is `Barbershop.lua` → native `Barber` scriptbind (`Create(soul)`, `TryHair`, `TryBeard`, `Commit`, `Revert`), options in `barber_option.xml`; where `Commit` stores the result is **not read**. **Dirt and blood:** per item and body part (`AddDirt`, `AddBlood`, `CleanDirt`, `WashDirtAndBlood`, `SetClothingDirtLevel`, `AnimCharSetBodyBlood`); the save's `Dude` record carries dirt; never read or written by us. *Needs him:* the stored fields behind `Barber::Commit`, body dirt/blood storage, the mod Storm root path, whether a mod soul can be non-persistent.

**6. Outfit stripped at the end of a crouched walk — partly (a candidate cause, not proven).**
Field fact (WO-144, observed): twice in four tries the whole outfit came off in one frame at the end of a crouched walk, no log line, and REST equips then failed for minutes. The avatar is an NPC soul driven by the NPC-state machine; element slots are Stance, LeftHand, RightHand, Unstance, **ChangeEquipment**, Minigame (stance enum 6 = crouch). The engine has machinery that does exactly "whole outfit off, items kept": `NPCState::C_ChangeEquipmentElement/Action/Condition/Effect`, save names `ChangeEquipmentDressDown/DressUp/BorrowedItems(Stash)` ("the undressed item was kept in NPC's inventory instead of in the undressing stash"). **Hypothesis (inferred):** a stance transition on the avatar (here the end of a crouch-stance element) makes the state machine reconcile the equipment element against its required state and run a dress-down into a stash, which also explains "equips fail for minutes" (items are in the stash, not the inventory). Alternatives not excluded: our activity reconcile (WO-141) or one-shots (WO-143) clearing the slot. No non-NPC-state "undress" cause was found in the data. *Needs him:* does ending a Stance element evaluate ChangeEquipment with an empty element; where dressed-down items are stashed and how to restore them.

**7. Journal marker letters — partly.**
The letter is made in the UI: four Scaleform files (`hud.gfx`, `ApseQuestLogList.gfx`, `ApseMap.gfx`, `ApseMapLegendList.gfx`) each hold `OBJECTIVE_CHAR = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"` and a `charAt`-based `GetObjectiveChar`; the index is an integer sent by native code (`AddCompassMarker(... ObjectiveNumber ...)`, `ShowObjectiveEvent(... ObjectiveIndex ...)` in the HUD definition). No letter or order data exists in quest XML or the `Objective` concept node. How native assigns the index is **not read**; our symptom (WO-144: joiner letters two ahead of the host) is consistent with an index counting something stateful (started or activated objectives), not a static order (inferred). *Needs him:* the rule and owner of `ObjectiveNumber`/`ObjectiveIndex`, and whether a State load recounts.

**8. The whistle — answered: yes, and native handles it too.**
Observed in WO-6: pressing X emitted `call` and `use_horse` in our `Player.OnAction` hook log, and the native `CallHorse` ran as well (our hook runs in addition and cannot consume the input). WO-144 §3.5 lists it as unverified; that was a missed cross-reference. Data: `defaultProfile.xml` defines `call` (press and release, keyboard and pad) included by the interaction and carry maps; `Player:OnAction` is in `player.lua` (identical in both builds) and forwards to the game rules, special-casing only `custom_quest_call_horse_action`. Native: `C_RiderPlayerControl::CallHorse` (`ComeToMe` to the owned horse; an AI sound of intensity 760), cvars `wh_horse_CallMaxDistance/MinDistance`. Limits (WO-12): movement actions never reach `Player.OnAction`; `Player.Client.OnAction` never fires on this build. Retail: the files are identical; whether our hook is installed and called there is part of the pak-loading question. A retail key press is the only check left.

**9. Retail: how engine functions can be found, what resists hooking, whether our pak and Lua load as-is — partly (from Phase 3 and 4).**
*Finding functions:* section 3.4 (RTTI → vftable → slot; RTTR registration names; cvar/job/label strings; the static `gEnv`; `.pdata` boundaries). *Resisting hooking:* PGO-duplicated prologues, hot/cold fragments, a string referenced by several functions, removed vtables and RTTI, the Steam launch gate; and the **combat/animation job windows**, which are a correctness issue rather than a hooking one (section 2.4). *Pak and Lua:* the mod manager is the same (31 of 31 strings), our manifest has no version restriction, the pak shape should be accepted; Lua auto-exec from a mod pak, table patches and the config overrides are **unproven until a run**; once loaded, the retail-missing names degrade or disable features (section 4.1); **the 22 missing engine log lines** are the quietest failure.

**What is still worth asking him** (short, precise; what we could not settle from the repo, the game data or static analysis):
1. **Scene end:** which call does the quest graph use to end an interactive scene cleanly; does `Interrupt`/`EndScene` raise the handler's `OnFinished`; can a dialogue scene and a cutscene scene be ended by the same call from outside the scene player?
2. **Dialogue:** what ends or times out a dialogue stuck in `WAITING_FOR_TWINS`; why `MakeTwin` stalls for a hidden, paused or externally moved participant; what "Edda" is.
3. **State setter:** is there a real setter or lazy `Set<Value>` generation behind `State::Exec`/`Value`; is pulsing `Exec` on a State with an unwired `Value` defined?
4. **Encounters:** what "parked" means for encounter NPCs; can the manager start a named variant without a player in the trigger area; do physics-less copies acquire physics when their event is started locally?
5. **Appearance storage:** what `Barber::Commit` writes and where body dirt/blood lives; the path convention for a mod Storm root; can a mod soul be non-persistent?
6. **Undress:** can ending a Stance element make the NPC-state machine run `ChangeEquipmentDressDown` for an avatar soul; where do stashed items go?
7. **Marker letters:** the rule and owner that assigns `ObjectiveNumber`/`ObjectiveIndex`.
8. **Retail-side, all:** which of the strings gone from retail (`wh_ai_PauseNPC`, Haste, "Patched database", the Storm mod root, the `C_NPCContext` function names) still have live code, and how retail's combat/animation update windows are ordered (when `C_CombatModule::Update` and the actor groups run relative to the job start and sync)?

---

## 7. Phase 7 — the estimate

### 7.1 The numbers
* **By class** (543 binary-anchored rows; 36 data rows excluded): **A 89 (16%)**, **B 254 (47%)**, **C 200 (37%)**.
* **By kind:** exports 92 (0 A / 52 B / 40 C), string anchors 84 binary-anchored (41 / 15 / 28), RTTI 52 (43 / 0 / 9), vtable slots 95 (2 / 80 / 13), fixed module+RVA 24 (0 / 7 / 17), prologues 37 (3 / 15 / 19), struct fields 140 (0 / 82 / 58), globals 19 (0 / 3 / 16).
* **By module (A/B/C):** RPGModule 40/33/16, XGenAIModule 14/21/35 (+13 data), CrySystem 2/34/24, EntityModule 6/36/12, CryEntitySystem 6/34/7, ConceptModule 6/22/6, CombatModule 3/10/17, WHGame 1/11/9, PlayerModule 0/10/9, GUIModule 6/9/3, Framework 0/10/6, Shared 0/5/8, QuestModule 1/1/10, CryPhysics 2/3/2, CryAction 0/4/2, `multi` 2/6/28.
* **Clusters** (rough row counts): RTTR ABI and reflection 71 rows; NPC-state context (XGenAI) 36 rows (12 C); combat (Combat/Entity) 36 rows (9 C); concept/quest 46 rows (16 C); about 39 fixed-RVA rows (20 C); the 22 hooks (8 need a new structural anchor).
* **Lua commands retail lacks** (shipped path): 5 console commands, 2 cvars, 22 engine log lines, plus the REST input channel; 12 entity methods and 3 scriptbinds that are missing on both builds and already guarded.
* **Hooks with threading risk:** 20 of 22 callbacks run on whichever thread the engine uses; the gait-tag detour (main plus 8 workers), the hit slots, the four `EnterImpl` thunks, `RequestJump`, the NPC-state capture, the quest recorders, and the dice detour (no thread suspension, no byte check). Patches installed from the worker suspend every other thread.

### 7.2 Plain English
* **How big is the port?** Big, but shaped. The feature set and protocol stay; the native layer needs a new front door (start-up gate, frame hook, `gEnv`, reflection ABI) and then a run of re-anchorings by cluster. About half of the rows (B) are "we know where to look", a third (C) are real reverse engineering, and a sixth work as written. My estimate (**inferred**; the project's unit is the work order): the foundation is **3–5 work orders** and decides viability; combat/animation **4–6**; the NPC-state, quest and UI layers **5–8** more; launcher, installer and Lua-degradation work **3–4**. That is roughly a dozen to two dozen work orders to parity, front-loaded with uncertainty.
* **The hardest part:** the NPC-state context (no RTTI, all strings gone, fixed offsets tied to 1.5.5), then the layout drift across `C_Actor` and friends (142 struct-field rows), then the reflection ABI. Combat is bounded by the job-window issue that already exists today.
* **What we cannot do on retail without replacement:** the NPC authority pause (`wh_ai_PauseNPC`), the join auto-load (`wh_sys_LoadGame`), quest catch-up, and log-driven holds. Each has a native class behind it in retail RTTI, so none is a dead end, but none is free.
* **Ongoing cost with retail stable:** low per patch if the build-identity check and per-feature fail-closed validation are built in (we already have the pattern); retail's `WHGame.dll` is unchanged since June 19. The hidden cost is maintaining two builds (and two launch paths, two sets of expected layouts) until one is dropped.
* **What it buys:** players' own saves in the load menu, and one day DLC co-op, without any change to the game data assumptions.

### 7.3 What the probe session should prove first (in this order)
1. **It loads and ticks.** Replace the `CrySystem.dll` gate, find `C_ModulesManager::Update` (via `ProcessMessage`), hook it, log the thread id on the first ten frames. **Also** call one stripped console command through `ExecuteCommand` to see what retail does. (D-007)
2. **It can look around.** Derive `gEnv`; locate the RTTR entry points from registration strings; do one `Soul` lookup; read one soul's health. (D-183, D-010)
3. **The layout question.** Read the `C_Actor` combat-actor field (+0x278 vs +0x300) and measure how many of the other `C_Actor` offsets shifted; confirm the `UpdateMannequinTags` slot and prologue and log the thread ids of its callers. (D-095, D-415, D-369)
4. **One hit, one swing.** Check `EnterImpl` slot 0x1C8 and one hit slot; if they hold, the combat cluster is mostly confirmation. (D-365, D-343)
5. **The NPC-state context.** Find it through the element classes that exist; confirm the +0x9C0 embedding. (D-553, D-516) This is the decision point for the activity/stance features.
6. **Does the mod load?** Put the existing pak under a `Mods` folder of a *copy* of retail and see whether the manifest is accepted, `kdcmp.lua` auto-executes and the table patches apply; check the RemoteConsole route for the agent. (Section 4.3.)
7. **One real 1.5.6 save header**, to settle the version gate (section 5.1).

### 7.4 Recommendation: **probe first**
* **Not "don't port":** nothing found is a wall. No packing or integrity check is visible; RTTI and `.pdata` are intact; game data is identical; the reflection names survive; the worst clusters have native classes behind them.
* **Not "port now":** the go/no-go depends on three facts we can only learn by running one build of ours on retail: whether the foundation (frame hook, `gEnv`, RTTR) works, how far the struct layouts have drifted, and whether the NPC-state context is reachable. A single focused session (steps 1–5 above, plus 6–7 when possible) will answer all three for a small fraction of the port's cost.
* **Preconditions for that session:** a copy of retail (not the install the maintainer plays on); the launcher started through Steam; add the thread id to `logf`; keep the session away from saves.

---

## Decisions made unattended

1. **Delegation.** Because the work order is read-only and the native source is 29,000 lines, I split the reading across nine read-only agents, each under the same written brief (hard rules, evidence marks, schemas). I merged their census rows (599 → 579), classified them with my own static probe, and re-checked their headline claims (for example, I grepped `native/` myself for the combat hook names).
2. **The A/B/C rules** (section 3.3) are mine, applied by script; I added class **D** for names that live in game data or in our working directory, because forcing them into A/B/C would mislead. Heuristics (owner-class RTTI making a struct/slot row a B) are marked inferred.
3. **Demotions after review:** a string row is A only if the same literal is found and all listed strings exist; where the retail string *extends* ours (`CEntitySystem::SpawnEntity %s %s 0x%x`) it is B; struct-field rows are A only if the retail function reads *all* tested offsets (none did).
4. **"Retail saves" = the 1.1.1 saves on this machine**, because no 1.5.6 save exists here; the version-gate case for genuine 1.5.6 saves is stated as inferred.
5. **The work order's combat hook names** are not hooks in our code; I documented what we actually hook and call, and treated the named functions as engine functions to look for on retail.
6. **Thread claims.** Our logs carry no thread ids, so every thread statement for a hook is either from WO-116's probe (observed) or from code shape (inferred); none is invented.
7. **Privacy.** Paths are placeholders; the vendor build-agent path, build host and build-account names embedded in binaries and saves are described, not reproduced; no IPs, no user paths; another retail developer is "an external retail developer" and an external offset/header reference is "an external retail-side reference" (the older WO pages name it; I did not). Steam app ids, DLC ids and game class/cvar names are game content and are kept.
8. **One slip, handled:** a reader's first extraction call wrote 415 of its own Lua files into a stray folder at the root of a drive outside both installs; it deleted exactly that folder and confirmed it gone (recorded in the progress page).
9. **Not committed:** the probe scripts and string dumps (they contain local paths and large extractions of game binaries), the copies of saves, and the extracted Lua. Only the three documents and the CSV are committed.
10. **No tool installs:** `pefile`, `lief` and Ghidra were not available; I wrote a small read-only PE/RTTI/`.pdata` toolkit and used `capstone` (already installed) for the few functions disassembled. A decompiler would sharpen the struct-field and slot rows.

---

## Appendix A — binaries, sizes, sha256

Retail and Modding Tools builds (executables and DLLs in the two build folders; the shared third-party DLL folders are the same kind of vendor files in both installs and are not listed).

| build | file | size (bytes) | sha256 |
|---|---|--:|---|
| retail `Win64MasterMasterSteamPGO` | BsSndRpt64.exe | 370,176 | `0e304c7e405187093ec0fb9a032bf8a57d8672b7c8a50171b4bab5bbe5201f55` |
| retail `Win64MasterMasterSteamPGO` | BugSplat64.dll | 635,904 | `3d1239b0bde9d436d4afc1ce3e57af22d853d3bed5c3e8fe3d4a17445865e608` |
| retail `Win64MasterMasterSteamPGO` | BugSplatHD64.exe | 330,240 | `2f25666be8740a9088aa22e5579ca6db3a9602829d3dac1695cd38c5ab3841e1` |
| retail `Win64MasterMasterSteamPGO` | BugSplatRc64.dll | 277,504 | `4e1586eb05187a196403494b1bce65325d42bdfb982ef330e67d80c13fda14c3` |
| retail `Win64MasterMasterSteamPGO` | KingdomCome.exe | 1,525,760 | `d85b5355842d16290dc0d97e1d02856c6f2811a2021c8957aa24d7782a771b77` |
| retail `Win64MasterMasterSteamPGO` | Quatmosphere.dll | 19,456 | `b018199590c705ab3d53b52fc31a0da8cdb75bebc8a7702b6acd0182a0b9b3db` |
| retail `Win64MasterMasterSteamPGO` | WHGame.dll | 89,180,672 | `bdf8f9e4a11257a72b64c84700e284c29e4c4ccaf5b8d4bfa7d0b2a7294479f7` |
| retail `Win64MasterMasterSteamPGO` | WHGameArm.dll | 107,047,936 | `eb91a9f044cbfee984403124c337f2f2f4fd93a53201b1a16314571718def070` |
| retail `Win64MasterMasterSteamPGO` | WhGdk.dll | 10,240 | `1c58e886237399b537a2fdb34b66326da85c615409bbc3935f484acfeeae466f` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | AnimationModule.dll | 2,145,792 | `fa90be848e9906676b2bddd54b985d93e1b78d13fa6aacddf40fddccbd392e4d` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | BsSndRpt64.exe | 370,176 | `0e304c7e405187093ec0fb9a032bf8a57d8672b7c8a50171b4bab5bbe5201f55` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | BugSplat64.dll | 635,904 | `3d1239b0bde9d436d4afc1ce3e57af22d853d3bed5c3e8fe3d4a17445865e608` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | BugSplatHD64.exe | 330,240 | `2f25666be8740a9088aa22e5579ca6db3a9602829d3dac1695cd38c5ab3841e1` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | BugSplatRc64.dll | 277,504 | `4e1586eb05187a196403494b1bce65325d42bdfb982ef330e67d80c13fda14c3` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CombatModule.dll | 8,928,768 | `1f90888d48e5afbebfae53e584beab65e45e114aa699fc6242cf3bb7a572d7ff` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | ConceptModule.dll | 6,201,344 | `ab4032ac0410e9072a98c25307796b641c2318bc5460c12ac3039997cb576240` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | Cry3DEngine.dll | 4,533,248 | `06ecd1f38847c1029eeb0894dd86c8e30f6e8c82e49f29fde92f7e1dd2db1e13` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryAction.dll | 5,983,744 | `0965359848bc682148d9da9965e48e0cfba15feccb61560f675cc90b0a9cca52` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryAISystem.dll | 3,245,568 | `b857fb6dadae4f32b522a6cee94af4a43322748bcf83639a24f30e3dc825d103` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryAnimation.dll | 3,143,680 | `d3111d175cdc553087a72db1c8a3053c1b23f8cc72fe666ec29e5d7d7a00af45` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryAudioImplFmod.dll | 590,848 | `29d05fac3f6ccd67431ef3758c1b9a24248da61ba92810739bef56fe6c19d8a8` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryEntitySystem.dll | 1,730,048 | `41f2304e4930a35d4a074267c100c439da166fca4bc1a61142be2fa3d6275edc` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryFont.dll | 354,304 | `c77a58b6056feb12ca9eb4e798ffeef6057014e8e735378fccc2c17e37bd6e16` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryGamePlatformSteam.dll | 175,104 | `b5b09b045b58f2ab0bcb827a3a3e2812a9ecfa5ed4ce1fb63383394391dbe2eb` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryInput.dll | 294,400 | `4ceec75465d3da8c12f1aea5877e11a4faa4d19de0756b1e71f14b4f63e97748` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryMovie.dll | 865,280 | `0423860aee36a0634192f8afbf4531e440223417d7884400b1faad9dc5d749f2` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryNetwork.dll | 1,146,880 | `92417a8de7cee8c03092d198610c6cef728be8816d44a10517106c5b1c991da5` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryPhysics.dll | 2,627,072 | `526c0fbaa60dcc901cafc31b273bcaf22dee14c9e938cf784fbac25f6c2f54c9` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryRenderD3D12.dll | 6,467,072 | `619c48f2760dd85cb29c67f554321264750095379b8939d73c79f8a3e1e98a8b` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CryScriptSystem.dll | 635,904 | `458b0aa183a8969d3a64d8d50c93cb1655dbac1da7ef64fab0ab4b1f4b419e1e` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CrySoundSystem.dll | 347,648 | `c20541da5663e9b81b2130edaeedd53e24bda4f824b6e05ca5ab9e7f19fc3c8c` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | CrySystem.dll | 6,849,024 | `b45510618332ccc54ec77e7b05be2879181daf1d62dff309fed0d67c2491af7e` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | DatabaseModule.dll | 639,488 | `46fc4cec4418e9b310e17a4e228c5c84f384c28c405e501eef6675038326329b` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | DialogModule.dll | 3,166,720 | `191132fd916a05aad874979ec6f17daa2081089cb6f8a3c01c97fdc162f0a27d` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | Editor.exe | 429,568 | `ae79e26926ea4ee49420a9da61ec42589aa2b40bd0704b652eda3696e4b79876` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | EditorCommon.dll | 1,263,104 | `548f87f7cdb4acd3917334da384ecc439bf8e7f03fadd75a84e4857ff879856e` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | EditorDll.dll | 23,075,328 | `ab82cde0f30afd28185b0f4d08f41299d373c932587352061fdfad0fb2563741` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | EntityModule.dll | 20,977,664 | `629befa13b0beb82757258b9680deaab9127366950aacb07c440a6afa56f17a0` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | EnvironmentModule.dll | 840,704 | `94752949f4383a8c39614e87b1bdb0b03a3555159ba5a946227faa1178e5e3fc` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | flogging.dll | 67,584 | `b717db247f803aeaae808ed2397b0c593c5ac784de942ca4ab399d7bba9450e8` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | Framework.dll | 4,531,712 | `f268bbc0b26d791f468097bddb23ad7003947cca6d14a82603e9c14773488617` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | GUIModule.dll | 6,405,632 | `ad603e23069f0b5d6b446b50d1d9fc2d48552bf39f75a1a29f31508d8f9f9d59` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | KingdomCome.exe | 1,540,608 | `a32d077e6b54e5cd0c5a5f3194f473779706ff266a711e4745ccdb598843340a` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | MusicModule.dll | 1,406,976 | `087219731174f2e980c4d8a2edf60efce8ddba8dc74fdf2475d688a20604fc18` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | OptickCore.dll | 172,032 | `357552e88d2805266435ba3da3c2787986d52db1591f49c0e5c8a13e1d5ab724` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | PlayerModule.dll | 9,547,264 | `a1b65a4ab7247730c7fe55ced3f39a01ed70530194a995d7c3335d84b4fecb12` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | Quatmosphere.dll | 21,504 | `7c64f8491bd626982efef1e64f6d86a48c45d136011613e86bce8e9067c511e7` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | QuestModule.dll | 917,504 | `88df6b40a8b0346af02fcd6a3e63cfa93f1b6c833ad8b6101fb0b91886798574` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | RPGModule.dll | 19,132,928 | `2b1b26f7aa2694c6d2d21aeec93c3fb7bb7320df49f11b33cf97e457f121d75c` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | Shared.dll | 119,296 | `54147c794dde9222a50080c8b84f2ba24bddfe8ec26d6578cf1e5853f731262f` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | ShopModule.dll | 894,464 | `6b70bf33fb82b99e45ad99d4098e8e5a3ffddfc1d33b1221dd5b45cbe957b682` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | SoundModule.dll | 497,152 | `f7f23d291fe504aa4b04aca10dffb8786e897f1afd96b6becc863cb319ba5930` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | TestModule.dll | 6,838,784 | `8d12ccf790a797c7bbbea18f591350c1775f072890d5d22d6f774e5df4345f70` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | UtilsModule.dll | 290,816 | `9db0b86fb01ec5497875a64579f8115982dcd70c9d4d427f72a09fe8eea7e179` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | WHGame.dll | 4,509,184 | `3461d9cca161c07587bdff2215e55aaca648130e429ed074be3ec52b49dc639c` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | WhGdk.dll | 11,776 | `cb5c798872d1bad2997afe55ec4be0934b97d1cb3dedf06ef17178ad77700dcd` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | XBehaviorModule.dll | 2,246,144 | `0eb0bafdd756218d64cc419c48db5eb7d3404dd344640f90d494745aebbf25a7` |
| Modding Tools `Win64ReleaseSteamLTO_DLL` | XGenAIModule.dll | 51,304,448 | `e20db768f17284a6eca8d6bb7411e6d2f08876d41eb5279368679d4d3ccd4847` |

## Appendix B — Modding Tools modules: exports and RTTI

| module | size (bytes) | exports | of which C++ (mangled) | RTTI names |
|---|--:|--:|--:|--:|
| AnimationModule.dll | 2,145,792 | 90 | 88 | 1291 |
| BugSplat64.dll | 635,904 | 45 | 45 | 35 |
| CombatModule.dll | 8,928,768 | 63 | 61 | 5882 |
| ConceptModule.dll | 6,201,344 | 404 | 402 | 4677 |
| Cry3DEngine.dll | 4,533,248 | 49 | 47 | 1043 |
| CryAISystem.dll | 3,245,568 | 204 | 202 | 333 |
| CryAction.dll | 5,983,744 | 59 | 56 | 2143 |
| CryAnimation.dll | 3,143,680 | 49 | 47 | 701 |
| CryAudioImplFmod.dll | 590,848 | 49 | 47 | 176 |
| CryEntitySystem.dll | 1,730,048 | 49 | 47 | 478 |
| CryFont.dll | 354,304 | 50 | 47 | 21 |
| CryGamePlatformSteam.dll | 175,104 | 51 | 48 | 64 |
| CryInput.dll | 294,400 | 49 | 47 | 55 |
| CryMovie.dll | 865,280 | 49 | 47 | 192 |
| CryNetwork.dll | 1,146,880 | 50 | 47 | 283 |
| CryPhysics.dll | 2,627,072 | 50 | 47 | 106 |
| CryRenderD3D12.dll | 6,467,072 | 1808 | 1765 | 954 |
| CryScriptSystem.dll | 635,904 | 49 | 47 | 85 |
| CrySoundSystem.dll | 347,648 | 49 | 47 | 150 |
| CrySystem.dll | 6,849,024 | 1087 | 1063 | 1402 |
| DatabaseModule.dll | 639,488 | 67 | 65 | 360 |
| DialogModule.dll | 3,166,720 | 73 | 71 | 2009 |
| EditorCommon.dll | 1,263,104 | 848 | 846 | 376 |
| EditorDll.dll | 23,075,328 | 5485 | 5468 | 4020 |
| EntityModule.dll | 20,977,664 | 798 | 796 | 13853 |
| EnvironmentModule.dll | 840,704 | 58 | 56 | 485 |
| Framework.dll | 4,531,712 | 997 | 995 | 2666 |
| GUIModule.dll | 6,405,632 | 63 | 61 | 4682 |
| MusicModule.dll | 1,406,976 | 76 | 74 | 851 |
| OptickCore.dll | 172,032 | 88 | 75 | 36 |
| PlayerModule.dll | 9,547,264 | 231 | 229 | 6532 |
| Quatmosphere.dll | 21,504 | 1 | 0 | 0 |
| QuestModule.dll | 917,504 | 89 | 87 | 668 |
| RPGModule.dll | 19,132,928 | 121 | 119 | 10820 |
| Shared.dll | 119,296 | 184 | 184 | 18 |
| ShopModule.dll | 894,464 | 54 | 52 | 542 |
| SoundModule.dll | 497,152 | 88 | 86 | 327 |
| TestModule.dll | 6,838,784 | 53 | 51 | 5772 |
| UtilsModule.dll | 290,816 | 114 | 112 | 71 |
| WHGame.dll | 4,509,184 | 52 | 48 | 1355 |
| WhGdk.dll | 11,776 | 1 | 0 | 2 |
| XBehaviorModule.dll | 2,246,144 | 52 | 50 | 697 |
| XGenAIModule.dll | 51,304,448 | 1784 | 1782 | 23717 |
| flogging.dll | 67,584 | 47 | 47 | 14 |

## Appendix C — the 22 hooks

| hook | what it hooks | how | installed from | runs on | retail check (static) |
|---|---|---|---|---|---|
| frame-tick | framework::C_ModulesManager::Update(float) via WHGame.dll's IAT entry for Framework.dll export ?Update@C_ModulesManager@framework@wh@@QEAAXM@Z | iat | worker (plugin_main) | main | iat:framework.dll absent \| str:wh::framework::C_ModulesManager x1 \| str:?Update@C_ModulesManager@framework@wh@@QEAAXM@Z x0 |
| dice-pause | wh::playermodule::C_Dice::SetPauseWorldTime(bool), export ?SetPauseWorldTime@C_Dice@playermodule@wh@@QEAAX_N@Z | detour | worker (plugin_main, dllmain.cpp:188) | engine-any | exp:?SetPauseWorldTime@C_Dice@playermodule@wh@@QEAAX_N@Z x0 \| rtti:.?AVC_Dice@playermodule@wh@@ x1 |
| gameover-start | C_GameOver::Start(int id) = slot 1 of the C_GameOver vftable (PlayerModule.dll) | vtable-swap | main (inside run_sync in plugin_main) | engine-any | rtti:.?AVC_GameOver@playermodule@wh@@ x0 \| str:Game over is already started x0 \| str:wh::playermodule::C_GameOver::Start x0 |
| combat-hit-melee | C_CombatSoul vftable slot 0x150 (RPGModule 0x70CE00 on 1.5.5, the melee CombatHit) | vtable-swap | main (inside run_sync) | engine-any | rtti:.?AVC_CombatSoul@rpgmodule@wh@@ x1 \| pat:40 53 57 41 55 41 56 41 57 48 83 EC 70 (history writer prologue) |
| combat-hit-missile | C_CombatSoul vftable slot 0x158 (missile CombatHit) | vtable-swap | main (inside run_sync) | engine-any | rtti:.?AVC_CombatSoul@rpgmodule@wh@@ x1 |
| action-enter-attack | EnterImpl (vftable slot 0x1C8) of C_CombatActorActionAttack | vtable-swap | main (inside run_sync) | engine-any | rtti:.?AVC_CombatActorActionAttack@combatmodule@wh@@ x1 |
| action-enter-dodge | EnterImpl slot 0x1C8 of C_CombatActorActionDodge | vtable-swap | main (inside run_sync) | engine-any | rtti:.?AVC_CombatActorActionDodge@combatmodule@wh@@ x1 |
| action-enter-perfectblock | EnterImpl slot 0x1C8 of C_CombatActorActionPerfectBlock | vtable-swap | main (inside run_sync) | engine-any | rtti:.?AVC_CombatActorActionPerfectBlock@combatmodule@wh@@ x1 |
| action-enter-block | EnterImpl slot 0x1C8 of C_CombatActorActionBlock | vtable-swap | main (inside run_sync) | engine-any | rtti:.?AVC_CombatActorActionBlock@combatmodule@wh@@ x1 |
| request-jump | C_ActorStateExpansion vftable slot 0x100 (RequestJump) | vtable-swap | main (inside run_sync) | engine-any | rtti:.?AVC_ActorStateExpansion@entitymodule@wh@@ x1 \| pat:FF 90 F8 0A 00 00 (RequestJump's actor vtbl 0xAF8 crouch check) |
| update-mannequin-tags | C_Actor::UpdateMannequinTags = C_Actor vftable slot 0xC98 (EntityModule+0x94750 on 1.5.5) | detour | main (inside run_sync: the patch suspen… | engine-any | rtti:.?AVC_Actor@entitymodule@wh@@ x1 \| pat:48 89 54 24 10 53 57 41 55 41 57 48 81 EC A8 00 00 00 |
| csystem-render | CSystem::Render (CrySystem.dll, found by the literal 'CSystem::Render') | detour | pipe | main | str:CSystem::Render x1 \| pat:40 56 41 56 48 81 EC D8 00 00 00 48 8B F1 |
| dialog-start-gate | the DialogModule function that logs "New dialogue '%s' is starting. Params: souls = ..." (DialogModule+0x98290 on 1.5.5) | inline-gate | worker (plugin_main, dllmain.cpp:167) | engine-any | str:New dialogue '%s' is starting. Params: souls = '%s' forced = %s, forced decision = %s x0 \| pat:40 55 53 56 41 55 41 57 48 8D 6C 24 C9 48 81 EC C0 00 00 00 |
| quest-state-execute | C_StateVariable vftable slot 33 (+0x108, C_Node::Execute's dispatch) | vtable-swap | worker (plugin_main, dllmain.cpp:170) | engine-any | rtti:.?AVC_StateVariable@conceptmodule@wh@@ x1 \| str:state value changed from '%s' to '%s' x1 |
| quest-state-changed | C_StateVariable vftable slot 42 (+0x150, the setter's change notification) | vtable-swap | worker (plugin_main, dllmain.cpp:170) | engine-any | rtti:.?AVC_StateVariable@conceptmodule@wh@@ x1 |
| quest-function-invoke | C_Function (conceptmodule) vftable slot 12 (+0x60, invoke) | vtable-swap | worker (plugin_main, dllmain.cpp:170) | engine-any | rtti:.?AVC_Function@conceptmodule@wh@@ x1 |
| hud-quest-sink-proxy | the game's I_UIHudEventsQuest sink, replaced through the exported C_QuestModule::SetUIHudEvents/GetUIHudEvents pair with a forwarding proxy object | callback | main (hud_on, on demand) | engine-any | rtti:.?AVC_UIHudEvents@guimodule@wh@@ x1 \| rtti:.?AVC_QuestModule@questmodule@wh@@ x1 \| exp:?SetUIHudEvents@C_QuestModule@questmodule@wh@@ x0 |
| hud-quest-sink-vtable | slots 0 and 1 of the C_UIHudEvents I_UIHudEventsQuest vftable (.?AVC_UIHudEvents@guimodule@wh@@ at vftable index 88) | vtable-swap | main (hud_on, on demand) | engine-any | rtti:.?AVC_UIHudEvents@guimodule@wh@@ x1 |
| pause-game-gate | CCryAction::PauseGame(bool pause, uint16 source, bool force, uint fadeMs), found via its log string (CryAction.dll) | inline-gate | worker (plugin_main, dllmain.cpp:173) | engine-any | str:CCryAction::PauseGame(), source:%d pause:%c, nFadeOutInMS:%d x0 \| pat:48 89 5C 24 08 48 89 74 24 18 48 89 7C 24 20 |
| trespass-listener | the HUD's trespass listener (this, uint8 level) in GUIModule, found as the single tail-jumper into C_UIHudStates::SetTrespassState | inline-gate | worker (plugin_main, dllmain.cpp:176) | engine-any | str:SetTrespassState x1 \| pat:48 89 5C 24 08 57 48 83 EC 20 48 8B F9 0F B6 DA |
| skiptime-show-dialog | wh::playermodule::C_SkipTime::ShowDialog (PlayerModule+0x4C5BF0 on 1.5.5), found by its __FUNCTION__ literal | inline-gate | worker (plugin_main, dllmain.cpp:179) | engine-any | str:wh::playermodule::C_SkipTime::ShowDialog x0 \| exp:?I@C_SkipTime@playermodule@wh@@SAAEAV123@XZ x0 \| pat:48 8B C4 48 89 58 08 48 89 70 10 48 89 78 18 55 |
| npcstate-request-change | NPCState::C_NPCContext::RequestStateChange (XGenAIModule+0x1881090 hard-coded, verified by prologue and by the string 'wh::xgenaimodule::NPCState::C_… | inline-gate | worker (plugin_main, dllmain.cpp:186) | engine-any | str:wh::xgenaimodule::NPCState::C_NPCContext::RequestStateChange x0 \| rtti:.?AVC_AnimAction@NPCState@xgenaimodule@wh@@ x1 \| pat:40 55 53 56 57 41 55 41 56 48 8… |
