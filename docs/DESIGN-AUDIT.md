# WO-219 — The design audit: is the way we build this the reason it falls short?

Read on the tree at `79018ea` (version 0.65.0). This work order fixed nothing, built nothing and shipped nothing: no installer, no
version, no change to the mod's code. It read the code, measured on the bench and reasoned. Roles only: "the host", "the joiner",
"the maintainer", "a tester". "A copy" is the joiner's local body of one of the host's NPCs; "a figure" is the body that stands for the
other player.

**The footing of every claim** is marked:

* **[code]** — read in this repository's code by this audit (file and place given where it matters).
* **[binary]** — read in the game's own Modding Tools files (function names and class names the files carry), with our own tools.
* **[bench]** — measured by this audit on the test bench (two copies of the game on one PC, joined through our own relay with the
  field's link shape; three sessions on the 0.65.0 code, the maintainer's real saves backed up before the first and unchanged
  after each), or computed by this audit from the probe files of the 0.65.0 acceptance run.
* **[record]** — taken from an earlier work order's findings (its own bench or field measurement), not measured again here.
* **[reasoned]** — an argument, not an observation. Every recommendation that rests on reasoning says what would prove it or kill it.

The working material (the full system map, the table of writers, the inventory, the runs and frames) is kept with the work order's
notes, not in the repository.

---

## 1. The verdict

**Yes, in part — and it is the part the players see.** The design is right about *who decides* and wrong about *how a body is
shown*. Host authority, two full games, quests by replayed decisions, loot by request and ledger, doors, time and weather are sound;
their open items are bugs and gaps that a fix ends. The family this work order asks about — gliding, activities that do not play,
bodies pulled between two places, fights that half-show — comes from three decisions about a copy's body, and patching will not end
it: **(1)** a copy's place is *written* from outside every frame instead of the body being *moved* through the engine's own movement
machinery, which is still running underneath and has to be fed or fought one part at a time; **(2)** a copy's actions are *re-installed*
from the host's animation state, and since 0.61.5 a copy is forbidden to ask the engine's own activity system for anything, which is
the only thing that can put a hammer in a hand; **(3)** one body's position, action, activity and fight travel on four lanes with four
delays, so "the same thing at the same moment" is rebuilt on the joiner from pieces that were never apart on the host. The record
already contains the proof that the other way works: every part of a body that the mod hands to the engine as a request (a
knockdown, a mount, an outfit, a copy's own attack, a death) meets or nearly meets its bar, and every part it writes as a value does
not. This audit's bench shows the same for the walk: at the same speed a copy's step takes 1.85 times its NPC's under the position
writer and 1.05 times when the engine's own movement moves it (section 2.2). The road from here is not a rewrite: it is finishing a migration the project has already made for half of the body, in an order
that proves each step before the next.

---

## 2. What the goal looks like in numbers today

Nobody had measured the three promises directly; each work order measured its own mechanism. This audit measured two things.

### 2.1 "The same world": position and action, every body, one clock [bench]

From the probe files of the 0.65.0 acceptance run (every default as shipped; 2.5 hours; village, tavern, smith's house, fights, loot):
every sample the joiner's probe took of one of the host's NPCs, against the host's own samples of that NPC at the same moment (within
0.6 s; the host's position taken between its two nearest samples). 43,077 matched samples.

| | samples | within 0.5 m | within 1 m | over 2 m | the same full-body activity |
|---|---:|---:|---:|---:|---:|
| every body | 43,077 | 84.8 % | 93.0 % | 3.7 % | 66.6 % |
| within 15 m of the joiner | 12,349 | 86.0 % | 91.8 % | 4.9 % | 63.3 % |
| 15–40 m | 22,470 | 84.7 % | 94.3 % | 2.8 % | 79.6 % |
| beyond 40 m | 8,258 | 83.2 % | 91.3 % | 4.2 % | 36.0 % |
| while the host's NPC walks | 6,411 | 78.3 % | 92.6 % | 5.5 % | 73.2 % |
| while the host's NPC does anything but stand or walk | 29,293 | 84.3 % | 91.9 % | 4.6 % | 72.2 % |

"The same activity" compares the name of the action on the full body, with variations of one activity counted as the same and a
walk's start and stop counted as the walk. It is a strict measure (a copy 0.4 s late with a new action counts as different for those
0.4 s) and it includes the acceptance's staged fights. Read it as: **at a moment picked at random, a body on the joiner's screen is
where the host's is about nine times in ten, and is doing what the host's is doing about two times in three.** The commonest
differences within 40 m: the host's NPC stands and the copy has nothing on its body or is walking; the host's walks and the copy stands;
a fight's footwork; cooking and eating shown as standing or walking.

This audit's own short run on the same build (8 minutes after a join: the village by day, the smith, the inn; 5,181 samples) gave
the same picture: 86.9 % within 0.5 m, 93.7 % within 1 m, 2.8 % over 2 m, the same activity 69.6 %.

### 2.2 "Nothing added": the walk [bench]

A walk is right when the feet agree with the ground. This audit sampled the hips and both feet of the bodies near the players on
both games at once, twenty times a second, with every skeleton updated, and took for each walking stretch the body's speed, the
left foot's step period, and how fast the foot moves over the ground while it is the planted foot (it should stand still).

**As shipped (the position writer), 0.65.0, the village by day:**

| the same NPC, the same walk | on the host | its copy on the joiner |
|---|---|---|
| a villager walking 1.4 m/s (18 s) | step period 1.00 s; planted foot slides at 4 % of the body's speed | **1.85 s; 44 %** |
| a second villager, 1.4 m/s | 1.00 s; 5 % | **1.85 s; 53 %** |
| a man running 3.6 m/s | 0.60 s; 13 % | 0.75 s; 35 % |
| a dog, a horse | 0.90 s each | 1.45 s; 2.40 s |
| every body that walked on both (5): median | **0.90 s; slide 13 %** | **1.85 s; slide 45 %** |

A copy takes one step in the time its NPC takes nearly two, at the same speed, and its standing foot travels at almost half the
body's speed. This is the glide, and it is the number WO-201 measured (1.82–1.86 s against 0.96–1.07 s) seventeen work orders ago.

**The same village, the same session, with `mp_route on`** (the copies moved by the engine's own movement, WO-216's replay):

| | on the host | the copies on the joiner |
|---|---|---|
| every body that walked on both (11): median step period | 1.00 s | **1.05 s** |
| median planted-foot slide / body speed | 8 % | **5 %** |
| seven of the eleven | — | period within 11 % of the host's, slide 3–7 % |
| four of the eleven (a crier, two women, the innkeeper) | — | still slid in some stretches (44–76 %): the route hands a copy back to the writer for a correction or a fight |

**When the engine's movement machinery moves the copy, the step is right and the foot stands.** Nothing else changed between the
two tables: the same build, the same stream, the same link, the same villagers. The glide is not in the link, the stream or the
animation data. It is the position write.

**And four minutes and forty-one seconds after the route went on, the joiner's game crashed** — an access violation on a job
thread inside the engine's AI update, 62 ms after the joiner's own world had removed two guards the route was moving (both within
35 m of the joiner). It is the family of WO-216's crash (the joiner's own world deleting a body while a movement plan of ours runs on
it); the 100 m rule WO-216 added does not cover it. The route as built shows the right walk and cannot ship.

**Place against motion — the dilemma in one table.** From the same session's probe files: every sample of a copy taken while its
host NPC walked (and showed its walk), what the copy's body showed and how far it was from its NPC.

| while the host's NPC walks | the writer (as shipped) | the route: the host's own goals replayed (WO-216) | the route by WO-116's rules: follow the newest sample, no lead, up to ten orders a second, no corrector under 8 m |
|---|---:|---:|---:|
| samples (bodies) | 151 (11) | 308 (20) | 266 (13) |
| the copy shows the walk | 78.8 % | 80.8 % | 45.1 % |
| it is in a start or a stop | 0.7 % | 10.1 % | **46.6 %** |
| it stands, turns, or has nothing on its body | **19.8 %** | 0.3 % | 0 % |
| within 0.5 m of its NPC | **88.1 %** | 38.6 % | 6.4 % |
| within 1 m | 96.7 % | 62.3 % | 15.0 % |
| over 2 m away | 0 % | 12.0 % | **72.9 %** |
| median distance | 0.23 m | 0.70 m | 2.89 m |
| the copy's step period against its NPC's | **1.85×** | 1.05× | not measured (the bench's sampler did not start on the joiner) |
| its planted foot's slide, of the body's speed | **45 %** | 5 % | not measured |
| the joiner's game | ran | **crashed** after 4 min 41 s | ran (4.5 min) |

The first two columns are two windows of 150 s in one session; the third is a third session on the same build at the same place.
One run each: small numbers, large differences.

**Three readings.** (1) The writer keeps a copy *where* its NPC is and gets the *motion* wrong — and one time in five the copy
does not even show a walk while it is moved. (2) The replay gets the motion right and loses the place. (3) The third column was this
audit's own test of a hypothesis it had formed from the record: that the route failed because WO-215 led its orders and added a
corrector, against two rules WO-116 had measured alone. **The hypothesis is wrong.** With WO-116's rules exactly, on the pair, a copy
spends half its NPC's walk starting and stopping and trails by three metres: every new goal is a new plan, as WO-215 said. The
goal request cannot follow a stream, led or not. Every work order on the walk has traded place for motion or motion for place;
section 4.2 is about the one door that has not been tried.

---

## 3. The map of faults

Each fault: the promise it breaks (1 the same world, 2 nothing missing, 3 nothing added), what the player sees, its cause, and its
class — **bug** (the design is sound here, the code is wrong; one fix ends it), **gap** (sound, never built), **consequence** (the
design produces it; fixes move it around).

The decisions a consequence is traced to (each is argued in section 4):

* **D1** — a copy's place is *written* every frame, not moved through the engine's movement machinery (4.2).
* **D2** — a copy's actions are *re-installed* from the host's animation state, not requested (4.2, 4.3).
* **D3** — a copy asks nothing of the engine's activity system: "the neutral rule", since 0.61.5 (4.3).
* **D4** — one body travels on four lanes with four delays: position, action, activity and extras, fight (4.5).
* **D5** — the joiner's own world keeps running and is silenced one system at a time (4.1).
* **D6** — a copy's brain is paused per body and given back for a feature (a talk, a scene, a guard) (4.3).
* **D7** — one authority, the host, everywhere (4.4).
* **D8** — the edge of each game is Lua, a console, a log tail and a pipe; per-body decisions live outside the game (4.5).
* **D9** — how the work is done (4.9).

### 3.1 Bodies

| fault | promise | what the player sees | cause | class |
|---|---|---|---|---|
| Gliding, moonwalking | 3 | a villager's feet slide; the step is too slow for the speed | The copy is placed every frame (`npc_drive.cpp`, the frame hook) and never given a movement request. The engine's own chain for a walk — a movement request to the body's movement controller, the animated character, physics — therefore sees "no movement". WO-116 measured it for this technique before the native writer was built: "the engine never leaves idle under it" [record]. Since then the walk is coaxed at three places downstream: the speed class and velocity re-applied in a hook on the tag update, because the movement controller overwrites both every frame "with its movement request's value", which is zero (`motion.cpp:86-92`) [code]; a hook that clears a stale turn angle the paused NPC's old request left behind (`motion.cpp:1210-1218`) [code]; and the step cycle, which stays locked at its slowest example whatever is written into it — ruled out one input at a time in WO-201 §9, cause never found [record]. Measured here: a copy's step takes 1.85 times its NPC's; with the engine's own movement moving the same copies it takes 1.05 times (section 2.2) [bench]. | **consequence** of D1 |
| Three failed attempts to move copies "by the engine's own movement request" | 3 | (with `mp_route on`) copies start and stop, are pulled back; two crashes | All three used the AI's *goal* request (go to a point: path finding on the joiner's own navigation, the engine's own start and stop). WO-116 followed a moving target with it, alone, at 10 Hz with a constant lag and no stop-start, and wrote two rules from that run: never lead the order; the next order is the correction, and a body is snapped only past 8 m [record]. WO-215 measured 10 Hz renewals on the pair as flickering between idle and walk, went to two orders a second aimed 1.5 s ahead, and added a corrector that cancels the request at 1.5 m — the limit cycle WO-116 had measured for a led order, plus 12 corrections a body-minute [record; `wo215_rules.h:126-151`]. WO-216 replayed the host's own request instead: closer, still 9.8 corrections a body-minute, and a crash when the joiner's own encounter pool deleted a body mid-plan [record]. This audit ran both again (section 2.2) [bench]: the replay walks with the right step and is within half a metre 39 % of the time, and crashed the joiner once more; WO-116's own rules on the pair give a start or a stop 47 % of the time and a copy three metres behind. So the attempts were not badly made: **the goal request is the wrong grain for following** — each order is a plan. The engine has a second movement door, below the goal, that none of the three used and no work order has ever probed (section 4.2). | the grain, not the idea — **consequence** of D1 for as long as the second door stays untried |
| Activities that show late or not at all | 1, 2 | a copy stands while its NPC cooks, eats, writes | The host's action is read off its animation state ten times a second and installed on the copy by "the owner" (`wo208.cpp`). An install queues behind whatever the copy's own body still has queued; the copy's own leftovers (a look, a movement transition that never started) hold the queue [record: WO-217 A.0]. It travels on its own lane (0.25–1.5 s behind in the acceptance) [record]. | **consequence** of D2, D4 |
| Work at a forge or pot without its prop | 1, 3 | the smith stands at the anvil, no blade, no strike | Only the engine's activity system uses a smart object and puts the tool in the hand. Since 0.61.5 a copy "demands nothing" of it: `wo141.cpp:849-906` keeps every copy's required state equal to what it is and never asks for a placement [code]. WO-200's own table recorded the cost when it made the rule: "held items: gap — no item-to-hand attach exists without the planner" [record]. WO-215 and WO-216 then deferred activities to this closed path (247 deferrals in WO-216's acceptance to a placement that could not come), and WO-217 removed the deferral [record]. Seen by this audit on the bench: the same smith at the same anvil on both screens, the bar on the anvil on the host's only [bench]. | **consequence** of D3 |
| A copy's own action taking over a placed one | 1 | a sleeper sits up, a worker stretches, then is put back | The brain is paused; the machinery under it is not. The copy's own variations, callbacks, idle movement and looks keep being queued by that machinery and replace what the owner installed. Stopped one class at a time (`takeover_stop`, the unclog) [code, record]. | **consequence** of D2, D6 |
| Bodies flipping between two spots | 1, 3 | a woodcutter jumps 8 m every frame for minutes | Two writers: the stream writes the copy where the host's NPC is; the copy's own activity state aligns it to *the joiner's* object. Six separate rules decide who yields (`npc_drive.cpp:1304-1405`: swing hold, activity hold, posture hold, seat hold, knockdown, drift) [code]. A stronger rule was built and taken out in WO-218 because other layers placed the body anyway [record]. | **consequence** of D1, D2 |
| Seated guests pulled | 1, 3 | a guest jumps to another seat and back | The same pair: the stream against the copy's own seat alignment (measured in WO-116: 4.1 % of the distance per tick, straight at the seat) [record]. 107 pulls in 10 minutes in 0.64.3, 111 with the seat rule [record]. | **consequence** of D1, D2 |
| Paired attacks, clinches, blocks rarely shown | 1, 2 | one fighter's move plays, the answer does not | A paired move is one action on two bodies in step. Per-body state cannot carry it: each half arrives on its own lane. Attempts to start it the engine's way used a wrong action type and were refused [record: WO-210]. | **consequence** of D2, D4 — and a **gap** (the engine's own start was never made to work) |
| Looks | 1 | heads turn late or not | sent as rows; 72–90 % within a second [record] | **gap** (tuning), under D4 |
| Copies without physics never animated | 1 | a fighter brought from far stands still through a fight | The joiner's own engine decides which bodies have physics and get their animation updated; a body moved in from outside is not on its list. Cause not traced [record: WO-216 G]. | **consequence** of D5 (cause open) |
| A copy retargets to the host's figure | 1, 2 | host NPCs' attacks at the joiner are not shown | The copy's own combat machinery picks its own target; four modules write the fight's relations (`wo132`, `wo187`, `wo209`, `wo208`) [code]. 97 % of attacks shown when the target matches, 0–6 % when not [record]. | **consequence** of D2 |
| A joiner crash in a fight, never explained | 2 | the joiner's game closes | Every fight crash that was caught came from our writes into the engine's fight relations out of the engine's own order (WO-207, WO-208, WO-209) [record]. The one left has no dump. | **consequence** of D2 (the family); the instance is unproven |
| Weapons pop in on a draw | 3 | a sword appears in the hand | the draw is sent as a state bit, not as the draw | **consequence** of D2 (small) |
| A joiner's trade that opens half the time | 2 | the shop closes after 3 s | For a talk the copy's brain is given back; it wakes with its own plan, starts its own job, the job fails and the game ends the conversation [record: WO-218 D.5]. | **consequence** of D6 |
| Carts without hitched horses | 1 | a cart drives without its horses | the cart's body is written from the host's transform (`wo217_carts.cpp`); the hitch was never built [code, record] | **gap** |
| Horses of the animal care | 1 | a horse stands frozen | names over 63 characters; the alias is built and unproven [record] | **bug / gap** |

### 3.2 The world

| fault | promise | what the player sees | cause | class |
|---|---|---|---|---|
| Road encounters unproven | 2 | unknown | The bench cannot start one. What an encounter leaves is sent; the joiner's own encounter manager still runs, its bodies hidden as they spawn [record]. | **gap** in the method; the mechanism is untested |
| Doors differing beyond 60 m | 1 | none until a player walks there | doors are compared by a sweep around the players, once a second, in Lua [code] | **gap**; harmless as built |
| Chests and herb patches per player | 1 | a chest one emptied is full for the other | a rule (WO-134), from before the loot ledger existed | a **rule** that breaks promise 1; the ledger of WO-217 would carry it — **gap** |
| Loose items | 1 | a sword on the ground exists for one player | never shown on the bench; no fix built [record] | **gap** |
| The stolen flag | 2 | a stolen item is clean after a transfer | the mod sends it; this build has no script call to set an item's owner, and no native call was built [record] | **gap** |
| Shop stock, prices | 1 | each player has their own shop stock | not sent | **gap** |
| Playing far apart | 2 | people near the far player freeze or are missing; the leash | the host's world simulates fully only around the host's player; the NPCs near a far joiner are dormant on the host (half of the dormant bodies were over 621 m away in WO-216) [record] | **consequence** of D7 |
| Barks and ambient talk not heard; no lip-sync | 2 | silent mouths | barks are not sent; the game's facial data is not in the Modding Tools install | **gap**; a house rule (section 4.8) |
| Cutscenes that wait black | 2 | a black screen until a load | a scene waiting for a quest step the other world owns | **gap** |
| Escorts follow only their leader; carrying does not count for quests; marker letters differ | 1, 2 | as listed in the README | never built | **gap** |
| No knockdown by an enemy's blow | 2 | fights hurt less than alone | a rule | a **rule** that hides a problem (section 4.7) |

### 3.3 The machine

| fault | promise | what the player sees | cause | class |
|---|---|---|---|---|
| Our main-thread cost | 3 | a stutter on the host | 1.6–1.7 ms a frame on average, 9.4–9.9 ms in one frame of twenty; the largest items are the bug report's recorders, not the synchronisation [record: WO-218 B.2]. About 45 repeating tasks of ours are registered on the game's main thread (seven of them research probes that look every 2 s since 0.65.0, every frame before) [code]. | **consequence** of D9 (instruments in the shipped frame) |
| The joiner fell minutes behind (0.64.x) | 1 | the world arrives late | one request a frame through the pipe; fixed by the batch in 0.65.0 [record] | was a **bug**, fixed |
| Every joiner conversation hung (shipped 0.64.3) | 2 | no talk reaches its choices | the rule "hide every body the host does not stream" hid the engine's own conversation stand-ins; found by the *next* work order's first check [record] | was a **bug** — and a **consequence** of D5 and D9 |
| A stall when something loads | 3 | the joiner's game stops for a second or two | the joiner's own game loading; everything waits for that frame | inherent to D5; small |
| A listener's keys stop working in the partner's talk | 2 | the listener cannot leave or choose | not traced (about one listen in five in some bench sessions; never staged since) [record] | not determined; most likely a **bug** |

### 3.4 The pattern

Counted over the tables above: **eleven consequences sit under the three body decisions (D1, D2, D3) and the four lanes (D4)**;
four under "the joiner's world still runs" (D5) and "the brain is given back" (D6); one under host-only authority (D7); one under the
method (D9); the rest — about fourteen — are gaps and bugs with no design behind them. The eleven are the faults the players name
first, and the ones that have come back work order after work order.

---

## 4. The pillars

### 4.1 Two full games, one truth — **keep; change how the second game is silenced**

*What it is.* Each player runs the whole game. The host's world is the truth. The joiner's NPCs are copies.

*What it buys.* Everything the engine does for free on both screens: rendering, physics, animation, sound, the level, the HUD. There
is no other way to show the game: the engine's own network layer is a stub in this build [record: WO-198], there is no server mode,
and a parked third copy simulates only a bubble around its own player [record: WO-51].

*What it costs.* The joiner's game is a second simulation that keeps trying to live: its brains, its quest clocks, its encounter
manager and pools, its wildlife spawner, its door schedule, its carts, its layers. Each was silenced when a fault showed it:
brains (a per-body pause, then a census every second), clocks ("quiet clocks"), quests (a gate on node execution), spawns (hidden in
the spawn frame), doors (the joiner's own locks refused), carts (overwritten). Each un-silenced system is a future fault, and each
silencer can hit something the engine needs — the conversation stand-ins hidden in 0.64.3 [record].

*The strongest alternative* is not another architecture; it is one written contract — "on the joiner, these engine systems run,
these do not, these run but only on request" — kept as a list beside the code and checked by a census on the bench, instead of a
rule added per incident. The system map made for this audit is the first draft of that list.

*Verdict.* **Keep.** Change the practice: one list, one module that owns "the joiner's world is a display", a bench check that
counts everything on the list. *Retail:* neutral; a brain hold that does not need a console command (4.3) is a retail prerequisite.
*Footing:* [record] for the constraints, [reasoned] for the practice.

### 4.2 State, not intent — **replace for the body; keep for values**

*What it is.* We send where a body is and what its animation state is; the joiner's engine is made to show it.

*What the evidence says.* Sort the mod's own mechanisms by whether they hand the engine a value or a request [record throughout]:

| handed to the engine as a request (the engine carries it out) | its bar | handed as a value (we make the engine show it) | its bar |
|---|---|---|---|
| a knockdown and the get-up (the engine's ragdoll request) | falls 106–245 ms, get-ups 101–180 ms after the host's: **met** | a copy's walk (position written) | pulls, slides, the step: **not met** in eight work orders |
| a rider on a horse (the game's own mount) | 3 of 3 passes: **met** | an activity (the animation re-installed) | 56–100 % shown within a second, bar 90 % and none lost: **not met** |
| an outfit (the game's inventory calls) | 393 of 395 checks: **met** | a seated guest's place | 111 pulls in 10 minutes, bar 0: **not met** |
| a copy's attack at the joiner (the input "attack" given to the copy's own combat machinery) | 89–93 % of blows judged by the joiner's own engine (the twin: 0): the best fight result in the record | paired moves (rows installed) | 1 of 4, 0 of 2: **not met** |
| a loot take (a request, a grant, a ledger) | 0 duplicated, 0 lost: **met** | one writer per body | 88 flip windows: **not met** |
| a joiner's conversation (its decisions replayed on the host's own nodes) | works: **met** | | |
| a death (the engine's own hit and death) | on the ground on both screens: **met** | | |

The one request that failed is the route — and the route asked for the wrong thing at the wrong grain.

*The three grains of a walk.* The engine moves a body in a chain [record: WO-116 §2.1, read again by this audit in the files'
own names — binary]: the brain's **goal** ("go there, at this speed class": a movement task, path finding) → every frame, a
**movement request** to the body's own movement controller ("this velocity, this facing, this stance, now"), which the AI's proxy
builds and the engine's own followers rewrite on the way (the files name a modifier chain on that request for formations, target
following, combat footwork) → the animated character → physics.

* *Position* (today's writer) enters after the end of the chain. Nothing upstream knows the body moves.
* *The goal* (WO-215, WO-216, and WO-116's probe) enters at the top. The joiner's engine then plans its own path on its own
  navigation and its own timing, starts with its own start transition 0.2–1 s later, and stops with its own stop. Following a stream
  with it means re-planning; a correction means cancelling and starting again. It also keeps a plan in flight inside the engine's AI
  update, which is what crashes when the joiner's own world deletes the body under it (twice now). It is the right door for "walk
  to the forge", and the wrong one for "be where the host's NPC is" — measured three ways in section 2.2 [bench].
* *The per-frame movement request* enters in the middle. It is continuous — there is nothing to re-plan and nothing to cancel —
  and everything downstream of it is the engine's own: the walk, the step that matches the speed, the turn, the start and stop,
  the ground. WO-116 listed it as route R4, "the literal feed-the-controller technique", marked it inconclusive, and noted that the
  moment exists in the frame to make it. **No work order has probed it since** [code: nothing in the DLL or the agent names the
  request or the controller's entry; record].

*The better way* [reasoned]. A copy is **driven**: every frame, a movement request made of the host NPC's streamed velocity and
facing plus a small pull toward its streamed position (the gain sets how far a copy may trail — WO-116 measured the engine's own
follower at about 0.28 s × speed). The position stream stays, as the target and as the snap for a body left far behind. For
activities: the engine's own state request at the same object (4.3). For fights: inputs to the copy's own combat machinery, as
WO-209 already does for an attack, extended to blocks, dodges and the *start* of a paired move.

*Why expect the step to be right.* Section 2.2: the moment the engine's movement chain moves a copy (the replay goes through the
same chain from the top), the step period and the planted foot match the host's. What the replay gets wrong — the place, the
re-planning, the plan in flight — all sit *above* the per-frame request.

*What it fixes.* Gliding (the step is produced by the movement, not matched to it); start and stop transitions; the stale turn;
sinking and hovering (physics places the body); two of the three hooks in the walk's stack; the pull-and-blend after a silence
(a driven body keeps walking on its last request and is steered back, not blended).

*What it risks.* (a) The request may be overwritten on a suspended body: the AI's proxy makes its own request every frame; WO-116
measured the order of the frame and says ours can be the last word before the controller reads it — not tested. (b) The request's
layout in this build is not decoded. (c) A body steered by velocity is deflected by collisions the host's did not have; the pull
term and the snap bound it. (d) The step could still lock: WO-201 wrote the *resulting* velocity into the actor before the
controller ran and saw no change. A request is a different input, and section 2.2 shows the chain producing the right step when it
is entered from above — but nobody has entered it in the middle.

*What must be tested before anyone commits* — one copy on the bench, the brain paused, driven for five minutes beside its host
NPC. Pass: its step period within 10 % of the host NPC's; its planted foot slides no more than the game's own walkers; within
0.5 m of the stream 95 % of the time while walking; the engine's own start and stop seen; no "stuck" line; no fault. Kill: the
request is not taken on a suspended body, or the error grows without the snap.

*Retail.* The chain is stock engine plus the game's own controller in both builds; the per-frame request does not depend on a
console command, where the goal request's door is a debug command.

*Verdict.* **Replace** position-writing with driving, behind proof. **Keep** values for what is a value: health, clock, weather,
door state, loot lists, outfits.

### 4.3 Suspended brains — **keep the suspension; change what it means**

*What it is.* A copy never thinks; everything a brain would have produced is rebuilt by us.

*What the evidence says.* The pause works and always did [record: WO-107]. But "the brain is off" has been read as "the body is
ours". It is not: the movement controller, the action queue, the activity state and its alignment, the combat machinery, the
ragdoll timer, the pools all keep running under a paused brain, and nearly every row of section 3.1 is one of them disagreeing with
a value written from outside. The mod then closed the one door that would let those machines do the work: the neutral rule (D3).

*The better way* [reasoned]. **Brain off, doors open.** The decision layer stays suspended — no copy ever decides. Below it, the
copy's body is asked the way the brain asks it:

* movement: the per-frame request (4.2);
* activities: the engine's state request — "this stance at this object" — on the joiner's own copy of the same object (the two
  worlds are the same level; the objects carry the same identifiers). The request the engine uses is already found and already
  called by the mod for one-shots (`wo143.cpp`) [code]. While an activity holds a body, the activity is its one writer; the stream
  takes it back when the host's NPC leaves. That is one writer by construction, not by six hold rules;
* the copy's *own* demands (what its paused scheduler still wants) silenced at their source, once, instead of rewriting the
  copy's state every half second.

*What it fixes.* The prop and the tool; the activity's own way in and out; the seat and the flips (the copy sits because it was
asked to sit *there*, not because a stream holds it near a seat); most takeovers (the engine's own activity owns the queue).

*What it risks.* This is the largest unknown in the whole audit. The engine's planner refused or paced placements on copies before
(WO-160: an eight-refusal give-up; WO-197: held items shown 50–70 % of the time) [record]. Whether a request made on a suspended
body *at* its object, with the copy's own scheduler silenced, plays the way in, the loop with its prop and the way out has never
been tried.

*What must be tested first.* One smith, one cook, one sleeper, one seated guest on the bench: the neutral rule lifted for that
copy only, the state request made when the host's NPC enters the activity. Pass: the tool in the hand and the loop within a second
in 9 of 10 entries; no pull over 0.5 m while it lasts; the copy stands up when asked. Kill: the planner refuses on a suspended
body whatever is silenced.

*The brain given back for a talk* (D6) stays for now: the engine needs an awake partner for a conversation. Its fault (the trade
that closes) is the woken brain starting its own job; the fix is to hold its schedule while it talks, not to keep it asleep.

*Retail.* The activity system is the largest unknown of the retail port as well [record: WO-212]; a probe here pays twice.

*Verdict.* **Keep** the suspension. **Replace** the neutral rule and the owner's re-installs with requests, behind proof.

### 4.4 One authority, always the host — **keep; the earlier scheme failed for another reason**

*The earlier proximity scheme* (WO-60) gave an NPC to whichever player was nearer, by claims through the relay. It ran in a world
where **both** games' brains were awake: the pause was believed not to work until WO-107, and the record of that time says so —
"puppet brain unsuppressed", "expect continuous contention until the pause lever is proven" [record]. Two thinking worlds handing
bodies back and forth is a nightmare whoever holds the claim. That was the implementation and its moment, not the idea.

*What host-only buys.* One place for every decision; no hand-over; saves that are one world's.

*What it costs.* The host's game simulates fully only around the host's player. Far from the host, NPCs are dormant on the host,
so there is no truth to send: the leash, and "people near the far player may freeze" (4.7).

*A finer authority* earns its place for exactly one thing: playing far apart. It would mean the joiner's game running the brains
near the joiner — which needs a brain handed from one game to the other with its state, and nothing in the record shows the engine
can do that short of a save and a load. Not before the body is right; not cheap after.

*One finer rule already exists and works:* a blow at the joiner is judged by the joiner's own engine (WO-208, WO-209). Each
player's game is the authority over what hits that player. Keep it; it is the same idea the fight needs everywhere — one judge per
blow, the victim's machine.

*Verdict.* **Keep.** *Footing:* [record]; the cost of a finer authority is [reasoned].

### 4.5 The hops — **change the edge; keep the processes**

*As built* [code]. Between one agent and its own game there are three channels: a pipe to the DLL (answered on the game's main
thread), the game's debug HTTP console (the agent runs Lua through it: 324 distinct functions and 31 commands), and the tail of the
game's log (the mod "sends" by writing a line; the agent matches 358 tags). Then agent → relay → agent, and the same three on the
other side.

*What the split costs, measured.* One request a frame through the pipe put the joiner up to 53 s behind until 0.65.0 batched it
[record]. Binding a copy is a three-party hand-off — Lua decides, the agent relays, the DLL binds — and its order once stranded
copies 10–28 m off [record]. A console line sent during a load is lost; the console cuts a command silently past about 2,100
characters; the log has no clock. The activity lane's age (0.75–1.4 s at the 99th percentile) is mostly round trips between the
agent's decision and the game's frame [record].

*What belongs where* [reasoned]. Anything decided per body per frame — who holds the body, what it is asked next, when — belongs
in the game's process, in one place, on the frame. The agent is good at what is not per-frame: sessions, the link, the join, the
bug report, the tests. The relay is fine.

*The change.* (1) Lua run in-process by the DLL through the engine's own script system, and the mod's events sent over the pipe
instead of written to the log. (2) The per-body decisions (the owner, the lanes, the bind policy) moved from the agent and Lua
into one native module with one table per body.

*Retail.* Both halves of (1) are prerequisites of the retail port: the debug console does not exist there and 40 of the 95 engine
log lines the agent parses are compiled out [record: WO-212]. Doing it now makes the Modding Tools build better and the port
shorter.

*Verdict.* **Change.**

### 4.6 The engine's own machinery — what we use, what we fight, what we ignore

| the engine has | we | |
|---|---|---|
| a per-frame movement request with its own follower modifiers | ignore it; write positions | 4.2 |
| an activity state machine with smart objects, props, alignment | closed it for copies (D3); fight its alignment with hold rules | 4.3 |
| a state request (sit, stand, work, one-shot) | use it for one-shots only | 4.3 |
| an animation action system (fragments on scopes, one queue per body) | install into it directly (the owner); its queue is where our installs meet the copy's own leftovers | 4.3 |
| a master/slave start for paired moves | tried once with a wrong action type | a gap |
| a per-NPC suspend latch that only its own requester can clear | use it well | keep |
| save and load of a whole world | use it well: the join *is* the host's save loaded | keep |
| a pause of the whole game with a named source | use it well: the join hold | keep |
| a "stop evaluating, keep state" switch for quest logic | use it | keep |
| ragdoll, mount, inventory, death, skirmish calls | use them; the parts that work | keep |
| its own level-of-detail rules for physics and animation | fight them (copies with no physics) | open |
| its own networking | dead in this build | — |
| follow and escort behaviours | unused (escorts follow only their leader) | a gap; they sit on the same per-frame request as 4.2 |

### 4.7 The rules of play

| rule | does it serve the promises? | verdict |
|---|---|---|
| The leash, no far-apart play | It hides D7: one authority simulates one place. Honest while that stands. | keep; say why in the game |
| Bodies shared, chests and herbs per player | Chests per player breaks "the same world" by rule. It dates from before the loot ledger; the ledger can carry a chest. | change (a gap) |
| Joint crime | One world, one party: coherent. | keep |
| The sleep vote | Time is one; someone must be asked. | keep |
| No knockdown by an enemy's blow | It hides a problem (what a knocked-down player meant for the fight's bookkeeping) and takes something from the single-player's fight. | revisit once the fight runs on inputs (4.2) |
| Flying off | harmless | keep |

### 4.8 The house rules

| rule | what it costs the goal, concretely | verdict |
|---|---|---|
| Never touch a file outside the install | Lip-sync: the game's facial data is not in the Modding Tools install. But the launcher already links the game's data into that install (its checklist links every data pak); the facial data is a folder the same step does not link. Linking it is inside the rule as it is already practised. Untested [record: WO-212]. | keep the rule; test the link |
| Never change a Windows setting | The fight crash could not be put under the system's own heap checker. A crash that leaves no dump stays unexplained. | keep; accept the cost, or use a second machine that is not the maintainer's |
| The Modding Tools build as the base | Players install a second (free) item; no lip-sync by default; the debug console and the log lines our transport stands on exist only here; the retail port was estimated at 18–30 work orders [record: WO-212]. | keep for now; **build nothing new that only this build can do** (4.5) |

### 4.9 How the work is done — **change**

*Where the method found the truth quickly* [record]. The twin (the same build with one switch off) on a link shaped like the
field's. The probe files that record every body on both games on one clock (section 2.1 was computed from them in an hour). The
line per attempt (WO-217 A.0), which named the top reason at once. The crash capture. The first check of a work order on the
*shipped* build (it found that every joiner conversation hung in 0.64.3).

*Where it led in circles.*

1. **Work orders are scoped by symptom, and a symptom outlives its work order.** The walk: WO-190, 201, 201's follow-up, 202
   (not built), 213, 214, 215, 216 — eight. Activities shown within a second: 72 % (WO-215) → 73 / 52 / 100 % (216) → 67 / 72 /
   20 % in the acceptance (217) → 75 / 56 / 100 % (218). Seated guests: carried from 213 to 218. Each work order ends with "not met"
   and a list of what was cut (16 items in WO-218).
2. **Every fix gets a switch, ships on, and stays.** 147 named switches in the tables of WO-201 to WO-218; 145 ship on [code].
   The off side of each exists only for the bench. Fixes are stacked, not replaced: the walk's stack is eleven layers deep
   (section 6.3). WO-218's "one writer" could not be made to hold *because* of the layers under it.
3. **The code is organised by work order, not by system.** 142 of the DLL's 219 files and 106 of the agent's are named after a
   work order [code]. No file owns "a copy's body": "one owner per copy" was declared in WO-208 and thirteen things still write a
   copy's position (section 6.2).
4. **New mechanisms ship on even when their own test failed.** The route shipped on in 0.64.2 having failed its test, crashed the
   joiner in the next work order's bench, and went off in 0.64.3 [record]. The field is then the test bed, and a tester's evening
   is spent on it.
5. **Research is not carried forward.** WO-116 mapped the movement chain, ranked seven routes, proved one alone, and marked one —
   the per-frame request — "inconclusive". The work that followed adopted the per-frame *write* as "the first step". The proved
   route came back 99 work orders later and did not hold on the pair (WO-215; confirmed by this audit with WO-116's own rules,
   section 2.2). The inconclusive one was never probed, while the walk was worked on in eight work orders.
6. **Instruments can report success while measuring nothing.** The rig's bone sampler asks for joint names this skeleton does not
   have and has logged empty rows since WO-206; its merge counts an empty row as "a sample with bones" [bench: this audit ran it,
   read nil for every joint, and found the names the skeleton uses]. WO-104's "0 of 155" was the same kind [record].
7. **The instruments ship in the frame.** The largest main-thread items on both machines are the bug report's recorders
   (section 3.3).
8. **There is no standing measure of the goal.** Every work order builds its own measure; numbers across work orders do not
   compare. 0.64.3 shipped with no joiner conversation working because nothing ran every scene on every build.
9. **Two games on one PC.** The joiner at 29–35 fps turned half of WO-217's acceptance into a measure of the bench.

*What to change for the next fifty.*

* **One scoreboard, run on every build, never changed by the work order it judges:** section 2's two measures (the same place,
  the same activity; the step), pulls and flips per body-minute, the lanes' age, our main-thread time at the 95th percentile, and
  every scene's "works at all" check. A work order's result is the scoreboard before and after.
* **A probe before a build.** A mechanism whose working is not known gets a probe work order with a pass and a kill written
  first, on a scratch branch, with nothing shipped. Section 4.2's and 4.3's tests are two.
* **Replace, do not stack.** A work order that supersedes a layer deletes it in the same work order. A budget for switches: one
  per mechanism that is still on trial, removed when the trial ends either way.
* **Code by system.** `body/` (movement, activity, fight, the one table of who holds a body), `world/`, `story/`, `link/`. A work
  order adds to a system's module.
* **Off until its own test passes** — for a mechanism that *replaces a working path*. (This changes a standing rule of the
  project. It is the maintainer's to decide; the evidence is item 4.)
* **Instruments out of the frame.** Recorders sample on request or once a second off the main thread; the bug report asks for
  them when a tester presses the key.
* **The field confirms; the bench discovers.** A tester script asks whether what the bench showed is true on two PCs, not whether
  a mechanism works at all.
* **The bench's joiner at low settings**, so that both copies hold 60 fps and a measurement measures the mod.

### 4.10 The question nobody asked: what *is* a copy?

There is no written answer. A copy has been whatever the last work order needed: a body we place (WO-118), a body whose inputs we
set (WO-121), a body that demands nothing (WO-200), a body with one owner (WO-208), a body that runs its own attack (WO-209), a body
with no brain at all (WO-216). Each is true of some part of the code. None says which of the engine's machines run on a copy, who
feeds each, and who holds the body when two of them want it — so every new mechanism meets the others in the field.

The answer this audit proposes, as the contract every later work order is checked against [reasoned]:

1. **A copy never decides.** Its brain is suspended. Nothing wakes it except a conversation, and then its schedule is held.
2. **A copy's body is alive.** Its movement controller, its activity state, its combat machinery, its ragdoll and its physics run,
   and are *asked* — by a movement request, a state request, an input — never overwritten while they hold the body.
3. **One holder at a time**, in one table: driven, in an activity, in a paired move, ragdoll, carried or mounted, talking.
   Whoever is not the holder does not touch the body.
4. **One timeline.** Everything about one body carries the host's time and is shown at that time plus one delay.
5. **Values are for values**: health, the clock, a door, a list of items.

And beside it, the second question nobody asked: **how far are we from the goal, as one number?** Section 2 is a first answer.

---

## 5. The two roads

### 5.1 Road A — the design stays as it is

What must be fixed or built, in the order this audit would do it. Size: S (part of a work order), M (one), L (two or more).

| # | item | contract | approach | proof | size | risk |
|---|---|---|---|---|---|---|
| A1 | The scoreboard | every build has the same numbers | section 4.9 | it runs on 0.65.0 and reproduces section 2 | S | low |
| A2 | Instruments out of the frame | our time on the host under 1.5 ms at the 95th percentile | recorders once a second off the main thread; file probes removed from the shipped DLL | the cost meter | S | low |
| A3 | One table of who holds a body | exactly one writer of a copy's place at any moment | replace the six hold rules in the writer and the owner's own holds with one per-body state (stream, activity, ragdoll, parent, swing, talk); every other writer asks it | flips and seat pulls on the scoreboard to under 1 per body-hour | M | medium: the copy's own alignment still pulls (it is not ours to switch off under this road) |
| A4 | The step | a copy's step period within 10 % of its NPC's | find why the step cycle locks at its slowest example (WO-201 §9's open lead), or start a copy's walk through the engine's own start transition | the stride measure of section 2.2 | L | **high**: three work orders have not found it |
| A5 | Props as values | the smith holds his blade | the item-to-hand attach WO-200 named and never built: attach by a native call, align the copy to the host's object | framed pairs; 9 of 10 | L | high |
| A6 | Takeovers at the source | a placed activity is never replaced by the copy's own | find what queues the copy's own variations and callbacks under a paused brain and hold it | "placed then lost" to 0 | M | medium |
| A7 | Paired moves the engine's way | a paired attack shows both halves | the engine's own start with the right action type; only the start crosses | 9 of 10 staged | M–L | medium |
| A8 | The fight's lock | a copy fights whom its NPC fights | hand the host's lock to the copy through the engine's own target call, and keep the figure out of the copy's own choice | attacks shown 95 % | M | medium |
| A9 | Copies with no physics | every copy near a player animates | trace the engine's activation rule; ask for it | the ambush with a far guard | M | unknown |
| A10 | The world's gaps | — | chests through the loot ledger; the cart hitch; long names proven; loose items; the stolen flag by a native call; a staged encounter; the facial data linked | each its own check | L in all | low each |
| A11 | The transport's edge | no debug console, no log tail | section 4.5 (1) | every Lua suite and the acceptance | L | medium |

**How close that gets.** Promise 2 (nothing missing): nearly, except playing far apart. Promise 1 (the same world): places yes
(95 % within half a metre is in reach); "doing the same thing at the same moment" perhaps 80–85 % — the four lanes and the
re-installed actions stay. Promise 3 (nothing added): **not kept.** If A4 fails again, copies glide for good; and A3 holds only as
long as no new engine machine turns up that places a body — under this road the copy's own machinery is an opponent, and the
record shows it has always had one more move.

### 5.2 Road B — driven copies

The same goal, the body asked instead of written. Every step is behind the one before.

| # | step | contract | approach | proof | size | risk |
|---|---|---|---|---|---|---|
| B0 | Two probes and the scoreboard | we know whether the road exists | section 4.2's test (one copy driven by the per-frame movement request) and section 4.3's (four activities by the engine's own state request); the scoreboard (A1). Scratch branch, nothing shipped. | the pass and kill lines written there | M | the probes can fail; then Road A |
| B1 | Every walking human driven | no gliding | the request for every bound copy on the ground; the position stream as target and far snap; the writer kept for what the request cannot do (riders, falls, bodies in the air) | the stride and the scoreboard against 0.65.0 | M–L | medium |
| B2 | Delete what B1 replaces | the walk's stack is two layers, not eleven | the gait hooks for driven bodies, the extrapolation, the route and the replay, their messages and switches — deleted | the gates; the line count | S | low |
| B3 | One table of who holds a body | as A3 | as A3, simpler: an activity holds its own body | as A3 | M | low–medium |
| B4 | Activities by request | the prop, the way in, the way out | the state request at the same object; the copy's own demands silenced at the source; the neutral rule and the owner's posture installs retired for what the request covers | the smith's house: shown within a second 90 %, none lost, the tool in the hand | L | **high** (the largest unknown) |
| B5 | The fight by inputs | one fight on both screens | WO-209's attack extended to block, dodge, the start of a paired move; the lock handed over (A8) | attacks shown 95 %, paired 9 of 10 | L | medium |
| B6 | One timeline per body | the same thing at the same moment | everything for one body carries the host's time and is applied at that time plus the play-out delay, by the one native module — not four lanes | "the same activity" on the scoreboard to 90 % | M–L | medium |
| B7 | The edge in-process | as A11 | as A11 | as A11 | L | medium |
| B8 | The world's gaps | as A10 | as A10 | as A10 | L | low |

### 5.3 The two, side by side

| | Road A | Road B |
|---|---|---|
| work orders, roughly | 12–18 | 1 (B0) + 11–15 |
| what can sink it | A4 (the step), three times unfound; A5 | B0's two probes; B4 |
| when you know | never for certain: the opponent is the copy's own machinery | at the end of B0 |
| promise 1 at the end | places yes; actions about four in five | places yes; actions about nine in ten |
| promise 2 at the end | nearly (not far apart) | the same |
| promise 3 at the end | gliding and pulls likely stay | the walk, the step, the prop are the engine's own |
| code | the stacks grow | the stacks shrink (B2 deletes) |
| retail | adds to the debt | the per-frame request, the in-process edge and a native brain hold are retail steps anyway |

Road B costs one work order to find out whether it exists. That is the cheapest decision on the table.

---

## 6. What this audit read

### 6.1 The systems, in short

The full map (every single-player system: what the game does alone, what reaches the other player, by which module and message,
who decides, how often, what does not reach, what goes wrong) is with the work order's notes. The shape:

* **Travels as state, four lanes:** NPC position (10–20 Hz near, about 1 Hz far, only while moving); NPC full-body and upper-body
  action, with the activity and the hands on the same line (read 10 times a second, sent on change, applied one play-out delay
  late); NPC combat state and opponent (10 Hz); lists swept by the host (doors once a second, corpses every 2 s, carts 4 times a
  second, outfits one body every 0.1 s, props, loot lists while a screen is open); the clock and the weather.
* **Travels as events:** committed combat actions, hits, deaths, knockdowns, quest state changes, drops.
* **Travels as intent (three things):** the joiner's conversation decisions (replayed on the host's own quest nodes), a copy's
  own attack, and requests (a loot take, a door use, a travel, a sleep).
* **Does not travel:** chests and herbs (by rule), shop stock, barks, hand-placed loose items, the stolen mark (cannot be set),
  the hitch of a cart, an encounter as an event.

### 6.2 Who writes a copy's place [code]

Thirteen: the stream writer; physics; the copy's own activity alignment (seat, bed, workstation, aligned one-shot, the engine's
reseat); the copy's own movement controller acting on the request its brain had when it was paused; the route (off); a swing's own
motion during a hold; ragdoll; 23 Lua sites that set a position; the cart writer; a parent (a horse, a carrier); the engine's
alignment of a paired move; a brain given back for a talk; and the joiner's own pools, layers and level-of-detail rules.

Twelve write its pose or action: the engine's base action; our gait inputs; the owner; the combat rows; the copy's own attack; the
activity placement (closed for copies); one-shots, hands and looks; the copy's own leftover actions; four modules on the combat
model; the ragdoll manager; 32 Lua sites that start an animation; outfits.

Twenty pairs of these have contradicted each other in the field or on the bench; each has a work order's patch; seven are still
open (the full table is with the notes).

### 6.3 The inventory [code]

| | |
|---|---|
| The Lua mod | 36,069 lines in one file; 1,276 global functions; 448 console commands; 139 timer sites |
| The DLL | 219 files, 64,344 lines; 142 files named after a work order; 490 fixed offsets, 34 fixed addresses, 41 hooks or patches, 516 guarded calls; about 45 tasks on the game's main thread every frame |
| The agent | 226 files, 69,387 lines; 106 files named after a work order; drives 324 Lua functions through the console; matches 358 log tags |
| Switches | 147 named in WO-201…218's tables, 145 on; each declared in Lua, the agent and the DLL, each with a bench twin |
| The walk's stack | the per-frame writer → gait inputs → a hook at the tag update → hysteresis on the move vector → walk-on through gaps → a hook clearing the stale turn → adaptive play-out → one bind, silence blend → the route (off) → the replay (off) → a rule that drops the route's messages |
| The activity's stack | the engine's placement → its give-up rule → the neutral rule (closes the first) → its mark → the owner → seat hold, queue cap, drift → hand-over, watchdog → posture hold → let-go → ladder, "one path" → one line → seat writer, unclog → drift release, takeover stop |
| Inert in the shipped configuration | the route and the replay (the host still reads every NPC's movement task 20 times a second and sends it; the joiner drops it [record: WO-216, WO-217]); the engine's placement for copies; the Lua NPC sender; the proximity-claim model in Lua and the relay; seven file-triggered research probes and three research modules in the DLL; 23 test environment switches |
| Still on Lua and the console | every inbound call; every outbound event; the brain's pause and resume (console commands, 62 Lua sites); loot, outfits, doors, corpses, props, drops, the HUD |

### 6.4 Where the record and the code disagree

| the record says | the code (or the bench) says |
|---|---|
| README, Architecture: the DLL works "through the engine's own RTTR reflection layer … not offsets or signature scans" | 490 fixed offsets, 34 fixed addresses, 41 hooks and patches, functions re-found by their bytes [code]. The reflection layer is one of several ways in. |
| README, Architecture: one "localhost" link between the game and the agent | three channels: a pipe, the debug console, the log tail [code] |
| WO-208: "one owner per copy" | thirteen writers of a copy's place, twelve of its pose (6.2) [code] |
| WO-215 and WO-216: activities "left to the NPC-state placement" (204 and 247 times) | that placement has been closed for copies since 0.61.5 (`wo141.cpp:849-906`); WO-217 found it and removed the deferral [code, record] |
| WO-206: the bone prototype "ran (813 lines, 1,458 samples with bones)" | it asks for joints this skeleton does not have; every row is empty, and the merge counts an empty row [bench] |
| WO-215: the route "ships on, as every new mechanism does"; WO-216: off, "as 0.64.2 should have" | the code ships it off; the host still reads and sends every NPC's movement request for it [code] |
| README: "Animals don't sync — out of scope by design" | dogs are streamed (WO-191), wolves and dogs bite both players (the same README) [record] |

---

## 7. What to put in WO-220

**A proof work order. Nothing ships; no version; no installer.** On a scratch branch that is parked at the end.

1. **The scoreboard** (A1): section 2's two measures, pulls and flips, the lanes, the cost, a "works at all" line per scene — one
   rig scenario, one tool, the 0.65.0 numbers recorded as the base line. The rig's bone sampler corrected (the skeleton's own
   joint names).
2. **The movement probe** (4.2): one paused copy on the joiner driven by the per-frame movement request, beside its host NPC, five
   minutes; then ten copies in the village walk. Pass and kill as written in 4.2.
3. **The activity probe** (4.3): the smith, a cook, a sleeper, a seated guest; the neutral rule lifted for that copy only; the
   engine's state request at the object. Pass and kill as written in 4.3.
4. **The decision**, in the findings' first paragraph: Road B (both probes pass), Road B for the walk only (the movement probe
   passes, the activity probe fails: B1–B3, then A5), or Road A (neither).

Nothing more is owed to the goal request: this audit ran it both ways (section 2.2). The route's code, its messages and its
switch can be deleted by whichever road is taken; until then `mp_route` must stay off — it crashed the joiner again here.

Two small things can ride along because they are safe and the players feel them: the instruments out of the frame (A2), and the
facial-data link tried on the bench (4.8).

What WO-220 must not do: build B1. A probe answers a question.

---

## 8. What this audit is not sure of

| claim | footing | what would prove or kill it |
|---|---|---|
| The glide is the position write, not the link, the stream or the animation data | **bench**: one session, two windows of 150 s, the same build and villagers (5 and 11 bodies) | the scoreboard on more runs |
| A per-frame movement request moves a suspended copy with the engine's own walk | **reasoned**, from the chain [record, binary], the frame order WO-116 measured, and section 2.2 (the chain below the goal produces the right step) | WO-220 item 2 |
| A per-frame request keeps the place where the goal request loses it | **reasoned**: nothing to re-plan, nothing in flight | WO-220 item 2's position bar |
| The engine's state request plays an activity with its prop on a suspended copy | **reasoned**; the planner has refused copies before | WO-220 item 3 |
| The route failed because of WO-215's lead and corrector | this audit's own hypothesis, **killed** on the bench (section 2.2, third column) | — |
| The route's crash is the joiner's own world deleting a body under a plan in flight | **bench** (the report: two routed bodies gone 62 ms before the fault, on the AI update's thread) and **record** (WO-216); the dump was not read further | reading the dump; it stops mattering if the route is deleted |
| Section 2.1's figures | **bench**, one acceptance run, one PC, staged scenes, 0.6 s of slack | the scoreboard on more runs; a two-PC session |
| The fight crash belongs to the family of our writes into the fight's relations | **record** for the caught ones; the open one has no dump | a dump |
| Why a copy brought from far has no physics | not determined | a trace of the engine's activation on the joiner |
| Whether the facial data can be linked like the other data | not tested | one bench start with it linked |
| Whether a woken copy's schedule can be held while it talks | not determined | a probe on the innkeeper |
| Road encounters | not determined: the bench cannot start one | the player module's encounter controls WO-218 found, tried in a staging |
| Everything on two real PCs | every bench figure here is one PC | the field |
| The work-order counts for the two roads | estimates | — |
