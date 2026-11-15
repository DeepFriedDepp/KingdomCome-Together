# WO-221 — 0.65.2: bodies that walk right, and a horse each

`SCORE place=84.7 thing=81.5 step=1.68 slide=44 pulls=1.09 flips=0.126 lane99=685 cost95=13.0 works=5/10`  (0.65.0, the base line: mean of 3)
@@SCORE_SHIPPED@@
@@SCORE_TWIN@@

@@SUMMARY@@

Roles only: "the host", "the joiner", "the maintainer". Words as in the design audit: a copy, driven, the writer, the owner, the
stream. Footing marks: [code] read in this repository, [binary] read in the game's own files, [bench] measured on the bench (two
copies of the game on the maintainer's PC, joined through our relay with the field's link shape, both at low settings 1280×720),
[record] an earlier work order's findings, [reasoned] an inference, said as one. "The twin" is the same build with the named
switch off (for the scoreboard: every WO-221 switch off, on both machines). Run names are the bench's own; their outputs, frames,
crash records and ledgers are in the WO's notes.

## 1. The scoreboard

`tools/rig`'s scoreboard, unchanged in what it measures (three of its works-at-all checks were corrected to read what the game
does now, §3.4). Three scenes runs and three checks runs as shipped, three scenes runs and one checks run of the twin, by turns
on one build (@@BUILD@@), nothing compiled beside them.

@@SCORE_TABLE@@

A difference smaller than the base line's spread is not a change. By that rule:

@@SCORE_READING@@

### Works at all, check by check

@@WORKS_TABLE@@

## 2. Part A — the fight crash (`mp_combat_action_end`)

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

| way | as shipped | the twin (`mp_combat_action_end off`) |
|---|---|---|
| rebind (unbound and bound again at once) | 40 of 40, 0 crashes, 0 faults | 40, clean (this way alone does not crash) |
| unbind (3 s unbound; the no-brains rule hides it) | 40 of 40, clean | 40, clean |
| hide (hidden 2 s, shown) | 40 of 40, clean | 40, clean |
| death (the host's world kills its NPC) | @@RM_DEATH@@ | **the joiner crashed 77 s into the death phase, after 4 deaths with a swing queued**: an access violation reading 0x474 inside slot 17, not on the main thread — the crash this work order is about |
| remove (the joiner's own entity system removes the body) | @@RM_REMOVE@@ | not reached (the twin crashed before it) |
| session end (the joiner's agent stopped and started) | @@RM_SESSION@@ | not reached |

@@A_VERDICT@@

**The scoreboard without a crash** [bench]: @@A_SCORE_RUNS@@

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

The bar: the shop screen 10 of 10, a sale 5 of 5. **Met for the talks that started** (11 screens of 11, 5 sales — the innkeeper
twice, the grocer, the inn's maid twice — each sale's item out of the joiner's inventory and its price into his purse once; a
shop's goods and money are the joiner's own game's, so nothing crosses worlds in a sale). **Missed by one start in twelve**: one
request timed out and its retry too. Two keepers never took the bench's item (an onion) in any run; whether that is the keeper's
own goods list or ours is not known.

### 3.3 The forced dialogue, the mount

- **Forced dialogue** (the bar: starts 5 of 5): 4 of 6 in trade8 — the two misses were one villager the game has nothing to force
  on (the same request on the host's own game starts nothing either). The rig now passes such a villager over for the next. On the
  checks runs: @@FORCED_WORKS@@.
- **A copy follows its rider onto a horse** (the mount check): @@MOUNT_WORKS@@. 0.65.0: never passed.

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

@@C4_TABLE@@

@@C4_READING@@

**What a pull and a flip are** [code] [bench]. A pull is the *engine* moving a written body away from the writer's last write within
one frame (over 50 cm); a flip is a 10 s window with a pull over 5 m in which the body moved in over 40 % of the frames. The flips
are not the walk's: in the run that was read body by body, one seated guest made 13 windows in a row with one 6.11 m pull each
(the copy sits on a seat 6.11 m from its NPC's place; the writer writes, its own seat puts it back), other seated guests were
placed 3–26 m by the seat rule, and one body was snapped every 3 s with a height difference of 5 m. `mp_holders` is for these.

**The other marks**: @@C4_OTHERS@@

### 4.3 The delete list (C.5)

@@C5@@

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

@@D_PROOFS@@

## 6. Part E

### 6.1 E.1 — the dead the joiner did not see die (`mp_corpse_look`)

[code] The host sends its dead bodies' blood and the joiner's DLL writes it by the game's own dirt helper; the clothes are
WO-216's; a dead NPC within 40 m of the joiner whose head is still over 0.9 m up is laid down by the game's own ragdoll (once per
3 s for six tries, then every 15 s), on the agent's 2 s tick. [bench] Three staged corpses — the save's own body, a villager killed
while the joiner was 600 m off, a body made dead by an event — lie on both screens, none upright, none in the bind pose (framed
pairs in the WO's notes, frames e1); the far villager took the sixth try, the event body the first. Before the rule ran (it first
sat in a function the agent stops calling once a body is "shown") the far kill and the event body lay on the host and stood on the
joiner. @@E1_TWIN@@

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
WO's notes (frames e2: the pass and a near frame, host and joiner). @@E2_TWIN@@

### 6.3 E.3 — an item dropped out of a horse's inventory (`mp_horse_drop`)

[code] The drop detector counts the player's own horse's inventory too. [bench] @@E3@@

## 7. Part F

- **The recorders off the main thread** [code] [bench]. Measured first on the host (a cost line per pipe operation and the frame's
  largest item, kept): 17 % of the host's frames spent over 1.5 ms in the mod. The bug report's recorders cost 550 µs a frame on
  average and 11 ms at most. Their files are now opened, written, flushed (once a second) and rotated on a writer thread of their
  own; the main thread hands it finished lines. Not moved: every engine read (main thread by nature) and the text of a sample,
  which is built between those reads; a cache of the digest's node per path was weighed and left out (a stale node pointer is a
  crash). **The bar (the host's p95 under 1.5 ms on the scoreboard's cost line) is not met**: @@F_COST@@. What is left, by area
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

@@BARS_MISSED@@

## 9. README status for 0.65.2

The README is the maintainer's to edit; these are the lines to paste. The version is named in three places: the title line's
badge or heading, the "Download" / installer name line, and the status table's heading.

@@README_BLOCK@@

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

Script calls added (the game's own binds, main thread by construction): `Player.GetHorseId`; `RagDollize` (E.1's lay-down);
`DialogModule.ForceDialog` (B); inventory create / remove / list on a horse through WO-217's ledger helpers (D.4); `SetWorldPos`
for a called horse's put-down (D.2); the game's pause / resume console commands for a recorded horse (D.5); `GetParent`,
`GetChild`, `GetLocalBBox`, `GetAngles` / `SetAngles` on a cart's wheel entities (E.2). The holders' rules (C) and the horse
modules' list (D.1) make no engine call. Reused as they are (audited by their own work orders): WO-215's lookup, WO-136's ride,
WO-217's ledger moves, WO-195's death.

### 10.2 The gates

@@GATES@@

### 10.3 Save safety and conduct on the maintainer's PC

@@SAVES@@

## 11. The switches

`mp_w221_status` lists them; each is on, each is its own twin when off:
`mp_combat_action_end` (A), `mp_shop_keeper`, `mp_talk_hold`, `mp_talk_force` (B), `mp_drive`, `mp_holders` (C), `mp_own_horse`,
`mp_saddlebags` (D), `mp_corpse_look`, `mp_cart_seat`, `mp_cart_wheels`, `mp_horse_drop` (E), `mp_route_quiet` (F).
