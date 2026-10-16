# WO-190 — Animation parity: both players see the same thing (0.60.9)

Code: native `wo190.cpp` / `wo190_rules.h` (the trace, the idle watch, a body's hands, the clinch refusals), `wo166.cpp` (A6), `motion.cpp`
and `npc_drive.cpp` (A3, A4), `wo187.cpp` (every scope read), `wo178.cpp` (the clinch refusal ring); agent `Wo190.cs` (the rules) and
`GameBridge.Wo190.cs`, with hooks in `GameBridge.Wo132.cs` (A7), `GameBridge.Wo187.cs` (A1, A9), `GameBridge.Wo121.cs` (A8) and
`GameBridge.Wo167.cs` (A2); `kdcmp.lua` (A2's torch guard, A3's look, the switches). Tests: native `wo190_tests`, agent `Wo190Tests`; live
harness `tools/wo118/run190.py` (the scenarios), `tools/wo118/batch190.sh` (a batch of them) and `tools/wo118/parity190.py` (the table).

Everything below is single-machine evidence: the Modding Tools game as the **joiner** of a synthetic host through a local relay, on the
`playline4` throwaway save (Troskowitz, 8 in the morning), no input, the game window never brought forward. "Before" is this build with every
WO-190 switch off (the trace itself only exists in this build); "after" is the defaults. Nothing here is two-player evidence.

## 1. Status

| category | scenario (what was sent) | shown, before → after | notes | switch (default) |
|---|---|---|---|---|
| combat | the host NPC hit, then swinging again 0.5–0.9 s later, 8 times, after one ordinary swing (A6) | wind-ups **1 of 9 → 9 of 9**; shield windows **1 → 9** | before: 8 swings refused, the copy stuck in 0x20; the swing animations played in both | `mp_copy_swing_takeover` (on) |
| combat | a copy switching joiner → host → joiner, 6 swings each (A7) | 18/18 → 18/18 | engine shield windows 12 both (the 12 at the joiner); the turn to the host: released at once → held 3 s, then released | `mp_engage_hold` (on) |
| combat | a village copy's clinch rows at 0.7 m (A8) | **5/6 → 6/6** | the one refused row that did not show got its plain attack; 5 refused rows that showed anyway were kept | `mp_sync_fallback` (on) |
| combat | 8 swings at hour 22 (A2) | 8/8 → 8/8 | 8 engine shield windows both; first after-run: a torch in the copy's left hand put away at the engage | `mp_copy_weapon_hold` (on) |
| combat | the partner's 8 fight moves on his figure | 8/8 → 8/8 | | — |
| combat | a copy's 10 moves against the host's figure (A5) | 10/10 → 10/10 | its guard idle between every move, no empty full body | — |
| hit reactions | the partner's 8 reactions, 4 of another weapon or none (unarmed figure) | 8/8 → 8/8 | 4 stand-ins both | `mp_anim_standin_weapon` (on) |
| hit reactions | the same on a figure holding a sword (A1) | 8/8 → 8/8 | the stand-in's weapon: bare hands (a guess) → **the sword in its hand** once drawn | `mp_anim_standin_weapon` (on) |
| hit reactions | 8 host reaction rows on a copy, 6 of another weapon or a dog | 9/9 → 9/9 | | — |
| locomotion | the partner walking a 12 m line, 350 ms gaps on 12 % of samples | 0 % gliding → 0 % | no gait flips either way | `mp_anim_gait_hold` (on) |
| locomotion | a copy walking 4 s / standing 3 s, the same gaps | 0 % gliding → 0 %; walking on the spot 2.5 % → 2.5 % of frames | 14 gait changes both | `mp_anim_gait_hold` (on) |
| NPC routines | every NPC within 30 m held by the host's stream, 60 s, the player standing / walking among them | 0 → 0 overflows (both) | the game alone at the same spot: 4,031 overflows a minute, all its innkeeper | `mp_anim_requeue_gate` (on) |
| all | full-body gaps over 0.25 s, in every run above | 0 → 0 | | `mp_anim_idle_watch` (on) |

The delays the rig can measure are the engine's side only (the row queued on this machine → its animation installed): 20–41 ms median,
under 0.3 s at the 95th percentile, in every run, both modes; the reads are 30 a second, so shorter steps read as one frame. A real
two-player delay adds the network and the render delay; the trace lines carry a UTC stamp so the two machines' logs can be paired.

**Below target, and why.** Every measured category is at or above the WO's line (95 % shown, no full-body gap, no overflow), before and after
-- the synthetic rig reproduces A6 and A8's refusal but not the field's T-pose, gliding or idle-copy floods, so for A1, A3 and A4 the
evidence is "the mechanism works and does no harm", not "the field defect is gone". The categories with no source in the rig (doors,
chests, seats, items, work, riding, emotes) have no number at all (§2).

## 2. Part 0 — the animation trace and the parity runs

**The trace.** `WO190-ANIM` lines: every change of the installed action on every scope of a body's action controller (`FullBody`,
`UpperBody`, the hold-item and detail scopes), with the action's class, fragment id, priority, flags, how long the previous one held, the
controller's tag state, the body's rendered speed and stream age, and whether it is in a fight. Bodies: the local player, the partners'
figures, and the copies this machine drives (the nearest 24 within 40 m). Read-only, through WO-187's reader (each controller and scope
checked by its class and name). Two modes:

* `mp_anim_trace on` (off by default): every body, up to 30 reads a second, 12 lines a second per body, 6,000 a minute in all;
* `mp_anim_scope_log auto` (**the new default**, A9): only the bodies in a fight (a figure whose partner is in combat, a copy whose host
  NPC fights, the local player in combat), 20 reads a second, 4 lines a second per body, 600 a minute. Live with the trace off: 61 lines in
  a two-minute fight, all for the two fighting bodies. `on` also keeps WO-187's lines around every move; `off` stops both.

**The parity measure** (`parity190.py`): a source change is a row the watcher's body was sent (the DLL's own `SWING: ... queued fragment`
line, one per row); it was **shown** when the trace has one of its fragments installed with priority 5 or more within 0.6 s and held at
least 0.1 s (WO-187's rule: an action gone within 0.1 s never animated). The rows queued on one body within 0.9 s (a reaction and its
stand-in) are one move. Locomotion is read from the DLL's own counters: frames rendered moving with the still class (gliding), frames
rendered still with a walking class (walking on the spot), gait class changes.

**Field weights** (every tester bundle on this machine, each distinct log once): combat moves ≈ 5,400 (the players' 2,200 attacks, the host
NPCs' 3,200), idle copies' animation-queue overflows ≈ 283,000 lines, gait changes ≈ 35,000, hit reactions ≈ 1,160, jumps ≈ 780, riding
≈ 330 lines, minigames ≈ 310, herbs ≈ 50. In the same bundles: 34 + 17 swings refused for 0x20 / 0x40, 796 clinch refusals, 482 blows
shown as a generic lunge, 127 engages against 204 releases. That set the order: combat, the idle copies, locomotion, hits.

**Not run.** The echo partner with the player's own moves (real input): the run was unattended and the input policy never brings the
game to the front. Interactions, seats and beds, hands and items, work, riding, emotes: the rig has no source for them (the synthetic
partner sends no such rows), so they have no number; they are on the two-player checklist.

## 3. Part A

**A6 — a copy stuck in its own last swing (fixed, live).** Reproduced exactly: the host NPC's reaction row on its copy left the copy in
0x20 (the withdraw of a hit-reaction fragment) and nothing took it out, so every following swing was refused its wind-up ("an engine
reaction of its own: never cut off") -- 8 of 8 in the run, 1 shield window in 9 swings. Now the new swing ends a leftover 0x20 / 0x40
through the engine's own state setter (Guard) and winds up: 9 of 9 wind-ups, 9 shield windows, the swing animations unchanged
(1.1–1.3 s each). Kept as "never cut off": a copy the local player hit within 1.2 s, or whose combat director runs a Hit action. On these
copies the director could not be read, so the player's own blow is what guards a real reaction (§5). `WO190-TAKEOVER` lines.

**A1 — the figure's pose through reactions.** (a) The stand-in for a reaction that did not show now takes the weapon the body really holds:
the DLL reads both hands' weapon classes (the reach's own weapon lookup, WO-178) and maps them to the reaction rows' hand tags; a bare-handed
stand-in is never chosen for an armed body. Live, a figure armed with a sword: before, every stand-in was a bare-handed reaction (the
body's weapon guessed); after, the sword's own reaction once it was drawn. A bug found live and fixed on the way: the hands-only body row
lost the body's actor class, so a dog's reaction row on a man searched the dog's rows (2 of 9 not shown); the body's own class is kept.
(b) The idle watch: a figure whose partner fights, or a copy held in a fight, whose full body stays empty 0.25 s gets its guard idle back
(WO-187's remedy), at most once a second, at any time in the fight; WO-187's restore ran once per move and only for 4 s after a move it was
sent. In the runs no full body stood empty, so it never had to act (`WO190-IDLE`).

**A2 — night fights.** A fighting copy is told its host NPC's weapon at every engage when the host's hand rows named one (as before at every
swing), and never a light: a torch or a lamp in its hand goes back into its inventory (the game's own put-away) and the weapon is drawn;
a host hand row naming a torch is not passed on. Live at hour 22: in the first run the copy held a torch in its left hand at the engage and
it was put away (`WO190-TORCH`); the final run saw none. The night T-pose itself (WO-185's) did not reproduce in either mode: 8/8 moves, 8
shield windows, no empty full body. A regression found live and fixed: an engage with the host's weapon unknown sent "?", which draws the
copy's own weapon -- a bare-handed villager drew one, and its bare-handed clinch rows then died at ~45 ms. Only a named weapon is sent now.

**A3 — the idle copies' animation queue.** Two causes in the code: (a) a paused copy's own look IK keeps asking for look poses (the field's
relaxed / crouched look-pose pairs). WO-157 found and fixed exactly this on the figures (8,076 overflows → 0) but never on copies; a paused
copy's look IK and AI look target are now off while it is paused and back at its resume. (b) A copy standing still got a tiny, flipping
move vector from millimetres of stream noise, which picked walk / turn / strafe-back blendspaces in turn every frame (the field's
`3d_relaxed_walk_turn_strafe` / `2d_relaxed_walk_strafe_spd_bwd` pairs): now below 0.12 m/s (with hysteresis) a copy stands -- speed and
direction both zero -- and its gait class changes at most 4 times a second. In the rig neither form reproduced (0 overflows on the held
copies before and after, with the player standing or walking among them), while the game alone at the same spot logged 4,031 overflows a
minute, all its own innkeeper walking on the spot. The pass line ("the joiner within twice the host") holds in every run; the field's
283,000 lines are the check that counts.

**A4 — gliding in an idle.** The gait no longer follows only the smoothed speed: a body that renders a walk is never left in its idle (the
rendered speed lifts it to a walk at once -- never further: uncapped, a catch-up after a gap turned a walk into a short run, 10 flaps on a
walking figure in the first build); through a gap (the render standing past the stream's newest sample) a figure keeps its last gait up to
0.5 s and stops at once when its partner's own speed says 0; a copy keeps it only 0.25 s, because the host's NPC stream is moved-gated (an
NPC that stops sends nothing until its 2 s heartbeat, so a stop looks like a gap: a 0.5 s hold walks every copy on the spot at each stop).
In the rig there was nothing to fix: 0 gliding frames before and after, walking on the spot and gait changes unchanged.

**A5 — a copy fighting the host's figure.** Confirmed: 10 of 10 moves, the guard idle between every one, no empty full body.

**A7 — engagement flapping.** An engaged copy whose host NPC turns to the host is now held engaged for 3 s (combat held, its engagement
refreshed), its opponent already the host's figure through WO-187's wish; at the real release its guard hold is sent in the same step (no
gap in combat mode). The field's case -- turned away for one second and back -- is held through (the agent's replay test). Live, with a
28-second turn: `MP-W190 engage hold`, then the release 3 s later and the engage again when it came back; 18 of 18 moves, 12 shield windows
at the joiner, both modes.

**A8 — clinch attacks.** The DLL's clinch gate now keeps a ring of the copies it refused (op 53). After a paired row is played on a copy,
the agent looks 0.3 s later: refused, and the row did not show (WO-187's move watcher) → its plain attack plays at once (`WO190-CLINCH`).
Live: the engine asks the gate only inside the move's sync distance (0.8 m bare-handed); at 1.5 m the copy simply played its own half.
At 0.7 m the gate refused every ask (2,196 of 2,196), and still the copy's own half played 1.3–2.7 s for 5 of 6 rows. A blind fallback
(every refused row, and the copy's later paired rows plain) scored 1 of 6 there: plain rows put in its place were cut after ~45 ms. So the
plain attack is only for a refused row that did not show, and only that row: 6 of 6 shown (1 plain, 5 kept).

**A9 — the evidence of a bad pose.** `mp_anim_scope_log auto` is the default (§2): the next field bundle has every fighting figure's and
copy's installed actions, scope by scope, with times.

## 4. Part B and Part C

Not done (budget): B1 recoil, B2 the victim's half, B3 both halves of a clinch, B4 a knockout on the wire, B5; and Part C's per-category
fills (interactions, seats, hands, work, riding, NPC routines, emotes). No `mp_anim_<category>` switch was added.

## 5. Needs an RE WO

* **B1 a player's recoil.** Where the attack helper's owner slot swaps `Attack` for `FailedAttack` after a block or perfect block
  (`OnCollision` / `OnAnimCollision`, WO-186) as a capture point: the function, its prologue in the 1.5.5 CombatModule, and what it is called
  with (the attacker's combat actor, the failed-attack row or its id), so the swap can go out as its own action.
* **B3 clinches.** The sync transition (`CombatClinchTransitionMaster` / `Slave`, then the clinch guard): which function starts it on the
  master, how the slave is chosen and aligned, and what a capture hook would read (the two actors, the transition row) -- WO-178's
  `StartSyncGuard` hook sees only a yes / no.
* **B2 the victim's half.** How the engine aligns the two bodies of a paired move once both opponents are set: whether `sync_hit` rows are
  started by the master's action or need their own call, and with which arguments.
* **A6's guard.** A copy's combat director (combat actor +0x2E0) does not read as an `ActionDirector` on these copies: where a copy's current
  combat action lives, so a real hit reaction can be told from the director and not only from the local player's own blow.

## 6. Defaults

| item | switch | default |
|---|---|---|
| 0.1 the full trace | `mp_anim_trace` | off |
| A9 the fight-only trace | `mp_anim_scope_log` | auto |
| A1 the idle watch | `mp_anim_idle_watch` | on |
| A1 the stand-in's weapon | `mp_anim_standin_weapon` | on |
| A2 the weapon at every engage, never a light | `mp_copy_weapon_hold` | on |
| A3 standing copies stand; no look IK on a paused copy | `mp_anim_requeue_gate` | on |
| A4 the gait through a gap; no idle while moving | `mp_anim_gait_hold` | on |
| A6 the next swing takes over | `mp_copy_swing_takeover` | on |
| A7 the engage hold | `mp_engage_hold` | on |
| A8 a refused clinch that did not show → its plain attack | `mp_sync_fallback` (WO-167's) | on |

## 7. Tests and regressions

| check | result |
|---|---|
| native unit tests | 9,575 pass (the new `wo190_tests`: the trace's limits, the idle watch, the hand tags, the re-queue gate, the gait hold and floor, the takeover) |
| agent unit tests | 1,762 pass (`Wo190Tests`: the switches, the trace mode, the stand-in's weapon and actor class, the torch guard, the engage hold incl. the field's one-second flap, the clinch look) |
| mod suites | all 68 synthetic suites pass |
| WO-187 echo test, input-free halves | copy side **10/10** moves (the guard between them), figure side **8/8** moves; the echo with the player's own input: not run (unattended) |
| WO-187 shield windows | **12/12** swings at the joiner got the engine's perfect-block slot, both modes |
| WO-184 save round with items | **pass**: 3 items picked up, the host's save paired in 2.6 s, quit, relaunch, the rejoin restored the paired snapshot, inventory equal (32 classes) |
| frame rate in town (Troskowitz, 11 copies held, the figure walking, trace off) | the WO-190 switches on 77.2 / 77.3 fps → off 77.2 / 77.1 → on again 76.9 / 76.4 (60 s windows): **0.997**; the DLL's own cost 586–596 µs a frame either way |
| saves | the whole saves folder backed up byte-equal (958 files) before the first launch; afterwards 958 of 958 equal. Only playline4 was used; for the save round it was set aside and put back |

## 8. The installer

Built from a fresh clone of `aaf11e4` (the start saves copied in and hash-checked) with `tools\Build-Installer.ps1 -SoakWaiver "Standing rule
of 2026-10-06 (the maintainer): release-candidate installers are built without the frame-rate soak; WO-190 restates it, 2026-10-15"`.
Exit 0 on the first attempt: the publishing gate, relay 63, agent 1,762, setup 77, log bundle 14, every synthetic suite and static check,
native 9,575, the payload checks, the live save round trip (PASS). Unsigned. Local only, nothing uploaded.

| | |
|---|---|
| file | `KingdomComeTogether-Setup-0.60.9.exe` |
| size | 107,114,855 bytes |
| sha256 | `97d6e899f5e2cbb6f7cc29ac38e75c9e435743e1b2ab7e99a23df48a369b0331` |
| header version | 0.60.9 |
