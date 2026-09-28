# WO-138 — No pausing + the host's NPC stream in the DLL: progress

Answer and evidence: `docs/WO-138-findings.md`. This page is what was done,
what it touched, and what is left.

## Phase status

| phase | status | notes |
|---|---|---|
| 0 — origin, clean tree, fast-forward | done | `origin` = the project's repo; WO-137's findings on `origin/main` |
| 1 — the frame hook in every pause state | done | ran in all six states measured (findings §2); the inventory and ESC states via console stand-ins |
| 1 — the NPC stream in the DLL | done | native sampler + sender at the frame hook; Lua sender kept as the fallback (observed h1–h3) |
| 1 — the pause on the wire, hold vs lost | done | PauseUp's state byte = the reasons; the joiner holds while paused + link alive (synthetic j1) |
| 2 — nobody's menu pauses the other's world | done | ESC menu and video pauses declined in a session; the inventory's divide set to 1; dialogue stops only the clock (not removed); outside a session unchanged |
| 3 — prove it | done | h1/h3 (host + avatarpeer), j1 (joiner + synthpeer), cost measured |
| gates, docs, checklist | done | every gate green (below); checklist section WO-138; `Verify-Install.ps1` markers |

## What was built

* **Native** (`native/KCDMP/wo138.{h,cpp}`, `wo138_rules.h`):
  * the NPC sender;
  * the frame meter;
  * the PauseGame gate (CryAction.dll, found by its log string) and the
    instance lookup (vtable slot → the singleton);
  * pipe 0x24 → 0x9F, unsolicited 0xA0 / 0xA1.

  Small hooks elsewhere:
  * `main_thread::last_dt()`;
  * `npcdrive::set_hold_all()`;
  * `npcscan::read_entity()`;
  * `inlinehook::install_gate4()`.
* **Agent** (`Wo138.cs`, `GameBridge.Wo138.cs`, `CombatPipe.cs`):
  * the track set and settings to the DLL, the rows out as 0x26, the Lua line
    dropped while the DLL streams;
  * the pause reasons in PauseUp;
  * the joiner's hold and link tracking;
  * the levers.

  `LogTailGameTransport` exposes its four pause states one by one.
* **Mod** (`kdcmp.lua`, WO-138 section + four touch points):
  * `npc_track` / `w138_cfg` / `w138_dialog`;
  * the Lua sender's gate;
  * hold-don't-hide in the silence release and the reconcile sweep;
  * the Apse ratio lever;
  * `mp_w138_status` / `_native` / `_levers` / `_pausetest`.
* **Tools:**
  * `tools/Test-WO138Synthetic.*`;
  * synthpeer `pause <hex>`, `npcquiet on|off`, `linkquiet on|off`;
  * avatarpeer `--record` now records PauseDown too.

## Live runs (all solo, one machine, the Modding Tools game)

| run | what | binaries |
|---|---|---|
| l1 | no agent, the DLL's pipe held by a script: frame hook / meter / native reads / Lua timers through a frozen time scale, a dialogue, a rendered video; the pause sources decoded | research build l1 |
| h1 | host + avatarpeer: native sender end to end; 30 s frozen world; host dialogue; rendered video with the levers on | r1 |
| h2 | host: the instance lookup still failing (fixed in r3) | r2 |
| h3 | host + avatarpeer: the ESC menu's PauseGame, levers on (declined) vs off (frozen) | r3 |
| j1 | joiner of synthpeer's world: host paused + silent (held), stream lost (hidden), paused + link lost (hidden); the joiner's own ESC pause with and without levers | r3 |

h1's ESC-menu phases did nothing: the pause call was refused because the
instance had not been found, and that is recorded as such, not as evidence.
h3 is the valid A/B.

## Side effects (everything touched outside the repo)

**Saves** (`Saved Games\kingdomcome2\saves`):

* **Backup:** before the first launch all 292 save files were copied to the
  scratchpad with a sha256 list. The save guard ran after every run.
* **Final check:** **all 292 files match their sha256, and no file outside the
  throwaway is new.**
* **The throwaway:** `playline4` held a copy of the maintainer's
  `playline1/autosave087`. The test games wrote into it only:
  * the host agent's scheduled world saves `autosave088`–`autosave091`
    (h1–h3);
  * j1's join, which placed its world file there and removed it itself.

  **`playline4` was removed at the end** (it did not exist before this work
  order). Its files were moved to the scratchpad.
* No test game ever saved into a real playline.

**The game** (the Modding Tools install):

* **The mod pak:** the installed `Mods\kdcmp\Data\kdcmp.pak` (sha1
  `b7adb756…`) and `mod.manifest` were backed up, replaced by the test builds
  for h1–h3 and j1, and **restored** (sha1s checked: `b7adb756…`,
  `18de23ef…`).
* **Test DLLs** were injected into the test game process only (`KCDMP-l1`,
  `-r1`, `-r2`, `-r3`). The game was quit with `System.Quit()` after every
  run.
* **`kcd.log`:** each launch's previous log was copied to the scratchpad
  first.
* **A stuck video (l1):** a console-played rendered cutscene stuck in the
  background window, and its pause survived a reload. It was cleared with the
  game's own `wh_game_unpause`. No crash, and no crash dialog.
* **Cvars changed during runs, and their end state:**
  * `t_scale` 0.001 → 1 (l1, h1);
  * `wh_ui_ApsePauseRatio` 1000 → 1 by the lever in h1–h3 and j1. The games
    were quit with it at 1. A last menu-only launch read it back: **1000**
    (it does not persist). That launch loaded no save, injected nothing, and
    was quit at once.
* **Files created in the game folder:** none.

**Programs:** the maintainer's launcher, agent and relay were not running at
any time, so nothing of theirs was stopped or restarted. The test relay ran
on TCP 7779 / HTTP 5274 and the test agents on IPC 5911/5912; all were
stopped after each run. The test agents' data folder was the scratchpad.

**Focus:** no input was sent. The game never took the foreground after a
load in these runs, so no push-down was needed. Frames were `PrintWindow`
captures of the game window only.

**Henry, on the throwaway copies only:**
* moved next to an NPC twice, to start a dialogue (`player:SetWorldPos`);
* turned once to face the copies (`SetWorldAngles`);
* two real dialogues with a quest NPC were started (`BasicAIActions.OnTalk`)
  and ended (`InterruptDialogs`); one was a reputation denial line.

## Tests and gates

* **Unit and relay tests:**
  * `KcdMp.Client.Tests`: 512 passed (`Wo138Tests.cs` new, 15);
  * `KcdMp.Relay.Tests`: 53 passed (a WO-138 round trip new: the reasons byte
    crosses unchanged).
* **Synthetic suites:** every `tools/Test-*Synthetic.ps1` suite passed,
  `Test-WO138Synthetic` 44/44 new.
  * `Test-WO90Synthetic` caught a swallowed error in the new dialogue edge on
    a stub player without `IsInDialog`. That is fixed with a guard.
* **The rest:**
  * the static checks (`Test-WO106ConsolePlaceholder`, `Test-WO110LuaLocals`);
  * the local payload publish and smoke;
  * the native unit tests: 147 passed (`wo138_rules_tests.cpp` new, 58).
* No installer, no GitHub Release, `VERSION` untouched.

## Left for later

* **The maintainer's checklist:** `docs/TWO-PLAYER-CHECKLIST.md`, section
  WO-138. It covers the real ESC menu, inventory and map with the world
  running, a quest's rendered cutscene, and the joiner's view of a host in
  its inventory.
* **The dialogue clock pause:** removing it needs a native hook at
  DialogModule's calendar pause (findings §4.3).
* **`npc_claim` (proximity claims, host authority off)** still streams from
  Lua.
