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
