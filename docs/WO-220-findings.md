# WO-220 — The two probes: does the better road exist?

**The decision: Road B for the walk only — B1 to B3 of `docs/DESIGN-AUDIT.md` §5.2, then the activities by Road A's way (A5, A6).**
The movement probe shows the road exists: a copy with its brain suspended takes the engine's per-frame movement request, and its
step and its feet are then the game's own — on the scoreboard's village walk with every copy driven the step ratio went from
**1.68 to 1.00**, the planted-foot slide from **44 % to 9 %** (the host's own walkers: 6 %), the pulls from **1.09 to 0.12** a
body-minute, flips to none (the base line: the mean of three runs on 0.65.0), with no fault, no crash and twenty removals of driven copies survived. It does **not** pass all of its
lines as built: with *every* copy driven only 61 % of the walking frames were within half a metre of the stream (one copy alone:
94–100 %) and the scoreboard's place fell to 62 % (the base line: 80–91 %), because the probe also drove bodies the request cannot serve — bodies too far from the player for the engine to update
them, bodies in a seat or at work, and the last half metre of a body that has arrived. Those are the writer's to keep, and B1 has to
prove the place line on the scoreboard before anything is deleted. The activity probe **meets its kill line**: asked for the very
state its own brain asks for, word for word, a suspended body is refused by the engine's planner — `NPC state search failed: can't
find a path from actions` — in 49 of 49 asks over two subjects, whatever was changed, while the same body with its brain given back
entered the same activity within seconds every time. B4 as the audit wrote it does not exist. Nothing ships from this work order:
no version, no installer, no change to the mod on `main`.

Roles only: "the host", "the joiner", "the maintainer". Words as in the audit: a copy, driven, the writer, the owner, the neutral
rule, the stream, Road A, Road B. Footing marks: [code] read in this repository, [binary] read in the game's own files,
[bench] measured on the bench (two copies of the game on the maintainer's PC, joined through our relay with the field's link shape),
[record] an earlier work order's findings, [reasoned] an inference, said as one.

## 1. The scoreboard (Part 1)

On `main`: `tools/rig/scen220.py` (the scenario), `tools/rig/scoreboard.py` (the tool), the bone sampler fixed
(`tools/rig/bones.lua`, `bones.py`: the rig's joints are `Hips`, `LeftFoot`, `RightFoot`; a row with no joints is no sample and the
sampler says so). One clock: both games run on one PC and every sample carries the PC's time.

**What it measures** [code]: the same place (a copy within 0.5 m / 1 m / over 2 m of its NPC at the same moment: every body, by
distance from the joiner, by what the host's NPC does, by scene); the same thing (the full-body action, variations and start/stop
folded); the step (the period of the foot's own cycle, copy over host, and how far a planted foot slides, as a share of the body's
speed — the village walk, every body that walked on both); pulls and flips per body-minute (a single-frame pull over 50 cm; a 10 s
window with a pull over 5 m and moves in over 40 % of its frames); the lanes' age on the joiner (p50, p99); our main-thread time on
both machines (mean, p95) and the frame rate each game held; works-at-all, ten yes / no / not run lines. The line it prints:
`SCORE place= thing= step= slide= pulls= flips= lane99= cost95= works=n/10`.

**Two runs, not one** [bench]. The scenario is the smith's house by day (3 min), the village walk (6 min, the bone sampler on both
games), the tavern by night (5 min), then the ten checks and one staged fight. On this bench one run cannot hold all of it: the
first full run was ended by the rig's own low-memory guard 36 minutes after its start (the two games held 13.5 GB and 9.1 GB of
32 GB), and a second, with the checks first, lost its joiner to the fight crash (§5) before the scenes. So the scenes are one run
(`scenarios.py score`, about 27 minutes with the join) and the checks another (`score-works`, about 15); the tool takes the checks
from the second: `scoreboard.py <scenes run> --works <checks run>`. This is the form that is fixed from here on.

**The base line, 0.65.0** [bench] (both games at low settings, 1280×720; no compiling during a run):

| | base 1 | base 3 | base 4 | mean | spread | the driven walk (probe p6) |
|---|---:|---:|---:|---:|---:|---:|
| place (within 0.5 m, every body) | 80.3 % | 82.3 % | 91.4 % | 84.7 % | 11.2 % | **62.0 %** |
| thing (the same activity) | 79.4 % | 79.0 % | 86.2 % | 81.5 % | 7.2 % | 81.3 % |
| step (copy / host) | 1.58 | 1.76 | 1.68 | 1.68 | 0.18 | **1.00** |
| slide (planted foot, of the speed) | 39 % | 49 % | 43 % | 44 % | 10 % | **9 %** |
| pulls a body-minute | 1.45 | 1.15 | 0.65 | 1.09 | 0.80 | **0.12** |
| flips a body-minute | 0.017 | 0.210 | 0.151 | 0.126 | 0.193 | **0** |
| the activity lane's age, p99 | 674 ms | 653 ms | 728 ms | 685 ms | 75 ms | 767 ms |
| our main-thread time on the host, p95 | 12.6 ms | 14.0 ms | 12.6 ms | 13.0 ms | 1.4 ms | 12.8 ms |
| the host / the joiner held | 36 / 59 fps | 37 / 59 fps | 42 / 66 fps | | | 38 / 59 fps |

Each run measured 14.1–14.3 minutes of scenes. Base 1 is the first full run (the scenes, then the checks in the same run): its
scenes were complete when the rig's low-memory guard ended it in the checks, and its score line carries `EXITED=joiner@works-drop`
for that — the rig's own quit, not a crash. "Base 2" is the checks run (below). The three runs' own tables:
`docs/WO-220-scoreboard-0.65.0.md`. **The spread is wide where bodies bunch up** (place 11 points, flips 0.02–0.21 a body-minute):
a difference inside it is not a result. The driven walk's step, slide and pulls are far outside it; so is its place.

**Does it reproduce WO-219?** Yes. Place: 84.7 % in the scenes' mean and 84.0 % over the whole checks run (the audit: about
85 %). The same activity: 66.8 % over the whole checks run (the audit: two times in three); in the scenes it is higher (79–86 %)
because the scenes stand where the audit's run walked through fights. The slide: 44 % (the audit: about 45 %). The step: 1.68
as the median over about 20 bodies; the audit's 1.85 was five walking men, and the scoreboard's walking men are 1.73–2.15
(`ttkc_man_1` 1.85, `ttkc_man_15` 1.76, `ttkc_man_22` 2.15 in base 1) — the same fault on a wider sample (women, dogs and runners
lock less).

**Works at all, 0.65.0** [bench] — **5 of 10** (the checks run `works1-b8`, complete, no crash; an earlier checks run, "base 2",
lost its joiner to the fight crash at the sleep vote's end and agrees on every line it reached but one):

| check | | |
|---|---|---|
| the join | yes | |
| a drop picked up by the other | yes | 2 of 2, both runs |
| a door use | yes | used by both, the same on both, both runs |
| a knockdown and get-up | yes | 2 of 2 falls shown, 2 get-ups |
| a sleep vote | yes | both runs |
| a trade screen | **no** | the trade option is chosen, no shop screen opens (2, 2 and 3 shopkeepers tried in three runs) |
| a forced dialogue | **no** | no dialogue started in two runs; it started in one (base 1) |
| a mount | **no** | the copy did not follow the host's rider onto the horse (the other run: the rider did not mount: not run) |
| a talk to its choices | not run | no villager was ready to talk at the spot, in three runs — the check's staging, not a result |
| a loot take | not run | the fight left no body to loot |

What the base line also showed: the host holds 36–38 fps on this bench with a joiner (59 on the joiner), our main-thread time on
the host is 12.6 ms at the 95th percentile (the audit's A2 bar is 1.5 ms); the host's player was in a fight in a third of the
village's checks although the bench tells the host's AI to ignore the player — the guards fight around the players all through the
village scene (the save's Henry is wanted), which costs "the same thing" a few points in every run alike.

## 2. The movement probe (Part 2)

### 2.1 The request [binary]

The per-frame movement request is the stock engine's, unchanged in layout. EntityModule's `C_ActorMovementController` (found by
its RTTI name, its `RequestMovement` checked by nine byte sequences before the probe arms) is the object `*(actor + 0x1E8)`;
`RequestMovement` (its vtable slot 1) merges a request field by field into the controller's own state; the controller's `Update`
reads that state once a frame. A request is: a flag word (bit n "has", bit n+31 "remove"), a move target, a desired and a target
speed in m/s, the distance to the path's end, a body target, and this game's speed class in the pseudo-speed field. CryAction's
`CAIProxy::Update` builds one for every AI body every frame — also for a suspended one, for which it writes "no move target". An
awake walker's brain asks for: class 1, 1.35–1.47 m/s (its own speed), the remaining length of its path as the end distance.
How found and how verified (the listing of each function, the frame order, the port note: every address is found by name or by
bytes, none is fixed): the WO's notes.

**Where the probe's call runs, and why that is right.** `RequestMovement` is made at the DLL's frame hook on the main thread, the
thread the proxy makes its own on, later in the same frame than the proxy and before the controller's next read: ours is the last
word (measured: the proxy rewrote the controller's state in 2,252 of 2,253 frames and the body still followed ours). The function
takes no engine lock and allocates nothing (read whole) [binary], and it leaves no plan in flight: when the joiner's own world
removes the body there is nothing to cancel. The one other engine call the probe makes is the cancel of a copy's *stale* movement
plan (the plan its own brain left when it was suspended — the source of the "Agent is stuck" lines): made once per driven copy by
the task's own cancel, on the AI update's thread at its entry, where the engine makes such calls itself (WO-215's door) [code].

### 2.2 One copy [bench]

| | step period | planted-foot slide | from the stream | snaps |
|---|---:|---:|---:|---:|
| host alone, the NPC's own walk (brain awake) | 0.95 s | 3 % | — | — |
| host alone, the same NPC suspended and driven (gains 1.8 / 3.6 / 7.2) | 0.95 s each | 3–4 % | mean 4–5 cm, 100 % within 0.5 m | 0 |
| host alone, driven at a run (3.3 m/s) | 0.65 s | 13 % | mean 25 cm, 89 % within 0.5 m | 0 |
| the pair, the copy under the writer (0.65.0's way) | 1.85 s against the host's 1.05 (×1.76) | 40 % against 8 % | | |
| the pair, the same copy driven, gain 3.6, five minutes | 1.15 s against 1.35 (×0.85) | 12 % against 7 % | 93.7 % of walking frames within 0.5 m, mean 21 cm | 0 |
| the pair, gain 1.8 / 7.2 | ×1.00 / ×1.05 | 15 % / 21 % | 92.4 % / 87.1 % | 0 / 3 |

The copy showed the engine's own start (`IdleToMove`, 18 times) and its own walk 96 % of the time. The three snaps at the high
gain were the stream itself jumping about 10 m. A body further than about 150 m from the player is not updated by the engine at
all: a driven body stopped dead there (the solo run) [bench]. The pair's five-minute window had frame pairs taken inside it (a
frame stalls both games); the later builds take them after the window.

### 2.3 Every copy: the scoreboard's village walk driven [bench]

Probe build p6, every bound human copy on the ground driven (gain 3.6, snap at 8 m), the writer kept for figures, beasts, riders,
falls, bodies in the air and fighting bodies. The run completed all three scenes; no fault line, no crash.

| | 0.65.0 (base 1) | driven |
|---|---:|---:|
| bodies that walked on both games | 20 | 19 |
| median step period, copy against host | 1.55 s against 1.00 s (×1.58) | **1.05 s against 1.00 s (×1.00)** |
| median planted-foot slide, copy against host | 39 % against 8 % | **9 % against 6 %** |
| pulls a body-minute / flips | 1.45 / 0.017 | **0.12 / 0** |
| "Agent is stuck" lines on the joiner | 182 | 24 |
| within 0.5 m of its NPC, the host's NPC walks | 75.7 % (within 1 m: 92.0 %) | 67.5 % (83.3 %) |
| within 0.5 m, the host's NPC stands | 85.9 % | 83.6 % |
| within 0.5 m, the host's NPC does anything else | 81.9 % | **57.2 %** |
| within 0.5 m: smithy / village / tavern | 89.1 / 81.1 / 72.5 % | 55.6 / 76.9 / 41.5 % |
| over 2 m away, every body | 4.4 % | 12.3 % |

The probe's own count against the stream it was given (the village scene): **60.7 % of walking frames within 0.5 m**; 322 snaps
in six minutes. Where the lost frames are:

* **Bodies the engine does not update.** 245 of the scene's 1,590 ten-second windows are bodies that did not move at all while
  the request asked for a walk; not one of them was in range of either game's sampler — they are far from both players. The
  engine does not run a far body's movement, so the request is not read there. Under the writer these bodies are simply placed.
* **Bodies that moved:** 86.6 % of their walking frames within 0.5 m, mean 48 cm — further behind than the one copy alone
  (the pull is a plain proportional one with a 10 cm dead band; nothing leads the stream).
* **Bodies in a seat or at work.** "The host's NPC does anything else" fell from 82 % to 57 % and the tavern from 73 % to 42 %:
  the probe drove bodies whose place is an object's (a bench, a pot, a table) toward a point the walk cannot reach — there is
  furniture in the way and no path, only a straight pull. A standing body rests where it arrived, up to half a metre off.

Each of the three is a body the request cannot serve and the writer can; none is the walk itself.

### 2.4 The risks [bench] [code]

* **Is ours the last word?** Yes: the proxy writes every frame, ours is later, the body follows ours (2.1). After the copy's stale
  plan is cancelled the proxy stops writing a target of its own.
* **Does a collision push it off, and does the pull bring it back?** Not settled: the staged pushes (the body set 1.5 m aside by
  script) fell in a session whose one copy had been pulled into the guards' fight, and a fighting body is not driven. Indoors the
  answer is visible on the scoreboard instead: a straight pull does not go round furniture (2.3).
* **Does the step still lock?** No: ×1.00 over 19 bodies; the men who lock at 1.73–2.15 under the writer walk at their own period.
* **The joiner's own world removing driven copies.** Twenty driven copies removed by the joiner's own entity system, one every
  six seconds, then two trips 150 m away and back (the joiner's pools and streaming): no fault line, no crash; 43 requests ended,
  12 writer drops. No plan is in flight for a driven body: the request is a merge into the controller's state, and the one plan
  that was there (the suspended brain's own) is cancelled when the drive starts — nine cancels logged, each on the AI update's
  thread.

### 2.5 Against the lines

| line | result |
|---|---|
| the copy's step period within 10 % of its NPC's | **pass** (×1.00 over 19 bodies; one copy ×0.85–1.05, the low figure in the window with frame pairs inside) |
| the planted foot slides no more than the game's own walkers on the host | **at the line, not under it**: 9 % against 6 % with every copy driven; 3–4 % against 3 % alone on the host |
| within 0.5 m of the stream 95 % of the time while walking | **pass alone** (94–100 %), **fail with every copy driven** (60.7 %; 86.6 % for the bodies that moved) |
| the engine's own start and stop are seen | **pass** |
| no "stuck" line | **not yet**: 24 on the joiner against 182 |
| no fault, no crash in 2.3 and 2.4 | **pass** |
| kill: the request is not taken on a suspended body | **no** — it is taken wherever the engine updates the body |
| kill: the error grows without the snap | **no** for a walking body in the open (no snap in five minutes at two gains); **yes** for the three kinds of body in 2.3, which are the writer's |

## 3. The activity probe (Part 3)

**What was asked, and where.** The request the mod already makes for one-shots (`wo143.cpp`): `C_NPCContext::RequestStateChange`
with no extra action, toward the context's required state — hands, the object use ("unstance") at the smart object with the same
identifier. For the probe's copy only: the neutral rule lifted (`wo141.cpp`), the owner's posture installs off (`wo208.cpp`), the
writer yielding while an activity holds the body. Because the first pair session answered nothing, the question was taken to one
game: the host's own NPC, its brain suspended exactly as a copy's is (`wh_ai_PauseNPC`), made to stand by the game's own reset of
its state element, then asked. Nothing else runs on that body — there is no scheduler left to silence. The request and the ticks
of the suspended body's NPC state (the three steps `C_NPC::Update` makes for an awake NPC) run at the DLL's frame hook on the main
thread, where the shipped one-shots have run since WO-143. **That is not the engine's own place** (it makes both inside its AI
update, on a worker thread [binary]), and the request goes through the DLL's guarded call as the shipped one does, although it can
take engine locks: both are the build work order's to move; the probe changed neither. The result below is a planner's answer,
not a timing effect [reasoned].

**What the engine's own brain asks** [bench] [binary]. Logged at the request's entry while the smith, the cook and a hoer took up
their work by themselves: no action, urgency 0, every flag byte 0, the default movement block; the wish is entirely in the
required state (for the cook: `RightHand wooden_scoop; Unstance housekeeper_cooking_home Using location object <the fireplace>;
Hand items: none` plus her tree's two `EnableBehaviorForNPC` elements); made on a worker thread; made again every ten seconds.
The tree's own node (`C_ExecuteNPCStateChangeBase::RequestStateChange`) zeroes the request and fills nothing in the plain case
[binary]. The probe's request is the same call with the same fields.

**What a suspended body answers** [bench]:

| asked of the suspended body | times | entered | the engine's word |
|---|---:|---:|---|
| the state it was captured in (the smith forging, the cook between two behaviours), by request, by the load's fast-forward, urgency 2 and 3, flag bytes `59`, `5a`, `5b` | 14 | 0 | `Couldn't find actions to get NPC into game loaded state` |
| the state its brain enters from standing (heating, stirring), tools named inside the use element (WO-143's way) | 10 | 0 | `NPC state search failed: can't find a path from actions` |
| the same, the use element's tools by turns named and left empty (the brain's own way), its NPC state ticked every frame, no 3 s drop | 10 | 0 | the same; the smith's four requests each `NPC state search has failed!` |
| the cook, her scoop named by the item she held before (word for word the brain's required state, from word for word the brain's current state) | 7 | 0 | answered "done" in one tick, nothing done |
| the same with the engine's "immediate success" switch off (`wh_ai_NPCStateSearchImmediateSuccessEnabled 0`) | 4 | 0 | `NPC state search has failed!` — `Curr state: RightHand wooden_scoop` → `Req state: RightHand wooden_scoop; Unstance housekeeper_cooking_home …`: `can't find a path from actions` |
| the cook set exactly on her own spot and facing before each ask (no movement needed), immediate success off | 4 | 0 | `NPC state search has failed!` |
| the brain given back, from the same standing start | 8 | **8** | the cook: a step, `Placing`, `CookingIn`, stirring with the scoop after 3–14 s (after 3 s when she stood on her spot); the smith: a walk, `Placing`, a walk, `ForgingIn` after 8 s |

Two things the suspended body *did* do: a request ran real engine actions on it twice — the cook picked her herb
(`HousekeeperCookingPickIngredience`, the herb in her left hand, 5 s) and put her scoop away (`Placing`, 3 s). The planner plans
and the executor plays on a suspended body; what it has no path to is the object use.

**Why (3.3)** — what an awake NPC has that the copy lacks. Thirty seconds after the planner failed `RightHand wooden_scoop` →
`… Unstance housekeeper_cooking_home`, the awake brain made that request from that state and the engine carried it out: a short
walk, the scoop put down, the way in. The plan the engine finds for an awake body can contain a *movement*, and it is carried by its
running tree (the tree's node logs `Can't update suspended node` on our asks). The movement is not the cause: set exactly on her
spot and facing, the suspended cook is refused all the same, and with her brain given back there she stirs after three seconds.
What is left is the running tree itself: the way into an object use is offered to the planner only while the body's own tree
for that object runs. The smith shows the other half: his own brain's
request for the forging fails the same search four times, and his tree answers by walking him to the hearth, placing the blade,
walking to the anvil and asking again — the entry is the tree's script of several requests and walks, not one request
[bench] [reasoned: *how* the running tree puts the way in before the planner — an action it registers, a link to the object it
holds — was not read out of the binary].

**Is it the same wall as WO-160 and WO-197?** Yes [record]. WO-160: "the planner cannot leave a work activity … `can't find a
path from actions`"; WO-197 Part C: the everyday categories stay under 95 % "where the game's own planner refuses the placement".
Same message, same search. This work order adds what is behind it: the search is not broken and the required state is not wrong —
the same request succeeds for the same body when its brain runs.

**Against the lines.** Pass (tool in hand and loop within a second in 9 of 10 entries per subject): **0 of 49**. Kill (the planner
refuses on a suspended body whatever is silenced): **met**. The ten entries and exits per subject on the pair, the sleeper and the
seated guest were not run: the first pair session's 290 requests on the smith's and the cook's copies had all been answered
"done" in one tick with nothing done (the engine's immediate success), and the one-game sessions say why. The picture that
matters is therefore the base line's, 0.65.0 as shipped: the host's smith at his hearth with the bellows' pole, and on the
joiner's screen at the same moment no smith at the hearth (one moment; over the whole scene his copy showed the same activity in
85 % of the samples). The frames are in the WO's notes.

## 4. What the probes found that WO-219 did not expect

1. **The step fault is not in the walk.** A suspended body walks with the game's own step the moment the engine moves it (×1.00,
   slide 9 %); the 1.58–2.15 lock is the writer's doing alone. A4, three work orders unfound, is closed by not writing.
2. **The engine does not update a far body.** A driven body far from the player does nothing. Under the writer nobody noticed,
   because a written body needs no update. B1 needs a rule for it (the writer beyond the engine's range).
3. **A suspended brain leaves a plan behind.** The "Agent is stuck" lines on the joiner (182 in a base-line run) are copies'
   own stale movement plans; cancelled once on the AI thread, they are gone (24 left).
4. **The planner works on a suspended body; the object use does not.** The audit's largest unknown is answered in the negative,
   and more exactly than "refused": hands and items can be asked for (the herb, the scoop), a seat or a work spot cannot.
5. **The "immediate success".** The engine answers a suspended body's state request "done" in one tick without doing it. Every
   WO-143 one-shot's "finished, result 2" on a paused copy has to be read with that in mind: it is not proof that it played.
6. **The fight crash needs no fight of ours.** `CombatModule.dll+0xF2A3E` on 0.65.0, on the joiner, on the animation job, twice
   in six pair runs of the unmodified build: once 57 s after the staged fight, once with no staged fight at all (§5).
7. **The bench cannot hold a 40-minute pair run.** Two games fill 32 GB in about 35 minutes (the host 13.5 GB); the rig's guard
   ends the run. Every long run on this bench has to be planned under that.

## 5. The crash on 0.65.0 [bench] [binary]

`0xC0000005` reading `0x490` in `CombatModule.dll+0xF2A3E`, the joiner, the animation job's thread, no frame of the mod on the
stack. The function is `C_CombatAnimAction`'s vtable slot 14: it looks its actor up by entity id, takes the actor's combat part
or nothing, and reads `[+0x490]` without a check. Both times a combat animation action was still running on a copy when its
actor was no longer there for it (the players had been moved on). Dumps and reports: the WO's notes. Not fixed here (nothing
ships); it is the first thing a release after 0.65.0 owes.

## 6. The next work order (Part 4)

**B1 — every walking human driven, proven on the scoreboard.** Rows from the audit's §5, sharpened by what the probes measured:

| # | contract | approach | proof on the scoreboard | size | risk |
|---|---|---|---|---|---|
| B1a | a copy on the ground, within the engine's update range of the joiner's player, with no activity holding it, is moved by the per-frame movement request and by nothing else | the probe's call as it is (frame hook, after the proxy), the stream's velocity and facing, the pull toward the stream's point; the copy's stale plan cancelled on the AI thread when the drive starts | step ×0.9–1.1; slide no more than the host's own + 2 points | M | low: measured here |
| B1b | every other body is the writer's, by one rule | the writer keeps: figures, beasts, riders, falls, bodies in the air, fighting bodies, **bodies the engine does not update** (find the engine's own flag or range; until then a distance), **bodies an activity or a seat holds**, and the **last half metre** of an arrived body (a short blend, not a walk) | no row of the place table under the base line's lowest run (base 1: 80 % all, 76 % walking, 82 % "anything else", the tavern 72 %); over 2 m ≤ 4.4 % | M | medium: this is where the probe lost its place |
| B1c | the walking place line | lead the stream (the play-out delay times the stream's velocity, WO-116's 0.28 s × speed) instead of the dead band; the gain kept at 3.6 | within 0.5 m of the stream 95 % of walking frames for driven bodies; snaps under 0.05 a body-minute | S–M | medium |
| B1d | no "stuck" line | the stale-plan cancel for every copy at suspension, not only when driven | 0 lines in a scenes run | S | low |
| B3 | one table of who holds a body | as the audit's B3: stream (driven), stream (written), activity, ragdoll, parent, swing, talk — one state per body, every writer asks it | flips 0; pulls under 0.1 a body-minute | M | low–medium |
| B2 | delete what B1 replaces | only after B1a–d hold on three scenes runs | the gates; the line count | S | low |
| then A5, A6 | the smith holds his blade; a placed activity is not replaced | Road A's way, with this work order's two facts: hands and items *can* be asked of a suspended body by the state request (the herb, the scoop); the object use cannot — it stays an installed posture | framed pairs, 9 of 10 | L | high |

Also owed before or with it, each its own small item: the crash of §5; the trade screen (no shop screen for the joiner on
0.65.0); our main-thread time on the host (12.6 ms at the 95th percentile against the 1.5 ms bar — the audit's A2).

**What the next work order deletes** [code] (when B1's proof holds, not before; each by its scoreboard line):

* the goal request and its replay: `wo215.cpp`'s and `wo216`'s route, its frames and `mp_route`, `route_skip` — settled as off in
  WO-219 §2.2, superseded by the per-frame request;
* the copy's gait for ground humans in `motion.cpp`: the speed-class and start/stop/turn installs and their switches
  (`mp_copy_no_idle_turn` among them) — a driven body's gait is the engine's own;
* the walking humans' extrapolation and blend in `npc_drive.cpp` (kept for the bodies B1b leaves to the writer);
* the owner's walk family for driven bodies in `wo208.cpp`;
* the gait read-back instruments for copies (`WO121-GAIT`).

Not deleted: the neutral rule, the owner's posture installs, WO-141's fast-forward — the activity road did not open.

## 7. Conduct, and what was not done

* The real saves: backed up before the first launch; the rig's hash list `MATCH` and `mirror roots: PASS` after every run of
  this work order. Nothing of the maintainer's (launcher, agent, relay) was running; nothing was stopped.
* `tools/Check-NoInputCapture.ps1`: passes on everything added, first and last (the last run: 11 binaries, 1,813 scripts, the probe DLLs and the scratch tree's sources included: PASS). Keys are sent, never read, in one
  check (the trade screen), to the bench's own game window, by the rig's existing send-only module.
* The probes: scratch branch **`wo220-probes`** (one commit, `48fe922`; builds p1–p6), parked — not merged, not deleted. `mp_route` untouched.
* Not done: the ten entries and exits per subject on the pair, the sleeper and the seated guest (§3); a clean five-minute
  one-copy window on the last probe build and the staged pushes (the session's copy was pulled into a fight); the talk and the loot checks (not staged: no villager ready, no body); the ride-alongs (the instruments out of the frame, the facial data link).

## 8. What is still not known, and what would settle it

| not known | what would settle it |
|---|---|
| which engine rule stops a far body's movement (a range, a visibility test, a flag), and its distance | the probe's window line with the body's distance from the player and the controller's own update count; one village run |
| whether a driven body's slide can come under the host's 6 % with every copy driven | B1c's lead in place of the dead band, on the scoreboard |
| whether the pull brings a pushed body back in the open | the staged pushes on a copy no fight can reach (the smith's village has no guards) |
| what exactly the running tree gives the planner for an object use (the movement is ruled out: §3) | the search's own gathering of actions read in the binary for the awake cook and the suspended one; or the same ask with her tree running and only her scheduler held |
| whether an object use can be had with the brain awake for the entry only | not asked here; it is a different design (a copy that runs a part of its own brain) and belongs to a design note, not to B1 |
| why the trade screen does not open for the joiner on 0.65.0, and why the forced dialogue started once in two | a checks run with the dialogue's own trace on |
| the cause of the combat-action crash's missing actor | the two dumps: whose action, and what removed its actor |
| how much of "the same thing" the guards' fights cost in the scenes | a save whose Henry is not wanted; or the scenes at the smith's village |
