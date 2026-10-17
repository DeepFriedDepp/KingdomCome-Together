# WO-197 — The animation-sync release (0.61.0)

Code: native `wo197.cpp` / `wo197_rules.h` (the recoil and clinch polls, the paired play, StartSyncGuard on a pair, EnsureGuardAction,
the Part C category switches), `wo187.cpp` (the combat director reader), `wo166.cpp` (A6's takeover), `wo190.cpp` (the trace's director
field), `wo141.cpp` (a changed activity applied at once), `motion.cpp` (a polled local action); agent `Wo197.cs` (the rules) and
`GameBridge.Wo197.cs`, with hooks in `GameBridge.Wo121.cs`, `GameBridge.Wo132.cs`, `GameBridge.Wo187.cs`, `GameBridge.cs` and
`ActionRowCatalog.cs` (three more tables: the victim halves, the clinch moves, the clinch guards); protocol `ActionKind.AvatarRecoil` (19),
`ActionKind.AvatarClinch` (20), `PlayerStateFlagEngineKnockout` (0x08), DLL ops 65–69; `kdcmp.lua` (the switches, the figure's knockout,
the doors' outcome lines). Tests: native `wo197_tests`, agent `Wo197Tests`. Harness (never shipped): `tools/wo118/run197.py`,
`batch197.py`, `rec197.py`, `parity197b.py`, `parity197c.py`, `lock197.py`, `live195b.py c2w`; synthpeer verbs `fmove recoil|precoil|clinchenter|clinchleave|whistle`,
`fclinch`, `pvit`.

All live evidence is one machine: the Modding Tools game as the joiner of a synthetic host (or the host of a synthetic joiner) through a
local relay, on the `playline4` throwaway (Troskowitz); the real-input sittings sent real keyboard and mouse input to the game in front.
The saves folder was backed up first. Nothing here is two-player evidence.

## 1. Status

| part | result | default |
|---|---|---|
| Part 0 — the baseline, and after | before (main `365133d`): every combat move shown, 22–40 ms median, p95 over 150 ms only for hit reactions and the clinch fallback, 0 gaps, 0 overflows, every `director=` `?`. After (this build): every move still shown, medians 15–44 ms, p95 within a few ms of before (hit reactions +3 … +15 ms), copywalk 60 → 80 %, `director=Guard` 7 of 7 (§2) | — |
| B0 the combat action reader | fixed: `act=` and `director=` fill in (a real NPC's duel trace: Attack / Block / Guard on 285 of 285 lines; a copy's takeover: `director=Guard` on 7 of 7, was `?`) | — |
| B1 a player's recoil | the watcher's side: 8 of 8 shown, 27 / 38 ms; the source: the game plays a recoil only for a blocked **stab** (its 8 failed-attack rows are all attack zone 5); the 6 real blocks were slashes, so nothing went out, as in vanilla. Live (real input, longsword stabs): 2 of 2 blocked stabs ran the engine's FailedAttack, went out as `AvatarRecoil` with its row and were played on the echo figure (§6) | `mp_anim_recoil` on |
| B2 the victim's half of a paired move | 10 of 10 victim halves shown, 31 / 37 ms (copy ⇄ figure, both ways) | `mp_anim_paired` on |
| B3 clinches, both halves | each clinch move: both rows in one step, 6 of 6 shown, 12 / 37 ms; the entry: the game's own clinch-start rows on both (StartSyncGuard on two puppets is refused by the engine); a real clinch's enter / leave went out and came back | `mp_anim_clinch` on |
| B4 a brawl knockout | the new bit and both its edges; the figure falls and lies as long as the partner lies; the get-up is a fresh figure 2 s after the wake, not the game's get-up animation (§3) | `mp_anim_knockout` on |
| B5 the copy's leftover, the engine's way | EnsureGuardAction found by its port notes (CombatModule+0x806A0, the same as WO-196's); 4 of 4 live calls: "a Guard runs"; the takeovers keep reading Guard, so it is asked only when the main action is not one | `mp_copy_guard_engine` on |
| B6 kicks, stops, master strikes | 10 of 12 rows shown (stops, combos and master strikes on the copy and on the figure, and their victim halves), 31 / 159 ms; the bare-handed kick on a sword-armed figure does not play (WO-187's known rule) | — |
| Part C — the everyday categories | measured on 7 minutes of a real host's town (§4): doors 100 %, seats and beds ~80 %, hand items ~50–70 %, NPC routines ~80 %, riding 4 of 4, emotes 10 of 10; below the 95 % line where the game's own planner refuses a placement | `mp_anim_seats`, `mp_anim_routines`, `mp_anim_work` on |
| Part D — the release checks | all PASS (§5): saves (scene pair, autosave, marker, wake, station lock), dead copies, stealth and sleepers, loot and locks after a reload, knockout wake, the shared cutscene and the save after it, the C1 round, the real-input echo test and shield windows, frame rate −2 … −3 % | — |

## 2. Part 0 — the baseline, and after

The parity measure is WO-190's: a source change is a row (or an activity, a door state) the watcher's body was sent; it is **shown** when
the watcher's trace has that fragment installed with priority 5 or more within 0.6 s and held at least 0.1 s (an activity: the DLL's own
apply reads what was wanted; a door: it moved, or already stood so); the delay is the engine's side on this machine (the row's arrival to
its install), read at 30 Hz. Pass line: 95 % shown, the 95th percentile at most 250 ms (combat 150 ms), no full-body gap over 0.25 s, no
queue overflow on a figure, and copies within twice the host's.

Before = main `365133d` (Part 0, measured before any change); after = this release's build (the committed tree at 0.61.0), the
same scenarios on the same save. Shown and delay as "share, median / p95".

| category | scenario | source changes | before | after | full-body gaps > 0.25 s | overflows |
|---|---|---|---|---|---|---|
| locomotion | figwalk (the partner walking) | — | glide 0.00 %, in place 0.03 % | glide 0.00 %, in place 0.13 % | 0 → 0 | 0 → 0 |
| locomotion | copywalk (a copy's starts and stops) | 20 | 60 % (12 gait changes) | 80 % (16) | 0 → 0 | 0 → 0 |
| combat | figfight (the figure's moves) | 8 | 100 %, 27 / 53 ms | 100 %, 25 / 39 ms | 0 → 0 | 0 → 0 |
| combat | copyfight | 10 | 100 %, 22 / 45 ms | 100 %, 15 / 45 ms | 0 → 0 | 0 → 0 |
| combat | copyduel (wind-ups, takeovers, shield) | 9 | 100 %, 37 / 45 ms; takeovers read `director=?` 7 of 7 | 100 %, 39 / 47 ms; `director=Guard` 7 of 7 | 0 → 0 | 0 → 0 |
| combat | opp (an opponent switching) | 18 | 100 %, 33 / 44 ms | 100 %, 27 / 40 ms | 0 → 0 | 0 → 0 |
| combat | clinch (WO-190's clinch rows) | 6 | 100 %, 27 / **450** ms | 100 %, 34 / **435** ms | 0 → 0 | 0 → 0 |
| combat | night (a bandit at hour 22) | 8 | 100 %, 39 / 47 ms | 100 %, 41 / 46 ms (re-run on a fresh load: by the batch's end its bandit was gone) | 0 → 0 | 0 → 0 |
| hit reactions | fighits | 8 | 100 %, 40 / **240** ms | 100 %, 44 / **255** ms | 0 → 0 | 0 → 0 |
| hit reactions | fighitsarmed | 8 | 100 %, 39 / **246** ms | 100 %, 36 / **249** ms | 0 → 0 | 0 → 0 |
| hit reactions | copyhits | 9 | 100 %, 26 / **257** ms | 100 %, 37 / **269** ms | 0 → 0 | 0 → 0 |
| NPC routines (held copies) | town, townwalk (every NPC within 30 m held) | — | — | — | 0 → 0 | 0 → 0 |
| B1 recoil (the watcher) | b1recoil | 8 | not built | 100 %, 27 / 38 ms | 0 | 0 |
| B2 victim halves | b2pair | 10 | not built (attacker half only) | 100 %, 31 / 37 ms | 0 | 0 |
| B3 clinch moves, both halves | b3clinch | 6 | not built | 100 %, 12 / 37 ms | 0 | 0 |
| B4 knockout | b4ko | 2 downs, 2 wakes | not built | 2 of 2 fall (ragdoll), 2 of 2 stand 2.0–2.1 s after the wake | — | 0 |
| B6 kicks, stops, master strikes | b6moves | 12 | — | 10 of 12, 31 / 159 ms | 0 | 0 |

Bold = over the 150 ms combat line, unchanged by this WO: the clinch's p95 is its one plain fallback (a row the figure refuses), the hit
reactions' p95 are the stand-ins (a second row whose install is the late one). The Part C categories are in §4.

## 3. Part B — combat, the rest

**B0, the combat action reader (fixed).** The combat director at combat actor +0x2E0 is a `C_CombatActorDirector`; its running actions
are a vector by scope. The reader now checks the director by its RTTI-found vftable and reads `[[director+0x50] + 8·scope]`, the class by
its RTTI name (cached by vtable: the B1/B3 polls read three scopes of the player every frame). Every `WO190-ANIM` full-body line now carries
`dir=<scope 0>/<scope 1>/<scope 4>`. Live: the duel trace's `act=` read Attack / Block / Guard on all 285 lines of a real NPC's fight
(every one was empty before); A6's `WO190-TAKEOVER ... director=` read `Guard` on 7 of 7 (was `?`); a copy's scopes read
`Guard/GuardMovement/PoseModifier` between moves and `Hit/none/PoseModifier` in a real reaction.

**B1, a player's recoil.** The source polls the local player's scope 4 every frame: a new FailedAttack goes out as `AvatarRecoil` with its
kind (the opponent's perfect-block state, else State 0x100) and, when the row object names a failed-attack row, that row. The watcher plays
the engine's row, else the failed-attack row for the weapon of the swing his figure last played (WO-165's picker); blunt, unarmed and stick
blows have none and nothing is played. Live, the watcher: 8 of 8 recoils shown (four picked by weapon, four the engine's own row; block and
perfect-block kinds), 27 / 38 ms. The source, in the real-input sittings (§5): the 12 `WO197-RECOIL state 0x10 without a FailedAttack`
lines were 6 plain hits (the diagnostic fired on every contact) and 6 real blocks / perfect blocks, all longsword **slashes**; no
FailedAttack ran on the player's body, so no recoil went out. That is the game's own rule, not a missed capture: its failed-attack rows
exist for stabs only (§6, "B1's source"). Live, with real-input longsword stabs: 2 of 2 blocked stabs went out as `AvatarRecoil` with the engine's row and
played on the echo figure.

**B2, the victim's half of a paired move.** A paired row (combat_action_sync_attack) on a copy or a figure: the agent asks the DLL for the
attacker's opponent (a copy or a figure, never this player), picks the victim's combat_action_sync_hit row with the same combo id, step and
variant and the attacker's tags mirrored to the victim's side (the victim's own weapon from his hands), sets both opponents to each other
(SetLogicalTarget both ways) and queues the victim's row first, then the attacker's (the engine's own order). Two bugs found live and fixed:
the victim's *death* variant was picked for a non-lethal punch (a death half now only for the attacker's own death row), and combo rows'
long tag strings were cut by a 120-byte cap (now the DLL's own 191). After: 10 of 10 victim halves shown, 31 / 37 ms, both ways (a copy
throwing the host's figure, the figure throwing a copy); the attacker rows of the same moves 9 of 10.

**B3, clinches.** The source polls scope 1 (GuardSyncMovement = the clinch idle), scope 0 (SyncTransition = a move, its role at +0x68, its
master and slave rows) and the clinch object's master flag; `AvatarClinch` carries enter / move / leave and both rows. The watcher: each move
plays the master row on the master and the slave row on the slave in one step after SetLogicalTarget both ways — 6 of 6 shown, 12 / 37 ms.
`StartSyncGuard(4, master, slave)` on two puppets was refused by the engine in every run, so per the WO the row path is the one kept; the
entry plays the game's own clinch-start rows on both (`CombatClinchStartGuardMaster/SlaveGen`); the game's tables have no bare-handed
clinch loop rows. The source, in the real-input sword sitting: a real clinch (the opponent took the player into it): `WO197-CLINCH local
enter role=slave` and `leave` went out as AvatarClinch; the echo's figure answered StartSyncGuard refused, then the clinch-start rows
(`dispatch=rows CombatClinchStartGuard`); no clinch move happened in it (enter, then the opponent's hit ended it).

**B4, a brawl knockout.** The sender sets `PlayerStateFlagEngineKnockout` from `IsUnconscious() && !_localDowned`, apart from the death
guard's knockdown. The watcher saw both edges (2 of 2 knockouts, 2 of 2 wakes). The game's unconsciousness on the figure's soul showed no
fall at all (profile alive, idle), so after 1.5 s the figure is ragdolled (it lay 18 s, as long as the partner); neither the engine's
StandUp nor its Fall lifted a ragdolled figure at the wake (25-45 s), so 2 s after the wake a fresh figure stands where the partner woke.

**B5, the copy's leftover the engine's way.** EnsureGuardAction is found by WO-196's port note (the one callee of StartSyncGuard that
imports `C_ActionDirector::TerminateInternal`) at CombatModule+0x806A0, and armed at startup. A6 keeps a copy whose main action is a real
reaction or a paired move (Hit, SyncHit, SyncPerfectBlockHit, SyncTransition, SyncAttack) and ends any other non-Guard main action with
EnsureGuardAction before the State setter. Live: every takeover read `Guard` (the copies play rows as bare animations, as WO-196 inferred),
so the call was not needed there; four direct calls on a fighting copy each answered "a Guard runs".

**B6.** Stops, combos, master strikes and kicks on a longsword copy and on the figure: 10 of 12 rows shown (with the victim halves),
31 / 159 ms. Not shown: the bare-handed kick row on the sword-armed figure (a weaponless row dies on an armed body: WO-187's rule).

## 4. Part C — the everyday categories

The rig now has a real source: `rec197.py` records 7 minutes of a real host's town (this game as the host, a synthetic joiner recording
everything sent, at hours 8, 19 and 23) — 2,035 NPC activity rows, 2,452 hand / gait / one-shot / look rows, 519 door states, 68,796 NPC
states — and replays it onto this game as the joiner. Riding and emotes come from the synthetic partner.

"Before" = two replays on the build without this WO's Part C fix; "after" = four replays with it (each 7 minutes, the same recording).

| category | source changes (per run) | shown, before → after | delay median / p95, before → after | full-body gaps > 0.25 s | overflows (copies) |
|---|---|---|---|---|---|
| doors | 519 door states (303 applied with a line) | not logged → **100 %** | the arrival's own Lua call | 0 | — |
| seats and beds | 121–122 | 79 % → 79–80 % | 181–225 / 634–701 → **76–87 / 288–474 ms** | 0–1 | 8, 336 → 52, 1,312, 46, 168 |
| hand items | 147–188 | 47–68 % → 52–71 % | 149–153 / 402–511 → 120–151 / 295–417 ms | (same runs) | (same runs) |
| work stations and minigames | 0 (none in the recorded town) | — | — | | |
| NPC routines | 194–224 | 76–77 % → 80–82 % | 142–211 / 484–512 → **43–81 / 378–415 ms** | | |
| riding (the partner's figure) | 4 mounts and dismounts | 4 of 4 | mount 94–117 ms; dismount ~700 ms (the game's dismount animation) | 0 | 0 |
| emotes (the partner's whistle) | 10 | 10 of 10 | — | 0 | 0 |

* **Doors** — 303 of 303 host door moves applied (203 moved, 100 already so); the apply runs in the arrival's own Lua call. New: every
  apply's outcome is logged (`WO197-DOOR`), evidence the field never had.
* **Seats and beds, NPC routines** — the activity apply ran on a 250 ms tick; a changed activity is now applied on the next frame
  (`mp_anim_seats`, `mp_anim_routines`, `mp_anim_work`): median 181–225 → 84–87 ms (seats), 142–211 → 48–81 ms (routines). The shown share
  does not move (~80 %): the rest are the game's own planner refusing the placement (exec 0: eating seated, wood chopping, egg picking, a
  guard post, a spindle), WO-160's known class, retried at its pace.
* **Hand items** — 50–70 %: some copies' tools are refused by the game with their activity (shown without them, as WO-143 does), and
  one-shots on paused copies (getting mash, feeding the dogs, washing) mostly time out without a result after 15 s.
* **Work stations and minigames** — the recorded town had no NPC minigame; the partner's minigames have no source in the rig.
* **Riding** — 4 of 4 mounts and dismounts: the figure's riding gait 94–117 ms after the partner's flag; a dismount ~0.7 s (the game's own
  dismount animation first, on both screens).
* **Emotes** — 10 of 10 whistles played at the figure; the town's talk gestures are the look rows (361, applied with the activities).
* **Overflows** — copies' queue overflows in five identical replays: 8, 336, 52, 1,312, 46, nearly all from one or two copies whose activity
  the planner refused, alternating between the refused placement and the stream's position (12 cm apart). A look-IK change for held
  copies was tried and removed (it changed nothing measurable).

The fix of this WO (a changed activity applied at once) has one switch per category it serves -- `mp_anim_seats`, `mp_anim_routines`,
`mp_anim_work` (the DLL gets them as one mask) -- all on, the standing rule (new mechanisms ship on). Doors, hand items, riding and emotes got
no fix here and have no switch; where a category stays under the line, the list above says why.

## 5. Part D — the release checks

On the build that ships (the same DLL, agent and Lua), one machine, `playline4` only.

| check | result |
|---|---|
| WO-192: a host save during a scene pairs | PASS — one interactive scene counted open, the host save asked all the same and `PAIRED` on the joiner in 3.1 s (`WO192-SAVE during a scene`) |
| WO-192: the host's autosave (interval shortened to 1 minute) fires and pairs | PASS — three host saves the schnapps way (no item used), each verified and announced (`WorldSaved seq=1..3`); in the join flow every host save `PAIRED` on the joiner (seq 1–3, 2.6–3.1 s) |
| WO-192: the partner's marker moves | PASS — `WO192-MAPMARK` moved with the partner (6 moves in the town run) |
| WO-192: the wake waits | PASS — the joiner held behind the fader and released 4.1 s later when the host woke 3 s after; with no host wake, released at the 15 s bound |
| WO-192: a station's lock is released | PASS — the trough's lock (`waterTub`) taken the game's way and left: after 15.2 s the agent ended the interaction the game's way and the lock was removed (`WO192-STATION released 'waterTub': abort=true end_interactive=true lock_removed=true`) |
| WO-192: the screenshot gate | PASS — `Test-NoScreenshots` 0 hits, the input-tooling scan 0 hits; the installer's payload gate (§7) |
| WO-195: a dead copy falls and ends the fight | PASS — the copy fighting the joiner fell 954 ms after its death (ragdolled from standing), its fight released; one killed 71 m away lay dead on arrival |
| WO-195B: the calibrated stealth rule | PASS — a pickpocket from behind (crouching and standing) unseen, face to face seen and judged a crime; a sleeper at night ignored the figure (asleep); noise and sleepers (hour 23:30, the figure 0.7 m from a sleeper): walking = `counted=woken` and the engine's own movement sound made at the figure (`WO195-WAKE ... -> ok`) in 4 of 4, the sleeper got up and walked off in 3 of 4 (1.9–11.4 m; WO-195B measured the engine waking on 81 % of its sounds); still and crouch-walking beside a sleeper: `ignored=asleep`, slept on |
| WO-195: body loot and the loot screen's key | PASS — the host's take: the key leaves the body (no second key); the joiner's loot screen on a dead copy takes the key into the joiner's pack |
| WO-195B: a chest / door the joiner unlocks stays unlocked after save and reload | PASS — on the joiner's claims (the agent's own entry points) `chest20` and `door_village_right1` were unlocked in the host's world; a host save (the schnapps way) and a reload of it: both still unlocked, their controls (`chest30`, `door_village_left3`) still locked (`tools/wo118/lock197.py`) |
| WO-195B: a knockout wakes later | PASS — the knocked-out NPC woke on its own after 17 s (the game's depth at x15 time) |
| WO-194: a shared in-game cutscene | PASS — staged at its quest step (the Suk scene in Troskowitz): held at start until the host's copy began (3 s bound), one clock; the host's part-skip jumped this copy 121.9 → 150.0 s, the host's skip-all stopped it; the end released 5 NPCs' fast-forward, this player positioned 0.69 s after the content and nudged 0.9 m off the shared tag point |
| WO-194: a host save right after the scene | PASS — `PAIRED` in 3.1 s |
| WO-184 C1 round | PASS — items picked, host save paired in 2.6 s, quit, rejoin restored that snapshot; inventory at the save and after the rejoin EQUAL (32 classes) |
| WO-187 echo test and shield windows (real input, the game in front) | PASS — two sittings with real input to the game in front (the maintainer away from the machine, by agreement): bare-handed, 48 of 48 of the player's attacks and 10 of 10 hits taken came back on the echo figure, the same rows in the same order, all accepted by its body; 53 of 58 shown within 0.6 s (33 / 381 ms; the five late ones are free-guard punches out of combat, 0.63–0.73 s); with the longsword drawn, 19 of 19 attacks and 26 of 26 hits came back in order and were accepted (that sitting's figure trace was too sparse to time them), and a real clinch went out and came back. Shield windows: the engine opened 83 perfect-block windows (7 openings refused) for the opponent's 76 swings while the player blocked with real input; 0 full-body gaps, 0 overflows |
| Frame rate in town | PASS — WO-190's town scenario (40 copies held, the figure walking): 74.6–76.4 fps on this build vs 77.2 / 77.2 / 76.7 for 0.60.9 (−2 … −3 %, inside 10 %); the mod's own frame cost 531–550 µs a frame |

## 6. Open

* **B1's source — where the game plays the player's own recoil (follow-up, 2026-10-17; tables, the logs on disk and one live run, no
  new disassembly).** It plays one only for a blocked or perfect-blocked **stab**; for a blocked slash it plays none, on the
  player's body or an NPC's, so there is nothing to send.
  * *The rows (READ, `combat_action_failed_attack`, all 8 rows).* Every row has `attack_zone_id="5"` and the tag `aZ5`. Zone 5 is
    `lower` (`combat_zone`, the bottom of the cursor, 225–315°), a real zone and not a wildcard: the attack table uses −1 for "any"
    (37 rows), and its 33 zone-5 rows are 27 stabs and 6 kicks, never a slash. Everything else on the rows matches the player: the
    human class (1578932418; 20967892 is the dog), guard type 0, a male opponent, longsword class 4 both hands. `player="false"` is not
    the filter: the player plays Hit actions from `combat_action_hit`, whose 273 rows are all `player="false"` too.
  * *The 12 lines re-read (LOG, `release/w197rig/results/real.after3/4.native.log` against `WO176-HOSTHIT` and the WO178 trace).*
    6 were plain hits (the bandit went to 0x40 / `Hit`): the old diagnostic fired on every contact, because the 8 → 0x10 write
    happens at any contact (WO-186 R4.3). The other 6 were real: 5 blocks and 1 perfect block, the opponent at State 0x100 /
    PerfectBlock at the contact, the Opponent set. All 6 were longsword **slashes** (`CombatAttackGen … aZ1/aZ2 slash`; the sitting's
    19 outbound attacks: UpperLeft 11, UpperRight 7, Head 1, **Lower 0**), so the rig's down-move never selected a stab.
    Scope 4 read `none` every time, so no pose modifier sat there: WO-196's refusal case was not the cause. The one blocked "Lower"
    blow on disk (`echosword.after`) was a bare-handed kick; that automated run never drew the sword.
  * *What the player's body does on a blocked slash (LOG, the perfect block at 10:14:00).* Scope 0 keeps the **Attack**
    (`dir=Attack/none/none`), the full-body fragment stays 897 (the attack's own), and its priority drops 5 → 3 at the 0x20 Withdraw
    255 ms after the contact; then Guard. This is the same "the attack runs on" that WO-196 R1 read for blunt and unarmed blows. The
    blade's bounce is the procedural weapon solver (`wh_cs_WeaponProcSolverRecoil*`), which runs on local physics on each machine.
    The watcher already shows the attack row, so a blocked slash needs no extra row.
  * *Code (this follow-up).* The poll's weapon read was a literal −1 in the log line. It is now `wo166::weapon_class_id`. The
    every-contact diagnostic is replaced by a per-blow one: at the 8 → 0x10 contact the opponent's block is read (now, or as last read
    while striking, because a blocker often reads 0x20 by then). A blocked blow is watched for 2 s: `WO197-B1BLOW kind zone type weapon
    failed_attack scope0`, and per-frame `WO197-B1PROBE` lines (State, scopes 0/1/4, the opponent) for the first 2 blocked blows. The
    status adds `contacts blocked blocked_stab blocked_stab_no_fa blocked_slash`. The recoil's kind uses the same pre-contact read.
    The send path is unchanged: a FailedAttack in scope 4 goes out as `AvatarRecoil` with the row's GUID.
  * *Live (2026-10-17 11:12, the Modding Tools game, `playline4/quicksave022`, host role with the echo partner; `run197.py stab after
    75 60`; results in `release/w197rig/results/stab.after.*`).* First 60 s of the engine fighting for the player with the sword drawn
    (no input): no blocked blow. Then 75 s of real input with the game in front (the maintainer away): the cursor was stepped down
    before each click, and 15 of the 16 outbound attacks were stabs (`zone=Lower`, `CombatAttackGen … aZ5+stab`). The bandit blocked
    2 of them. **Both** ran the engine's FailedAttack on the player's own body (`WO197-B1BLOW kind=block zone=5 type=0 weapon=4
    failed_attack=ran`). Scope 4 held it from the contact for 0.41–0.45 s, with scope 0 still Attack; State 0x10 → 0x20 came at
    359 / 422 ms, then Guard with PoseModifier in scope 4. Both went out as `MP-ACTION section=outbound kind=AvatarRecoil recoil=block
    row=cdaf144b-… spec="CombatAttackFailed, l_longsword+r_longsword+aZ5+oppMale"`. On both the opponent already read 0x20 at the
    FailedAttack, so the kind came from the pre-contact read (the old poll would have sent "unknown"). The echo figure got both back
    (`kind=AvatarRecoil … (his engine's row) dispatch=native-row result=ok`), and its body queued fragment 199 `CombatAttackFailed`
    2.05 s after the source (the echo's 2.0 s delay); the agent's stats read `recoil_out=2 recoil_in=2 recoil_played=2`. The figure's
    full-body install was not in that trace. Scope 4 was empty before both stabs, so WO-196's equal-priority refusal did not come up.
    Whether a running PoseModifier ever refuses one is still unseen.
  * *Done:* B1 works as built; it fires on blocked stabs, the only blows the game gives a recoil. The per-frame probe is kept for the
    first 2 blocked blows of a session; the `WO197-B1BLOW` summary is logged for the first 30 and every 50th.
  * WO-196's question whether the pose modifier refuses a FailedAttack on a puppet stays open (the figure played every recoil row it
    was given).
* **B3's moves from a real clinch** — the real clinch's enter and leave went out and came back; no clinch move happened in it, so the
  source's move capture is proven only by the synthetic partner (6 of 6).
* **B3's entry** — `StartSyncGuard(4, master, slave)` is refused by the engine on two puppets; the clinch start is the game's clinch-start
  rows on both bodies instead (shown). Why the engine refuses it (the puppets' combat state, or the guard-request scope WO-186 found) is RE.
* **B4's get-up** — the game's own stand-up from the ragdoll is not shown: neither StandUp nor Fall lifts a ragdolled puppet; a fresh
  figure stands where the partner woke, 2 s after the wake. The unconsciousness buff itself shows no fall on a figure (profile alive), so
  the fall is the engine's ragdoll. RE: what the game's own knockout calls on an NPC's body.
* **B6** — the bare-handed kick row on a sword-armed figure does not play (WO-187's rule: a weaponless row dies on an armed body).
* **Part C below the line** — seats and beds and NPC routines ~80 %, hand items 50–70 %, p95 above 250 ms: the game's planner refusing a
  placement (WO-160's class) and one-shots on paused copies timing out; copies' queue overflows from the same refused copies (the stream's
  position and the refused placement 12 cm apart). The new switches stay on under the standing rule (new mechanisms ship on): they only
  make a change apply sooner. "Copies within twice the host's" could not be judged: the replay's host is synthetic (no host count).
* **Work stations and the partner's minigames** — no source: the recorded town had no NPC minigame, and the rig cannot make the partner
  use one. Unmeasured.
* **A relay test flake** — the relay suite's `Loot_messages_cross_the_relay_joiner_to_host_and_host_to_joiner_only` (WO-134) timed out
  (a 5 s read) in 3 of 6 full runs while the game ran at full load on the same machine; alone it passes every time. The relay is
  unchanged here; the build passed on its fourth start.
* **RE items** (no new disassembly here): the pose modifier and FailedAttack; StartSyncGuard's refusal on puppets; the knockout's get-up;
  the planner's refusals for copies (WO-160).

## 7. Build

`tools\Build-Installer.ps1 -SoakWaiver` (the standing rule of 2026-10-06, restated by this WO, 2026-10-17) in a fresh clone at `bd9fa45`
with the three start saves: every test suite, the static checks, the live save round trip (a healthy save written in `playline4` and
reloaded; the draw was refused before and after alike: no drawable weapon at that throwaway), the payload smoke, the no-input-tooling
gate (0 hits) and the no-screenshot gate (0 hits) passed; the installer and its files carry 0.61.0; unsigned. The first start stopped at
WO-106's console placeholder check (four new commands, fixed in `bd9fa45`), the second at a dead Henry left by the real-input sitting
(the save round trip needs a living one), the third at the relay test flake above.
