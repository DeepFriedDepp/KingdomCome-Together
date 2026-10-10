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
