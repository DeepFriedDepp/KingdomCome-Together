# WO-191 — Hotfix: a relaunched joiner's moves count, fighting copies are not reset, attackers have a body, no 20 s black (0.60.7)

Code: `dotnet/KcdMp.Client/Wo191.cs` (the rules), `GameBridge.Wo191.cs` (the agent), `ActionChannel.cs` (the receiver), `kdcmp.lua` (the
fight gate, the attacker pin, the switches). No native change. Tests: `Wo191Tests` (rules, per item), `Wo191ReplayTests` (both 0.60.5
bundles), `tools/Test-WO191Synthetic.lua` (the mod's half, 39 checks); live harness `tools/wo118/run191.py`.

Clocks: the host's agent log is the reference; a joiner line's own `off=` (+3.9 to +4.1 s) puts it on the host's clock.

## 1. The joiner's moves refused as "stale" after a relaunch

**Evidence: confirmed.** The host's log has 134 `reject=StaleOrDuplicate` and 37 accepted Attacks, all from ghost 1. The accepted Attacks
carry seq 1–16 (21:19–21:26) and then seq 17–37 (from 23:07:15): in the joiner's last run its first 16 attacks (23:02:46–23:07:10, all
`gen=1.1.0`) were each refused 4 s later on the host, and the 17th was the first above the old run's 16. The 23:03 fight's five blows on
`ttac_smolik` were applied as `fallback=no-swing-row` (5 of 5). The same refusals hit every kind: in the last three runs 3 jumps, 3 door
asks, 2 emotes and the once-a-minute NPC resync asks. The host saw 10 disconnects and 11 connects; the joiner's mirrors show starts at 21:45, 21:53, 22:05,
22:30, 22:32 and 22:36.

**Cause** (as the WO says): every agent run starts its outbox at 1.0.0 and bumps the epoch once on connect, so every run sends `1.1.0` with
its sequences back at 0; `ActionInbox.Forget` existed but nothing called it, so the host kept the old run's last sequence per kind.

**Fix.**
- 1a the receiver forgets a sender when it disconnects, when the relay names the id again, and when that player joins (`mp_action_forget`).
- 1b each agent run picks a run id (1–255) and sends it in `ActionGen.Revision`, the header byte reserved since WO-100.5 (no layout change;
  the relay forwards it as before). A different run is always adopted; the epoch compare is now wrap-safe. A sender older than 0.60.7
  sends 0 and relies on 1a (the relay's release check keeps mixed builds apart anyway).
- 1c `WO191-ACTION rejected kind=<k> why=<w> sender= seq= held_seq= gen= total= since_last_line=`, once per kind per minute; the summary
  line gains `rejected_kinds=`, `forgotten=`, `runs_adopted=`.

**Replay** (the joiner's three logged runs rebuilt from its own send lines, the host's connects and disconnects, the receiver primed with
what the host held at 22:28): the 0.60.5 receiver refuses 46 (Attack 16, Jump 3, DoorAsk 3, Emote 2 — exactly the host's — and NpcResync 22;
the host's log has 47 after 22:28, NpcResync's earlier sequence is only a lower bound). The new receiver: **0 refused** with 1a alone,
with 1b alone, and with both (145 accepted).

**Live** (the game as host, a synthetic joiner beside a villager, `jhit` swings with hits; then the joiner process killed and started again,
the relay gives it id 1 again): run A 6/6 Attacks accepted (seq 1–6), run B — the relaunch — **6/6 accepted from seq 1**, 0
StaleOrDuplicate, `WO191-ACTION forgot sender=1 why=disconnected` between; W173: 13 hits, **0 `fallback=no-swing-row`** (one
`replay-refused` on run A's first blow, the rest paired: `applied=engine`).

## 2. A fighting copy reset to neutral

**Evidence: corrected.** `WO164-SWEEP ... trigger=focus` at 23:03:44.7 is written 6 s after the sweep (it reports `errors_after_6s`); the
sweep itself ran at **23:03:38.7** (kcd.log t=1689.468), 0.6 s before the joiner's first swing (23:03:39.3) and 1.3 s before the engagement
(23:03:40.0). Right before it the joiner's own log has `wo124_reply drawn=1 ... ttac_smolik:0.66:5.1` and `npc_target ttac_smolik`: the
joiner was walking up to it with a weapon drawn. So WO-183's gate (engaged, or in combat at that moment) had nothing to see yet, and the
DLL's half (the loaded state made neutral, the activity hold released for 20 s) landed at the start of the fight. Sweeps in the joiner's
last run: focus 87, skip 60, hostconv 10, adaptive 4, retry 1.

**Fix.** One gate for every trigger, in the mod (`KCD2MP_W191Fighting`, before any release) and in the agent (before the DLL's
neutralise), and also on the context release before a placement (`KCD2MP_W160Release`). A sweep is refused while the copy is engaged
(WO-132, WO-147), in combat mode (the WO-187 guard hold sets it), its host NPC is in combat or targets a player, this player faces it within
4 m with a weapon drawn, or a blow or a swing went at it — and for 10 s after the last of these. The skip is logged once per copy per fight
(`WO191-SWEEP-LUA` / `WO191-SWEEP`). One exception: after the host's talk (`hostconv`, WO-167 T2) the copy's own combat flag is the stale
state T2 clears next, so that flag alone does not block it; every other sign does (`mp_sweep_fight_guard`). Two older checks follow the
new rule: WO-167 H7 (a copy whose host NPC fights is no longer swept) and WO-183 C2 (the skip line's wording).

**Replay:** the ttac_smolik sweep is **skipped** (`a-player-faces-it-armed`). Of the 162 sweeps in the joiner's kcd.log that sign alone
skips 1 (focus 1/87); the agent's fight signs (engagement, blows, the host's combat stream) are not in kcd.log and skip more.

**Live** (joiner role, a host copy 2 m in front of Henry, faced): calm, the focus sweep ran every 20 s (2 in 40 s); then the synthetic host
put it in combat against this player for 60 s: **no sweep**, one `WO191-SWEEP-LUA ... SKIPPED why=host-combat`.

## 3. An attacker with no body on the joiner's machine

**Evidence: confirmed.** The host: `ttac_dog_1` turned on the joiner's figure (`npc-combat out ... target=Avatar:1`, bites from 23:04:04.3,
blows 23:04:05.6–23:04:11.9). The joiner: `dispatch=dropped-no-entity (not a puppet here)` and `WO161-HIT ... reason=row-refused-no-entity
applied=yes` 4 times.

**3a, the cause.** `ttac_dog_1` is a village dog, entity class **Dog**. The host's NPC stream carries NPC, NPC_Female, Horse and the
encounter animals Wolf, WildDog and Boar (the native scan's class list and the mod's `w136.ANIMAL`); class Dog is in neither, so the host
never tracked it (no `NPC-SYNC tracking ttac_dog_1` in the whole session) and the joiner's own `ttac_dog_1` stayed paused as
"not-streamed" (`WO131-GUARD pause ... class=Dog why=not-streamed`). It joined the fight as `ttac_smolik`'s companion (the engine's
`SKIRMISH_SOULADDED_SOURCE_COMPANION`) and set its target on the joiner's figure about 15 s before its first bite.

**Fix.** 3b: the host pins an NPC or animal into the stream the moment the engine logs `[<npc>] Set target: kcd2mp_<N>` (any living class,
Dog included; kept 60 s after the last sign; logged `WO191-BIND pinned`); the joiner asks for any attacker it has no body for (an attack or
hit row, an `npc-combat` aimed at it, a blow) with an NpcResync of a new reason (6, the name after it; at most once per 2 s per NPC), and
the host pins it (`mp_attacker_bind`). 3c: a blow that still comes from an attacker with no body is counted and logged (`WO191-GHOST`); no
lunge is played on the joiner's own paused NPC of that name (it is not the host's copy). The scan's class list itself is unchanged:
streaming every village dog all the time is a larger change than this hotfix.

**Replay:** the joiner asks at 23:04:00.3, **1.3 s before** the first bite; the host's target line is at ≈23:03:49 (anchored by the
`ttac_smolik` death both logs have), **16.6 s before** its first blow.

**Live:** host role — `ttkc_dog_2` (class Dog, not tracked): `WO191-BIND pinned npc=ttkc_dog_2 class=Dog` → `NPC-SYNC tracking` → its first
`npc_state` (flag 128, not human) 28 ms later. Joiner role — a synthetic host's streamed `ttkc_dog_1` (class Dog) is bound as a copy, plays
**3/3 bites** natively and its **3/3 blows show** (`shown=yes`); an unstreamed `ttkc_dog_2` biting: `WO191-BIND ask` and **2/2
`WO191-GHOST`**. No animal fight was staged against the joiner's real body in a two-player session.

## 4. A 20-second black screen on the joiner

**Evidence: confirmed.** 22:45:09.0–22:45:29.1, `WO179-FADE on scene=-` … `held_ms=20109`, `Last fader took 20.10s`; before it the shop talk
with `ttac_man_4` (dialogue 164, 22:45:03.7–07.4).

**4a, the cause.** 1.5 s after that talk ended, a second request for the same shop dialogue (id 175) came from the game itself, outside the
mod's talk path (`WO167-TALK fallback-off id=175 ... a request no talk action made is the game's own`): no talk free, no resume of the copy,
which the trade hold still held. The dialogue stayed in `WAITING_FOR_TWINS` until the engine's own `PlayerDialogController::
NPCPauseRequests timed out after 20.014269` interrupted it; the fader was black the whole time. WO-179's 3.5 s bound for a conversation with
no twin (A3) never armed: its clock starts only on the mod's own talk ask, which this request never had.

**Fix.** 4b: on the joiner, a fader that belongs to no scene and shows nothing (no scene, no menu or trade screen, no running dialogue, no
cutscene, load, sleep, wait or fast travel, not dead) is ended after 3 s: a conversation of this player still waiting for its NPC is ended
the game's own way (`InterruptDialogs`, as WO-179 A3); otherwise the black is lifted the way WO-179 A1 lifts a stuck one. Logged
`WO191-FADER released after <s> s (<what held it>)`. Never on the host (`mp_fader_bound`).

**Replay:** the 22:45:09 fader shows nothing at 3 s (scene `-`, local state running) → released at 3.0 s; the game's own log names its
holder: conversation 175, never past WAITING_FOR_TWINS. The other faders over 3 s: 22:36:58 (a load: left alone), 23:34:31 (conversation
1272 waiting: released at 3.0 s instead of WO-179's 3.5 s).

**Live:** joiner role, the game's own fader put black with no scene (WO-184's test fade): `WO191-FADER released after 3.1 s`, the black
lasted 3,125 ms. A real shop talk needs the talk key (a scripted talk is not a key press): not run.

## 5. Tests

| what | result |
|---|---|
| unit (`Wo191Tests`, `ActionChannelTests`) | relaunch, reconnect, new incarnation after death, an older sender, wraparound (sequence and epoch), the reject line, the fight guard per trigger, the target line, the ask payload, the fader content and the waiting conversation: pass |
| .NET suites | agent 1,746 (with the replays), relay 63, setup 77, Farkle 59, log pack 14: all pass (WO-170's summary-line test follows the new fields) |
| mod suites | all 71 synthetic suites and static checks pass, the new `Test-WO191Synthetic` 39/39 |
| offline replays | items 1–4 as above (`Wo191ReplayTests`, 5 tests) |
| live L1–L4 | pass, as above |
| WO-187 echo test, input-free halves | copy side **10/10** moves (`result=ok`: 5 rows, 5 defences; combat mode held throughout), figure side **8/8** moves received and played. The echo with the player's own input needs real input: not run (unattended) |
| WO-187 shield windows | **12/12** swings at the joiner planned (12 perfect-block slots); the 12 at the host's figure not planned for the joiner |
| WO-184 save round with items | **pass**: 3 items picked up, host save paired 3.1 s, quit, relaunch, rejoin restored the paired snapshot, inventory equal (32 classes) |
| WO-189 auto-snapshot | **pass** (interval 1 min): paired 1.6 s, again a minute later |
| frame rate in town (Troskowitz) | the WO-191 switches on 75.6 / 82.7 fps → off 86.8 / 86.9 → on again 85.9 / 86.0 (60 s windows, the window left as it was): 0.95 overall, 0.99 for the second on phase; the DLL's own time 573–665 µs either way. No native change; a 0.60.6 agent was not run beside it |
| saves | the whole saves folder backed up byte-equal (958 files) before the first launch; afterwards 958/958 equal. Only playline4 was used; the four saves the tests made there were moved out |

Seen on the way, not from this WO: when a test agent is killed outright, the DLL's `wo137::set_send_callback` faults about 9,000 times
(guarded, counted, never switched off). WO-189's rig logs show the same; the field logs do not (29 faults in the host's whole session).

**Fixed after 0.60.7 (commit 20d5815, no version bump).** The site name was wrong: it is wo137's guarded read helper `rd`, now
labelled `wo137::rd`. Cause: `on_disconnect` posts `hud_on(false)`, and with the QuestModule not in the modules registry (never on 1.5.5)
`hud_on` searched for it first -- a brute-force scan of the game interface 0x400 bytes deep and one level down, every unreadable
candidate a counted fault. Off only undoes what on did (on found the module or set the vtable flag), so off no longer searches
(`wo137rules::hud_may_search`, 4 checks in `wo137_rules_tests`). Live (Modding Tools, playline4 quicksave022, relay + run165 agent,
three `run165.py stop agent` kills): no FAULT line, no `WO137-MODULES` dump, `faults=0 sites=0` on every FRAME line; playline4
byte-equal afterwards. Rig: release/w137rig. Native tests 9,525 pass.

## 6. Defaults

| item | switch | default |
|---|---|---|
| 1a forget a sender at disconnect / re-naming / join | `mp_action_forget` | on |
| 1b run id in the action header | (the wire) | always |
| 2 no sweep on a copy that fights | `mp_sweep_fight_guard` | on |
| 3 attacker pinned / asked for, ghost damage counted | `mp_attacker_bind` | on |
| 4 a fader with no content ends after 3 s (joiner) | `mp_fader_bound` | on |
