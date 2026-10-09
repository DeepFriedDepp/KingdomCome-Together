# WO-164 — Joiner talk and haggle, the map-marker crash and a partner marker, the flee tug-of-war, sitting and unstuck, a state snapshot on every mark_odd, random events, the 0.47.0 session

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L]** live, on this machine's Modding Tools build, throwaway playline only; **[L-field]** counted from the testers'
logs on disk (matched by event or by game time, never by wall time across machines; no log line is copied, no player name,
address or path is written here — entity names such as `ttac_blacksmith` are the game's own); **[code]** read in our code;
**[unit]** / **[syn]** / **[native]** a test that gates the build; **[not tested]** no run; **[not determined]** looked for
and not found inside the box. The version is the maintainer's (**0.47.5**, given at the start of this run); no tag, nothing
uploaded.

This file is written phase by phase: a section is appended when its phase ends, so a cut-off session still leaves a
complete record.

## Inputs

All eleven bundles of the WO's table are on disk (copied from the maintainer's desktop into the git-ignored `logs/wo164/`,
one folder each: `j_wait`/`h_wait`, `j_crash`/`h_crash`, `j_talk`/`h_talk`, `j_mutt`/`h_mutt`, `j_imm`, `j_047`/`h_047`).
None is missing.

**Time base.** kcd.log has no wall clock. The mod's `[KCD2-MP-DATA] v2` rows carry the Lua clock `t`; the native log carries
the wall clock. In every joiner bundle one constant maps the two (wall = t + offset), fitted by matching each native
`WO141-APPLY … (exec 0` line to the planner refusal the same call printed in kcd.log for the same NPC (the refusal is printed
inside the call): j_wait 65,147.3 s, j_crash 63,639.6, j_talk 62,168.7, j_mutt 60,407.4, j_047 70,859.1 (60–116 votes each,
one value per session) **[L-field]**. The scripts that count (not copy) are in the session scratchpad; the method is above.

## Phase 0 — measure before building

### 0.1 Does an op reduce a copy's planner errors? — neither does; the "storm" is our own retry pace [L-field]

The WO's number 1 ("planner errors never fell after an op, 0 of 2,141") comes from the agent's `WO160-CTX … errors_before=…
errors_after=…` pair. **Both numbers are the same cumulative counter read 6 s apart, so `after < before` is impossible by
construction**; the 0 % is an artifact, not a measurement. Re-measured as a rate — that NPC's planner errors in the 60 s
before the op against the 60 s after it:

| Op | Bundles | n | fell | grew | same | no error in the 60 s after |
|---|---|---|---|---|---|---|
| `WO160-NEUTRAL` (native, loaded := the body) | wait bundle only | 96 | 15 (16 %) | 55 (57 %) | 26 | 31 |
| `WO160-CTX` (agent, a placement) | wait bundle only | 375 | 64 (17 %) | 110 (29 %) | 201 | 221 |
| `WO160-NEUTRAL` | four 0.46.5 joiner bundles | 826 | 155 (19 %) | 442 (54 %) | 229 | 269 |
| `WO160-CTX` | four 0.46.5 joiner bundles | 2,136 | 562 (26 %) | 820 (38 %) | 754 | 879 |
| `WO160-NEUTRAL` | 0.47.0 joiner | 1,501 | 222 (15 %) | 772 (51 %) | 507 | 553 |
| `WO160-CTX` | 0.47.0 joiner | 2,671 | 574 (21 %) | 1,093 (41 %) | 1,004 | 1,116 |

(The native NEUTRAL line is paced one per body per minute, so its rows are a sample of the ops, not all of them.)

**Why neither works — the cause found while measuring.** The gaps between one NPC's consecutive "Couldn't find actions"
lines cluster on exactly the reconcile's retry pace (`wo141_rules.h`: 1.5 s, 15 s, 60 s): 0.47.0 joiner, 2,614 refusals
on 120 NPCs: **1,121 gaps of 1–2.5 s, 714 of 10–20 s, 136 of 59–62 s**, the rest scattered; wait bundle 205 refusals:
93 / 60 / 0 **[L-field]**. Each refusal is printed inside our own `g_execLoaded` call (that is what the time base above is
fitted on). **The planner error storm is the mod asking, again and again, for a placement the planner already refused**;
NEUTRAL only repairs the loaded state between two such asks, and the next paced ask undoes it. The refused demands are
led by `Stance: sitting` (2,025 lines), `Unstance: housekeeper_eat` (970), `Stance: lying` (883), then work unstances
(woodchopping 419, home cooking 330, guard 312) **[L-field]** — a copy whose body sits (its own schedule) asked for the
host's "standing, bowl in hand", or the reverse.

**Decision for T1:** the op that clears a copy is (1) the game's own stance/unstance release (`wh_ai_NPCStateResetElement`
Stance + Unstance — the only two resets the game performs, WO-160 §1.1 [L]) so the body is free, then (2) NEUTRAL
(loaded := what the body now is), and (3) **no further ask of the refused activity for a hold period** — the copy is shown by
the position writer alone, exactly as WO-160's give-up does, but after the first refusal instead of after 8 refusals and
~70 s of errors. The solo acceptance (errors per minute, the fraction of swept copies whose errors fall) is in Phase B.

### 0.2 Element kinds in the planner's state text, and which reset [L-field] [L, WO-160]

Kinds seen in the joiner logs' `Current state` / `Required state` texts (occurrences across the five joiner bundles):
AddContext 29,001 · ChangeAreaLabel 23,644 · EnableRoleOnHub 10,732 · AddLink 9,091 · RightHand 7,192 · Unstance 7,118 ·
LeftHand 7,035 · Stance 3,525 · EnableBehaviorForNPC 2,980 · ChangeBehaviorState 2,562 · OpenShop 2,556 · ItemSetAside
2,240 · **AddMetarole 1,502** · ChangeEquipment 756 · **AddBuff 659**. The two in bold are not in WO-160's list of the
15 names the reset knows.

`wh_ai_NPCStateResetElement` was asked live for each of its 15 names in WO-160 (§1.1): **only `Stance` and `Unstance`
reset anything**; the other 13 print `Unsupported element type to reset!` (the mod's own line used to say ok for LeftHand
/ RightHand). Not repeated here: the result is a disassembly of the one handler plus a live run, and no newer build of the
game has been installed since. AddMetarole and AddBuff are not among the handler's names (an unknown name answers
`Unknown element type`) **[code, WO-160 disasm]**. So "reset every accepted element" (the WO's candidate ii) is the same
op as candidate i plus the stance/unstance pair; T1 uses exactly that.

### 0.3 The dialog-request cancel [L-field] [not determined: a script route]

* The engine prints `Canceling dialog request id N from soul '…'. Request timed out.` in two cases: its own 20 s timeout,
  and **when the same soul makes a new request** — "Soul '…' already requested dialog. Timing out due to player request has
  higher priority." followed at once by the cancel and the new id (wait bundle t=432.2: the request to `ttac_man_1`
  cancelled when the player turned to `ttac_vojka`, the new id assigned in the same frame) **[L-field]**.
* So **the player's slot does not stay jammed across NPCs**: a request to a second NPC cancels the first. What does block
  is a second E on the **same** pending request ("Request is now in progress", 20+ in a row for request 90) — the engine
  ignores those, and our OnTalk hook ran free/resume/pause churn for some of them.
* The failing talks after the wait were each a fresh request that the NPC never joined (all 7 `never-started`), including
  Manka and Procek with no planner error in their window. That is the third cause the WO anticipated; it is not the slot.
* **No script API to cancel a pending request was found** (the mod's Lua has no binding by that name in the Modding Tools
  script-bind docs; the engine's cancel runs inside DialogModule on its own timeout or on a newer request). The route that
  exists — and that the engine itself uses — is "a new request from the player supersedes the old one". T3 therefore
  re-issues the talk once through the game's own request path instead of cancelling natively.

Per talk in the field (the T0 rule applied offline: planner errors of that NPC in the 10 s before the request and during
it; `never-started` = the engine's 20 s timeout or a newer request superseded it):

| Bundle | ok | failed, with planner errors (torn copy) | failed, no errors (neither) |
|---|---|---|---|
| 0.47.0 joiner | 11 | 6 | 0 |
| Mutt joiner (0.46.5) | 9 | 5 | 5 |
| talk joiner (0.46.5) | 4 | 0 | 1 |
| wait joiner (0.46.5) | 0 | 4 | 3 |

Successful talks also show 3–6 planner errors while they run (the release at the talk asks the planner once): the count
alone does not separate torn from healthy; T0 records it with the request age and the open-request count.

### 0.4 What holds a fleeing enemy in combat [L-field] [code]

Lackey pair (`j_crash` / `h_crash`):

| Mod-side action | Where | Count | Has a removal? |
|---|---|---|---|
| `combat_forcedTarget` relation (WO-136 retarget) | host | 9 set, cleared by the WO-136 read-back (`clear relation … readback=false` after 21–35 s) | yes (WO-136's own clear) |
| `WO136-TARGET` leave + re-add | host | 14 | yes (same) |
| WO-147 hostile engage (`engaged=`) | both | **engaged=0 the whole session** (stats every minute) | n/a |
| `skirmish_remove` (WO-147 leave) | both | 12 joiner / 16 host | it is the removal |
| WO-141 apply of the host's `FleeLookingAround` with the weapon in the right hand | joiner | 97 in one minute | **no** — re-applied every ~1.5 s while the one-shot ends |
| Lua `re-asserted sheathed (local brain fought back)` | joiner | 118 | **no** — every 1.5 s per copy |

The joiner's "stuck in combat" is therefore not a mod-held skirmish pair (engaged stayed 0): it is the copy itself, kept
with a drawn weapon by the WO-141 apply (the host's fleeing bandit holds his sword) and holstered by the Lua 1.5 s later,
260 weapon-collision re-creations in the session. D1 removes the tug-of-war; D2 adds the watchdog over what the mod does
create (forced targets, engage entries, skirmish pairs) so no mod hold can outlive a flee.

### 0.5 The map object, and when a marker may be added [L-field] [code]

* Route (`respawn_actions.cpp`): `ui_map()` = the game's own route (C_ShowMapMarker's getter of the APSE UI object, then its
  slot 0x50) else a walk of the GUI module's element vector for a `C_UIApse`; the mark is created with `C_UIMap` slot 0x50
  and added with slot 0x58 **[code]**.
* The crash bundle: the host's mirror grave (`MirrorGrave op=1`) reached the joiner **51 s before the DLL's first look at the
  world** (`MP-GRAVE world changed (first look after install)`), i.e. during the join's own load; the map object was found,
  its add read a null inside GUIModule and was swallowed; at the next look the map held **1 mark** (the half-added one) and
  after the host world's load **23** **[L-field]**. A host's map holds 21–22 marks in the other bundles.
* **Readiness test used by C1:** (a) the grave rescan has run for the current world (the "world changed" look), (b) the
  map object is found, (c) its mark vector is readable and holds at least one of the game's own marks (a loaded world always
  has quest/POI marks; 0 means the map's content is not built), (d) at least 5 s since that look. Until then a marker is
  queued. The first open of the map is not observable from the DLL without a new hook **[not determined]**; (c) stands in.
* Mark types available (GUIModule's own type→name switch, already in the code comment): 0 Checkpoint (the player's single
  waypoint), 1–3 Main/Side/Micro quests, 0x2B Grave, 0x2D ConcCross, 0x30 GeneralPoi. The partner marker uses **0x30
  GeneralPoi** (the grave keeps 0x2B), never the Checkpoint (that is the player's own waypoint).

### 0.6 What `no port` means, and the direct write [code] [L-field]

* A quest State is a `C_StateVariable` node; its value is an rttr variant at node+0x68; the game changes it only by firing
  one of the node's **Set ports** (the port's trigger runs the setter, which writes +0x68 and then calls slot 42, the
  change notification that WO-137 hooks). The joiner applies a host change by firing **the port the host's change came
  through**; a checkpoint mismatch is corrected by a port the agent has **seen produce that value** (WO-147's port memory).
  `no port` = the host's change (or the value) came through no port the joiner knows to produce it: an AI behaviour's
  worker-thread change, or a counter whose ports add one (`numberOfMealsIgnazHasEaten`).
* **A direct write exists:** write the variant's value at +0x68 and call slot 42 (old, new, notify) — exactly what the setter
  does after its port. For an `int` or `bool` variant the value is stored inline and the type does not change, so the
  write is the setter's own effect without a port. Enum-typed States (`Progress`, `Tribool`, `QuestProgress`, …) are
  **excluded**: their underlying width and range are not read here, and a value outside an enum is the corruption the WO's
  guard names. Counted in the 0.47.0 joiner log: **2 `no port` variables**, both `int` counters —
  `ignaz_logic.numberOfMealsIgnazHasEaten` (joiner 2, host 1; 7 checkpoints, 20:31→20:38 wall, ~7 min, until the quest step
  ended) and `resolvedbanditscounter.state3` (4 vs 3, once, at the session's end) **[L-field]**. Both are in the safe set.

### 0.7 The three dead huntsman talks (0.47.0 joiner) [L-field]

| Joiner t | What the quest was doing (host changes applied on the joiner in the same minute) | Quest mismatches at that moment | Cause |
|---|---|---|---|
| 3750 (requested at 3730, timed out after 20 s) | the host was in the huntsman's own dialogue: "the host holds tvid_huntsman for this conversation (busy on the host)" 25 s earlier, then `drinkAccepted`, `talkToHunterFall SetDone` from the host's talk | **0** (checkpoint 5/5 at the same minute: 0 mismatches) | the NPC was in a conversation with the host |
| 4192 | `hunterCarriable` → `pickUpHunter SetDone` → `hunterIsCarried SetTrue` (20:43:50 wall): the host was **carrying the huntsman** | **0** (checkpoints 0; the only later mismatch, `carryHunterPub`, was corrected by port) | the NPC was on the host's shoulders |
| 4220 | same (carried) | 0 | same |

The dialogue library and condition the game would have offered were not read (the game's dialogue data is packed; no
unpacked copy is on this machine) **[not determined]**. **The baseline T6 is judged against therefore does not show a
quest-value cause for these three**: they were the NPC being busy (talking with the host, then carried). T6 is still built
(the `no port` drift is real and lasted 7 minutes on the Mutt bait), and T0 now names the case: `host_busy=1` when the host
holds that NPC for its own talk.

## Phases A–G — what was built (the live results follow in "Solo live")

Every item below is gated by tests: **[unit]** `dotnet/KcdMp.Client.Tests/Wo164Tests.cs` (22), **[syn]**
`tools/Test-WO164Synthetic.lua` (58, the real kdcmp.lua under MoonSharp), **[native]** `native/tests/wo141_rules_tests.cpp`
(+3). The older suites that pinned the changed behaviour were updated where the change is the point (two timing lines in
`Test-WO137Synthetic.lua`: a press under 3 s after a talk to the same copy is now not passed on — T2).

### A — instrument
* **M** — `mark_odd` ("Something's wrong here") makes a mark id (10 × [a-z0-9]); the agent writes `MP-MARK-SNAP mark=<id> agent …`
  (≤ 60 lines, scrubbed of paths, addresses and e-mail: role, build, open talk requests and the last 5 outcomes, sweep counts,
  the copies with planner errors in the last 30 s and their host activity, every avatar's wanted vs body activity (the DLL's
  read), the engaged copies with ages, current quest mismatches, the map-marker state, the activity counters), the Lua writes
  `MP-MARK-SNAP mark=<id> lua …` (≤ 40 lines: game time, position, in-dialogue / in-combat, leash and join hold, talks, the 5
  nearest copies within 25 m with paused / dead / flee / last sweep, the avatars, random-event actors within 40 m with their
  owner, the last 10 toasts, fps over 5 s and the largest frame gap), and a new append-only join message **0x74/0x75 `W164`
  kind 1 MarkPing** makes the other machine write its own two blocks under the same id (the host passes a joiner's ping on).
  [unit] [syn]
* **T0** — `WO164-TALK npc=… kind=talk via=… placed=… paused_before=… planner_err_10s=… open_requests=… oldest_request_s=…
  quest_mismatch=… first_mismatch=… host_busy=…` at the ask, and `WO164-TALK npc=… kind=… -> started_ms=… | ended why=…
  commands=… cause=busy|preempted|quest|torn|wedged|neither preempted_by=… …` at the end (kind from the dialogue's own name:
  haggle / chat / dice / quest / other; commands = the engine's `DialogCommand-*` lines of that dialogue id). [unit]

### B — talk, trade, haggle
* **T1** — the sweep = the game's stance/unstance release + native **WO-141 op 9** (loaded := the body, the host's refused
  activity not asked again for a hold). Triggers: after a skip (replaces WO-160's re-placement: every copy within 300 m,
  nearest first, 4 per tick, a 0.6–5 s frame gap waits), adaptive (5 refusals in 10 s, the agent), focus (the copy faced within
  4 m, 0.5 s cadence), retry (T3). `WO164-SWEEP npc=… trigger=… errors_before=… errors_after_6s=…`. **And the cause found in
  0.1 is cut at the source: the reconcile now gives a refused activity up after 3 refusals (was 8)**. [syn] [native]
* **T2** — a press to a copy whose talk ended under 3 s ago is not passed on ("Wait a moment…" once); presses while a request
  is open were already ignored by the engine and by the WO-137 entry (no churn). [syn]
* **T3** — a request open 4 s: the player soul's `RestrictDialog(true/false)` (the script-bind docs: "deletes all unfinished
  requests"; restricted-again is checked and undone), the copy swept, the same game action re-run once 300 ms later; never a
  second time; then "Try again in a moment." [syn]; whether the engine honours the cancel is in "Solo live".
* **T4** — no own code: haggles are classified (`kind=haggle`) and get T1–T3.
* **T5** — the copy's soul `RestrictDialog(true/false)` before its talk is resumed (its own unfinished greeting / bark
  requests deleted); the agent records `preempted_by=` from the engine's "Running dialogue … with soul '<copy>'" lines. [syn]
* **T6** — native **WO-137 op 9 SetValue** writes an `int` / `bool` quest State directly (the variant at +0x68, then slot 42
  with old/new/notify, inside the apply depth so it is a mirror; read back, undone if it does not read as written; any other
  type answers `type-refused`); the agent writes a `no port` mismatch that stood 10 s, and every current one before a talk
  (`WO164-QFIX var=… joiner=… host=… applied=…`). Never on the host. [unit] [native build]

### C — the map
* **C1** — `mark_add` waits until the map is ready (the world looked at 5 s ago, the map object found, ≥ 1 of the game's own
  marks in it); a grave / mirror with no mark is retried by the 100 ms guard (one per entity); a fault in the add removes the
  half-added mark with the map's own remove, turns markers **off for the session**, logs `WO164-MAPMARK FAULT site=C_UIMap::add`,
  and the agent shows "Map markers are off this session (a game error)." once (the WO-151 status line carries `markers=`).
  [native build]
* **C2** — **not built** (pocket list): the safe form (a hidden marker entity of our own, moved every 2 s) needs its own
  load-safe lifetime; a mark on the avatar entity itself would hold a raw pointer that a reload destroys (the WO-113 crash).

### D — fleeing enemies
* **D1** — Lua: one weapon re-assert per 2 s at most, none while the host's NPC flees (the agent reads the host row's unstance
  name through native **WO-141 op 10**), 20 s of rest after 3 in 10 s (`WO164-FLEE npc=… backoff`); agent: a fleeing copy is
  given no hand tool. [syn]
* **D2** — the agent's watchdog over the holds the mod makes (WO-132 and WO-147 engagements): older than 45 s, the enemy's
  host row in flee for 20 s → released (`WO164-FLEE disengage`). Forced targets (WO-136) already clear themselves (0.4). [unit]

### E — sitting and unstuck
* **S1** — native: an avatar's refused seat logs `-> FAILED` (exec 0, body not in step), and after 3 the figure stands beside
  the seat (`WO164-SIT body=… apply=failed obj=… fallback=stand`), kept 120 s while the same seat is asked again. [native]
* **S2** — agent: a peer's stream leaving its seat → 1.5 s later the avatar body is read; still seated → Leave + release
  (`WO164-SIT cleared why=stale`); a (re)joining peer's figure is cleared first (`why=rejoin`).
* **S3** — `mp_unstuck` step 1 also resets this player's stance and unstance (the same game reset, on his own body); a second
  press within 10 s asks the agent to place him beside his partner (the leash's own placement). [syn]
* **S4 / WA** — `WO164-SITSTATE enter|leave obj=…` and `WO164-USE enter|leave obj=… kind=<unstance name|minigame-N>` from the
  DLL's capture of this player.

### F — random events
* **R0** (Mutt pair) [L-field]: the host streams `dummyWanderer_horse_8` and its rider; on the joiner the rider copy is paused
  (35 pause re-asserts) and written by the native writer while the horse copy is a separate puppet (77 `idle` anims) — **the
  rider is never mounted on the horse copy** (no mount line for horse_8 on the joiner), so it moves at saddle height without a
  horse. Meanwhile the joiner's **own** random event used the same rider name on another horse ("starts mounting horse
  'dummyWanderer_horse_7' instantly"). R1 removes the second cause; the first is R2 (pocketed).
* **R1** — on a joiner in a session the Lua sets the game's `wh_pl_RandomEventsAutoSpawnEnabled 0` (WO-145's census found it;
  it gates the trigger-area spawns) and puts the old value back when the session ends; toggle `mp_joiner_events` (on = the
  joiner's own events off). [syn]

### G — the 0.47.0 additions
* **TR** — the host's torch never reached the joiner through the state block (0 `MP-W136 peer` lines on the joiner in 0.47.0,
  2 in the Mutt joiner; the host's packets came at the 2 s position heartbeat while it stood with the torch out, i.e. without
  the 1 s state-block heartbeat a set torch bit forces) — the exact cause is **[not determined]**. Built: a reliable
  side-channel, `W164` kind 2 Torch `"<ghost> 1|0"`, on every local torch edge and every 10 s while lit; the receiver applies
  an edge the block did not already show (`MP-W136 peer N torch OUT … (side-channel)`). [unit]
* **RL** — "Your host is reloading the world - please wait" when the host starts a load, "Back with your host" at the rejoin,
  on the game's own HUD line.
* **N** (clothing on the wire), **ESC** (Ignatius) — **not built** (pocket list). **ID**, **SL** — not built (dropped first
  by the WO's order). **DI** — the minigame entity lines are logged (`WO164-TALK kind=minigame`); dice dialogues get
  `kind=dice`.

### Two changes made after the record above
* **The focus sweep does not reset the stance.** As first built, looking at a seated guest within 4 m would have stood him up for
  20 s (the release). The focus trigger now runs only the DLL's half (the loaded state := the body, so its brain can take a
  talk); the key press still releases the copy as before (WO-157's talk free). `released=no-focus` in `WO164-SWEEP-LUA`. [syn]
* **ID** — the agent's `MP-W143 stats` line ends with `idle_window in=… played=… refused=… refused_why=[…]` (the host's one-shot /
  idle rows since the last line, and why each refused one was: `not-active`, `oneshots-off`, `blocked-<why>`, `dll-refused`,
  `dll-no-answer`). [build]

## Solo live — not run (the maintainer's decision, 2026-10-09)

The run needs the Steam client, a throwaway playline copied into the saves folder (the maintainer's saves now hold only
`playline0`; the older playlines were moved to `OLD`), the installed `Mods\kdcmp` swapped and restored, and the Modding Tools
game, which takes the foreground at start and after a load. Asked before any of it, the maintainer chose **"Skip live, build
RC"**. So **no item of this WO was run in the game**: every result above is **[unit] / [syn] / [native]**, and the
TWO-PLAYER-CHECKLIST items 177–196 are the first live proof. What a solo run would have answered first, in order: does
`soul:RestrictDialog(true/false)` cancel the player's pending request in this build (T3); does the WO-137 op 9 write read back
on an `int` State and notify its consumers (T6); does `wh_pl_RandomEventsAutoSpawnEnabled` exist in the Modding Tools build
and stop `<RandomEvent> … starting` lines (R1 — `mp_joiner_events` ships **on** under the standing "ship new features on"
rule: it is a reversible cvar the Lua puts back at the session's end, and its failure is a console error, not a fault);
does the sweep reduce a seated copy's refusals (T1: the WO's ≥ 80 % target against the 15–26 % of 0.1); the 15-minute soak
with 13 bodies; the frame rate in the three scenes.

## Gates

| Gate | Before (0.47.0) | Now | Mark |
|---|---|---|---|
| Agent unit tests (`KcdMp.Client.Tests`) | 1,258 | **1,280** (Wo164 22; Wo123's join-range pin widened to 0x75) | [unit] |
| Setup | 77 | 77 | [unit] |
| Farkle | 59 | 59 | [unit] |
| Relay round trip | 63 | 63 (the W164 row crosses the real relay like every JoinWire row) | [unit] |
| Native | 533 | **536** (give-up after 3, the sit fallback, the FAILED word) | [native] |
| Lua synthetic suites | 50 / 50 | **51 / 51** (WO-164: 59 checks; WO-137: two timing lines for T2) | [syn] |
| Static (no raw `__try`, console placeholders, Lua locals, WO-157) | pass | pass | [code] |
| Frame rate (WO-148 rule) | not measured | **not measured** (no game run) | [not tested] |

## Pocket list (outside this WO, or put out of reach by its probes)

* **C2 — a map pin for the partner.** Built safely it is a marker on a hidden entity of the mod's own (no model, NO_SAVE),
  moved every 2 s and removed before any load or leave; a mark on the avatar entity itself holds a raw linkable pointer that a
  reload destroys (the WO-113 map crash). The C1 gate (queue, readiness, the disable) is what it would ride on.
* **N — NPC clothing on the wire** (the herbalist in night clothes on the joiner): the host's equipment element (slot 4, the
  `ChangeEquipment … sleepUnequip` text) as an outfit id on the WO-141 wire, applied through the WO-144 dress path after a
  sweep. Needs its own native read and a live check of the dress path on a paused copy.
* **ESC — Ignatius following the joiner.** The host's `SetFollowsPlayer` / `SetLiesDown` refusal stands; the probe (can the
  game's follow behaviour target an avatar's soul?) needs a live session. T6 now corrects the `numberOfMealsIgnazHasEaten`
  drift itself; the port attribution of that counter is Part B.
* **R2 — a rider and its horse as one unit** (R0: the rider copy is never mounted on the horse copy on the joiner).
* **TR's root cause** — why the host's torch bit never reached the joiner through the state block (the side-channel works
  around it; the block path stays as it was).
* **D2's distance clause** (> 30 m from every player) — the agent has no copy positions; the watchdog uses the host's flee only.
* **SL** — the non-initiator's sleep screen and the rest top-up timing.
* **The "neither" talk cause** — Manka and Procek (wait bundle) failed with no planner error and no other request open; T0's
  `cause=neither` plus the mark snapshot is how the next round names it.
* **"Carry him to the tavern completes at a birch tree"** — needs the host's quest trace of that step.
* **The lackey fight's sync** (Part B set pieces) and **enemies' copies fighting by their own brain** (the `re-asserted sheathed
  (local brain fought back)` lines are D1's symptom, not its cause).
* Noted, not this WO's: WO-163's `wo137::set_send_callback` read-fault flood on a loaded world (guarded, counted).

## The 0.47.5 build

From a fresh clone of `1c17cb6` (`release\c0475`) with the three git-ignored start saves (sha1 checked equal), `tools\Build-Installer.ps1
-SoakWaiver "The maintainer's standing rule, stated in WO-161 on 2026-10-08 and applied to WO-164 on 2026-10-09: release candidates are
built without the soak test (0.47.5)."` The header says **0.47.5**. **`release\KingdomComeTogether-Setup-0.47.5.exe`, 106,396,246 bytes, sha256
`3d017566532723dd60322336e7d69c9253a58b1ccd7bcb8a6b8ed76bc2099990`.** Local only; not tagged; not pushed; **unsigned** (no signing
settings). Transcript `release\BUILD-0.47.5.log`, waiver `release\SOAK-WAIVED-0.47.5.txt`. Inside the build: relay 63, agent 1,280, setup 77,
native 536, **all 51 synthetic suites**, the static checks, the installer cases, the payload smoke (`protocol=v10 release=0.47.5`); no
FAIL line; no user or machine name in the payload. `mp_joiner_events` ships **on** (the maintainer's choice, 2026-10-09, when asked:
the WO's solo run that would have decided it was skipped).
