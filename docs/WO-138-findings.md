# WO-138 — No pausing, and the host's NPC stream in the DLL: findings

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios.

Evidence marks: **(observed)** seen live in the game, **(synthetic)** the real
game against a synthetic peer or a console stand-in for a player's action,
**(code-verified)** read in the code or the game's binaries but not run,
**(inconclusive)** tried, no clear answer.

All live runs were solo on one machine (the Modding Tools game), on a
throwaway copy of a save: the real game as the host with a scripted partner
(avatarpeer), and the real game as the joiner of a synthetic host (synthpeer).
No input was sent, so every menu here is a console stand-in (§2). What each
stand-in does and does not prove is said where it is used.

## The answer

1. **The host's NPC stream no longer stops for a menu.** The sampling and the
   sending moved into the DLL, at the frame hook, which runs through every
   pause state measured. The Lua sender is kept as the fallback when the DLL is
   absent. **(observed)** — through a 30 s frozen world the partner got
   between 3 and 39 rows every second (mean 15, none missing); the Lua timers
   stopped the whole time (§3.1).
2. **The pause is on the wire, with its reasons.** The host announces
   "paused: menu / inventory / dialogue / cutscene / load / skip-time /
   frozen" and "running" in the existing Pause message (0x1C/0x1D). While the
   host is paused and its link is alive, the joiner **holds its copies
   visible** where the stream left them. The 3-second hide applies only when
   the stream is really lost. **(synthetic)** — 35 s of a paused, silent host:
   the copies stayed; a silent host with no announcement hid them in ~3 s, as
   before; a paused host whose link went silent hid them 6 s later (§3.3,
   frame 1).
3. **Nobody's menu stops the other's world. In a session, most menus no longer
   stop even your own.** The engine allows it:
   * **ESC menu:** `CCryAction::PauseGame` is a per-source pause, and the ESC
     menu is its own source (InGameMenu, 7). The DLL declines that source's
     pause during a session. **(synthetic)** — the engine's own PauseGame
     from source 7, declined: NPCs walked on for 30 s. The same call with the
     levers off froze the world (§3.2).
   * **Inventory, map, journal (the "Apse" screens):** these don't pause at
     all. They divide the game's time scale by `wh_ui_ApsePauseRatio`, which
     is **1000** on 1.5.5. In a session it is set to 1. **(observed** for the
     cvar and its effect on time; the real screens need a key press, §5**)**
   * **Rendered cutscene:** its video pauses through source 4 (VideoMode),
     which is declined as well. **(observed)** — the host's world ran on
     through a whole rendered video, and the video played.
   * **Dialogue:** a dialogue never stopped the world. It stops only the world
     **clock** (time of day): NPCs walk, the stream flows, Lua timers fire.
     That pause is not removed (§4.3).
   * **Outside a session nothing changes:** menus pause as usual, and the
     levers are off with no partner present. **(observed)**
4. **The joiner's own menus:** its copies follow the host's stream even while
   its own world is frozen, because the native writer runs at the frame hook.
   Closing the menu leaves nothing to catch up. **(synthetic)**
5. **Cost:** the native sender tick takes 105–116 µs (median) every 100 ms
   for 66 tracked NPCs, 157–210 µs at worst. That averages about 15 µs per
   frame at 74 fps. When new names appear, one entity walk resolves them
   (~14 ms, once; at most every 3 s). **(observed)**

## 1. What was broken, and why

* The host's NPC stream was a Lua timer (`KCD2MP_NpcSyncTick`, 100 ms): it
  wrote `npc_state` lines to `kcd.log`, which the agent turned into 0x26.
* Menus, the inventory, skip-time, rendered cutscenes and Game Over stop
  `Script.SetTimer` chains (WO-12, WO-78). So the host's stream stopped too.
* On the joiner, WO-131's guard releases (and parks, hidden) every copy that
  gets no packet for 3 s. All nearby NPCs vanished until the host closed its
  menu.
* **Why the timers stop (observed, §3.1):** the script timers run on game
  time. The inventory's 1/1000 time scale and a PauseGame source both stop
  game time. Even a timer created with the "update during pause" argument
  stopped under a frozen time scale.

## 2. What runs where, per state (Phase 1's first question)

The DLL counted frames and the game's own frame time (the `dt` the engine
passes to `C_ModulesManager::Update`) through each state:

| state | how it was made (no input) | frame hook | game time | Lua timers | NPCs | world clock |
|---|---|---|---|---|---|---|
| normal | — | 75 fps | 1.0× | run | move | runs |
| inventory / map (Apse) | `t_scale 0.001` = the Apse's 1/1000 divide | 75 fps | 0.001× | **stop** | **stop** | stops |
| ESC menu | `PauseGame(true, InGameMenu)` via the DLL (live-check op) | 74 fps | 0 (source 0x80 held) | **stop** | **stop** | stops |
| dialogue | `BasicAIActions.OnTalk` on a quest NPC (a real dialogue with Henry) | 65 fps | 1.0× | run | move | **stops** (`IsWorldTimePaused` true) |
| rendered cutscene | `wh_ui_PlayCutscene` (a rendered video) | 73 fps | 0 (VideoMode 0x10 held) | **stop** | **stop** | stops |
| save load | `wh_sys_LoadGame` | runs | — | killed (known) | — | — |

All observed except the first two stand-ins, which are synthetic:
* **The inventory stand-in** reproduces the Apse's effect (the game's time
  scale divided by 1000), not the screen. The real inventory, map and journal
  need a key press: §5.
* **The ESC stand-in** is the engine's own PauseGame with the ESC menu's own
  source number, through the same hooked entry the real menu reaches.

Earlier work already showed the frame hook ticking through the real inventory
and the real pause menu (WO-11, WO-124).

**The frame hook ran in every state measured.** WO-10's "the tick doesn't run
at the menu" stays refuted.

Further findings (observed):
* `Game.PauseGame` is **not registered** on 1.5.5. A Lua call to it errors and
  pauses nothing, so it is useless as a stand-in.
* `wh_game_unpause` is a forced resume from source 1, and it clears every
  counter.
* A rendered video's VideoMode pause **survived a save load** once, when the
  video stuck in a background window. The world stayed frozen after the load
  until `wh_game_unpause`. In a session that pause is now declined.
* `wh_ui_PauseGameOnFocusLoss` reads 0 on this build (the Modding Tools game).
  Alt-tab did not pause it.

## 3. What was built, and what it did live

### 3.1 The native sender (host)

* **Where:** `native/KCDMP/wo138.cpp`, rules in `wo138_rules.h`.
* **Tracked set:** the mod's rescan (unchanged, every 2 s) emits it as
  `npc_track` lines, with the sender's settings as `w138_cfg`. The agent
  forwards both to the DLL (pipe 0x24 op 2 / op 1). The agent pushes the
  partners' positions as cull anchors every 500 ms.
* **Every 100 ms, at the frame hook, the DLL reads:**
  * the position and yaw from the entity's world matrix (the same read as
    WO-102.5's scan);
  * the soul's `health`, `IsDead` and `IsUnconscious`, and the combat soul's
    `HasWeaponInHand`, round robin so every NPC is refreshed every 200 ms.
* **What it sends:** the Lua sender's own rules apply — the flags (1 dead,
  2 KO, 4 drawn, 8 swing cue, 32 engaged, 128 not human), engaged at 12 m,
  the swing cue at 4 m, the cull at 60 m with the 150 m far band in a shared
  world, and the send gate (5 cm / 0.5 hp / 2 s heartbeat / any flag change /
  first dead). Rows go to the agent as the unsolicited 0xA0; the agent puts
  each one on the wire as the same NpcState (0x26) the Lua line made.
* **The Lua sender** stays quiet while the agent confirms, every 2 s, that the
  DLL sends. The confirmation lapses after 6 s. If an `npc_state` line still
  arrives, the agent drops it, except a resync (flag 0x40): 14 were dropped
  at one startup.
* **No DLL, a pre-WO-138 DLL, `mp_w138_native off`, or a stopped DLL:** the
  Lua sender, exactly as before. **(synthetic, Test-WO138Synthetic a)**
* **Values (observed):** native position, yaw and health equal the Lua reads
  for 5 NPCs (to the centimetre, 100.00 = 100).
* **Streams (observed):** the partner received 50–129 rows/s from 66 tracked
  NPCs in normal play (h1, h3).

**The host's world frozen for 30 s** (h1 `freeze`, the inventory stand-in):

| | rows/s the partner got | NPCs moving in the stream | host Lua timers |
|---|---|---|---|
| before (8 s) | 67–88 (mean 77) | ~10 | run |
| frozen (30 s) | **3–39 (mean 15), no empty second** | 0 | stopped |
| after (8 s) | 50–90 (mean 68) | ~8 | run |

The frozen rows are the 2 s heartbeats of standing NPCs, which is exactly
what the Lua sender would send for a still world, only now nothing stops them.
The partner received PauseDown `0x40` (frozen) at the start and `0x00` at the
end. **(observed; the freeze itself a synthetic stand-in)**

### 3.2 The levers (both machines, only in a session with a partner here)

* **The PauseGame gate** (`wo138.cpp`):
  * **Hook:** an entry gate on `CCryAction::PauseGame`, found by its own log
    string (`CryAction.dll+0x86EC0`, 15-byte prologue checked).
  * **What it declines:** only while the agent says the levers are on, only a
    **pause** (a resume always runs), and only from a masked source. The mask
    is InGameMenu (7) and VideoMode (4); GameOver, ScriptBind, the flow-graph
    and load sources pause as before.
  * **Held sources:** the engine's own counters at `this+8+source*4` give the
    held sources. The instance is found through the image: the vtable slot
    holding the hooked entry (slot 13), then the static that points at an
    object with that vtable (`CryAction.dll .data+0x3C248`).
  * **Off:** the pipe closing turns the gate off.
* **The inventory divide:** the mod sets `wh_ui_ApsePauseRatio` to 1 and
  remembers the original (1000). It restores it when the session ends. Every
  agent start restores one an earlier agent left set. **(observed** in the
  agent's own log: 1000 → 1**)**
* **When:** relay connected, DLL connected, a partner's position seen in the
  last 10 s, and `mp_w138_levers` on (the default). Otherwise off, and menus
  pause as usual. **(observed** — hosting with nobody joined: levers off**)**

**ESC menu, A/B on the real host (h3):** the engine's own
`PauseGame(pause, InGameMenu)`, through the hooked entry, for 30 s with the
levers on, then 20 s with them off.

| | rows/s | NPCs moving | the DLL | the announcement |
|---|---|---|---|---|
| levers on (30 s) | 54–129 (mean 81) | ~9 | `DECLINED (a session: the world keeps running)` | none (the world runs) |
| levers off (20 s) | 3–27 (mean 14) | **0** | runs; held 0x80; `WORLD FROZEN` | `0x40` frozen, then `0x00` |

The declined pause's matching resume ran harmlessly (all counters 0).
**(synthetic: the call is the menu's own, the menu screen is not)**

**The rendered video, levers on (h1):** VideoMode's pause was declined, and
the host's world ran through the whole video (the watched NPC walked 13 m, the
stream stayed at 23–57 rows/s). A frame showed the video playing normally.
**(observed)**

### 3.3 The joiner: hold, don't hide

* **The agent** notes every packet's source at the socket read. The host is
  the source of the NPC stream.
* **The hold is on** while the host's last PauseUp is non-zero **and** the
  host sent anything (position heartbeats included) in the last 6 s, capped
  at 15 min. It is re-asserted every 2 s.
* **The mod** skips the 3 s silence release and the reconcile sweep's gap
  rule while held. The hold lapses by itself 8 s after the last assert, if the
  agent goes away.
* **The DLL** skips its own 4 s silence drop (`npcdrive::set_hold_all`).
* **Release:**
  * the host resumes: every copy's silence clock restarts, so the 3 s rule
    counts from the resume;
  * the host's link is lost: no grace, the copies are hidden at once.

Live, the real game joined a synthetic host's world. Three host NPCs were
streamed, one walking (j1):

| phase | what the host did | the copies |
|---|---|---|
| host paused | PauseUp 0x02 (inventory), its NPC stream silent, 35 s | **all 3 puppets, visible, the whole time** (hold=true) |
| resumed | stream back, PauseUp 0x00 | unchanged, hold off |
| stream lost | stream silent, **no** announcement | **hidden within ~3 s** (as before) |
| stream back | — | shown again |
| paused, link lost | PauseUp 0x01, then **nothing at all** from the host | held ~5 s, then **hidden** (link 6 s silent) |

**(synthetic: the host side is scripted; everything on the joiner is the real
game)**

Frame 1 (`docs/wo138-shots/1-joiner-held-vs-lost.jpg`): top, the host paused
with its stream silent for 16 s, and the copy is still standing there;
bottom, the stream really lost for 6 s, and the copy is hidden.

## 4. Phase 2 — what the engine allows

### 4.1 Menus: yes, in a session

* The ESC menu is a PauseGame source, so it can be declined (§3.2).
* The Apse screens (inventory, map, journal, crafting, codex; `ApseMap` is one
  of them in `IPL_GameData.pak`) never call PauseGame. Their "pause" is the
  time-scale divide, and its ratio is a cvar.
* **The host's own Henry is exposed while a menu is open, as in any online
  game.** That is the intended trade.
* **Not declined, by design:**
  * GameOver (5): the mod's own death path replaces it;
  * ScriptBind (2): scripts that pause on purpose;
  * the flow-graph sources;
  * CCET_PauseGame (6): the load path uses it (seen in the log at every
    load);
  * Photomode (12).

### 4.2 The joiner's menus

* The host's world never pauses for the joiner: its PauseUp only tags the
  nameplate and informs WO-127's leash on the host. **(code-verified)**
* The joiner's own ESC menu is declined like the host's in a session. With
  the levers off it freezes the joiner's world locally, and its copies still
  follow the host (§3.3, j1 `jmenu-nolever`: the walker copy advanced
  0.8 m/s through 14 s of the joiner's frozen world). **(synthetic)**

### 4.3 Dialogue and cutscenes

* **Dialogue does not freeze the world.** It pauses only the world clock
  (WO-112's `DialogInstance` time-pause handle; `IsWorldTimePaused` true
  during Henry's dialogue). NPCs, the stream and the partner's world all run
  on. **(observed** — h1 `dialog`: 15–51 rows/s, NPCs walking, the partner got
  PauseDown `0x04` and later `0x00`**)**
* **What remains:** time of day stands still for both players while the host
  talks, because only the host's clock moves the shared world (WO-133). The
  clock pause itself was not removed.
  * Removing it means hooking the calendar's pause-handle path in
    DialogModule; WO-112's option T2 names the slot, but this work order did
    not build it.
  * For the players it is a time-of-day stall, not a frozen world.
* **Rendered cutscenes:** declined in a session (§3.2).
* **In-game (TrackView) cutscenes** don't stop the Lua timers (WO-95). They
  were not touched.

## 5. What could not be proven without a key press

These go to the maintainer's checklist (docs/TWO-PLAYER-CHECKLIST.md, section
WO-138):

* **The real ESC menu with the levers on.** The world should keep running
  behind it. Here only its PauseGame call was made, not the screen.
* **The real inventory, map and journal with the ratio at 1.** The world
  should keep running at full speed behind them. The cvar's own help text
  ("the game time scale will get divided by this number when the inventory
  is entered") and its value are observed; the screen is not.
* **A rendered cutscene that comes from a quest** (not a console-played one),
  with the world running behind it.
* **Whether any menu or screen misbehaves with the world running** (a save
  from the ESC menu, the map's fast travel).

## 6. Hazards and limits

* **An ESC-menu pause that began before the levers came on** is resumed
  normally. **One declined while on, then resumed after the levers went off,**
  logs the engine's harmless "wasn't stopped from this source" warning.
* **The ratio cvar** is restored when the session ends, and by the next agent
  in the same game process (the mod remembers the original). **It does not
  outlive the game:** after the test games were quit with it at 1, a
  relaunch read 1000. **(observed)**
* **The native sender covers only the authority's stream.** WO-60's
  proximity claims (`npc_claim`, host authority off) stay on the Lua sender.
* **No protocol change:** the Pause state byte keeps its length. The old
  "entered" (1) is the menu bit, and the relay forwards the byte verbatim (a
  relay test pins it). Mixed builds are already refused by the release check.
* **The Lua death bookkeeping** (WO-86's `mp_npc_death_observe`) still runs
  in the Lua tick. The dead **bit** goes out natively even while Lua is
  stopped.
* **The retail monolith** is out of scope, as for every native feature: the
  DLL resolves CryAction.dll's pieces by string and vtable on the Modding
  Tools build.

## 7. Wire and pipe

* **Pipe:**
  * 0x24 [op] → 0x9F. The ops: 1 Config, 2 Track, 3 Anchors, 4 Status
    (48 bytes), 5 Levers, 6 Hold, 7 Text, 8 Read (one native NPC read), and
    9 Pause (live checks: PauseGame through the gate).
  * Unsolicited: 0xA0 NpcStream, `[count]{[flags][x][y][z][rot][hp][nameLen][name]}`;
    0xA1 World, `[world][scalePermille:2][heldMask:2]`.
* **Wire:** PauseUp 0x1C / PauseDown 0x1D state byte: 0 running, else the
  reasons 01 menu, 02 inventory, 04 dialogue, 08 cutscene, 10 load,
  20 skip-time, 40 frozen.
* **Mod events:** `npc_track <gen> <part> <parts> name:f,...`,
  `w138_cfg <emitMs> <hbMs> <epsMm> <cull> <cullR> <farBand> <engage>`,
  `w138_dialog 0|1`, `w138 native|levers on|off`, `w138 pausetest <src> 0|1`.
* **Commands:**
  * `mp_w138_status`;
  * `mp_w138_native on|off` (default on);
  * `mp_w138_levers on|off` (default on);
  * `mp_w138_pausetest <source> on|off` (live checks).
* **Log lines:**
  * DLL: `WO138-SEND`, `WO138-TRACK`, `WO138-WORLD`, `WO138-PAUSE`,
    `WO138-GATE`, `WO138-LEVERS`, `WO138-HOLD`;
  * agent: `MP-WO138`, `MP-WO138-STATS`;
  * mod: `WO138-SEND`, `WO138-HOLD`, `WO138-LEVERS`, `WO138-STATUS`.
