# WO-215 — The animation substrate: copies walk, stand, sit and fight the engine's way

Version **0.64.2** (the maintainer's). Measured on the bench (two copies of the game on one PC, joined through our own relay with the
field's link shape: 246 KB/s, 26 ± 10 ms each way, 0.5 % loss, two stalls a minute) against the bundles of the 0.64.0 and 0.64.1
internet sessions. Every new mechanism ships **on** with its own switch (`mp_w215_status` lists them); its bench twin is the same build
with that switch off (`KCDMP_TEST_W215_OFF=<names>` for both agents and both games) — the scenes' twin turns every WO-215 behaviour off and
keeps the instruments on. Nothing built here reads input:
`tools/Check-NoInputCapture.ps1` passes on every binary and script, test tools included, first and last (below).

Roles only: "the host", "the joiner". "A copy" is the joiner's local body of one of the host's NPCs; "the writer" is the joiner's
per-frame position writer (WO-118); "the owner" is the joiner's animation owner of copies (WO-208).

## A.0 — the engine's movement request

**What it is.** The call a brain makes to move its body is XGenAI's movement request: a target, a speed class and an optional exact
positioning (an end point with a facing), queued into the NPC's own movement task, which the stock movement system plans and the actor's
movement controller, animated character and physics carry out — the same request the shipped console door `wh_ai_RequestDebugMovement`
builds. (`wh_pl_FollowEntity`, named in the work order, only sets the debug camera's follow target.) Its **stop** is the task's own cancel —
the one the queue itself runs before a new request; its **turn-to-face** is the exact positioning with a direction. The DLL re-finds every
piece at start by what it is — the debug door's worker by its two texts, the request's constructor, the path request (by its RTTI name),
the exact setter, the movement root, the queue, the destructor, the task lookup and the task's cancel out of their callers' own bytes, each
checked by bytes of its own — and builds the request the worker's way with its own speed class. A build that moves any piece leaves the
route unarmed (the status says which piece) and the writer moves every copy as 0.64.1 did; a fault inside a request disarms it for the session.

**How it was verified.** On the bench, a probe (a bench-only op) took one copy on the joiner whose brain the mod had paused, put it out of
every writer of ours — the writer, the route and the owner — and gave it requests, logging its position ten times a second beside the
animation probe's record of its full body. A standing copy walked to each point with the engine's own locomotion: the start transition,
the walk, the stop transition (`IdleToMove`, `MotionMovement`, `MoveToIdle`), at the speed its class asks (walk 1.6, jog 2.9 m/s on
average), and stopped 4–44 cm from the point; an exact stop turned it to face within a few degrees. **A target renewed every frame is not
accepted:** every request restarts the plan, so the body stayed in its start transition (116 of 162 samples), stopped and started six
times and fell 21 m behind a target walking at 1.4 m/s.

**What carried the route — and how far.** The same request, renewed at a cadence the plan can follow (every half second or every metre),
toward a point a second and a half ahead of the host NPC's newest sample along its own velocity, with the writer as the corrector. Half a
second ahead was not enough: that point lies inside the distance where the engine plans its own stop, so the copies of walking host NPCs
spent half of their samples in a start or a stop transition and fell behind (16 corrections a body-minute); a second and a half cut the
starts and stops by a third and the corrections to 12. The request does carry a copy with the engine's own walk; a stream-follower built
on it does not yet hold the host NPC's pace: the engine walks to a goal at its class's speed, the host NPC walks its own path at its own
pace, and the gait meter's median gap between them is 10–15 %.

**What the twin showed** (the final build's 32-minute scene run against the same build with every WO-215 behaviour off, the instruments
on; the copies of walking host NPCs): single-frame pulls over 50 cm **225 against 366** — 39 % fewer; the walk shown **70 % against 82 %**;
sliding **3.1 against 2.6** per 100 samples; a start or a stop while the host walks on **33 % against 19 %**. The route takes the pulls (the
field's "teleport") down and keeps every move on the engine's own locomotion, but on the bench it shows the walk less often than the
writer's own walk tags did, and its corrector (12 a body-minute, the work order asked for at most one) slides a copy it pulls back. The
route ships on, as every new mechanism does, with its switch (`mp_route off` puts every copy back on the writer); the tester script asks the
field to compare the two on real machines.

**Found on the way: the request belongs to the AI update's thread.** A bench run of the route crashed the joiner in the AI module's
navigation update, on a job worker, calling through a freed object — a crash the bench had never logged before. The AI module's whole
update runs as a job on a worker thread, and the engine's brains make their movement requests inside it; the route made its own from the
main thread. Starting a request frees the task's previous path request, which that update may be walking at the same moment. Every
request and cancel of the route is now posted by the main thread (the newest order per copy) and made at the AI update's entry, on its
own thread, before the update runs — an entry hook found by the update's own profiler label, its first bytes checked; the route arms
only when that hook is in. No movement call is left on the main thread.

**Found on the way: a guarded fault inside an engine lock is a hang.** The first route build stopped requests with the queue manager's
cancel by request id. That function takes a mutex and then looks the id up; for a request that has already ended the lookup returns a
list's head and the call faults. The DLL's fault guard caught it — inside the locked section — and the joiner's next movement update
waited on that mutex for good (the game stopped answering; the bench stopped it). The stop now is the task's own cancel, which takes no
mutex, and only for a request that is still the task's current one.

## A — copies move by the movement request (`route`, `gait`)

- **A.1 the route.** Every bound copy of a man or a woman on the ground is moved by the request while its stream moves: toward a point
  a second and a half ahead of the newest sample along its velocity, renewed every half second or every metre (early as it nears its
  target), at the gait class of the host NPC's own pace. (A target half a second ahead lies inside the distance where the engine plans
  its own stop: copies of walking host NPCs showed a start or a stop transition half of the time, fell behind and were corrected 16-18
  times a body-minute.) Standing still is one exact request to the host NPC's spot, facing its heading (the engine walks
  the last bit, stops with its own stop and turns). The stream is moved-gated — a host NPC that stops goes silent — so a body's silence is
  its stop, after twice its own usual gap (half a second near, about two seconds far); but when every stream is silent at once (a link
  stall, two a minute on the field's link), the request in flight plays out instead of a stop at the last sample, which a led walker has
  already passed. The writer is only the **corrector**: a copy more than `mp_route_correct_m` (1.5 m) off its way — from the rendered
  sample to its led target — has its request cancelled and is blended back by the writer alone (never snapped; the engine's movement and
  the writer never pull one body two ways), then routed again from there; each is counted (`route_corrections`). At most 12 requests a
  frame.
- **A.2 the gait.** The host's speed, smoothed over 0.6 s, picks walk, jog, run or sprint (a class's own top speed with 5 % headroom calls
  for the next; a step down only under 85 % of the speed that called for it), so a copy does not flap between two clips at a class's edge.
- **A.3 the writer keeps** what the request cannot do, each counted by its reason in `mp_w215_status`: the partner's figure, horses and
  animals, riders, bodies in the air (falls), fighting bodies (a fight's own positioning is WO-209's), bodies without physics here, a body
  whose request the engine refused (5 s), and every copy while the request is not armed.

**On the bench** (the final build, the scene run; the twin's numbers above): 15,573 walk requests and 2,271 exact stops, all made on the AI
update's thread — 0 refused, 0 faults; 497 link stalls held the request in flight; the gait within 15 % of the host's pace in 57.5 % of
the 2 s windows of copies within 40 m; 3,537 corrections in 18,007 routed body-seconds (11.8 a body-minute). The crash the route caused
(above) did not recur in the six bench runs with the route on after the fix (about two hours joined).


## B — no empty body (`ladder`, `letgo_cull`)

**The ladder.** A copy is never left with nothing on its full body. When the host NPC's activity cannot be shown here (C's named refusals
below), the owner gives the next rung at once — the copy's own neutral idle at the base priority, else the engine's (a guard idle in
combat mode, an idle request otherwise) — instead of leaving the refused activity's empty slot; withdrawing a fight's leftover pose
already hands over to the base in the same call (WO-214). **The let-go.** The owner took copies within 60 m and let them go at 70 m; a
copy let go while its host NPC still ran an activity had nothing of ours and its own paused brain gave it nothing — the field's empty
bodies at 60–70 m. An owned copy is now kept to the cull range (150 m) and let go only there, where nothing is drawn.

**On the bench**, neither the final build nor its twin left a body empty: no `WO214-EMPTY` stretch on a living bound copy within 60 m,
no `WO204-TPOSE`, and no empty full body over 0.3 s in the probe's whole-session record — through the village, the smith's house by day
and by night, and an 80-second fight with both players in it. The bench does not reproduce the field's empty bodies (it has no 60–70 m
let-gos of working copies nor pool ghosts), so B's twin shows nothing to compare here; the field's numbers are in its table below.


## C — one activity, one path (`activity_line`)

**Why the two descriptions disagreed.** The host describes an NPC's activity three ways: its NPC-state (WO-141: a stance with its smart
object, an unstance with its location object), its one-shots (WO-143: a smart object's use such as stirring a pot), and its full body's
fragment family (WO-210: working, sitting, lying). The field's joiner reseated 19 "working" copies 95 times and 17 "sitting" copies 65
times, every one `16-install-pending`; **every one of those bodies had a stance or an unstance in the host's NPC-state capture** (cooking at
home, chopping wood, mending a fence, a bench), and most had one-shots as well. Two of ours wrote the same activity onto the copy: the
NPC-state placement, and the owner's watchdog installing the activity's fragment over a copy it judged "standing" — again and again. The
"stance=none while want=working" the work order saw is the moment between: an activity done as a one-shot or an unstance has no stance.

**One path.** The owner's watchdog now asks the NPC-state placement first. When the placement holds the host NPC's activity (placing it
or in its step), the copy is left to it (path `24-npc-state`); when the game's planner gave up on that placement here, the activity is a
named refusal (`25-npc-state-refused`); otherwise the owner installs the activity's fragment twice at most, and a third miss is a named
refusal (`21-refused-here`) — the base instead (B), the copy reported missing that activity (`WO215-ACTIVITY-MISSING`, with the
activity's fragment), asked again after 30 s or when the host's activity changes. A posture of ours that outlived the host's with no exit
recorded — the field's `path=unknown` — is `22-stale-ours`; a host body held dormant is `23-dormant-host` (D). `16-install-pending` is
now at most two tries per activity, never a loop.

**On the bench** (the final build against its twin, the same scene run): the watchdog's `16-install-pending` reseats **0 against 192** —
204 activities were left to the NPC-state placement instead; no `path=unknown`; one named refusal (a woman's lying pose, refused here,
given the copy's own idle). The probe's activity-missing time (a copy standing or empty while its host NPC sits, lies, kneels or works):
**1,285 s against 2,628 s** over the session. C.1: of the activities the host NPCs held for 2 s or more, the copy showed the activity
within a second for 72 % (137 of 190) — the twin 72 % too (260 of 362) — and never while the host held it for 26 % (both). C.2: two
over-full action queues in 31.5 minutes (0.6 per 10 minutes; the twin the same), none of them a look pose.


## D — only live NPCs become copies (`dormant`)

**D.1, the host's census.** The field's Cumans on the road (`dummyWanderer_cuman_*`, `dummyWanderer_horse_*`) were encounter-pool bodies
the host holds parked: an empty full body and no physics, or a brain not running. The host's sender now checks every body it would
stream: one empty for 2 s that way is dormant and not sent (`WO215-CENSUS`), checked again every half second and sent the moment it
wakes. On the joiner, a copy whose host body has been empty for 2 s is parked (paused, hidden, unbound: `WO215-DORMANT`) instead of
standing on the road. **Ghosts.** A body whose combat actor the joiner's engine cannot set up (`Combat actor init failed for actor '…'`,
1,606 lines in the field's joiner log, all one pool Cuman) is a ghost: the agent tells the mod, the copy is unbound and parked, and its
stream is refused until a minute passes without the engine's failure. **D.2 (outfits) is not built**: a copy still wears its own
clothes; the tester script says so.

**On the bench (the final build, a 32-minute scene run).** The census kept 18 bodies out of the stream: the town's dogs and horses, the
pool's Cuman (`dummyWanderer_cuman_3`, the field's), and three villagers of other villages — every one far from the host, with no
physics there and nothing on its full body; none woke while the run lasted. Scene 4's stand-in (both players beside the pool's parked
body): with the census on, the field's Cuman was **hidden and unbound** on the joiner; the twin (the census off) showed the pool's body
there **as a bound copy**. The engine's "Combat actor init failed" did not occur on the bench in any run (1,606 times in the field); the
injected ghost (E) was parked — paused, hidden, unbound — in both runs.


## E — the player is never held in a fight with a ghost (`ghost_fight`)

While a ghost is about, the agent asks the mod once a second: is this player in combat with only ghosts failing, no one else living
within 15 m and no fighter within 30 m? After 4 s of that, the game's own end of combat (WO-174's: TryEndCombat and the skirmish's
StopFight) — `WO215-GHOSTFIGHT`. (A copy on the joiner reports no combat state of its own, so every living body near counts as a
possible opponent: a first version that counted only bodies reporting combat fired beside two paused, living bandits and kept the
player in its fight — the bench's injected test, below.)

**On the bench** E has not been shown to end a ghost fight. The bench never meets a ghost, so the test was injected: the host sets a
bandit on the joiner's figure, pauses it (and its group) once the joiner's player is in combat, and the joiner's game logs the engine's
own failure line for the bandit's copy. With the first version, E fired once — and the game's TryEndCombat refused — and the player
stayed in combat for 45 s; with E off, the game itself ended the fight 2.3 s after the line. That run found the flaw fixed above (the
two paused bandits beside the player were living opponents E could not see). With the fix, E kept out of that fight (living bodies
within 15 m) and the game ended it 2.3 s after the line, as with E off. Ending a fight that only a lone ghost holds — the field's case —
is for the two PCs. The brawl scene (bandits paused, alive, beside the joiner) leaves the joiner in combat for 30 s and more with E on and
off alike: the engine's own rule with hostiles standing near.


## F — riders, mounts and carts

**Measured, not built.** The route never takes a horse, an animal or a rider: it moves men and women with physics only, and a parented
body (a rider on a horse) and a horse stay on the writer and WO-210's rider rules, as in 0.64.1. **Carts, measured first (F.2):** the
"huge object added to the octree" errors of the 0.64.1 session are a wagon wheel's particle effect (`WH_Particels.other.wagon_wheel`), and
the **host** logged more of them than the joiner (949 lines against 387: 840 and 360 of them the wagon wheel, the rest candles); the 0.64.0
session logged none on either machine.
They come with the game's own moving wagons on each machine, not with our writer, and the mod does not stream carts at all (each machine's
caravan is its own). The field's T-posing horses were encounter-pool horses (`dummyWanderer_horse_*`) moving with an empty full body on the
joiner: D's census keeps such bodies out of the stream while their host body is dormant. A rider attached the engine's way, a horse moved
by the route and a cart placed by the host's transform are left for a later work order (Open).

## G — the instruments (`probe_all`)

- **The whole session.** Beside its detail file (every body within 40 m, ten times a second, the last minutes), the animation probe now
  writes `kcdmp-anim-session.jsonl` for the whole session: one body a line, 20 lines a second, round-robin over the bodies within 40 m
  (with thirty bodies, each every second and a half), rotating at 96 MB and keeping the file before it. The bug report bundles both files;
  the briefing reads the session file for empty bodies and missing activities over the whole session.
- **A missing activity is a finding.** A copy standing (or empty) while its host NPC sits, lies, kneels or works, for over a second, is
  `activity_missing` — per copy, with its seconds, from the probe's own fields (the copy's family against its host NPC's as the owner last
  applied it); and the owner's named refusals (`WO215-ACTIVITY-MISSING`) say which activity a copy could not show here, and why.
- **The briefing.** The Link section ends with one line per contract in plain words: how the copies walked (the game's own walk, its walk
  orders and stops, how often the writer pulled one back, how often their pace matched the host's), the single-frame jumps over 50 cm,
  empty bodies over the whole session, the activities missing and refused, and the dormant and ghost bodies kept out.

## The acceptance scenes

On the bench pair at the field's link shape, one run with every switch on and its twin with every WO-215 switch off, each scene a
window of the same run (frames: the joiner's view, taken after each measured window — a frame stalls the game for a moment).

| scene | the final build | its twin |
|---|---|---|
| **1. The village walk** — 10 min, the joiner following walkers 10–30 m behind (near in 21 of 21 checks) | single-frame pulls over 50 cm 225; walk shown 70 %; sliding 3.1 / 100; a start or stop while the host walks 33 %; 11.8 corrections a body-minute; gait within 15 % in 57.5 % of the windows; no empty body | pulls 366; walk shown 82 %; sliding 2.6; starts/stops 19 %; no empty body |
| **2. The blacksmith's house** — the field's own smith: 4 min at the anvil by day, 2 min at midday, 2 min at night | 0 install-pending reseats (103 activities left to the NPC-state placement); activity missing 416 / 161 / 22 s (day / midday / night); the smith's copy at the heating and forging loops in 90 of 168 posture samples by day, the copy's own idle in the rest | install-pending 5; activity missing 355 / 324 / 26 s |
| **3. The brawl** — no tavern brawl starts without input on the bench: three bandits set on both players at their camp for 80 s, then stopped by the host | no empty body, no T-pose; the joiner still in combat 30 s after (the bandits alive beside it) | the same |
| **4. The road encounter** — both players beside the pool's parked body | the field's Cuman (`dummyWanderer_cuman_3`) hidden and unbound on the joiner | a pool body shown and bound as a copy |
| **5. The ride-past** | not run: F.1 is not built (riders stay on the writer, as in 0.64.1) | — |

The frames show the village, the smith's workshop by day and at midday, the smith at the forge at night in the copy's own idle, the pool's
spot at night with nothing standing there, and the fight with a bandit's copy at its swing — no copy in the bind pose in any of them.
An encounter firing (its copy appearing within 2 s, dressed as the host's) and the ride-past need the two PCs.

## The field's table

The 0.64.1 field session's joiner (74 minutes; its own logs, the same measures as the bench's) against the bench's final build and its
twin.

| seen in the field | the field's logs (0.64.1, the joiner, 74 min) | part | the bench: the final build / its twin | |
|---|---|---|---|---|
| NPCs moonwalk; "not smooth"; fade and teleport | 467 windows with a single-frame pull over 50 cm (the largest 143 m); the copies of walking host NPCs shown walking in 25 % of the probe's last-minutes samples | A | pulls over 50 cm 225 / 366; walk shown 70 % / 82 %; sliding 3.1 / 2.6 per 100 | **improved** (pulls), **not** the walk itself |
| enemies drop into the T-pose or freeze fist-up after fights; "animations freeze" | 32 empty-body stretches (one body: the pool Cuman), 163 owner let-gos | B, E | no empty body, no T-pose in either run (the bench has no such let-gos) | **improved** (let-go at the cull range, the ladder) — the bench cannot show it |
| activity animations T-pose all session; the sleeper and the smith at the blacksmith's | 167 `16-install-pending` reseats (95 "working"), 61 "sitting not installed", 8 `path=unknown` | C | install-pending 0 / 192; `path=unknown` 0; activity missing 1,285 s / 2,628 s | **retired** (the loop), **improved** (missing time halved) |
| a Cuman nobody on the host saw, empty at 10 m for 5½ min; the joiner kept boxing | 1,606 "Combat actor init failed" for that Cuman; 33 empty-body lines | D, E | the Cuman not streamed, hidden and unbound / a pool body shown and bound as a copy; E not shown ending a ghost fight | **retired** (the Cuman), E **not proven** |
| NPCs naked or a different person | the host NPC's outfit never sent | D.2 | — | **not touched** |
| riders and carts flying, NPCs hovering | 387 "huge object added to the octree" on the joiner (949 on the host), the wagon wheel's particles | F | measured only | **not touched** |

## Gates and the installer

The installer is built by `tools/Build-Installer.ps1` from a clean clone of the commit, under the maintainer's version, with the safety
gates hard and the frame-rate soak waived (the work order gates only the safety ones and the join):
`KingdomComeTogether-Setup-0.64.2-f6bca07-2611090723.exe`, 107,829,676 bytes, sha256 `02299b476ac3069e2d92f10a785ce72a1d5831483bd73004556c156e3a49333f`,
build id **f6bca07-2611090723** (the bench's final build is that commit's code). Inside the build: the publishing gate PASS (nothing on
the denylist in the pending commits); the live save round trip PASS (the bench's copy, its own user folder); payload coherence and smoke
PASS; the no-input-capture, no-input-tooling and no-screenshot gates PASS; every unit suite with 0 failures (agent 2,069, log bundle 87,
setup 77, relay 229 + 99 + 37); the three start saves in; Setup's 41 install checks PASS. The real saves' hash list matched before and
after every bench run and the installer's round trip. The installer stays local.

Beside it, every offline gate on the commit (99 of 99): the native build and its 10,360 unit checks (the new hook's first bytes among
them), the pak, the relay's round-trip, message-table and fault-mode gates, the agent's, Setup's and the log bundle's suites, the 80
synthetic suites, the four static gates (console placeholders, Lua locals, native guards, WO-157), the digest analyzer, the rig's own
tests and the save tools' tests. The no-input-capture gate ran first and last over every binary and script built here — the dev builds,
the rig, the bench harness and its tools: its self-test caught all 8 planted captures and passed the clean files, then 60 binaries
(1,016 imports) and 1,474 scripts — **PASS: nothing checked can read what a person types or clicks**.

## Open

- **C.0's single wire line is not built.** The host still sends its two descriptions (the NPC-state capture with its objects and hands,
  and the full body's family); the joiner joins them in one decision — the NPC-state placement first, the owner's fragment otherwise,
  every outcome named. A host-side line carrying activity, object, stance, posture, hands and looping together is the next step.
- **D.2 (outfits) and F.1 (riders attached the engine's way, horses on the route, carts as the host's props) are not built.** F.2 was
  measured: the octree errors are a wagon wheel's particles, more on the host than on the joiner — the game's own, not the writer's.
- **E is not proven.** The bench never logs the engine's combat-actor failure; on an injected ghost (the engine's own failure line for a
  bandit's copy the host had paused) E's first version kept the player in the fight and its fix keeps out of fights with anyone near — it
  has not yet been seen ending a fight only a ghost holds. A real pool ghost after a real brawl needs the two PCs.
- **A does not meet its own test.** Pulls over 50 cm are not 0 (fewer than the writer's, not none), the probe still sees sliding,
  the gait is within 15 % in 57.5 % of the windows, and the corrector works 12 times a body-minute (the work order: at most once). The
  request is a goal at a class's speed; following a stream with it costs starts, stops and pace. The next step is the host NPC's own
  movement request — its goal and its class — replayed on the copy, so both engines walk the same path at the same pace; until then the
  field compares `mp_route on` and `off` (the tester script, scene 1).
- **The census and far bodies.** A body with no physics on the host and nothing on its full body counts as dormant — the pool's parked
  bodies, and also far animals and villagers of other villages. With the players far apart, copies near the joiner but far from the host
  are not streamed and run on the joiner's own engine until the host's world wakes them: the two PCs will say whether that is better
  than the empty copies 0.64.1 showed there.
- **The whole-session probe file** is written only while `probe_all` is on; a bug report made with it off can carry an earlier
  session's file (the briefing reads its own times, but says nothing of the gap).
- **A on the internet.** The bench's relay has the field's link shape, not its machines: the gait meter, the pulls and the corrections
  under two real PCs' frame rates are the field's to confirm (the briefing's Link section now says them in plain words).
