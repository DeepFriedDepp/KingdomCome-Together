# WO-166 — 0.48.2, unattended: the loot crash, the joiner's talk, quest values, enemies that fight the joiner, weather, the fast-travel message, the map pin, enemies near the joiner, respawn, Steam friends

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L]** live, this machine's Modding Tools build (1.5.5), throwaway playline only, the game in the background, no input;
**[syn]** the synthetic peer or a Lua synthetic suite; **[unit]** the engine-free .NET suites; **[native]** `KCDMP_NativeTests`;
**[L-field]** counted from the testers' logs on disk (no line copied, no player name, address or path written here; entity names
such as `ttac_blacksmith` are the game's own); **[disasm]** read in the game's machine code this session; **[code]** read in our
code; **[not run]**; **[needs two players]**. Unattended run: nobody at the machine; every open decision takes the WO's stated
default, recorded where it is taken. The version is the maintainer's (**0.48.2**).

This file is written phase by phase: a section is appended when its phase ends.

## Inputs

The eight bundles of the WO's table were on the maintainer's desktop, not in `logs/`; they were copied (unchanged) into the
git-ignored `logs/wo166/zips/` and each unpacked into its own folder `logs/wo166/<bundle-time>/`. None is missing. Both OCE
bundles carry the testers' own time zone (UTC+13); the maintainer's are UTC−7. Times below are each machine's own.

## Phase 0 — locate (static, no game)

### 0.1 The loot reconcile and the open loot screen [code][L-field]

* **Where it writes:** `kdcmp.lua` `W134.bodyState` → `W134.applyList(e, want)`: deletes the copy's items that do not match the
  host's list (`inventory:DeleteItem`) and creates the missing ones (`CreateItem`, then `EquipInventoryItem` for worn ones). It runs
  for every host `update` of that body (the host's `W134.hostWatch` sends one whenever the body's item signature changes, once a
  second) and for the `open` answer.
* **What the mod knows about an open screen:** nothing. `W134.sessions[name]` is the joiner's *loot session* (created when our
  open path calls the game's `OnLoot`), but it lives until the player is 5 m away or 10 minutes pass; it is not a screen state.
  So a host update that lands while the loot screen is open rewrites the inventory the screen is showing.
* **The crashes:** both joiner crashes (21:09, 21:22) are exactly that: an `update` with `deleted=1` applied between
  `ui_inv_screen_in_one_pane` and `ui_inv_screen_out_one_pane`, the game gone at the close (the second crash had five such
  rewrites under one open screen while the host looted the same body). The OCE joiner's six loots had no update under an open
  screen. The loot screen is the game's `ItemTransfer` UI element (`BasicAIActions:OnLoot` → `self.actor:RequestItemExchange`);
  its definition (`Libs/UI/UIElements/ItemTransfer.xml`) declares `OnOpened` and `OnClosed` events, which a Lua element listener
  can receive (the WO-159 menu uses the same `UIAction.RegisterElementListener`).
* **A second writer under the screen:** a take the host refuses (`gone`/`mine`/`none`) is taken back off the *player's*
  inventory (`W134.deleteClass(player, …)`) the moment the answer arrives — the other pane of the same open screen.

### 0.2 The talk requests the mod makes [code][L-field]

* **`request-fallback`** (`KCD2MP_W137TalkRequest`): the agent relays every engine line "Soul 'Dude' requested dialog. Assigned id
  is N". When no talk of ours is waiting for an id, the mod resumes the **nearest living host copy within 3 m** and claims the id
  for it. The request itself is the engine's (the player's soul); the mod's act is to resume a copy and to keep a talk entry open
  for it — and that entry later triggers WO-164's T3 "retry": `player.soul:RestrictDialog(true/false)`, which deletes **all** of
  the player's unfinished requests, and a sweep of the copy.
* **The field case (the maintainer's joiner, 22:24):** the player was still inside a shop conversation (the game's own request
  for its next step arrived at that moment); the fallback attached it to a copy 1.5 m away, resumed that copy, and 4 s later the
  retry cleared the player's requests. The player's next talk to the same shopkeeper never started (`cause=wedged`,
  `open_requests=2`), the one after it needed the engine's own "player request has higher priority" to get through. The log
  also shows a script error in the retry (`IsDialogRestricted()` takes an argument).
* **`forced`** (`KCD2MP_W137TalkAttempt`): a conversation the game started itself on a copy (quest logic, or a shop's haggle
  step). The mod tracks it and, with `mp_scene_guard`, resumes the copy. It issues no request.
* Neither path checks whether the player is in a dialogue, just left one, or has a request of his own open.

### 0.3 `WO164-QFIX` [code][disasm][L-field]

* **All 635 field cases are one variable**: `Barbora.trosecko.kovar.hibernace.porovnani_kvality.kvalitaMece` (the smith's
  "sword quality"), joiner 1, host 3 — 340 on the maintainer's joiner, 295 on the OCE joiner.
* **`asleep` (612):** the DLL's `set_value` refuses when `C_Node::GetRuntimeState() == 1`. Read in ConceptModule this session:
  `C_Node::HibernateInternal` writes 1 to `[node+0x18]`, `WakeInternal` writes 0 — so 1 is **Hibernating**: the quest graph is
  loaded and keeps its values but runs nothing (this State sits in the quest's `hibernace` sub-module, which is hibernated
  whenever the smith's logic is idle — which is exactly when the player walks up to talk). Nothing is "not loaded" about it; a
  module that is not loaded has no node (`NoNode`). The test conflated the two.
* **`type-refused type=` (23):** two faults. (1) The quest file declares the State `TypeT="uint"` (Scripts.pak,
  `Quests/Final/Barbora/trosecko/kovar/hibernace/porovnani_kvality.xml`), and the DLL's guard writes only variants named `int` or
  `bool` — the native log names the type correctly (`uint`). (2) The agent's reply parser reads the type's length one byte early
  (`b[10]`, which is the top byte of the after-value; the length is at `b[11]`), so every type printed empty. The same off-by-one
  is in the op-2 (`Wo137ApplyAsync`) parser (logs only there).
* **The write the normal path uses:** WO-137's apply pulses a `Set<Value>` port (`apply`, the engine's own trigger); WO-164's
  direct write stores the variant at `+0x68` and runs the State's change notification (slot 42) with the old and new value, read
  back and undone if it does not read as written. The notification picks the State's output ports by name and triggers them;
  consumers in a hibernated module refuse to run (their own runtime test), so the value is what the graph reads when it wakes.

### 0.4 The row-stale rule [code][L-field]

* **The rule** (`Wo161Rules.Judge`, the joiner's clock only): a verdict is "shown" when a row of that NPC was *played* on the copy
  between 1.5 s before and 0.5 s after the verdict's arrival (or the played row's own start+hit lag fits within ±0.6 s); `row-stale`
  = some row of that NPC was received or played earlier than that. There is no clock offset in it; `off=` in the agent's line
  prefix is the log's own clock note, not used by the rule.
* **What the 29 stale blows were** (OCE pair, joined by hit id on both machines): **27 were `no-swing-captured` on the host** and
  **2 `swing-unmatched`**. The host never sent a row for those blows; the joiner called them stale only because an older row of the
  same NPC existed. So their lateness is not a clock question: the median gap to the last row of that NPC was many seconds.
* **What those blows are:** they came in strict **3.0 s cadences** (one NPC: 7 blows at 3.00 ± 0.02 s; another 3; wolves 4 and 3)
  on a figure whose stream was heartbeat-only (the joiner standing still), with **no combat action of any hooked class** for
  them (host DLL: `cap_npc=36` actions for `npc_avatar_hits=48` in the session, no drops). This is WO-162 Q3.1's "anim-collision
  hit that no attack action started" — the same 3 s cadence WO-162 counted for a cat's bites — now seen from men too.

### 0.5 Why the maintainer's NPCs never struck the joiner's figure [code][L-field]

* Host DLL (`kcdmp-native.log`, the maintainer's session): **`npc_avatar_hits=0`**, `cap_npc=206` (NPCs swung a lot — at Henry),
  `automation_off=13` (the figure's own combat automation was switched off 13 times: the joiner drew his weapon).
* WO-136 asked the bandit (`tbuk_zibrid`) to take the figure as its target 8 times: 2 "taken (read back)", 6 "NOT taken after 3 s
  (the engine kept its own target)"; even when taken, no attack followed. The lock-on pair was set 3 times (`near-and-facing`).
* The two field sessions differ in one thing that our code decides: in the OCE session (48 blows) the figure was mostly **not**
  in combat on the host (its stream heartbeat-only; automation_off=4), and its blows were the anim-collision kind above; in the
  maintainer's session the figure was in combat with its **combat automation switched off by us** (`motion.cpp apply_combat`:
  `combat_EnableAutomation(false)` whenever the joiner's combat bit is set). **No committed attack on the figure in either
  session.** The leading candidate gate is therefore the figure's switched-off combat automation (the engine's attack
  coordination works between two automated combatants); the WO-155 "never knock down" path does not stop an attack (it acts on
  the damage), and no mod hold acts on the attacking NPC. Phase 2 C1 tests this live with the synthetic joiner before changing it.

### 0.6 The rest [code][L-field][disasm]

* **NPC scan centre:** already the host **and** every partner with a position fresher than 5 s (`NpcScanTickAsync`, up to 8
  anchors; the WO-138 sender culls by the nearest of the same anchors). In the OCE episode (the host died and woke ~190 m away,
  the joiner was then struck to death in 18 s) the striking NPC's copy **was** on the joiner, standing idle (`anim … -> idle`,
  its weapon re-asserted sheathed from the host's stream) while the 3-second blows of 0.4 arrived — "enemies just standing
  there". An invisible attacker is a copy that is not streamed at all; the host's scan tick runs from the host's own position
  loop (`NpcScanTickAsync(px, py, pz)`), so it pauses whenever that loop does (the host's death hold, a load), and its 8-anchor
  cap counts the host first. S1 makes the partners' anchors independent of the host's own loop and state.
* **Weather:** the host's agent sent **one** profile (`semicloudy_clear_B`, the random arbiter's first pick) 39 times: the WO-151
  time-of-day reader (`BlendToProfile`) never fired in 80 minutes on any machine — the game's forecast does not blend through it.
  The game's rain is computed **per machine from the local cloud density** (EnvironmentModule: "It starts raining when
  rainThreshold < localCloud * rainProbability"); `weather_heavy_rain`, `weather_windy_rain` … are `C_GameProfileManager` layer
  profiles that follow the rain. The engine has `EnvironmentModule.GetRainIntensity()` (Lua) and the cvar
  `wh_env_RainIntensityOverride` (−1 = off).
* **Fast travel:** the agent turns every engine line "FastTravel: unable to start fast travel from/to outside navmesh!" into a
  toast. With `wh_pl_FastTravelEnabled 0` the map prints that line for its own checks (highlighting a point, opening the map: one
  of the host's six followed `ApseOpen` directly), not only for a confirmed trip. The map element declares
  `OnHighlightFastTravelPoint` and `OnDoubleClicked`; the modal dialog `OnQuestionDialogConfirmClicked`.
* **Map pins:** keyed by ghost id. A crash-rejoin gives the joiner a new ghost id; the old id's pin stays until that id has no
  position for 10 s — and any path that keeps the old id's position fresh keeps its pin. The game's mark types were read from
  GUIModule's own name table (97 types; `0x09 Dog` is the companion's icon, `0x30 GeneralPoi` the current choice when the map draws
  its category, `0x2B Grave` the fallback).
* **Respawn ("the bailiff kept hunting us"):** OCE second session, host time ~18:03–18:05: `ttkc_drozd` killed the host, then
  the joiner (two hits); both woke at the **same** wake point, and the host died again 2 minutes later. Our death handling leaves
  the skirmish (`WO132-LEAVEFIGHT`, `MP-W132 … left its skirmish`) but sends the NPC nothing: its own pursuit (a crime-driven attack
  interrupt) goes on, and it finds the woken players. The game's own end of an NPC's attack interrupt is the `stopFight` message
  (WO-139, observed); the crime record is untouched by it.
* **Steam friends:** the OCE joiner's launcher looked up friends five times: twice `hosting=0` (the host was not up yet), three
  times `hosting=1`. The lookup runs once per button press; a list opened before the host is up stays empty.

## Phase 1 — the crash and talking (built)

### L1 — never rewrite a corpse copy under an open loot screen [code][syn]
* `kdcmp.lua` WO-166 section: the mod marks the screen open on a body when its own open path calls the game's loot
  (`KCD2MP_W166LootOpened`), and closed on the game's `ItemTransfer` **OnClosed** event (an element listener, armed on the first
  open), on the agent's relay of the engine line `PlayAudio: ui_inv_screen_out…` (a second signal), when the player is more than
  5 m from the body (nobody walks with a menu open), when the body is gone, or after 10 minutes.
* While it is open, `W134.bodyState` keeps a host `update` for that body instead of applying it (`WO166-LOOT deferred npc=…`; the
  newest list wins) and applies it **one frame after the close** (`WO166-LOOT applied npc=… after_ms=…`), or before the next open (the
  open answer always applies first). A refused take is not taken back off Henry under the open screen either
  (`WO166-LOOT takeback-deferred` → `takeback-applied`, with the "Someone already took that." notice after the close).
* No switch: a crash fix. *Checks:* Lua suite L1–L13: an update under the open screen leaves the copy untouched, is applied one
  frame after OnClosed, the newest list wins, a refused take waits, the 5 m rule and the agent's line close a screen whose event
  was missed, **30 open/update/close cycles** never rewrite under the screen and always apply after **[syn]**. Live: see the live
  section.

### T1 — our own requests never block the player [code][syn]
* `KCD2MP_W137TalkRequest`: the request-fallback attaches **only** when the player is not in a dialogue, his last dialogue ended
  ≥ 3 s ago, and he has no request of his own open; otherwise `WO166-TALK own-request suppressed via=request-fallback why=in-dialogue|after-dialogue|player-request-open`.
* The player's own talk press ends every not-yet-started talk the mod attached to another copy (`WO166-TALK own-request cancelled
  npc=… for=…`; the copy is paused again). The WO-164 retry (which clears **all** of the player's requests) never fires for a
  request-fallback or forced talk — only for the player's own press. The retry's `IsDialogRestricted()` call (a script error at every
  retry in the field: it takes an argument) is gone. A forced conversation now reports its start to the agent, so it no longer
  counts as an open request (the field's `open_requests=2..3` included such entries).
* *Checks:* Lua T1–T6 **[syn]**.

### T2 — quest values reach the joiner [code][native][unit][disasm]
* Native `set_value` (WO-164 op 9): the runtime gate is now `runtime_loaded(rt)` = Awake (0) **or Hibernating (1)** — both loaded,
  both keep their value; a hibernated write returns the new result **`ChangedHibernated` (11)** ("the graph reads it when it wakes").
  Only a runtime state that is neither is refused (`asleep`). *The corrected test, recorded:* "genuinely not loaded" is `NoNode` (the
  module was never deserialised); Hibernating is loaded and dormant.
* The type gate (`wo137_rules.h set_value_type_ok`): `int`; **`uint` with a non-negative value** (the quest files declare 13 States
  `TypeT="uint"`, among them the smith's `kvalitaMece`); `bool` 0/1. Everything else stays refused (the WO-164 guard: enums, floats,
  strings). The write is still read back and undone if it does not read as written; the notification is the State's own.
* The agent reads the reply's type at its real offset (byte 11; `Wo137Rules.ReplyType`), in both parsers. The type also comes from
  **the quest's own State definition**: `QuestValueIndex` now indexes every State's `TypeT` by its engine path (Scripts.pak
  `Quests/Final/…`, ~5,150 files, the same background load as WO-147's value index); the QFIX line prints `type=` (the live variant) and
  `type_def=` (the file), and a declared enum/float is refused before the DLL is asked.
* *Checks:* native — runtime and type gates, and **the field's 635 cases replayed through the new gates: 635 of 635 pass** (612
  hibernated uint + 23 awake uint; ≥ 90 % required) **[native]**; agent — the reply parser byte for byte, the hibernated result counted
  as applied, uint safe and enums not, the State-type index on the smith's file, the 635 cases through the agent's gate **[unit]**.
  A synthetic mismatch on a loaded quest: see the live section.

### T3 — the `neither` case [code][syn]
* **Root cause by structure (Phase 0 + this reading):** while the player's request waits (and during the conversation), the
  copy is still **written by the host's stream every frame** (the native writer is bound; WO-157's "talk free" only released its
  stance and paused the NPC-state placement) — and the host's NPC goes on with its work until the conversation has started (the
  host holds its NPC only then, WO-144 1.3). The copy is dragged along with the host's NPC and never turns to the player; the
  engine's dialogue never leaves the request (blacksmith, villager) or, for a forced haggle, waits for its twin until the dialogue
  controller's "NPC pause request" times out (`WAITING_FOR_TWINS`, the field's haggle).
* **`mp_talk_resume_first`** (default **on**): (1) a press on a **paused** copy resumes it first and passes the press on **200 ms**
  later (`WO166-TALK resume-first npc=… delay_ms=200`); (2) the stream **does not move** a copy while its request waits (up to 6 s) or
  while its conversation (or a forced one) runs: the puppet tick returns before any write and the native writer is held, renewed
  once a second (`WO166-TALK hold npc=…`); the copy stays resumed (the pause path already skips a talking copy).
* At every talk that never started the Lua sends the copy's facts and the agent adds the engine's: `WO166-TALK timeout npc=…
  state=<last engine dialogue state for that copy | request-only> pause_requests=<timed-out|-> waited_s=… placed=… paused=…
  copy_dialog=… dead=… resumed_for_talk=… stream=held|native-driven|lua-driven via=… retried=…`.
* *Checks:* Lua R1–R11 (resume first, the 200 ms press, the hold and its 6 s end, the switch, the facts) **[syn]**; agent: the state
  line parser **[unit]**. Whether the blacksmith now answers: **[needs two players]** (scripted talks do not start a request at all —
  WO-164).

### T4 — `busy` says so [code][syn]
* A press on a copy whose host NPC is talking to the host (`w160.conv`, the host's own talk) or fought in the last 3 s (the host's
  combat event) sends no request: "They're busy with your partner." (`WO166-TALK busy npc=… why=host-talking|host-fighting`). *Checks:*
  Lua B1–B3 **[syn]**.

### T5 — haggling
Rides on T1–T3: the haggle is a forced conversation; with T3 its copy is held still and resumed for it. `kind=haggle` keeps its own
WO-164 line. **[needs two players]**.

### Phase 1 gates (offline)
Lua: the new suite **45/45**; WO-165's 11/11 unchanged. Native: **625** (0.48.0: 593). Agent: **1,329** (0.48.0: 1,320).

## Phase 2 — enemies that fight the joiner (built)

### C1 — host NPCs attack the joiner's figure [code][native] — the live A/B decides the default
* Phase 0.5 points at our own switch: while the joiner's combat bit is set, `motion.cpp apply_combat` turns **all four** of the
  figure's combat automations off (`combat_EnableAutomation(false)`); in that state no NPC ever committed an attack on it in the
  field, while the WO-165 harness (figure not in combat, automation on) took 369 real guard blows in 9 minutes.
* Read in CombatModule this session: the automation switch calls four setters of the actor's automation manager — two gated by
  the command's bytes `+0x7A` and `+0x79`, one by the enable flag alone, one with `+0x7B` as an argument (the module's own classes:
  `C_CombatAutomationAttack / Defense / Guard / ZoneChange / Weapons / Director …`).
* Built: a lever for the figure's pattern while held in combat (`wo166::auto_mode`: 0 = all off as 0.48.0, 1 = all left on, 2–9 =
  the enable flag with the byte patterns); **NPC copies are always all off** (a copy never acts on its own). Console
  `mp_w166_automode <n>` (a test tool); the agent pushes the mode with the other WO-166 switches every 10 s. The default is set
  from the live A/B (below): the guard's attack rate on the figure per pattern, and the figure's own committed actions
  (`cap_ours`) — a pattern that lets the figure attack on its own is never the default.

### C2 — real swings are shown, not dropped [code][unit]
* 0.4 showed the 29 "stale" blows had **no row at all** on the host. The rule now takes the host's word: a verdict whose host
  captured no swing (`SwingKnown` clear) is `no-swing-captured`, never `row-stale` (an older row of that NPC is another blow's); the
  generic lunge still plays before its damage. A row the host did pair is accepted late up to **its own start+hit time + 0.5 s +
  the measured one-way latency** (half the clock's median round trip, which is already a median of the last samples), early within
  the WO-163 tolerance.
* *Checks:* the OCE joiner's 46 blows through the new rule: **row-stale 0 of 46** (field 29, 63 %), 15 shown with their row, 31 with
  the generic lunge **[unit]**; the late-row bound with and without latency **[unit]**. The synthetic fight with 150 ms added delay:
  see the live section.

### C3 — copies count as striking (Option B) [code][native][unit] — `mp_copy_strikes`, default **on**
* Joiner: every host row played on a copy (`MP-ACTION … dispatch=native-row` ok) also asks the DLL for that swing's **striking
  window**: from the row's own `attack_time_to_start` until its hit + 150 ms (no timings: 250 ms → +450 ms). At the window's start
  (main thread, the copy looked up that frame) the copy's model State is read; only from **Idle or Guard** (never over the engine's
  own Hit / PreparingToParry / ParryInPlace / Dodge) the four attack fields are written (AttackType/Zone from the row, strength 1.0,
  the right hand) and State := **Striking (8)** — each property block checked by its own name first (no setter, no listener). At the
  end the prior State goes back **only if the model still reads our Striking**, and the prior fields always. A swing that arrives
  inside an open window extends it with the new row's fields. `WO166-STRIKE npc=… window=… begin|mid|end …` (the first 30 windows,
  then every 50th); the mid read logs the copy's state and **the local player's opponent** (`player_opponent=this-copy|another|none`).
* **Double damage guard:** the copy is on the WO-132 discard list for its window + 1.5 s (added only if it was not already, removed
  only if this module added it): any engine blow it lands on the local player is measured and put back, and counted
  `WO166-LOCALHIT dropped attacker_eid=… striking=1` — the host's verdict stays the only damage. `mp_victim_decides` is **off** again
  (below), so the replay never applies a blow either.
* **No silent guard:** a failed write after the block named itself switches C3 off for the session (`WO166-STRIKE switched OFF …`),
  the next request is refused `switched-off`, and the agent says it on screen once.
* *Checks:* native rules (the window, the states it may and may not overwrite, the restore rule, the field ranges) **[native]**;
  agent (the request from a row, refused rows) **[unit]**; the switch **[syn]**. The unattended checks (Striking read mid-window,
  restored after, the player's opponent, 0 faults over 200 windows, LOCALHIT = the copies' would-be hits, `applied=dup` 0): see the
  live section.

### `mp_victim_decides` — **off** again
The WO: "stays off (its proof needs a human holding block)". The second 0.48.0 build had shipped it on under the maintainer's
"new mechanisms ship on" rule (WO-165); this WO names that existing switch explicitly, and its C3 design has the host decide the
outcome (the local engine's blow put back), which C2's engine-applied path would contradict. So 0.48.2 ships it **off**
(`mp_victim_decides on` turns it back on; the help says so). Recorded for the next attended session.

### C4 — fight snapping, from the field [L-field][code] — `mp_snap_fix`, default **on**
`tools/wo118/snapcause166.py` over the five 0.48.0 joiner native logs (counts only): 1,288 fight windows, 17 excluded (a step over
20 m), **212 with a resume ≥ 50 cm**:

| cause (WO-161 0.2) | rule (the DLL's own lines in the window) | windows | share |
|---|---|---|---|
| A combat state applied on arrival | the copy's combat state changed in the window while no whole-swing hold ran | 0 | 0 % |
| B pose / placement (swing on arrival) | a large resume after a short hold (< 900 ms): the body moved during it | 26 | 12 % |
| **C hold-resume** | a hold of a swing or longer (≥ 900 ms) ended in the large resume | **186** | **88 %** |

The field's fight-window holds ran **p50 1.0–1.7 s, p90 3.9–4.0 s** (the 900 ms swing hold renewed by every next swing while the
host's NPC moved on). C is the largest share (≥ 50 %): fixed. With `mp_snap_fix` (default on): the writer's hold for a swing is the
row's own start+hit + 250 ms (300–900 ms; no timings: 900 as before), and **one stretch of chained holds is capped at 1.2 s**, then
the writer catches up (the existing blend) for 0.35 s before the next hold may take the body (`npc_drive.cpp`; capped / skipped
counts in its status). Kept on only if the synthetic fight does not regress: see the live section.

### C5 — the lock-on's not-set line [native]
`WO165-LOCK … pair=not-set` once per NPC per minute (`wo166_rules.h LineLimiter`, the field's 417 lines in 104 s → 2) **[native]**.

### Phase 2 gates (offline)
Agent **1,335**; Lua WO-166 suite **51**, WO-165 suite 11 (its default check updated for victim-decides off); native **625**; the
native guard check clean.
