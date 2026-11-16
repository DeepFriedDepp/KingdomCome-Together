# WO-221 — 0.65.2: bodies that walk right, and a horse each

`SCORE place=84.7 thing=81.5 step=1.68 slide=44 pulls=1.09 flips=0.126 lane99=685 cost95=13.0 works=5/10`  (0.65.0, the base line: mean of 3)
`SCORE place=78.0 thing=78.1 step=1.06 slide=31 pulls=0.20 flips=0.034 lane99=914 cost95=10.2 works=9.3/10`  (0.65.2 as shipped: mean of 3)
`SCORE place=89.8 thing=84.0 step=1.74 slide=44 pulls=1.05 flips=0.283 lane99=819 cost95=9.5 works=9/10`  (the twin, every WO-221 switch off: mean of 3)

**In short.** The joiner's crash when a fighting body is taken away mid-swing is fixed (forty removals clean, the twin
crashes); a different crash after many deaths near the joiner is not (2 to 26 deaths in a staged massacre: Known Issues). The trade
works for the joiner (shop screens 7 of 7 started talks on the final build, sales go through once) and the works-at-all checks
pass 9, 9 and 10 of 10 (0.65.0: 5). Each player owns a horse: buy, call, ride together, saddlebags, a host reload and a rejoin
all hold on the final build. The dead lie, cart riders sit and wheels turn. Walking copies walk on their own legs with the host's
step and a fifth of the pulls, but are within half a metre of the host's place less often: the walk's bars (C.4) are not met, so
nothing is deleted. The host's p95 cost fell from 13.0 to 10.2 ms against a bar of 1.5. Every switch ships on.

Roles only: "the host", "the joiner", "the maintainer". Words as in the design audit: a copy, driven, the writer, the owner, the
stream. Footing marks: [code] read in this repository, [binary] read in the game's own files, [bench] measured on the bench (two
copies of the game on the maintainer's PC, joined through our relay with the field's link shape, both at low settings 1280×720),
[record] an earlier work order's findings, [reasoned] an inference, said as one. "The twin" is the same build with the named
switch off (for the scoreboard: every WO-221 switch off, on both machines). Run names are the bench's own; their outputs, frames,
crash records and ledgers are in the WO's notes.

## 1. The scoreboard

`tools/rig`'s scoreboard, unchanged in what it measures (its works-at-all checks were corrected to read what the game does
now: three in §3.4, the leave step and the body loot below the works table). Three scenes runs and three checks runs as shipped, three scenes runs and one checks run of the twin, by turns
on one build (`35e99e8`, the build this release ships), nothing compiled beside them.

| | fs1 | fs2 | fs3 | **shipped, mean** (spread) | ft1 | ft2 | ft3 | **twin, mean** (spread) | **0.65.0** (spread) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| place (within 0.5 m, %) | 77.7 | 78.2 | 78.1 | **78.0** (0.5) | 92.9 | 83.5 | 93.1 | **89.8** (9.5) | **84.7** (11.2) |
| thing (same activity, %) | 79.2 | 75.6 | 79.5 | **78.1** (4.0) | 85.1 | 81.9 | 84.9 | **84.0** (3.2) | **81.5** (7.2) |
| step (copy / host) | 1.00 | 1.17 | 1.00 | **1.06** (0.17) | 1.76 | 1.76 | 1.68 | **1.74** (0.08) | **1.68** (0.18) |
| slide (% of speed) | 42 | 40 | 10 | **31** (32) | 44 | 43 | 45 | **44** (2) | **44** (10) |
| pulls (a body-minute) | 0.18 | 0.30 | 0.12 | **0.20** (0.18) | 0.91 | 1.04 | 1.21 | **1.05** (0.30) | **1.09** (0.80) |
| flips (a body-minute) | 0.026 | 0.026 | 0.050 | **0.034** (0.024) | 0.072 | 0.421 | 0.357 | **0.283** (0.349) | **0.126** (0.193) |
| lane99 (ms) | 771 | 865 | 1105 | **914** (334) | 725 | 916 | 816 | **819** (191) | **685** (75) |
| cost95, the host (ms) | 10.7 | 10.0 | 9.8 | **10.2** (0.9) | 9.2 | 9.0 | 10.2 | **9.5** (1.2) | **13.0** (1.4) |
| works (of 10) | 9 | 9 | 10 | **9.3** (1) | 9 | 9 | 9 | **9** (one checks run) | **5** (0) |

Scenes runs fsN / ftN, checks runs fwN / ftw1 (each shipped scenes run is scored with its own checks run; the twin's three with
its one). No game closed in any of the ten runs; faults 0 wherever the frame line was written.

A difference smaller than the base line's spread is not a change. By that rule:

- **Better than 0.65.0, outside its spread:** the step (×1.06 against ×1.68), the pulls (0.20 against 1.09), the host's cost
  (10.2 against 13.0 ms), the works checks (9.3 against 5), and the slide (31 against 44, spread 10 — but the shipped runs
  themselves range 10–42). Better than the twin by the same rule: the step, the pulls, the flips.
- **Worse than 0.65.0, outside its spread:** the lane's 99th percentile (914 against 685 ms, spread 75) — the twin's is worse too
  (819), so it is not the WO-221 switches'.
- **No change by the rule:** the activity (78.1 against 81.5, spread 7.2), the flips against 0.65.0 (0.034 against 0.126, spread
  0.193) and the place (78.0 against 84.7, spread 11.2) — though all three shipped runs' place lies under 0.65.0's lowest run
  (80.3), and the twin's is higher (89.8): the driver costs place (§4.2).

### Works at all, check by check

| check | fw1 | fw2 | fw3 | the twin (ftw1) | 0.65.0 (3 runs) |
|---|---|---|---|---|---|
| the join | yes | yes | yes | yes | yes |
| a talk to its choices | yes | yes | yes | yes | yes |
| a trade screen | yes | **no**: two talk requests not taken (one timed out by the engine) | yes | **no**: both talks started, no shop screen (`mp_shop_keeper` off) | no |
| a forced dialogue | yes | yes | yes | yes | no |
| a loot take (a chest and a body) | yes | yes | yes | yes | not run (no body) |
| a door use | yes | yes | yes | yes | yes |
| a drop picked up by the other | 2 of 2 | 2 of 2 | 2 of 2 | 2 of 2 | yes |
| a knockdown and get-up | **no**: 2 falls, 1 get-up shown | yes | yes | yes | yes |
| a mount | yes | yes | yes | yes | no |
| a sleep vote | yes | yes | yes | yes | yes |

**The checks runs were run twice** on the final build. In the first set (kept as fw1–fw3 / ftw1 "-leak" in the WO's notes) the
rig's own leave step did not end the dialogues it started: it asked the player's `IsInDialog`, which reads false while the mod's
dialogue stand-in holds the player's talk, so the talk check's merchant dialogue ran on through the trade check (no trade could
start) and the forced check's dialogue (a surrender, with choices) was still open at the sleep check, where the joiner's game
refused the sleep (the twin's forced dialogue happened to be a four-second chat; its sleep passed). The loot check waited for
WO-134's take line while WO-217's live loot (on since 0.64.4) took the item by its own lines. Both are the rig's
(`tools/rig/scen220.py`: a check's dialogue is ended by its own id until the engine's end line for it is in the log;
`tools/rig/scenarios.py`: the body take read from WO-217's ask, its applied verdict and the host's body one item lighter);
nothing that counts as a pass was loosened. 0.65.0's base line ran with the old leave step: its trade "no" may be partly the rig's.

## 2. Part A — the fight crash (`mp_combat_action_end`, `mp_ragdoll_once`)

**Cause** [binary] [bench]. The 0.65.0 joiner's crash dump: the faulting function is `C_CombatAnimAction`'s "fragment started"
callback (vftable slot 14); slot 17 ("the fragment is about to end") and 18 read the same thing — the body's combat part (the
actor's +0x300, then +0x490) — without a check. The engine gives a body's combat part back whenever the body's level of detail
drops (`C_Actor::SetMonsterLOD`), when it dies or is reset and when its actor is destroyed, all through one virtual,
`C_Actor::ReleaseCombatActor`. An action the mod had queued on that body (a swing, an owner's install; queued with no time-out, it
can wait minutes behind the body's own leftovers) is then run by the body's action controller the next time the player comes back
to it. In the dump the action was the mod's own, on a villager's copy that had died 13 minutes earlier, 970 s after it was queued.

**Fix** [code]. Every combat action the mod queues is recorded with its actor (`native/KCDMP/combat_actions.cpp`).
`ReleaseCombatActor`'s slot in `C_Actor`'s and its derived classes' vftables (found by RTTI, checked by the function's own bytes,
seven slots) runs ours first, while the combat part is still there: each recorded action of that body is stopped the engine's own
way (`IAction::Stop`) and its vftable pointer is set to a copy whose callbacks that reach for the combat part do nothing. The
session's end ends every recorded action.

**The removal test** [bench] (the WO's notes: `remove221.py`): copies near the joiner are made to swing by the mod's own swing,
each is taken away mid-swing, and the joiner is sent 600 m away and back after every ten so that the engine drops the far bodies'
combat parts. The test found three more faces of the same thing, each in the mechanism's own death way, each fixed in turn:

1. *The table deleted what the queue still held* (run rm3, 25 deaths in: an access violation executing a data address from the
   action controller's walk of its queue, on an animation worker thread). A rule a later build of the table had gained — "an action
   only this table still holds, reference count 1 or less, is dropped after 5 s" — deleted two of the owner's own waiting installs;
   the count does not say what the controller's queue holds. Now the table gives its reference back only for a finished action;
   every other record stays until its body's combat part is given back and is then forgotten with the reference kept (0x1A8 bytes
   that are never freed; the table holds 16,384 records, the oldest is ended and forgotten, never deleted).
2. *A stop on a dying body* (run rm4, 26 deaths in: an access violation reading 0 in a scope's per-layer clip swap, 1.0 s after a
   death was applied). The owner's let-go of a dead body called the game's stop on its base action and lowered its scopes'
   priorities while the death's own animation was being installed. Now the owner only gives its references back for a dead body.
3. *A death takes the combat part apart without the hooked call* (run rm6, 9 deaths in: the queue walk ran into a freed waiting
   dialogue-gesture install; the table had deleted nothing, and of 627 calls the release hook saw in that run none carried a combat
   part). Now, for a death the mod applies, the DLL ends every recorded action of that body first (the game's stop and the quiet
   vftable, whose delete slot is silent too, so the block stays allocated under whatever still points at it), and the agent waits
   200 ms before it applies the death.
4. *Heap corruption after deaths, in the engine's allocator* (run rm11 on the final dev build, 2 deaths in: the main thread, a
   time-of-day update into the renderer; run rm13 on the build after it, 22 deaths in: an animation worker thread, the action
   controller's update). Both stop in the same allocator path (one CrySystem global, then the system's heap), following a free-list
   link that is not one: in rm13 the chain runs from a block into an action scope's vftable and on into code bytes — a live object's
   pointer sat on a free list, so something wrote into memory after it was freed. Every crash of the death test (rm3, rm4, rm6, rm11,
   and the twin's) came within seconds of a dead copy's skeleton change (a ragdoll); in rm4 and rm11 about a second after WO-195's
   ragdoll of a body 0.65 s dead. `mp_ragdoll_once` (the fourth approach) makes it one ragdoll per body at a time, after the game's
   own death action had 2.5 s, never on a body that already lies — and rm13 crashed 2.2 s after a death with no ragdoll in the six
   seconds before it. So the ragdoll timing is not the cause. Whose write it is was not found; the leading suspect is approach 3's
   own end of a dying body's actions (the stop and the quiet vftable) on a block the engine had already freed when a death took the
   body's combat part apart without the hooked call — unproven.

| way | as shipped | the twin (`mp_combat_action_end off`) |
|---|---|---|
| rebind (unbound and bound again at once) | 40 of 40, 0 crashes, 0 faults | 40, clean (this way alone does not crash) |
| unbind (3 s unbound; the no-brains rule hides it) | 40 of 40, clean | 40, clean |
| hide (hidden 2 s, shown) | 40 of 40, clean | 40, clean |
| death (the host's world kills its NPC) | **not clean**: crashes at 25, 26, 9, 2 and 22 deaths (rm3, rm4, rm6, rm11, rm13; one face each, above); 18 deaths without one (rm7, rm8: staging ended them) | **the joiner crashed 77 s into the death phase, after 4 deaths with a swing queued**: an access violation reading 0x474 inside slot 17, not on the main thread — the crash this work order is about |
| remove (the joiner's own entity system removes the body) | 40 of 40 (26 with a swing queued), 4 trips of 300 m, 0 crashes, 0 faults (rm12) | not reached (the twin crashed before it) |
| session end (the joiner's agent stopped and started) | 2, not 40: 148 recorded actions ended in two session ends, 0 faults (rm9, rm14). After one the restarted agent is not joined and the joiner holds no copy, so each needs its own launch of the pair; forty were not run | not reached |

**Verdict.** The crash the work order is about — a combat action of ours running on a body whose combat part is gone — is fixed:
the removal ways that crashed 0.65.0 by it (a level-of-detail drop on a far trip, a removal, a session end) are clean, and the
twin crashes. The bar (0 crashes) is **not** met for deaths: four approaches each removed one face of a crash that follows deaths
on the joiner, and the fifth face is open. In a staged massacre (a death every 15 s near the joiner, swings queued on the dying)
the joiner's game closed once per 2 to 26 deaths; in the field one death near the joiner is far rarer, and 0.65.0 closes by the
twin's face as well. Both switches ship on; Known Issues names it.

**The scoreboard without a crash** [bench]: every finals run on the final build — six scenes runs, four checks runs and the four checks runs the rig first spoiled —
and the four E runs: no game closed, faults 0 wherever the frame line was written.

## 3. Part B — the trade and the basic checks

### 3.1 Why the trade failed

Three faults stood behind "the trade fails" [bench] [binary].

1. **No shop** (`mp_shop_keeper`). The game finds a shop by its keeper, and a shop has a keeper only while the keeper's own NPC
   state holds its "open shop" element (the keeper's brain puts it there when it takes up its work). A joiner's copy never takes up
   its work. 0.65.0: the dialogue reaches the trade option and the game ends it with "Failed to find shop from dialog" (5 of 12
   trades). Now the host lists its keepers near the players and their shops every 10 s; the joiner's DLL makes the copy that shop's
   keeper by the call the element's activation makes (`C_Shop::Open`, on the AI update's thread) and takes it off again when the
   host's keeper keeps the shop no longer.
2. **The woken copy's own schedule** (`mp_talk_hold`). A copy woken for a talk wakes with its own scheduler, which asks for its
   job's NPC state at once; the dialogue (requested, or forced) waits behind it, its twins are never made, and the mod's rescue ends
   it. Now, while a copy is held for a talk, its scheduler's state requests are refused with the engine's own refusal value — the
   engine's request function is gated, nothing is called — and it is woken only once the DLL holds it. A talk the copy never takes
   is started the game's forced way (`mp_talk_force`).
3. **The mod's own rescue.** WO-179's rescue ends a conversation that shows no sign of running 3.5 s after its start. Its signs
   never appear before a first choice, and its dialogue-id rule wanted the player first among the souls: every rescue of one proof
   run (3 of 3) ended a healthy conversation waiting for the player's choice. Now a choice line of a dialogue the player is a soul
   of counts as running. (The first build of this rule never ran: the agent sees only the engine lines on the log tail's forward
   lists, and the choice lines were on none. They are forwarded now, with a test that pins the rule's literal against the list.)

### 3.2 The trade, measured

| run | build | talks started | shop screens | "no shop" | sales | rescued at its choices |
|---|---|---|---|---|---|---|
| trade1 | 0.65.0's trade | — | 2 of 12 | 5 | — | — |
| trade2 | + the keepers' shops | — | 8 of 9 | 0 | — | — |
| trade3 | + the talk hold | 10 of 12 | 10 of 12 | 0 | (staging) | all forced ones |
| trade5 | + the scheduler held, the forced start | 10 of 10 | 9 of 10 | 0 | (staging) | 3 |
| **trade5, the twin** (`talk_hold`, `talk_force`, `shop_keeper` off) | | 7 of 7 | **2 of 7** | **5** | 1 | 5 |
| trade7 | + the rescue's choice rule seen | 5 of 6 | 5 of 5 started | 0 | (staging) | **0** |
| **trade8** | the same, the sale's keys right | **11 of 12** | **11 of 11 started** | **0** | **5** | **0** |
| trade9 | f2 | 6 of 6 | 6 of 6 | 0 | 4 of 6 (the 2 others: an empty sell list for the bench's onion) | 0 |
| **trade10** | **f3, the final build** | **7 of 8** | **7 of 7 started** | **0** | 4 of 8 asked (the same two keepers twice: empty sell list) | 0 |

The bar: the shop screen 10 of 10, a sale 5 of 5. **Met for the talks that started** (11 screens of 11, 5 sales — the innkeeper
twice, the grocer, the inn's maid twice — each sale's item out of the joiner's inventory and its price into his purse once; a
shop's goods and money are the joiner's own game's, so nothing crosses worlds in a sale). **Missed by one start in twelve**: one
request timed out and its retry too. Two keepers never took the bench's item (an onion) in any run; whether that is the keeper's
own goods list or ours is not known.

### 3.3 The forced dialogue, the mount

- **Forced dialogue** (the bar: starts 5 of 5): 4 of 6 in trade8 — the two misses were one villager the game has nothing to force
  on (the same request on the host's own game starts nothing either). The rig now passes such a villager over for the next. trade9 (f2): 5 of 5;
  trade10 (f3): 2 of 5 — three women with nothing to force (no dialogue at all), both tries on a man started. On the
  checks runs: 3 of 3 shipped, the twin's 1 of 1 (each check's own villager).
- **A copy follows its rider onto a horse** (the mount check): 3 of 3 shipped and the twin's (the copy mounted and dismounted). 0.65.0: never passed.

### 3.4 The rig's checks, staged properly

Three checks failed on what the rig read, not on what the game did [bench]: the talk helper gave up at the first cancelled request
(the mod re-asks at 4 s and forces at 8 s; the talk did start); a forced dialogue the game answers with a "chat" was not counted;
the loot check waited for an older work order's line while the loot screen did open. All three read the game's own lines now
(`tools/rig/scen220.py`, `scen217.py`, `scenarios.py`); nothing about what counts as a pass was loosened — a talk has to reach its
choices, a loot take has to move the item.

## 4. Part C — the walk (`mp_drive`, `mp_holders`)

### 4.1 What was built

[code] WO-220's probe ported (`native/KCDMP/body_drive.cpp`, `npc_drive.cpp`): a human copy whose host NPC walks is moved by the
engine's per-frame movement request. Its speed is the NPC's plus a pull along the NPC's way; it is driven only where the engine
runs the body in full; it arrives by the request; a body that makes no way is the writer's for a rest. One table says who holds a
body (driver, writer, seat, activity, fall, mount, one-shot); a body its own seat holds is the seat's. `mp_holders` adds two rules
for bodies the joiner's engine holds elsewhere than the stream says: a written body the engine moves back over 2 m twice within
30 s is left where the engine holds it (60 s, doubling to 480 s, ended when its host NPC has walked 3 m off); a driver's placement
that is repeated within 8 s hands the body to the writer for a rest (30 s, doubling to 240 s).

Three approaches were measured in turn [bench]: (1) every walking copy driven, start hysteresis and the "host walks" rule
(place 75.7–76.2 %, pulls 0.24–0.42); (2) the driver's arrival, far-body and back-off rules (the run with place 84.9 %, pulls
0.18, walking bodies within 0.5 m 90.1 %); (3) the holders' rules above, against the flips.

### 4.2 The bars (C.4)

The three shipped finals (fs1–fs3) beside the three twins (ft1–ft3, every WO-221 switch off) and 0.65.0's base line, all on the
final build's scoreboard [bench]:

| measure | bar | fs1 | fs2 | fs3 | the twin (mean; runs) | 0.65.0 (mean) | held? |
|---|---|---:|---:|---:|---:|---:|---|
| step period, copy / host NPC | within 10 % | ×1.00 | ×1.17 | ×1.00 | ×1.74 (1.76/1.76/1.68) | ×1.68 | 2 of 3 (fs2 had four walkers) |
| planted-foot slide, copy | host's walkers + 5 | 42 % (host 8) | 40 % (host 22) | 10 % (host 5) | 44 % | 44 % | 1 of 3: missed by 29 and 13 points |
| the same place, all bodies | ≥ 84.7 % and outside its spread | 77.7 % | 78.2 % | 78.1 % | 89.8 % (92.9/83.5/93.1) | 84.7 % (80.3–91.4) | 0 of 3: 6.6 points below |
| the same place, walking bodies | ≥ 90 % | 68.7 % | 86.1 % | 59.4 % | 85.1 % (89.6/74.3/91.4) | 77.3 % | 0 of 3 |
| pulls over 50 cm a body-minute | ≤ 0.2 | 0.18 | 0.30 | 0.12 | 1.05 (0.91/1.04/1.21) | 1.09 | 2 of 3 |
| flips a body-minute | 0 | 0.026 (7) | 0.026 (3) | 0.050 (10) | 0.283 (3/30/20) | 0.126 | 0 of 3 (an eighth of the twin's) |
| a seated guest's pulls in the tavern | under half of 0.65.0's | 21 (14 guests) | 20 (7 guests) | — (no guests) | 25 (ft2, 13 guests; ft1, ft3 no guests) | 56 and 28 (base1, base3) | 0 of 2: 21 and 20 against under 14 |
| crashes, faults | 0, and 20 driven copies removed mid-walk | 0 | 0 | 0 | 0 | — | met: 0 in six runs; 23 driven copies removed mid-walk (9 on f3, 14 on f2, the same drive code), 0 crashes |

(The place of the walking bodies is the scoreboard's "the host's NPC walks" row; 0.65.0's mean is of its three runs, 75.7, 71.5
and 84.8 %. The tavern scene lasts five minutes; how many guests it had was the save's hour, not the build's.)

**Reading.** The driver does what it was built for and costs what it was feared to cost. The feet: the step is the host's (×1.00
in the two runs with a dozen or more walkers, against ×1.74 for the twin), and the pulls fall to a fifth of the twin's and of
0.65.0's (0.20 against 1.05 and 1.09), the flips to an eighth. The place: a driven body walks on its own legs towards where the
host's NPC is and arrives a little late or a little wide, so the walking bodies are within half a metre less often than when the
writer places them (71 % against 85 %), and the slide is the host's own in one run and well over it in the other two (42 % against
8, 40 % against 22; walkers at an ordinary pace slide too, so it is not only the running guards). The rest of the loss in place
is the tavern's: with guests in it the shipped build put them 54–61 % within half a metre, the twin 83 %, because `mp_holders` leaves
a guest where the joiner's own game holds him (on his seat) instead of fighting it — the twin's tavern had 22 flips in 118 windows,
the shipped build's 3–5 in 750–1,000. Without the tavern the shipped village was 87.2, 97.3 and 77.6 % (the twin 94.4, 91.3,
94.2 %; 0.65.0 81.1, 85.4, 86.8 %).

**C.4 does not hold on any of the three runs.** Missed: the place of all bodies (by 6.6 points under the base line), the walking
bodies' place (by 4 to 31 points), the slide in two runs, the flips (not 0), the tavern's pulls (21 and 20 against under 14), the
pulls in one run (0.30) and the step in one (×1.17, four walkers). Three approaches were measured (§4.1), the third twice (score5
and the finals). The driver ships on with `mp_drive off` as its switch.

**What a pull and a flip are** [code] [bench]. A pull is the *engine* moving a written body away from the writer's last write within
one frame (over 50 cm); a flip is a 10 s window with a pull over 5 m in which the body moved in over 40 % of the frames. The flips
are not the walk's: in the run that was read body by body, one seated guest made 13 windows in a row with one 6.11 m pull each
(the copy sits on a seat 6.11 m from its NPC's place; the writer writes, its own seat puts it back), other seated guests were
placed 3–26 m by the seat rule, and one body was snapped every 3 s with a height difference of 5 m. `mp_holders` is for these.

**The other marks**: *the frames* [bench] — a driven walker beside its host NPC and a handover, framed on both screens at once.
Four tries to frame them: walk1-f1 void (a copy closed at the pause), walk1-f2 (the viewers placed by the walker saw walls),
walk2-f3 (the check passed, but the walker had walked out of the shot by the time it was taken — looked at, not counted), and
walkc-f3 (the viewers put 4–7.5 m ahead of the walker on its own heading, with a clear line, facing back). Of walkc's four pairs,
two show it: the same villager at the same spot by the white house on both screens just as he arrived from his walk (the
arrival, where the driver hands him back), and the same road scene on both screens with the walker on the road and a guard beside
it, each at the same place; the other two missed (an empty road, a wall). *Removals*: 23 driven copies taken away mid-walk on the
f2 and f3 builds (the drive code is the same in both), 0 crashes, 0 faults.

### 4.3 The delete list (C.5)

**Nothing is deleted.** The order deletes WO-220's list for the walk (the route and its replay, the gait hysteresis, the
extrapolation and blend layers, the six yield rules) only when C.4 holds on all three runs; it held on none (§4.2). The route
stays in the build, off by default (`mp_route`), and the host no longer reads or sends its lines for a joiner who has it off
(`mp_route_quiet`, §7). The writer's layers stay because the writer still places every body the driver does not drive, and
`mp_drive off` must give 0.65.0's walk back whole.

## 5. Part D — a horse per player (`mp_own_horse`, `mp_saddlebags`)

### 5.1 D.0 — how the game itself handles the player's horse

[binary] [bench] The player's horse is the player soul's primary companion of kind Horse (`Player.GetHorseId`). A purchase is a
quest module per horse for sale (`horsebuying_utils/horseinstance`): the cash or haggle branch, the payment, then
`ReplacePlayerHorse(Horse)` makes the bought one his and the previous horse is killed with its body hidden. "Available to buy" is
a rule *per player*: the horse is not dead, the buyer has no horse or this one is not the buyer's, and the horse stands inside the
trader's area — and the dialogue takes the price before any quest node runs. The whistle is the horse's own brain reading the
player's command and walking, then sprinting, then dashing to the player for 15 s of game time, then the game's own put-down near
the player. The bags are the inventory screen's horse side; a bought horse's starting saddle is a reward to the buyer. In 0.65.0
a joiner's purchase is not replayed on the host (the purchase node sits in an included dialogue the replay did not treat as his
talk): he pays once in his own game, his game's horse is the copy, the host's world knows nothing, his whistle is WO-160's fetch,
and after a rejoin he has no horse. The engine's goal request moves a host horse only while its brain is paused; with its brain
running it walks back to its stable.

### 5.2 What was built

[code] **Owning (D.1).** The host's agent keeps a record of whose horse is whose, keyed by each joiner's install key, stored with
the world's co-op data (beside the chest ledger) and paired with every save of the host's: a reload brings back that save's
record, and a save with no paired record empties it. A joiner's purchase is his conversation's: a dialogue node of the same quest
module as the talk's own is the talk's too, so the purchase's out-ports replay on the host and the host's world sells the horse;
there the quest gate passes the "player's horse" node through without running it (and the disposal of the old horse after it),
refuses the purchase scene and the buyer's reward for the host's Henry, and keeps the payment from the host's purse. The agent
learns from the passed node which quest module sells each recorded horse and tells the DLL; a "player's horse" node under such a
module is passed through in the host's *own* conversation too, so a host who buys a joiner's horse at his own groom keeps his
horse, the joiner keeps his, and the host is told (the price stays with the groom: Known Issues). Riding the partner's horse is
never theft: the record's horses are not the crime system's to judge. **Calling (D.2).** A joiner's whistle calls his own horse by
the engine's goal request, made plain on the AI update's thread, with the game's own put-down after 15 s; the host's whistle is
the game's own and never brings a recorded horse. **Riding (D.3)** is WO-136's ride on the recorded horse. **Saddlebags (D.4).**
A recorded horse's inventory is its owner's list: the host's horse follows the owner's list, the partner's take waits in escrow
until the owner's game gives the item up; a refused or timed-out request changes nothing. **Away (D.5).** A recorded horse waits
paused on the host while nobody rides or calls it.

### 5.3 The proofs

[bench] Three runs on the pair (frames in the WO's notes: the purchase on both screens, the call, the ride as each player sees the
other):

| | f1 (horse5) | f2 (horse6) | f3 (horse7, the final build) |
|---|---|---|---|
| D.1 buy | joiner bought one, paid once; the host bought another; neither touched by the other | joiner bought one, paid once; the bench's driver made the host pick the joiner's horse three times: it stayed the joiner's each time, the host was told and the price stayed with the groom (Known Issues) | joiner bought one (paid once, 170); the host bought his own; neither touched by the other |
| D.2 call | 22.3 → 4.8 m in 6 s; the host's horse 0 m | 56 → 4.3 m in 12 s (the game's own put-down at 15 s); the host's horse 0 m | 22 → 8 m in 9 s; the host's horse 0 m |
| D.3 ride | not run (the bench's heading did not hold: the rider hit an overhang) | joiner alone 525 m, 17/17 stretches mounted; with the host 270 m away his copy of the horse jumped back 20–128 m twice (the game's fall-through rescue: Known Issues) | **side by side**: 535 m and 681 m, 24 stretches, both mounted throughout, the horses at most **1.25 m** apart between the two games (mostly 0.1–0.4 m) |
| D.4 bags | 4 moves, duplicated 0, lost 0 | 4 moves, duplicated 0, lost 0 | 4 moves both ways, counted on both sides |
| host save + reload | record paired and back; **the joiner's game then held the host's horse** (the fault below) | the fix: the joiner's horse is his again 1 s after his session comes back | record paired and back; the joiner kept his horse, the host his |
| D.5 rejoin | horse moved 0.0 m in 60 s away; back in 90 s, told, has it | 0.0 m; back in 85 s | 0.0 m; back in 136 s with his horse |

**The reload fault (found in f1, fixed in f2, `750c786`).** After the host loads a save, the joiner's world comes from that save,
whose "player's horse" is the host's. The joiner's session is inactive while the host loads (about 190 s on the bench), and every
inactive tick wiped the mod's word on the joiner's horse; the mod then waited for the host's next send (every 30 s) and two more
ticks. Now the mod asks its agent at once (`w221_horseask`, 3 s apart at most), the agent answers from the host's last word or asks
the host, and the first tick after the word applies it. Proof: the synthetic Lua scenario W1–W7 (W3–W6 fail on the old Lua) and the
bench (f2: asked at once, the horse his 1.0 s after the session came back; f3: kept).

## 6. Part E

### 6.1 E.1 — the dead the joiner did not see die (`mp_corpse_look`)

[code] The host sends its dead bodies' blood and the joiner's DLL writes it by the game's own dirt helper; the clothes are
WO-216's; a dead NPC within 40 m of the joiner whose head is still over 0.9 m up is laid down by the game's own ragdoll (once per
3 s for six tries, then every 15 s), on the agent's 2 s tick. [bench] Three staged corpses — the save's own body, a villager killed
while the joiner was 600 m off, a body made dead by an event — lie on both screens, none upright, none in the bind pose (framed
pairs in the WO's notes, frames e1); the far villager took the sixth try, the event body the first. Before the rule ran (it first
sat in a function the agent stops calling once a body is "shown") the far kill and the event body lay on the host and stood on the
joiner. **On the final build** (e1s, f3): the three bodies lie on both screens — the head's height over the body's place −0.07, −0.42 and
−0.08 m on the joiner, −0.01 to −0.07 m on the host (the game's own bone positions; a standing body reads 1.55 m). **The twin** (e1tw, f3, `mp_corpse_look off`): the save's
own body lies, and the far villager and the event body **stand** on the joiner (head 1.55 m up, hips 0.90 m) while they lie on
the host. Frames: the event body lies on the road on the joiner's screen (e1s); the far body's frames of both runs are blocked by
a living villager who stepped into the shot — the bone heights are the proof for that one.

### 6.2 E.2 — carts (`mp_cart_seat`, `mp_cart_wheels`)

[bench] Measured on the save's caravan first: the cart's copy within a metre of the host's, both horses at their place and
unhidden — and the rider seated in the first sample and standing from then on, the wheels still. Two causes for the rider: the
puppet start's stance reset clears the engine's own "cart" pose tags (a body that is a cart's child is not reset now), and the
mod's own walking clip — picked from the speed the cart carries the rider at, started on the seat's layer and parked 100 ms
later — replaced and then stopped the seat's animation (a cart's child gets no clip and no stop now). The wheels are entities of
their own turning about their local x axis; on the joiner nothing rolled them. Now the joiner turns them on its 50 ms tick by the
way the cart's copy moved along its own heading over the wheel's radius.

The caravan pass on the shipped build (30 samples, by day, nearest pass 24.9 m): the rider seated in 30 of 30 samples (head
1.09–1.14 m over the cart on both screens); the joiner's wheels turned 58.1 rad each, the host's 56.7–58.7; both cart horses
within 0.59 m (p95) of their place, unhidden; the cart's copy 0.95 m (p95), 1.59 m at most from the host's. Framed pairs in the
WO's notes (frames e2: the pass and a near frame, host and joiner). **On the final build** (e2s, f3; 30 samples, 5 near the joiner): the rider seated in 5 of 5 near samples (head within 0.25 m of
the host's), 0.05 m off his place at the cart (p95); the joiner's wheels turned 58.0 rad each, the host's 57.6–58.2; both cart
horses within 1 m of their place in 5 of 5, 0.63–0.65 m (p95), unhidden; the cart's copy 0.87 m (p95), 1.34 m at most from the
host's. **The twin** (e2tw, f3, `mp_cart_seat` and `mp_cart_wheels` off): the joiner's wheels turned **0 rad** (the host's
55.8–57.4), the rider's head was within 0.25 m of the host's in 3 of 5 near samples; the horses and the cart's place, which
these switches do not touch, were as shipped (cart 1.18 m p95).

### 6.3 E.3 — an item dropped out of a horse's inventory (`mp_horse_drop`)

[code] The drop detector counts the player's own horse's inventory too. [bench] Horse inventories at 180 m down the open lane from the stable (f2, f3) and at the
trader's yard (f1):

| item | joiner's horse → host | host's horse → joiner |
|---|---|---|
| weapon (mace) | 3/3 | 3/3 |
| money (25) | 3/3 (f2: it reached the host in 0.9 s; the bench's ground read missed it) | 3/3 |
| tool (broom) | 3/3 | 3/3 |
| apple | 2/2 at 180 m; **1/1 vanished** at the yard | 3/3 |
| onion / food | 2/2 at 180 m; **3/3 vanished** at the yard (seen by the host after 2.3 s, gone 4.5–9 s after it was made there, nobody has it) | not tested: the bench's script cannot put food into the host's own horse (every try made nothing) |

Every drop went through the ledger: no duplicate anywhere; the vanished food is a loss, never a copy. Cause of the yard's losses not
found; Known Issue as it stands, with `mp_horse_drop off` as its switch.

## 7. Part F

- **The recorders off the main thread** [code] [bench]. Measured first on the host (a cost line per pipe operation and the frame's
  largest item, kept): 17 % of the host's frames spent over 1.5 ms in the mod. The bug report's recorders cost 550 µs a frame on
  average and 11 ms at most. Their files are now opened, written, flushed (once a second) and rotated on a writer thread of their
  own; the main thread hands it finished lines. Not moved: every engine read (main thread by nature) and the text of a sample,
  which is built between those reads; a cache of the digest's node per path was weighed and left out (a stale node pointer is a
  crash). **The bar (the host's p95 under 1.5 ms on the scoreboard's cost line) is not met**: on the three shipped finals the host's p95 was 10.70, 9.97 and 9.82 ms (mean 10.2 ms; 0.65.0: 13.0 ms), its mean
  2.14, 1.86 and 1.79 ms (0.65.0: 2.79 ms) — the p95 a fifth lower, the bar missed by 8.7 ms. The twin (every WO-221 switch off,
  the recorders' thread kept: it has no switch) held 9.5 ms. What is left, by area
  (the host's own lines, mean / max a frame): the two stream reads the joiner's copies live on (the host's animation line and the
  opponents, 6–7 a second each, 1.1–1.5 ms mean, 9–13 ms max — the largest item in a fifth of the slow frames; not recorders); the
  recorders' own engine reads and sample text (0.55–0.78 ms mean, 11–12 ms max); the health sampler, WO-138's and WO-213's ticks
  (0.1–0.17 ms each); WO-176's tick and the crash watcher (under 0.07 ms mean, 9–21 ms max).
- **The briefing reads only this launch** [code]. Every line of the native log starts `[time] t<thread> L<launch id>` (six hex
  digits, made once per injection) and the probes' rows carry the same id; the log bundle's briefing reads only the newest launch
  of each file (`LaunchFilter`, with tests on a two-launch file).
- **The route lines** (`mp_route_quiet`) [code]: C.5 did not delete the route, so the host stops reading and sending route lines
  nobody uses — a joiner says every 10 s whether its `mp_route` is on; the host's 20-a-second route read and its lines run only
  for joiners that said yes (or said nothing: an older build). With the default (`mp_route` off) none is read or sent.

## 8. Every bar missed

| part | bar | result on the final build | by how much | what was tried |
|---|---|---|---|---|
| A | 0 crashes in the removal test, deaths included | removals, unbinds, hides, rebinds 40 of 40 each, clean; **deaths: the joiner crashed after 2 to 26** | five crashes in five death runs that reached 25 deaths | four approaches, each removing one face (§2): the release hook, the table that never deletes what the queue holds, no stop on a dying body, ending a dying body's actions first, and one ragdoll at a time (`mp_ragdoll_once`); the fifth face (a write after free in the engine's heap) not found |
| A | 40 session ends | 2, both clean (148 actions ended, 0 faults) | 38 not run | each session end leaves the joiner unjoined and needs a launch of the pair; the session's time went to the death crash |
| B | the talk starts 10 of 10 | 11 of 12 (trade8), 7 of 8 (trade10, f3) | one start in eight to twelve | a request is asked again at 4 s and forced at 8 s; the miss is a request the engine itself timed out twice (Known Issues) |
| B | a sale 5 of 5 | 5 of 5 asked in trade8; 4 of 8 in trade10 | the misses are two keepers who never take the bench's onion (an empty sell list), and one talk that did not start | not traced: the keeper's own goods list or ours is not known (Known Issues) |
| B | the forced dialogue 5 of 5 | trade9 5 of 5; trade10 2 of 5 | three villagers the game has no dialogue to force on (the host's game starts nothing either) | the rig passes such a villager over for the next, three at most |
| C.4 | step, slide, place, walking place, pulls, flips, the tavern's pulls | §4.2: none of the three shipped runs holds all; step 2 of 3, slide 1 of 3, place 0, walking place 0, pulls 2 of 3, flips 0, tavern 0 of 2 | place 6.6 points under the base line; walking bodies 4–31 points under 90 %; slide up to 29 points over; flips 0.026–0.050 a body-minute against 0; tavern 21 and 20 pulls against under 14 | three approaches (§4.1), measured on seven scoreboard runs; C.5 deletes nothing |
| E.3 | a drop out of a horse's inventory reaches the other player, food included | weapons, money, tools 3/3 both ways; the joiner's food 2/2 at 180 m; **food at the horse trader's yard vanished 4 of 4**; the host's food untested | food at the yard: all lost (never duplicated) | the drop runs through the ledger like any drop; the yard's losses are not traced (Known Issues, `mp_horse_drop off`) |
| F | the host's p95 under 1.5 ms | 10.2 ms (9.8–10.7) | 8.7 ms over (0.65.0: 13.0) | the recorders on a writer thread of their own; what is left is engine reads on the main thread (§7) |

## 9. README status for 0.65.2

The README is the maintainer's to edit; these are the lines to paste. The version is named in three places that must agree: the
release badge at the top (`latest release-v0.63.9` today), the "As of release …" line under the status table (0.63.9 today), and
the Known issues link (`docs/KNOWN-ISSUES.md`, whose heading says 0.65.2). For this build: the badge and the "as of" line read
0.65.2 once the maintainer publishes it.

The status table, with this build's changes (rows the build does not touch are as they stand in the README today):

```markdown
| Works well | Works, but rough | Not yet |
|---|---|---|
| Joining a friend's world with your own character, or a new one | Joining each other's conversations: you listen in and vote on answers, but the camera and keys can misbehave | Playing far apart (you are kept within about 650 m) |
| Seeing each other: walking, riding, clothes, weapons | Villagers walk on their own feet on the partner's screen, but are not always in exactly the same place; now and then one is pulled back | Grabs in fights |
| Fighting the same enemies: hits, knockdowns and deaths show for both | Some villagers stand idle, or work without their tools, where the host sees them busy | Shared chests and shop stock (each player has their own) |
| Shared quests: either of you can move a quest forward | Cutscenes play for both, but one can leave the partner on a black screen for a while | Road encounters matching on both screens |
| Looting bodies and dropping items for each other | The partner's game can close after many deaths nearby | Stolen marks on shared items |
| Sleeping together, one shared clock | Paired moves and weapon draws in fights don't always show | The regular (non-Modding-Tools) game |
| Crime: the host's guards judge both of you | Bows and crossbows: the shot shows, the draw and aim don't | |
| A horse each: buy, call, ride and use the saddlebags of your own | The partner's horse looks bare (no saddle) on the host's screen; food dropped from a horse's bags by the stable can vanish | |
| Trading as the partner: the shop opens and selling works | Lip sync, voice chat and dice between players are unpolished | |
| Carts on the partner's screen: riders sit, wheels turn | | |
```

What changed against the README's table: three rows added under "Works well" (a horse each, trading as the partner, carts);
"Trading as the partner can close by itself" is gone from "Works, but rough"; "Villagers glide when they walk" is replaced by the
walking line above; the crash after many deaths, the bare horse and the food at the stable are new under "Works, but rough";
"Carts moving on the partner's screen" is gone from "Not yet". The line under the table: `<sub>As of release 0.65.2.</sub>`, and the
badge: `latest%20release-v0.65.2`.

## 10. The audit, the gates, save safety

### 10.1 Thread and lock audit of every engine call added

The rule: an engine movement or AI call is made on the thread and at the point in the frame the engine makes it; no engine call
that may take an engine lock runs under our fault guard ("guarded" = our structured-exception catch). Memory reads of engine
objects are guarded everywhere (they take no lock) and are not listed.

| # | part | call | thread, point in the frame | guard | may lock? |
|---|---|---|---|---|---|
| 1 | A | `IAction::Stop` on a recorded combat action | inside our `ReleaseCombatActor` slot (the thread the engine releases the part on, before the engine's own body); and on the main thread in the pipe op that announces a death | guarded | no: sets the action's flags; read whole |
| 2 | A | the vftable pointer swap to the quiet copy | as 1 | guarded | no: one interlocked compare-exchange |
| 3 | A | the action's own delete when the table's reference was the last (finished actions only) | main thread (the table's tick), session end | **plain** (the count is written under the guard, the delete is called outside it) | yes: frees memory |
| 4 | B | `C_Shop::IsKeeper` / `Open` / `Close` | the AI update's entry, on that update's own thread | **plain** | yes |
| 5 | B | the scheduler's state request refused for a held copy | the engine's own call; our gate returns the refusal instead of running the body | no engine call is made | — |
| 6 | C | the movement controller's per-frame request, per driven copy | main thread, in our frame tick before the engine's entity update — where the engine's own AI proxy hands its request | guarded | no: a field-by-field merge, read whole in WO-220 (no lock, no allocation without the actor-target flag, never set here) |
| 7 | C | the movement task's cancel of the copy's left-over request | the AI update's entry, on its thread | **plain** | an AI call |
| 8 | D.1 | the quest node's out-port fired without running the node | inside the engine's own node execution, same thread, same point | **plain** | yes (runs the game's graph on) |
| 9 | D.2 | the goal request for a called horse | the AI update's entry, on its thread | **plain** | yes: the manager's own mutex |
| 10 | D.2 | the cancel of our request for the horse | the AI update's entry, on its thread | **plain** | as 7 |
| 11 | E.1 | the dirt helper's accessor, blood read and set | main thread (a pipe op in our frame tick; the game's own blood script call runs there too) | **plain** | unknown (the accessor allocates) |
| 12 | F | none: the writer thread touches our own files only | its own thread | — | — |

Script calls added (the game's own binds, main thread by construction): `Player.GetHorseId`; `RagDollize` (E.1's lay-down; A's `mp_ragdoll_once` only refuses or delays it, reading the head bone by `GetBonePos`);
`DialogModule.ForceDialog` (B); inventory create / remove / list on a horse through WO-217's ledger helpers (D.4); `SetWorldPos`
for a called horse's put-down (D.2); the game's pause / resume console commands for a recorded horse (D.5); `GetParent`,
`GetChild`, `GetLocalBBox`, `GetAngles` / `SetAngles` on a cart's wheel entities (E.2). The holders' rules (C) and the horse
modules' list (D.1) make no engine call. Reused as they are (audited by their own work orders): WO-215's lookup, WO-136's ride,
WO-217's ledger moves, WO-195's death.

### 10.2 The gates

Every offline gate on the final tree (the build `35e99e8` plus this commit's documents and the rig's two checks), run once after
the last bench run with nothing else on the machine: **100 of 100 passed** — the native build and its unit tests, the pak rebuild,
the relay round trip, the agent, setup and log-bundle tests, every synthetic Lua and C# suite, the protocol and version checks.
**The no-input-capture gate** (`tools/Check-NoInputCapture.ps1`, with its self-test: every planted capture caught, the clean
files passed) ran first and last over the native DLL and injector, the bench's builds, the rig, the harness and every test tool
built here: "RESULT: PASS -- nothing checked can read what a person types or clicks", both times. The shipped mod takes no
screenshot (no screenshot call in its DLL, agent, relay or scripts; the bench's frames are taken by the rig through the game's
console). The same gates ran again after the
rebase and before the installer (below); the installer's own safety gates and the publishing gate are hard.

### 10.3 Save safety and conduct on the maintainer's PC

- **Before the first launch** the maintainer's whole saves folder was copied byte for byte into a git-ignored folder of the
  repository, with a list of every file's SHA-256 and size (1,136 files). Checked against that list again after the last bench run (2026-11-15, 16:23): changed 0,
  gone 0, added 0.
- **Every bench run** played in the bench's own mirror folders (two linked copies of the game with their own user folders); none
  saved into a real slot. The rig checked the real saves' hash list at each run's start and end: MATCH every time, never a
  difference; the mirror-roots check passed at each run's end.
- **The host's horse record and the chest ledger** live in the mod's own data folder, never in a save file; a save the record is
  paired with is named by its hash, not changed.
- **Items and money** moved between the players only through the ledger (the saddlebags' escrow, the horse drops, the trade's sale
  in the joiner's own game): duplicates 0 in every run that counted them; the losses found (food at the horse trader's yard; a
  money pile claimed after the host's reload) are losses, never copies.
- **The maintainer's PC**: his launcher, agent and relay were not running and were not touched; no Windows setting was changed;
  the bench's games ran in their own windows without taking focus from anything of his (the rig's own "front" helper only orders
  its two copies). The maintainer's own modified documents and untracked files were never staged.

## 11. The switches

`mp_w221_status` lists them; each is on, each is its own twin when off:
`mp_combat_action_end`, `mp_ragdoll_once` (A), `mp_shop_keeper`, `mp_talk_hold`, `mp_talk_force` (B), `mp_drive`, `mp_holders` (C), `mp_own_horse`,
`mp_saddlebags` (D), `mp_corpse_look`, `mp_cart_seat`, `mp_cart_wheels`, `mp_horse_drop` (E), `mp_route_quiet` (F).
