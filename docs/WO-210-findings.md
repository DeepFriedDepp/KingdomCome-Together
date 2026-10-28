# WO-210 — Playable from the join on: every copy animated, clinches that end, deaths and knockdowns the engine's way

Version **0.63.9**. Measured on the bench (two copies of the Modding Tools game on one PC, joined through our own relay, started the
launcher's way) against the field (the last two-player session's six bug reports, measured with the same tool). Every new mechanism
ships **on** with its own switch, or a test switch, and a twin (the same build with it off). Nothing built here reads input:
`tools/Check-NoInputCapture.ps1` passes on every binary and script, first and last.

Roles only: "the host", "the joiner". "A copy" is the joiner's local body of one of the host's NPCs; "the figure" is the host's local body
of the joiner.

## Part 0: the field, the baseline

0.63.8 (`main`) built as the baseline; its offline gates passed (97 of 97). The real saves folder was backed up byte-equal with a hash
list, checked before and after every bench run of this WO (every check a match; mirror roots only).

**The field (the session's three host/joiner pairs), as measured:**

| measure | field |
|---|---|
| copies lying or sitting while their host NPC walked (stale postures) | 18 bodies, 40 stretches, 359 s, 380 m slid while lying -- the shared night's 10 h sleep: 14 bodies, 338 s |
| copies with an empty whole body (a T-pose) | 1 |
| clinch refusals on the joiner / refusal loops | 537 / 5 (158-202 asks a loop) |
| the joiner's killing blows / through the engine's death reaction | 5 / 0 (all "victim cannot react"); 6 bodies laid down by the mod's fall, none lootable |
| the joiner's blows applied raw / through the engine | 9 / 22 |
| the host's knockdowns / the copy fell / both within 300 ms | 18 / 5 / 0 |
| double NPC-state requests on copies | 36 |
| prop events refused on copies (spawn / pickup) | 53 / 466 |
| copy-only combat idle over 1 s | 13 |
| the joiner's game queue behind, p99 | 11.7 s (one burst per join) |

## Part A: every copy always shows its host NPC's whole-body state

**Cause (the field's floating sleepers).** A copy put to bed by the activity placement ran its **own** idle state (lying, at priority 3).
When its host NPC got up, the host's row said "nothing", and the neutral rule (a copy's loaded state equals what its body is) kept the bed:
the copy stayed lying while the position stream slid it after its host NPC (one copy 81 m). The owner's own exits also left bodies
empty: a withdrawn action of ours stepped to priority 0 on a paused copy whose own base never came back.

**Built** (`mp_copy_state_always`, on):
- a copy whose host NPC left a bed, seat or workstation has its stance and unstance released the game's own way before the next state;
- every owner exit hands over (the host's current fragment, or the copy's own neutral idle -- never nothing; a host scope that is itself
  empty hands over the neutral idle);
- a watchdog checks every owned copy within 60 m every 250 ms: an empty whole body or a stale posture is reseated within 0.3 s, a missing
  posture within 1 s, a posture against an unnamed fragment of the host's (a seated gesture) within 2.5 s;
- after a time skip, a load or a join the joiner asks the host for every body's whole-body state again (`NpcAnimAsk`, 0x7E / 0x7F);
- a fight's base is the copy's own engine's: nothing of the host's footwork is installed over it (see Part E).

**Numbers.** The bench reached a real morning only on some runs (its host's own sleep or wait can end after a second; logged, never a gate).
On the dev build's night run that reached the morning: **0 stale postures** (field: 40 stretches), the watchdog's reseats all with a known
path after two watchdog fixes (an empty host scope; seated gestures). Empty bodies: 0 in every dev run after the fix (baseline: 1 in a
switch-flip run). The tavern at night (ten minutes, dev build d8): every copy where its host NPC was, the joiner's queue p99 63 ms.
These are the dev builds' runs (d6-d8); A's own code changed after them only in the fight base (d10). The candidate's night, morning and
tavern runs were cut for time.

**Twin.** `KCDMP_TEST_W210_OFF=copy_state_always`. Run only in a three-fight run (the tavern twin was cut): 0 empty bodies and one
stale posture of 1 s with A off, against 0 and 0 with it on -- fights hardly exercise A, so this twin says little.

## Part B: clinches

**Cause (B.1).** The clinch component's auto-trigger asks the engine's clinch test every frame for a close pair; the refused start set its
pending flag and both bodies retried every frame: four refusals a frame, 158-202 a fight, the copy's own attacks cut, its body empty.

**Built (B.1, `mp_clinch_guard`, on).** A pair of the local player and a copy fails the engine's own clinch test (the clinch component's
`CanClinch`, found through its vtable and checked by prologue): no start, no pending, no retry. WO-178's refusal stays as the counted
backstop. On the bench the engine's own test answered "no" about 14,000 times a minute in a close fight (its per-frame check), refusals
at the start fell from 8 to 5 a fight, and **refusal loops: 0** in every run (the baseline: 3 loops in the switch-flip fight).

**B.2 / B.3 (a clinch played on both screens): not built.** The engine can take a copy as the master of a paired clinch, but the bench never
produces a clinch between a figure and an NPC (0 in every clinch run, on both builds), so the acceptance (10 clinches each way) could not be
run, and the start's preconditions (both guards, the start row's distance within 0.2 m, the moves and the end driven from the host) are each a
live probe. **B.4: clinch v2 is not in this build** (no switch is shipped for a mechanism that is not there); B.1 is on. The tester script
asks for a clinch.

## Part C: deaths and knockdowns, the engine's way

**C.1 cause.** The joiner's blows went through the engine's hit processor but not through the collision handler's last step (the game
rules' `ClientHit`): the victim's own hit event (its death reaction, the corpse's dead state) never ran, and a victim out of combat mode got
"cannot react". Bodies died upright or fell by the mod's physics ragdoll, with no dead state: the game's own loot test (`CanLoot`) failed.

**Built (`mp_death_engine`, on).**
- After every blow the engine applied, the game rules' own `ClientHit` is called with the replay's result -- where its call is found in
  the collision handler. **On this build it is not** (the call site's pattern is not there once; the mod says so in its log and does
  not guess), so the death reaction comes from the next step on every kill:
- Every body a joiner's blow killed is checked after the engine had its time (2.5 s); one dead without the dead state is finished the
  engine's way: its ragdoll manager's `RequestRagdoll` and the state expansion's `StartDeadAction` (op 142, both found in the hit-death
  handler's own lethal branch). The mod's physics fall is left only for a build where that cannot be found.
- **C.2:** a dead copy is finished the same way on the joiner, and the owner never drives it again.
- The game's own loot test is logged for each body on both machines.

**Numbers.** Dev build d8's kill run: 2 kills by the joiner, both deaths through the engine's own finish (no physics fall), each body
searchable on both machines by the game's own loot test (the field: 0 of 5 through the engine, none lootable). Since then E.2 sends
the joiner's killing blow to the host's engine as a blow with no numbers, through the same replay; the candidate's kill run was cut, so
that path's kills are **not measured**.

**C.3 knockdowns (`mp_knockdown_sync`, on).** The host's stream marks a body down from its knockdown or ragdoll action (or limp physics);
the copy falls through its own ragdoll manager on the down edge and is stood up with `RequestStandUp` (what the script's `StandUp` calls)
on the up edge; while it lies the owner and the position stream leave it to the engine. Dev build d8's ten fights: 30 copy falls
against 4 real knock-outs on the host (the down test read limp physics), the real ones 0.7-1.0 s late; d9 marks the body down from the
knockdown's own action at once (twin `kd_by_action`). The forced-knockdown run (the game's own fall and stand-up on a host bandit) was
cut, so the 300 ms acceptance is **not measured** after the fix (the field: 0 of 18).

**C.4 no raw blows.** A blow with no swing row takes the attack's own data (the last row's, else a plain slash); a blow whose figure had no
strike window (inside its own hit reaction) goes through the engine with the figure reading Striking for that call alone (written plainly
and put back in the same call). `mp_blow_joiner_values` is retired. **Found on the way:** the host still brought its engine's result to
the joiner's own numbers after every blow (WO-173's correction): extra damage when the joiner's game had said more, and a direct health
write ("raised back") when it had said less -- 72 health writes in one baseline fight run. Retired: the host's engine's result stands; a blow
its engine did not judge (no swing row, a refused replay, the switch off) is not applied from the joiner's numbers at all, it is logged
(`WO210-BLOW ... not applied`); and the echo guard is told the engine's own drop at once instead of the joiner's number corrected later (a
late credit could eat the next real drop). Twin: `KCDMP_TEST_W210_OFF=no_joiner_values`. With E.2 the joiner's blows carry no numbers at all, so the old
correction would have put every engine blow's damage straight back (40 times in one three-fight run): it is never run for such a blow,
twin or not. **Numbers:** the baseline's ten-fight run applied 32 of the joiner's 88 blows raw, 15 corrections and 40 health raises;
the four three-fight runs since the last fix applied 0 raw, 0 corrections, 0 raises -- each of their 45-54 blows through the host's
engine.

## Part D: enemies ready and fighting at once

**D.1 split.** `idle_check` with the host's bandits on the joiner's figure, then the same bandits on the host's own player:
- **host-idle:** the host's bandits idle 43 s a combat minute against the figure and **39 s against the host's player**; they attack the
  figure 4.6 times a minute and the host's player 5.5. That is the game's own fight pacing (attacker slots, circling), within 10 % on both
  targets: **D.2 is not warranted** and nothing was changed in the host's AI.
- **copy-only:** 128 copy-only idle intervals over 1 s on the baseline, **16** on the dev build: D.3 below.

**D.3 (`mp_copy_ready`, on).** The copy holds combat mode from its host NPC's newest streamed state at once (its draw and its stance with
the host's). Copy-only idle over 1 s in the idle check: 128 on the baseline, 16 on dev build d8 (the field: 13 in a whole session) --
the acceptance (0) is not met; the draw's p90 was not measured (the candidate's idle check was cut). The twin (`copy_ready` off, three
fights) judged 88 / 24 / 15 % locally, inside the fights' spread above: not a clean measure.

**D.4 the first swing.** A fight's first swing on the joiner was judged by the host's game when the copy's own attack started late: its
attack row reached the joiner before the opponent's word (the host read opponents on the stream's 10 Hz loop, after the stream itself),
and the copy waits for that word (up to 0.5 s) before it swings at the joiner. Now the host reads its NPCs' opponents before each stream
tick, and once more the moment an NPC's attack row goes out, so the word follows the row within a frame (the twin `opp_with_row`: the old
order). **Numbers** (a fight's first swing = the first blow after 10 s without one): judged by the joiner's own game 89.5 % (10
fallbacks of 95) on the candidate's three-fight run, against the baseline's 76.4 % (13 of 55) on the same scenario and 91.7 % (22 of 266)
on its ten-fight run. The acceptance (no first-swing fallback) is not met. The candidate's own run judged 36.4 % of first swings
locally: the host's swing capture gap (the bench, below) left whole stretches without attack rows. The twin's run was cut.

## Part E: the rest of combat

- **E.1 host attacks shown** (fix `ai_in_fight`, twin of its own). Measured where both machines' animation traces cover the same
  stretch (each trace keeps its newest part). Before the fix, the host NPC's attack started on the copy as the same fragment 50.4 % / as
  any attack 65.5 % (238 host attacks, the dev build), against the baseline's 53.3 % / 62.4 % (197) and 51.9 % / 77.8 % (27).
  **Cause, measured:** split by the copy's own opponent on the joiner's screen at that moment, the host's attacks at the joiner were
  shown as an attack **96.7 %** (90) and **97.4 %** (115) of the time when the copy fought him there -- and **6 %** (32) and **0 %** (43)
  when the copy fought the host's figure instead. Such a copy is held in his fight by the mod (the host's NPC fights him), but its own
  engine has retargeted it to the host's figure (the game's own "set target" line), in the figure's fight: on the dev build's runs a third
  of the copy-time in his fights (two bandits for four minutes at a time). The mod's own attack path claimed every host attack of such a
  copy and then refused it (outside his fight the copy's own attack at him is not allowed: F.1, the crash rule) -- so nothing showed, and
  the blow was the host's. **Built:** the copy's own attack claims the host's attack only while the engine holds the copy in his fight (the
  same relation, read-only); otherwise the copy shows the host's attack the owner's way (the host's fragment, as every copy not on him;
  its blow the host's to judge). Why the engine retargets the copy is not found (the moment: every copy of his fight out of the relation
  at once while he fought on, then the engine's own retarget). **Not measured on the candidate:** its run where both traces would
  overlap was cut, and in the run before it the joiner's trace rotated away (the bench, below); there the fallback took 16 stretches,
  with no fault.
- **E.2 the joiner's blows on copies never judged locally** (`mp_copy_hit_local_none`, on). **Cause:** the joiner's blow on a copy ran
  his own game's whole hit: damage, stamina, the copy's flinch or stagger, sometimes its death -- then the host's verdict arrived and the
  copy's health was put back. **Built:** the engine's own "no" for a hit, used for exactly this pair. The melee handlers' hit processor
  (`RPGProcessHit`, found by its label and checked by its prologue and its code) returns "filtered" -- what the engine itself says for the
  second frame of a blade passing through a body -- when the local player hits a copy of the host's NPC. Before that it runs what the
  processor runs, under the same actor lock, but the hit core with its "apply" argument off: the form the engine's own two dry-run callers
  use (record and result filled, nothing applied; both callers are checked at install). The copy still plays the impact: its own hit event
  with that result, material effects only, the hit reaction stopped by the game's own entity context (`combat_actorSupressHitreactionAnimation`,
  set and cleared by the mod on the copies it drives, only where it was not set already). The blow goes to the host as a blow with no
  numbers and a "judge it there" flag; the host's engine judges it like any of the figure's blows. The one-target-per-swing rule and the
  engine's own repeat filter are mirrored. A fault in any engine call switches E.2 off for the session (the old way). **Found on the way:**
  the gate sits at the start of the hit processor, which the host's replay checks by its first bytes before it arms: the first E.2 builds
  turned the host's replay off and no blow of the joiner's landed at all, until the replay was taught to read the bytes our own gate
  replaced. And the engine's dry run marked the copy as "hit by the player", so any other drop on it in the next 1.5 s went out as his
  blow; the dry run now skips the hit hook's own bookkeeping. **Numbers** (three fights, the dev build before the last fix): 26 blows of
  the joiner's on copies, all 26 filtered on his screen with the impact played and judged by the host's engine (51 of 51 host-side blows
  through the engine, 0 raw); 0 faults. Since the last fix (four three-fight runs): 45-54 of the joiner's blows on copies a run, every
  one filtered with its impact played and judged by the host's engine; 0 judged the old way, 0 faults, never switched off. The twin's
  run was cut.
- **E.3 defence judged locally** (`mp_combo_hold`, on: a combo's later swing holds the host's verdict for the swing's window plus a round trip,
  at most 650 ms). **Found on the way:** the dev builds had *bad fights* -- per fight the baseline judged 79-91 % of the blows on the joiner
  with his own game every time, the dev builds 83-87 % in most fights and 2-64 % in some. In a bad fight one or two copies were out of the
  engine's skirmish relation with the joiner while the mod held them in his fight: the joiner's engine refused their every swing at him
  (each frame, for minutes; 200 refusals in the baseline's ten fights, thousands in a dev build's three) and the host judged their blows.
  The relation fails even right after the engagement's own skirmish add, so adding the copy again (tried: the engagement's add every
  2 s) changed nothing and was removed. **Cause, as far as found:** in 0.63.8 the joiner's own hits on a copy ran WO-136's retarget,
  a leave and a re-add, which also pulled a copy that had drifted into another skirmish (an ended fight, a stopped one) back into his;
  E.6 removed that route (the WO's own crash route) and E.2 keeps his hits off the copies, so nothing put such a copy back, and an add
  alone does not move a soul out of the skirmish it is in. **Built (`fight_heal`, a fix with its twin):** a copy the mod holds in his
  fight that the engine answers "not in a fight" about for a second is healed the engine's way -- its stale fight ended (the game's own
  StopFight on its soul, as WO-201's combat end does) and every copy's hold in his fight let go (WO-209's own recovery when he leaves his
  fight), so the mirror engages each again with a fresh add; at most once every 5 s. **What the heal does not do** (found after, with
  the E.1 split above): it does not bring back a copy its own engine retargeted to the host's figure -- 50 heals in one such four-minute
  stretch, none took. The difference in the table below between the heal and its twin is the spread of three fights each (which fights
  had such a stretch), not the heal. It is kept (on, with its twin) as harmless -- no fault in eight fight runs -- but whether it ends
  any stretch cannot be told from these runs.

  | blows on the joiner judged by his own game (three fights, per fight) | all | per fight | refusals | first swings local |
  |---|---|---|---|---|
  | baseline 0.63.8 (ten fights) | 87.9 % | 79-91 % | 200 | 91.7 % |
  | baseline 0.63.8 (three fights) | 85.0 % | 91 / 85 / 78 | 300 | 76.4 % |
  | candidate | **88.0 %** | 97 / 93 / 76 | 5,100 (55 heals) | **89.5 %** |
  | candidate, the heal's twin | 69.9 % | 72 / 64 / 72 | 10,300 | 74.0 % |
  | earlier dev builds (before the heal) | 35-75 % | 2-97 | 2,500-24,600 | 46-73 % |

  The acceptance (95 % in every fight run) is not met, as on the baseline: the rest is the copies' own reactions and the round trip.
  The candidate's own run judged 20 % locally (20 / 0 / 34 per fight): there the host's swing capture missed most bandit attacks (the
  bench, below), and without an attack row a copy cannot swing, so every such blow is the host's -- the twin of that run had the same gap.
- **E.4 a parry's and a block's consequences on the host: explained, nothing to add.** In the engine itself an attacker whose blow is
  perfect-blocked or blocked gets exactly one reaction: the failed-attack recoil, for a stab (attack zone 5) with four weapon sets only
  (halberd, longsword, sword and shield, short sword without shield) -- the engine's own per-contact step picks it from the defender's
  perfect-block state. A slash, an overhead, a combo strike, a punch, a hook or a kick gets **nothing** (no action, no stun, no flag): its
  attack runs on to its withdraw, and the blade's bounce seen in single player is each machine's own weapon solver on a physical contact.
  The host already plays the stab's recoil when the joiner's game judged a perfect block or a block (WO-208); for every other attack type
  there is no engine reaction to play, and a made-up stagger would not be the engine's way.
- **E.5 lock-on (recorded).** With a copy swinging at the joiner from in front (45 degrees or less) his lock-on was on that copy 52 %
  (baseline) / 59 % (dev) of the swings -- the other attackers in front hold the rest; from the side 1 %, the engine's own picker rule.
- **E.6 leave and re-add: gone.** No path takes a soul out of a skirmish to change its opponent (the add alone); a copy's opponent is the
  host's (the mirror), never this machine's threat rule. 0 lines in every run.
- **E.7** the host's recoils and paired hits played on the joiner's Henry are counted in the briefing.
- **E.8 friendly fire off, crime shared: not recorded.** The smoke run that records both through fights was cut. This WO changed neither
  system.

**The fight regressions found on the way.** The early dev builds judged fewer of the blows on the joiner with his own game (49 % over a
10-fight run against the baseline's 88 %). Two causes were found and fixed: copies falling for no knockdown (the down test read limp
physics, 30 falls against 4 real knock-outs -- d9 reads the knockdown's own action) and the host's base footwork installed over a
fighting copy's own (a fight's base is the copy's own engine's, twin `fight_base_own`). The bad fights above remain: E.1's retargeted copies (their attacks now shown the owner's way, their blows the host's)
and, on the last runs, the host's capture gap.

## Part F: the joined conversation

**F.1 cause.** The listener's actions are the mod's own (`kcd2mp_listen_*`) in its exclusive listen map; the game's own list also moves on the
mouse wheel and confirms on the mouse button, which the map did not have -- the host as listener scrolled and clicked, nothing moved. Built:
the wheel and the mouse button in the listen map (no new key, no input read), and the listen-once guard by frame. **F.2:** the starter's
cursor goes to the listener (`ConvHover`, kind 14 of the conversation channel, at most 10 a second) and shows as a ring on that row; the
pick shows on its row for a second before the line plays (a line that arrives first waits up to 0.4 s for it, never more than 1.4 s); a vote,
the ring or the pick shows over a row's own icon too. Switches `mp_listen_wheel`, `mp_conv_hover`, `mp_conv_pick_hold`,
`mp_conv_mark_over_icon` (all on). **Not demonstrated on the bench:** dev build d8's joined-conversation run failed both ways
(the host as starter: no line came through; the joiner as starter: the pick not followed) on a 2.8 s stall of the joiner's game at the
vote (a burst of copy binds), not on the listener's keys; it was not re-run. The tester script's step 7 asks for it.

**F.3 the decision timer:** no timed decision is reachable on the bench saves (as in WO-207); the tester script asks for one.

## Part G: riders, wagons and caravans

**Why the field's caravan read "on foot":** the host's mount reader knows the rider-horse link only; a harnessed horse and a cart's driver are
in the NPC state's cart stance, a different link. **Why riders ran through the air:** the joiner's copy of a parked wanderer horse has no
physics, so the engine's mount is refused (a mount onto a horse without physics crashed the game before) and the rider followed his saddle
height on foot.

**Built (`mp_rider_sync`, on):** a rider whose horse this world cannot mount him on is put on the ground under the host's rider instead of
in the air; the host's own player is no longer reported as an NPC rider. **Not built:** a stand-in horse that can take the rider, and the
moving cart (a cart stream, the cart copy moved, horses harnessed, passengers seated) -- G's acceptance is not met. The rider fix was not measured on a dev build (the caravan run was cut).

## Part H: the smaller items

- **H.1 the double state request** (`mp_state_one_request`, on): one pending NPC-state request per copy; the next waits for the first's
  answer. 34-35 a fight run on the baseline -> **0** (tavern 0 -> 0; field 36).
- **H.2 props from animation events.** The engine refuses a pickup or spawn event on a copy because its reference object belongs to the
  host NPC's own state request; the copy's hand props are WO-201's (the host's hand items). Spawn errors 0 on the dev build (field 53);
  pickup errors 4-34 a run (field 466; baseline 10-150): explained, not zero.
- **H.3 a host alone climbs in memory: not fixed** (the acceptance -- flat for 10 minutes -- is not met). What was found, on the bench's
  host alone with the relay up (30-87 MB a second, varying run to run):
  - **Not the mod's own memory.** The game's Lua heap stays at 36-47 MB (its collector cycling) while the process climbs; every
    per-frame task of the DLL switched off (a test switch) still climbs 27 MB a second; every request to the DLL blocked still climbs
    25 MB a second; the ping line drawn or not makes no difference.
  - **What the new memory is:** sets of a 64 MB, a 4 MB and a 1 MB block about every 1.5 s, holding render geometry (compact static-mesh
    vertices -- half-float positions, a white vertex colour, half-float texture coordinates -- and 16-bit index buffers). The 64 MB blocks
    do not come through the process's own memory calls (a probe on them saw none): they are the graphics driver's. The large allocations
    that do go through them during the wait carry, on their stacks, the DX12 renderer's pipeline-state cache (a new pipeline state being
    compiled by the graphics driver) and the engine's asset streaming: animation databases being read (the animation system's DBA
    controller loader) and geometry chunks being loaded (the CGF loader). The game keeps streaming animations and meshes in, and building
    pipeline states for them, while a session waits -- and does not give the memory back.
  - **Found on the way and fixed** (a sound change of its own, not this climb's cause): every DLL request that needs the game's main
    thread was waited on by a new operating-system thread, created and torn down per request (~50 a second on a lone host); the request
    is now posted to the main thread directly and the pipe waits on it, bounded, as before.
  - Open for the next work order: what in a waiting session keeps requesting animations and meshes (the streaming system's own debug
    output, or a stack probe at the animation database's stream completion, names the files and the caller). The tester script asks
    for the graphics card.

  | lone host, relay up, 150 s | climb (private, MB/s) |
  |---|---|
  | dev build, nothing blocked (three runs) | 87 / 54 / 42 |
  | every request to the DLL blocked but its heartbeat | 25 |
  | every per-frame task of the DLL blocked | 27 |
  | the ping line off for 80 s of 240 s | 33 before, 80 while off, 55 after |
  | without the relay (WO-209's run, the same save) | flat after 3.5 min |
- **H.4 the briefing:** the role is the agent's own session role; only this launch's marks count (the launch id); one line per instrument:
  empty bodies, stale postures, the owner's reseats, clinches, killing blows with and without the engine's reaction, bodies finished, death
  checks (unlootable), knockdowns, riders, the host's recoils and paired hits, the conversation's keys, votes, rings and picks, the clocks.
- **H.5 the time bug:** at every mark both machines' world time, the sun's time on this machine and the gap; a line when the gap is over 2
  game-minutes outside a skip. Found on the bench: after a shared wait the joiner's own clock pull was read as a save reload and undone for
  30 s (the joiner 9.8 game-hours ahead); fixed (`KCDMP_TEST_W210_OFF=skip_end_sticks` is the twin).
- **H.6 the joiner's queue.** The field's 8-12 s behind came ~8 s after every join: the host's catch-up of its quest layers sends one
  message per layer (88-89) and the joiner asked its game about each one, one a frame, though 87-88 were already active. Now one snapshot
  answers them (`mp_profile_snapshot`, on). Every bench pair: p99 under 50 ms.
- **H.7** the tester script explains voting in plain words.

## Part I: every switch safe to flip live

`toggle_live`: each switch off and on twice, in a tavern at night and in a fight. Dev build d8: 80 flips of the ten switches built by
then: 0 empty bodies, 1 stale posture (1.7 s, `mp_copy_ready` off in the fight), 0 clinch loops (the baseline's run: 1 empty body, 3
loops). The candidate's run of all 20 switches was cut: the ones added after d8 (`mp_combo_hold`, the four conversation switches,
`mp_rider_sync`, `mp_state_one_request`, `mp_profile_snapshot`) were **not flipped live** on the bench.

## Part J: the release

- **Offline gates on the candidate's source: 98 of 98 pass** -- native unit tests 10,204; relay round trip 198; agent 2,027; setup 77;
  log bundles 76; every synthetic Lua suite (WO-210's own 63); the static checks; the relay message table (97) and fault-mode (37) gates;
  the digest analyzer (62) and rig (42) tests; the save tools.
- **No-input-capture gate** (`tools/Check-NoInputCapture.ps1 -SelfTest`), first and last over every binary and script built here, the
  dev builds and the bench's own tools included: PASS both times, the self-test catching every planted capture.
- **The installer** `KingdomComeTogether-Setup-0.63.9.exe` is built from this commit with `tools/Build-Installer.ps1 -ReleaseCandidate`
  (not soak-tested: a release candidate for the maintainer's session, not a public release): the publishing gate, the payload's
  no-input-capture check and the live save round trip run inside it. The real saves' hash list was checked before and after every bench
  run of this WO -- every check a match. Installers stay local.

## Switches

Console: `mp_<name> on|off` (either machine; `mp_<name>` alone says its state). Every one ships **on**.

| switch | part | what it does |
|---|---|---|
| `mp_copy_state_always` | A | every copy always shows its host NPC's whole-body state (hand-over at every exit, the 250 ms watchdog, the re-ask after a skip, load or join) |
| `mp_death_engine` | C.1 / C.2 | the joiner's killing blow is a normal death (the engine's reaction, a lootable body); a copy dies as its host NPC did |
| `mp_knockdown_sync` | C.3 | the copy falls and gets up with its host NPC |
| `mp_copy_ready` | D.3 / D.4 | a copy draws and takes its stance with its host NPC |
| `mp_copy_hit_local_none` | E.2 | the joiner's blows on copies are judged by the host's game only |
| `mp_combo_hold` | E.3 | a combo's later swing waits for the host's word on it too |
| `mp_listen_wheel` | F.1 | the listener moves with the mouse wheel and votes with the mouse button |
| `mp_conv_hover` | F.2 | the listener sees the starter's cursor as a ring |
| `mp_conv_pick_hold` | F.2 | the starter's pick shows on its row for a second before the line |
| `mp_conv_mark_over_icon` | F.2 | a vote, the ring or the pick shows over a row's own icon too |
| `mp_rider_sync` | G | a rider no horse here can carry walks on the ground under the host's rider |
| `mp_state_one_request` | H.1 | one pending NPC-state request per copy |
| `mp_profile_snapshot` | H.6 | after a join, quest layers the joiner already has are answered at once |

The earlier switches this WO's flips cover (all on): `mp_copy_anim_owner`, `mp_copy_opponent_mirror`, `mp_copy_attack_ai`,
`mp_defence_local`, `mp_clinch_guard` (B.1's mechanism), `mp_copy_paired_engine`, `mp_copy_oneshot_request`. `mp_clinch_v2` is not in this
build (B.4). Test twins with no console switch of their own (the bench's `KCDMP_TEST_W210_OFF=<name>`): `clinch_refusal_final`,
`empty_host_neutral`, `posture_other_grace`, `no_raw_window`, `kd_by_action`, `fight_base_own`, `no_joiner_values`, `hit_fx`,
`fight_heal`, `ai_in_fight`, `skip_end_sticks`, `opp_with_row`.

## Still open

**The mod:**
- **E.3 stays under 95 %**, as on the baseline: the blows of a copy its own engine retargeted to the host's figure (E.1) are the host's
  to judge -- the copy is outside his fight on his screen, where the engine allows it no attack at him -- and so are blows while his copy
  plays its own reaction or the round trip runs out. What makes the engine retarget a copy of his fight to the host's figure is not found;
  0.63.8 undid it by accident (the leave-and-re-add on his hits, E.6's crash route).
- **H.3, the host waiting alone climbs in memory** (above): narrowed to the game streaming animations and meshes and building pipeline
  states while a session waits; the trigger is not found.
- **B.2 / B.3 a clinch played on both screens:** not built (the bench produces no clinch between a figure and an NPC); B.1 stops the loops.
- **G:** the stand-in horse and the moving cart are not built.
- **E.1:** a copy its own engine retargets to the host's figure (above) -- the retarget's trigger is not found. This build shows such a
  copy's attacks the owner's way (not measured), the host judges their blows. Before it: 97 % shown from copies on him, 0-7 % from
  retargeted ones.
- **C.1's ClientHit** (the game rules' own hit call) is not found in this build's collision handler (its call site's pattern is not
  there once); the deaths come through the finish (op 142) instead, which every kill on the bench took.
- **A Lua table of the mod's grows** on a host alone (+500 entries a minute, small); not identified.
- **Blows the host's swing capture does not see:** a bandit's blow on the figure from a paired hit action (or with no attack running)
  sends no attack row, so the joiner's copy shows nothing and the host judges it (the bench's last runs, above). The capture should
  learn those actions; not done in this WO.

**The bench:**
- The knock-out scenario produces knock-outs, not knockdowns: C.3 is measured with the game's own fall and stand-up (`kdforce`).
- The two animation probe files cover different stretches of a run; E.1 counts only the stretch both cover.
- No clinch between a figure and an NPC happens on the bench (B.2 / B.3's acceptance cannot run); a timed answer in a joined
  conversation is not reachable on the bench saves (F.3).
- The lone host's climb rate varies 30-87 MB a second from run to run on the same build.
- The fight runs drift between bandit camps from run to run (the rig sets on the three bandits nearest the host), and the per-fight
  numbers swing with it (one fight 97 %, the next 2 %); three-fight runs compare builds only roughly.
- In the last fight runs the host's swing capture missed most of some bandits' blows on the figure (73-168 a run with no swing of
  theirs captured, against 0-15 on every earlier run and 1 of 28 in the field): no attack went out, so nothing showed on the joiner's
  screen and the host judged the blow. The blows the traces cover came while the bandit was in a paired hit action or standing idle --
  not attacks to the capture. Not explained; on the twins too, so not this WO's last change (see "the mod" above).
- The animation probe rotates its file past 32 MB, and the bundles held only the newest piece: on one run the joiner's piece covered
  two seconds and E.1 could not be measured. The rig now bundles both pieces.
