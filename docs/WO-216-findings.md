# WO-216 — The rest of the body: the host's route replayed, no brain runs on the joiner, every pose through one owner

Version **0.64.3** (the maintainer's). Measured on the bench (two copies of the game on one PC, joined through our own relay with the
field's link shape: 246 KB/s, 26 ± 10 ms each way, 0.5 % loss, two stalls a minute). Every new mechanism has its own switch
(`mp_w216_status` lists them); the acceptance twin is the same build with every WO-216 switch off (`KCDMP_TEST_W216_OFF=all` for both
agents and both games), the instruments kept on. Nothing built here reads input: `tools/Check-NoInputCapture.ps1` passes on every
binary and script, test tools included, first and last (below).

Roles only: "the host", "the joiner". "A copy" is the joiner's local body of one of the host's NPCs; "the writer" is the joiner's
per-frame position writer (WO-118); "the owner" is the joiner's animation owner of copies (WO-208); "the route" is WO-215's movement
request on a copy (`mp_route`).

## A's answer: the host's own request replayed is closer, and still not good enough — `mp_route` ships off

**Not yet.** A copy that makes its host NPC's own movement request — the same goal, speed class and exact end, once per change — walks
closer to its host than WO-215's walk-to-sample: on the same clock (both games on one PC) its median distance from the host NPC was
0.55 m against 0.85 m, level with its host along the way (0.06 m behind) where the walk-to-sample ran 0.81 m behind, its pace within
15 % of the host's 56 % of the time against 42 % (an earlier pair: 0.62 m against 0.79 m, past 1.5 m 2.7 % against 11 %); and in the
scene-1 pair below it is ahead on every count. **But it does not pass A.1's test.** In the 10-minute village walk the stream
still pulled copies back 9.8 times a body-minute (the bar: at most once), a copy was in a start or a stop 23 % of the time its host
walked (5 %), 79 windows had a single-frame pull over 50 cm (none), the copies slid 2.0 times per 100 samples (never), and the gait was
within 15 % of the host's in 81 % of the 2 s windows (90 %); only the walk shown passed (93 %, bar 82 %). **And with the route on, the
joiner crashed once** (b10, the acceptance run's smithy scene): its own encounter pool deleted a replayed copy 150 m away while the
copy's path request ran, and the engine's path finder, turning the result into a movement block, read the freed body. **So `mp_route`
ships off, as 0.64.2 should have:** copies walk by the writer; `mp_route on` in the console turns the route and the replay on for a
session — now with every copy farther than 100 m from the player left to the writer, well inside the pool's 150 m.

## A.0 — the host's request, read

**What a brain's request is, where it lives.** Every movement request a brain makes — the behaviour tree's move nodes, the activity
system's moves to its smart objects, an action's next step, a fight's positioning — goes through the movement-task manager's queue, which
starts the NPC's own movement task with it. The start copies the request into the task: its id, its speed class, the path request it
plans (the goal), the exact end (a point and a facing) when one is set, the request's style and its two tolerances, and the path
finder's request in flight. The host reads those fields out of each streamed NPC's task — at the AI update's entry, on that update's own
thread (the entry hook WO-215 made the route's requests at), reads only: the tasks are found through the manager's own map, never through
its get-or-create (which would give an NPC with no task one). Each field is checked at start by the instruction that writes it; a build
that moves any of them leaves the read unarmed, and the copies follow the stream as in 0.64.2.

**How it was verified.** On the bench, the joiner's probe copy (WO-215's) was given known requests and the same read ran on its own
task: every step read back what it was asked — the goal to the millimetre, the walk (class 1), the jog (3), the sprint (7), both exact
facings (the yaw), a new request every frame (a new id each time, the goal following), and the end (the id back to none). On the host,
the village's brains made about 85 requests a minute on the streamed bodies: 86 % with a goal the read can see, 57 % with an exact end,
13 % following a path handed over with the request (no path request to read: those copies stay WO-215's). The speed classes the
brains use run 0 to 11 — class 0, the town's slow walkers, is common, and is replayed as it is.

**On the wire** the change of a body's request (another request, a re-aim, its end) goes out as state on the same lane as the NPC
states, its own kind (`npc-route`: `NpcRoute 0x84 → 0x85`, latest wins per body), polled twenty times a second, again every 2 s while
a request runs (6 s while none does): a lost line is soon replaced.

## A.1–A.4 — the replay (`route_replay`, under `mp_route`)

- **A.1 the replay.** A copy makes its host NPC's request once per change — the same goal, speed class, style, tolerances and exact end
  with its facing — on the AI update's thread, as WO-215's own requests are made. The stream only corrects: a copy more than
  `mp_route_correct_m` (1.5 m, plus the stream's own gap for a far body) off **its host NPC's way over the last 1.2 s** has its request
  cancelled, is blended back by the writer and makes the host's request again from there; every correction is counted by its cause
  (`drift`, `refused`, `no-path`, `stall`). A first build measured the copy against the host NPC's point at that moment: a copy
  replaying the same request runs about 0.8 s behind its host (the link, the poll, its own start) and was pulled back 15–17 times a
  body-minute, restarted from a stand each time and fell behind again; against the host's way it is on its way while it walks the host's
  path a moment later. A link stall holds the request in flight. When the host's request ends with the host NPC at its goal, the copy
  finishes its own (it is a moment behind) instead of stopping a metre short; a request its brain cancelled away from the goal is
  cancelled here too. A body whose host request has no goal the read can see (a path handed over with it), or no live line, or a
  request the engine refused here (5 s), is WO-215's walk-to-sample, counted.
- **A.2 path parity.** Each replayed route's mean offset from its host NPC's way, in 10 cm bins (`mp_w216_status`, the briefing).
- **A.3 the gait.** The replay asks for the host NPC's own speed class, as the host's brain asked for it (no hysteresis: the class is
  the host's, not a guess from the stream's pace); WO-215's hysteresis is left to the bodies on WO-215's own requests.
- **A.4 the default.** `mp_route` ships **off** (A.1's test not passed, and one crash with the route on: above). `mp_route on` in the
  game's console turns the route and the replay on for a session; `mp_route_replay` stays on and does nothing while `mp_route` is off.
  With the route on, a copy farther than 100 m from this player is the writer's, its request cancelled (the joiner's pool despawns
  bodies about 150 m out — the crash's body was a replayed copy it deleted there).

**Scene 1 — the village walk** (10 minutes; the joiner following walkers 10–30 m behind; every copy within 40 m sampled 10 times a
second by the probe). The replay: b10, every switch on and `mp_route on`. Its twin: the same walk with every WO-216 switch off and
`mp_route on` (WO-215's walk-to-sample). A single run's scene varies with the villagers who walk and where; an earlier run of the
same walk-to-sample (the first dev build) is beside it.

| A.1's test | the bar | the replay | walk-to-sample (the twin) | walk-to-sample (an earlier run) |
|---|---|---|---|---|
| windows with a single-frame pull over 50 cm | 0 | 79 | 373 | 110 |
| corrections a body-minute | ≤ 1 | 9.8 | 14.7 | 11.9 |
| a start or a stop while the host NPC walks | ≤ 5 % | 22.7 % | 50.5 % | 12.1 % |
| walk shown while the host NPC walks | ≥ 82 % | **92.7 %** | 79.3 % | 96.1 % |
| gait within 15 % of the host's (2 s windows) | ≥ 90 % | 80.8 % | 58.0 % | 47.0 % |
| sliding (per 100 samples) | 0 | 2.0 | 2.6 | 0.55 |

The way there, the same walk on each build: the first replay (b1) corrected its copies 24 times a body-minute and showed a walk 48 % of
the time — measured against the host NPC's point now, a copy replaying the same request 0.8 s later was "drifting"; against the host's
way over its last 1.2 s (b7 on) it is on its way. b6 and b8 lost the agent's connection to the DLL two or three times a run (a 24-body
route line was over the pipe's 1,024-byte frame: every copy dropped and bound again); from b9 the line goes in pieces. Corrections a
body-minute: 15.1 (b6), 11.7 (b8), 9.8 (b10). The path parity (a route's mean offset from its host's way) is 0.64 m; the copies take
0.2 s (median) to 1 s (p90) to start after a request, and stand in 26 % of the quarter-second windows their host walks.

## B — no brain runs on the joiner (`no_brains`)

**The contract: no NPC on the joiner runs its own brain.** Every body here is suspended the way WO-107 suspends a copy (the game's own
`wh_ai_PauseNPC`, made by the DLL through the console on the main thread); a body the host does not stream is also hidden. The join
sweeps the world once (the bench's town: 512 bodies with a brain, 438 of them running, all suspended; 493 not streamed, hidden), and a
**spawn watch** on the entity system's spawn call hides every body this game spawns on its own in the same frame, suspended as soon as
its brain exists (`WO216-LOCALSPAWN`). Once a second a **census** counts the bodies with a running brain — the contract is 0 — names any
it finds and suspends it again (`WO216-BRAINS`); it walks a cached list (about 1 ms), refreshed by a full entity scan every 30 s (about
10 ms: the moment that also finds a body a layer switched on rather than spawned). A resume of a copy the stream let go (WO-102's release:
its silence, the dwell after it, the coverage-gap detector) is refused (`WO216-NORESUME`: it stays frozen); a feature's resume through
the same call (a talk, a forced dialogue, a scene, WO-139's guard stop, a ridden horse, a corpse) passes, and a resume the game's own
scripts make (a quest, a scene) exempts that body for two minutes (`WO216-EXEMPT`), counted apart. (The bench's runs had a first rule
that refused every resume through WO-102's call; the offline gates' synthetic suites caught it — a copy's conversation needs it awake —
and the release-only rule above is the shipped one, proven by those suites, not by a bench run.) A body whose name the stream cannot carry (over 63 characters: the animal care's horses) can never be
a copy: it is suspended but left shown. The leash is unchanged; the host's census (WO-215 D.1) now logs each dormant body's distance from the
host (the briefing's p50 / p90 / nearest).

| B on the bench | every switch on | every WO-216 switch off (the twin) |
|---|---|---|
| once-a-second checks with a brain running here (the acceptance run, 52 minutes) | 4 of 3,149 | 2,939 of 2,939 (every check) |
| the most at once | 8 (six villagers and two guards of another village, far from both players, switched on with their layer) | 438 |
| the split-up walk (scene 6: the joiner 545 m out and back) | 1 check of 373 s, a far villager; none within 150 m | every check (the census only counts) |
| the join's sweep | 438 running brains suspended, 491 bodies hidden | — |
| bodies this game spawned on its own | 49, each hidden in its spawn frame | 54, left shown, their brains running |
| the census's cost | 1.2 ms a second (the full scan every 30 s: 11 ms) | — |

Every running brain the census found was named and suspended again within the second: bodies a layer switched on far away (found by
the 30 s full scan), and in another run two copies WO-139's guard stop gave back to their own brain on purpose (the stop resumes the
guard so it can talk to the joiner's player; its exemption reached the census a second after the resume). **The host's dormant bodies**
(WO-215 D.1's census, now with each one's distance from the host): 25 in the acceptance run, **p50 621 m, p90 765 m, the nearest 47 m** (its
twin: 43; 652 / 844 / 50 m); the leash is unchanged.

## C — one activity line from the host (`activity_line_host`)

**One line, one message.** The host's activity of each NPC used to reach the joiner as two descriptions on two channels — WO-141's
NPC-state rows (a stance or an unstance with their objects) and WO-143's hand rows — and the joiner joined them with the full body's
state from the animation stream (WO-208). Now the host puts all of it in the body's own animation-stream message: after the full body's
and the upper body's state (their fragment, its family and its tags — the posture and whether it loops), one record with the NPC-state
activity (stance, unstance, minigame, their objects, the night undress) and one with the items in its hands, read in the same frame.
The joiner applies the activity and the hands from that line, and the old NPC rows and hand rows of a lined body are retired. An activity
the host cannot read goes out as no record (the joiner keeps what it has) — a first build sent it as zeros, which are real ids on that
wire, not "none". The route's class is A's own line. The twin (`activity_line_host` off): WO-141's and WO-143's rows, as 0.64.2.

**Scene 2, the blacksmith's house** (the field's own smith: 4 minutes by day, 2 at midday, 2 at night; the acceptance run, every
switch on, against its twin). C's test: of the activities the host NPCs held 2 s or more, the copy shows the activity within a second
for 90 % (0.64.2: 72 %), and the activity-missing time is at most half of 0.64.2's (416 / 161 / 22 s; by day at most 200 s).

| the smithy | every switch on | every WO-216 switch off (the twin) | the bar |
|---|---|---|---|
| shown within 1 s, day / midday / night | 73 % (63 of 86) / 52 % (16 of 31) / 100 % (12 of 12) | 75 % (86 of 114) / 50 % (8 of 16) / 0 % (0 of 5) | ≥ 90 % |
| activity missing, day / midday / night | 395 / 235 / 9 s | 582 / 111 / 93 s | ≤ 208 / 80 / 11 s (day ≤ 200) |

**C does not pass its test.** The line itself works — over the acceptance run 631 activities and 1,109 hands were applied from it and
3,496 of the old rows retired, and the owner left 247 activities to the NPC-state placement (`24-npc-state`) — but the copies' activities
show no sooner: what keeps an activity off a copy is its placement on the joiner (the game's planner refusing or pacing it), not the two
descriptions the line replaced.

## D — outfits (`outfits`)

**The host's outfits.** The host reads each streamed NPC's worn items, every slot (the soul's equipment manager: its armour and its
weapons, by class), one body every 0.1 s in turn, and sends a body's set when it is first streamed and whenever it changes (an event
row: `ActivityExtra` kind 7). **The joiner** compares that set with what the copy wears here and dresses it through the game's own
inventory calls: the missing items are made in the copy's inventory and equipped, the extra ones taken off; 2 s later it reads the
copy again (`MP-W216 outfit`). An item made and equipped in the same call does not take — a first build's copies wore none of a freshly
made set 2 s later (in a 10-minute village run 84 of 106 checks matched, 67 items missing) — so a check that still finds items off
dresses the copy again, at most twice, with the items that now exist.

**On the bench** (a village by day, every bound copy within 30 m of the players — 13 of them; the work order asked for 20, the town had
13 there): **13 of 13 copies matched their host NPC slot for slot, 51 of 51 items**, read at both ends through each DLL. The frame pairs
(the host's view and the joiner's of the same NPC) are taken, but the joiner's camera, a metre from the host's player, is mostly the
host's figure: the slot reads are the measure.

In the acceptance run the outfit check (every bound copy within 30 m) matched 7 of 7 copies, 38 of 38 items; over the whole run 105
copies were dressed and 393 of 395 checks matched (no item missing, 2 extra), 68 of them after dressing again. The twin (`outfits` off): 11 of 16 copies matched their host NPC, 62 of 80 items — the copies' own clothes.

## E — riders (`riders`)

**The horse.** With `mp_route` on, a horse copy whose host horse runs a movement request with a goal is moved by that request — the
replay, never the writer's entity writes — and returns to the writer (WO-210's horse rules) when its line ends. With `mp_route` off (the
default), horses are the writer's, as in 0.64.2. **The rider** is attached the engine's way: WO-200's mount (`ForceMount`), once the horse
copy is bound with physics here and within reach; it was already the path in 0.64.2, and E keeps it.

**The ride-past** (scene 5): on the host one of its NPCs is mounted on one of its horses (the game's own mount) and the horse is given the
engine's movement request (the same request a brain makes) along a 60 m line past the joiner: twice at a trot, once at a gallop. On the
joiner, twice a second, the rider's copy mounted or not, the horse copy's height over the ground, the rider's over the horse.

| the ride-past | every switch on (`mp_route` off: the horse on the writer) | every WO-216 switch off (the twin) | the bar |
|---|---|---|---|
| the rider on its horse, whole passes | 2 of 2 sampled (the first trot's 25 s got no answer from the joiner while its copies bound) | 3 of 3 (the first trot sampled once) | 3 of 3 |
| the horse's height over the ground | 7 mm (every sample) | within 2.9 cm | ≤ 10 cm |
| the rider's height over the horse | 1.55 m, steady (no hovering) | 1.47–1.55 m | — |

A dedicated run (b9, the same build's shipped settings) had the rider on its horse in 3 of 3 passes, the horse within 6 mm of the
ground. With `mp_route on` the horse is replayed: **not measured** — the bench run with `mp_route on` crashed (A) before its ride-past, and the shipped default leaves horses on the writer, which carried every pass above.

**Which of WO-210's rider rules retire: none.** WO-210 G built one rule (`mp_rider_sync`): a rider whose horse this world cannot mount him
on — a horse with no physics here — is put on the ground under the host's rider, not left at saddle height in the air. In every pass the
mount carried the rider and the ground rule never fired; it stays as the fallback for a horse with no physics on the joiner (one parked by
its pool, or out of this machine's physics range). (A first bench run picked an animal-care horse whose name is 65 characters long; the
stream carries names of at most 63, so that horse was never a copy and its rider could not mount — a limit of the stream, noted below.)

## F — poses through the one owner (`poses`)

- **F.4 the combat rows go through the owner.** A host NPC's attack row (a swing, a special, a paired attack) on a copy the owner holds
  was refused until now (WO-214 D.2's `owner-owns`: the row path stood back for the owner, which then showed only the stream's state a
  moment later). The owner now plays the row itself: it retires its own full-body action, installs the row's fragment and holds the full
  body for 1.4 s (about one swing) so that its own watchdog does not replace it (`WO216-ROW`). Hit and death reactions keep WO-214's own
  path, which already played them on owned copies. Rows of a fight with a player are `poses`'; rows of a fight among the host's NPCs are
  `npc_fights`' (G).
- **F.2 falls and get-ups.** A host NPC knocked down reaches its copy as the stream's down bit (WO-210 C.3: the engine's own ragdoll,
  then the engine's get-up); the host now logs each of its NPCs' down and up as its stream sees them (`WO216-KD host`), so a copy's
  latency is measured from its host NPC's own fall — not from the moment the fall was asked for (on the bench the host's own bandit went
  down 2.06 s after the call, and its copy 0.18 s after that).
- **F.1 looks** come from the host's look rows (WO-143: the host NPC's look target — its player, the joiner's player, another NPC); a
  copy that fights, is down or talks takes no look (its fight has it). Measured both ends (`MP-W216 look host`, `MP-W216 look copy`).
- **F.3 clinches** are the engine's paired moves (a sync attack and its partner's answer), counted from the joiner's clinch lines; the
  host's paired attack rows go through the owner like every attack row.

**Scene 3, the brawl** (three bandits on both players for 80 s by night, two of them knocked down by the game's own `Actor.Fall` and
stood up 4 s later; the acceptance run against its twin):

| the brawl | every switch on | every WO-216 switch off (the twin) | the bar |
|---|---|---|---|
| a copy's fall after its host NPC's own | 97 ms, 158 ms | 149 ms (one of two: the second bandit never went down on the host) | ≤ 300 ms |
| a copy's get-up after its host NPC's own | +118 ms; one copy up 3.9 s **before** its host | +69 ms | ≤ 300 ms |
| the host's attack rows shown on the copies | 53 of 59 (attacks 52 of 55, paired 1 of 4) | the owner refused 7 of the 12 rows sent to its copies (`owner-owns`, WO-214 D.2) | ≥ 95 % (recoils, paired hits) |
| clinch lines on the joiner | 10 | 5 | 2 of 2, none looping |
| looks matching the host's within 1 s | 17 of 19 (89.5 %); the village walk 599 of 836 (72 %) | 9 of 9 (all "no target"); the village walk 150 of 203 (74 %) | ≥ 90 % |

Falls reach the copy in time; the early get-up is WO-210's own: a copy's knockdown is held about 4.5 s and then stood up, while that
host bandit lay 10 s. The paired attacks (a sync attack, played on the attacker; its partner's answer needs the copy that receives it in
step) are what keeps the rows under the bar. No recoil row came: a recoil is a blocked stab, and no one on the bench blocks.

## G — fights the players are not in (`npc_fights`)

A fight among the host's NPCs (neither player in it) reaches the joiner as what it already had — each fighter's opponent (WO-187/209's
mirror, pinned with `combat_forcedTarget`), its state on the animation stream — and now its rows: every attack row of an owned copy in a
fight among NPCs is played by the owner as F.4 describes (`npc_fights`); hit and death reactions keep WO-214's path.

**On the bench** the work order's ambush (bandits and guards set on each other at the bandits' camp, both players watching from 30 m,
60 s) needed two tries to stage: the first picked a caravan's escort, who sit in the cart stance and never fought; the second brought a
guard of another village's point of interest to the camp. The guard and a bandit fought for the 60 s: **the host's 9 attack rows of those bodies all reached the
joiner and were played there (9 of 9, the row path)**; the probe saw 5 of the 12 rows (attacks, hits, a paired attack) on the copies.
The 7 it did not see were all the guard's: brought 2 km to the camp, his copy on the joiner was bound with no physics and its owner never
took it in the fight's 70 s — its full body stayed empty, and nothing played on it. The bandit's rows showed. In the acceptance run (scene 7, by day: three bandits against three guards of a point of interest, 66 s)
**the host's 19 attack rows of those six bodies were all played on the joiner (19 of 19)**; the probe saw 15 of their 25 rows (the rest
were the far guards' copies out of its 40 m) and 11 of those 15 were shown (attacks 8 of 10, hits 3 of 3, paired attacks 0 of 2). The twin (`npc_fights` off), the same scene: of the host's 40 attack rows 11 were played on the joiner (27.5 %) — the owner refused the rest (`owner-owns`) — and the probe saw 28 of its 46 rows shown (61 %).
After the fight the living copies stood in their idle (no copy left in a fight's stance). Two bandits died (guards killed them on
the host): on the joiner each death was applied and the dead copy, left upright by the engine's own death, was laid down by the game's
own ragdoll (`WO195-RAGDOLL … ok`, WO-195's rule) — the deaths are on the ground. (The probe cannot see that: a ragdolled body still
reports its animation's standing idle, so its "standing" is no evidence of a body upright; the after-frame showed neither body.)

## H — the ghost fight (WO-215's E)

WO-215's E (`ghost_fight`): while a ghost is about (a copy whose combat actor the joiner's engine could not set up — the game's own
"Combat actor init failed" line names it), the mod asks once a second whether this player is in combat with only ghosts — no one else
living within 15 m, no fighter within 30 m — and after 4 s of that ends the fight the game's own way (WO-174: `TryEndCombat` and the
skirmish's `StopFight`). WO-216 adds one thing: a body B froze and hid here (the host does not stream it) is no opponent — with B on,
the frozen, hidden bodies around a player would otherwise have kept every fight.

**The test** (scene 4, the road): the encounter pool's parked body on the host (a `dummyWanderer_*`, the field's kind), both players
beside it; on the joiner it is hidden and unbound (B and WO-215 D). The host wakes it (its brain resumed, shown) — the joiner's copy
comes — and sets it on the joiner's player; once the joiner's player is in combat the host parks it again mid-fight (paused, hidden:
dormant), and the joiner's game logs the engine's own failure line for it, as the field's did, with no other living body within 15 m.
The twin is the same with E off (`KCDMP_TEST_W215_OFF=ghost_fight`): the engine's own behaviour.

The bench's runs met no pool body (none came in 2–6 minutes beside the inn, by day or night), so the scene's ghost is a stand-in when
none comes: a lone bandit taken 60 m from his camp — no one else living within 15 m.

| the road, the injected ghost | every switch on, E on | every switch on, E off (H's twin) | every WO-216 switch off, E on (the acceptance twin) |
|---|---|---|---|
| the joiner's player in combat after | 10.1 s | 9.4 s | 9.7 s |
| others living within 15 m at the failure line | 0 | 0 | 0 |
| the ghost parked here (WO-215 D: unbound, paused, hidden) | yes | yes | yes |
| out of combat after the failure line | **2.4 s** | not in the 30 s hold: another bandit of the camp, 42 m off, was fighting the joiner's figure on the host (its mirror had joined this player's skirmish 16 s before the line) — a real fight | **2.5 s** |
| E's own end (`WO215-GHOSTFIGHT`) | not needed | — (off) | not needed |

**What the bench shows:** a lone ghost's fight ends by itself here — the copy parked by WO-215's ghost rule is gone from the joiner's
fight, and the game ends the player's combat 2.4–2.5 s later, inside E's 4 s, so E never has to act; a real opponent keeps the
fight, as it must — but E looks for fighters within 30 m only: with E on, the twin's fight (a real opponent 42 m off, mirrored into
this player's skirmish) would have been ended after 4 s. Counting the mirror's opponents whatever their distance is the fix; it is not
built here (Open). **What it does not show:** the field's case — a player held for minutes by a ghost that is not parked (the
engine's own failure with nothing for this world to unbind) — and E ending it: both PCs.

## I — the instruments

- **The session probe file belongs to its launch.** The bug report bundles the whole-session probe file (WO-215 G) only when it was
  written during this launch of the game — after the start time the game's own log carries (`Log Started at …`), with a minute's slack
  for the file clock. A file from an earlier session is left out and the bundle says so in its place: `no session file for this launch`
  (the briefing's line too). WO-215 left a gap there: with `probe_all` off, a report could carry an earlier session's file.
- **The Link section's new lines** say in plain words, one per contract: how the copies walked (the host's own requests replayed, the
  corrections by cause, the path parity), the brains found running on the joiner (none, or which), the outfits matched slot for slot,
  the riders on their horses, the poses played (combat rows through the owner, falls and get-ups, looks), the activity line's
  applies, the host's read of its NPCs' requests, and the census's dormant bodies' distances from the host (p50, p90, the nearest).
- **`queue_ms`.** The briefing never prints Steam's "no estimate" sentinel any more: 0.64.1 (WO-214 A.0) prints `-` for it and the
  Link section's reader skips it; the bench's runs here show only real values (the relay's own shaper, 0–481 ms). Nothing to fix.

## The acceptance scenes

On the bench pair at the field's link shape: one run with every switch on — **the shipped defaults** (`mp_route` off, A.4) — and its
twin with every WO-216 switch off, each scene a window of the same run (b12, the installer's code but the version). A's own scene 1
with the route on is in A above (the run with the route on crashed in scene 2). Frames: the joiner's view after each window.

| scene | every switch on (shipped) | every WO-216 switch off (the twin) |
|---|---|---|
| **1. The village walk** (10 min) | walk shown 94.6 %, sliding 0, a start or stop while the host walks 6.4 %, 104 windows with a pull over 50 cm (95 of them two seated tavern guests handed between their seat's hold and the stream) | walk shown 92.6 %, sliding 0, starts/stops 1.6 %, 16 pull windows |
| **2. The blacksmith's house** (day / midday / night) | shown within 1 s 73 / 52 / 100 %; activity missing 395 / 235 / 9 s | 75 / 50 / 0 %; 582 / 111 / 93 s |
| **3. The brawl** (80 s, two staged falls) | falls 97 and 158 ms after the host's own; get-ups +118 ms and one 3.9 s early; rows shown 53 of 59; looks 17 of 19 | fall 149 ms, get-up +69 ms (one fall); 7 of 12 rows refused by the owner; looks 9 of 9 |
| **4. The road encounter** | no pool body in 6 minutes (the scene since takes a lone bandit: H below) | the lone bandit: E's ghost fight ended by the game in 2.5 s (H) |
| **5. The ride-past** | rider on his horse in both sampled passes, horse 7 mm over the ground | 3 of 3, within 2.9 cm |
| **6. The split-up walk** (545 m out and back) | 1 check with a brain running (a far villager), none within 150 m; 4 of 3,149 over the run | every check, up to 438 brains at once |
| **7. The ambush** (60 s) | 19 of 19 host attack rows played on the joiner; 11 of 15 seen shown; the two dead laid down | 11 of 40 played (the rest refused by the owner); 28 of 46 seen shown |
| D (the outfit check) | 7 of 7 copies, 38 of 38 items | 11 of 16 copies, 62 of 80 items |

The frames (village, the outfit pairs, the smithy's three windows, the ride-past, the split-up, the fight and after for the brawl and
the ambush, the road's stand-in) are kept with the WO's notes; most do not frame the copies well (at the smithy the joiner's camera sits
inside a wall, at the brawl the joiner's own bleeding reddens the view), so the scenes are judged on the logs and the probe files, not
on the frames.

## The thread and lock audit

The work order's two rules: every engine movement or AI call is made on the AI update's thread, through the entry hook, never the
main thread; no engine call that may take an engine lock runs under our fault guard (a guarded fault inside a lock unwinds past the
unlock: a hang). Every engine call this WO adds, against both:

| part | the call | thread | under our guard | lock | |
|---|---|---|---|---|---|
| A, the host's read | the movement root's task manager (two getters), the NPC manager's C_NPC of an entity (a hash lookup), the manager's task map and the tasks' fields | the AI update (the entry hook) | yes | none: getters, a lookup, reads | ok |
| A, the replay | WO-215's request build and queue, with the host's style, tolerances and exact end in our own request's memory; the task's own cancel | the AI update | yes | none: the queue has no lock (WO-215 A.0); the manager's cancel-by-id, which locks, is never called | ok |
| A, the end check | the manager's task map, the task's id | the AI update | reads only | — | ok |
| A, the corrector | none (the host's way kept in our own memory) | — | — | — | ok |
| B, the census | the entity system's iterator, an entity's class, name and id; the AI object's suspend byte; the soul's reflected "is dead" | main | yes | none: the main thread's own maps, a byte, a getter | ok |
| B, the act | the console's `ExecuteString` (`wh_ai_PauseNPC <name>`, the call Lua's `System.ExecuteCommand` makes); `CEntity::Hide` (the call Lua's `entity:Hide` makes) | main | **no, on purpose** | may lock (the console's own; render and physics proxies) — so never under our guard: a fault would crash as Lua's own call would, never leave a lock held | ok |
| B, the spawn watch | the original `IEntitySystem::SpawnEntity` (its slot swapped once); the new entity's id | the spawning caller's | the original: no (the engine's own call as its caller made it); the id: yes (a getter) | whatever the spawn takes — never under our guard | ok |
| C, the host's line | the NPC-state context's reads, the hands' item lookups and class getter, the night undress's reads (WO-141's, WO-143's and WO-164's own reads) | main (the stream's read is a main-thread op) | yes | none: lookups, getters, reads | ok |
| D, the outfits | the soul's reflected equipment manager, its two equipped maps and their views | main | yes (the reflection's own sites, as every reflected read since WO-113) | the getters copy two maps; no lock in their registration (the game's REST API serves the same reads) | ok — the one guarded call whose lock is not proven absent |
| D, the dressing | Lua's `FindItem`, `CreateItem`, `EquipInventoryItem`, `UnequipInventoryItem` | main (Lua) | Lua's own `pcall` | the game's own | ok |
| E, the horses | A's queue on a horse's C_NPC | the AI update | yes | as A | ok |
| F.4, G, the rows | the row install WO-208's owner already makes (`ghost_swing`), its retire | main (the pipe's main-thread task) | as before | as before | ok (unchanged calls, a new caller) |
| H, I | none in the game (the bench's staging; the agent and the bundle tool) | — | — | — | — |

No movement or AI call this WO adds runs off the AI update's thread, and no engine call that may take an engine lock runs under our
guard: the two unguarded calls are the console's command and the entity's hide, each the call Lua makes itself, on the same thread.

## Gates and the installer

The installer is built by `tools/Build-Installer.ps1` from a clean clone of the commit, under the maintainer's version, with the safety
gates hard and the frame-rate soak waived (the work order gates only the safety ones and the join): `KingdomComeTogether-Setup-0.64.3-9dfc03a-2611100119.exe`, 107,879,762 bytes, sha256
`11b72c01e58e86407730fbec71a2a612e80e2626d4886e434f8c6e0aa504f132`, build id **9dfc03a-2611100119** (the acceptance runs' build is that
commit's code but B's release-only resume rule, the version and the README). The installer stays
local.

The safety gates: inside the build, the **no-input-capture** gate (its self-test caught all 8 planted captures and passed
the clean files; the payload: PASS — nothing can read what a person types or clicks), the **no-input-tooling** gate (1,036 files, 0 hits)
and the **no-screenshot** gate (1,036 files, 0 hits); the **payload**: the native build and its 10,399 checks, the pak, the agent's,
relay's, Setup's and the bundle tool's suites with 0 failures (agent 2,077, log bundle 92, Setup 77, relay 230 + 100 + 37), the 80
synthetic suites, Setup's 41 install checks, the three start saves in; the **publishing** gate (nothing on the denylist in the two
pending commits: PASS); the **live save round trip** on the bench's copy in its own user folder (WO-175 S5: saved, reloaded, the draw kept,
no stuck fight: PASS); **the real saves unchanged** — their hash list matched before and after every bench run and the round trip.
**The join** worked in every bench run whose games came up (two start-ups failed on the bench itself: a relay left running by an earlier
run, and a host save load that never confirmed).

Beside them, measured and reported (never a gate): every offline gate on the committed code, 99 of 99 (the bench's own gate run): the native build and its
10,399 unit checks, the pak, the relay's 230 round-trip tests, the message-table and fault-mode gates, the agent's 2,077 tests, Setup's
77, the log bundle's 92, the 80 synthetic suites (a first run failed four of them on B's first resume rule: fixed, then all passed), the
four static gates (console placeholders, Lua locals, native guards, WO-157), the digest analyzer, the rig's own tests and the save tools'
tests. **The no-input-capture gate ran first and last** over every binary and script built here — the dev build, the rig, the bench
harness and its tools: its self-test caught all 8 planted captures and passed the clean files, then 60 binaries (1,016 imports) and 1,487
scripts — **PASS: nothing checked can read what a person types or clicks**. The real saves' hash list matched before and after every
bench run.

## Open — what needs the two PCs

- **A, with `mp_route on`, on two PCs.** The replay's numbers above are the bench's: one PC, the field's link shape, the same frame
  rate at both ends. Whether a copy replaying its host's request reads better or worse than the writer under two real machines' frame
  rates and the internet's own stalls is the field's to say (`mp_route on` in both consoles; `mp_w216_status` and the briefing's Link
  lines carry the counts). Before the route is on by default: the corrections (a copy pulled back several times a body-minute), the
  starts and stops at each pull, and the pulls over 50 cm must come down — the next step is a corrector that blends a copy along its own
  request (never cancelling it) and a start that keeps the copy's walk when the host's next request replaces the last.
- **B, bodies a layer switches on.** A body the game does not spawn but switches on with its layer (the bench's two point-of-interest
  guards of another village) is found by the 30 s full scan, so its brain may run for up to 30 s, far from both players, before it is
  suspended; it is named (`WO216-BRAINS`).
- **The stream carries names of at most 63 characters.** The animal care's horses (`horse[animalcare/…_<guid>]`, 65) are never copies:
  on the joiner they are its own, frozen by B and — since a body that can never be a copy must not vanish while the host shows its
  own — left shown. Seen by the ride-past's first try; widening the name on the wire is for a later work order.
- **F**: the paired attacks (a sync attack and its partner's answer) show on the copy less often than single attacks; the recoils of a
  blocked stab and the clinches need a player who attacks and blocks (no input on the bench).
- **G**: a guard the bench brought 2 km to the bandits' camp was a copy without physics on the joiner for the whole fight, and its owner
  never took it; NPC fights the game starts by itself near the players (an ambush on a road) are the field's.
- **H**: the bench never held a player with a lone ghost: a parked ghost's fight ended by itself in 2.4–2.5 s (E on, WO-216 on and
  off); the field's minutes-long case, and E ending it, need the two PCs and a real pool ghost.
- **H, a far real opponent.** E ends a player's fight when only ghosts fail and no fighter is within 30 m — a real opponent farther
  out (a mirrored host NPC fighting the joiner's figure from 42 m, as in H's twin) is not counted. E should count the opponent mirror's
  bodies at any distance before it ends a fight.
- **Two PCs** for everything the bench's single machine cannot show: a pool ghost after a real brawl (H), the outfits of a whole
  session's crowd (D), riders and horses on the road with `mp_route on` (E), and a real ambush (G).
