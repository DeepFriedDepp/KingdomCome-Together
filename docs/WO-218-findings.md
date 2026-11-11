# WO-218 — The release candidate: the joiner's pipe, stability, and the open items of 215–217

Version **0.65.0** (the maintainer's). Measured on the bench (two copies of the game on one PC, joined through our own relay with the
field's link shape: 246 KB/s, 26 ± 10 ms each way, 0.5 % loss, two stalls a minute). Every new mechanism has its own switch
(`mp_w218_status` lists them); a twin is the same build with that switch off (`KCDMP_TEST_W218_OFF=<names|all>` for both agents and
both games), the instruments kept on. Nothing built here reads input: `tools/Check-NoInputCapture.ps1` passes on every binary and
script, test tools included, first and last (below).

Roles only: "the host", "the joiner". "A copy" is the joiner's local body of one of the host's NPCs; "the writer" is the joiner's
per-frame position writer; "the owner" is the joiner's animation owner of copies; "the pipe" is the channel between a machine's agent
and its game's DLL; "the frame processor" and "the activity lane" are the joiner agent's two queues of the host's NPC traffic.

**The pipe's answer.** The joiner fell behind because every request of its agent to its DLL was one round trip of one frame, one at a
time, while the host's streams asked for more than one a frame. Now one round trip carries every waiting request, the host's NPC
states are posted and merged per body, and the activity lane runs each body's rows on that body's own queue. In the acceptance (every
default as shipped, the field's link shape) the joiner's frame processor stayed **88 ms behind at the 99th percentile in the packed
tavern and 130 ms in the brawl** (0.64.4 on the same bench: 6.9 s and 756 ms; the twin of this build in the same acceptance, every WO-218 switch off: 53 s and 7.3 s); its worst moments
(2–5 s) were the joiner's own game standing still while a trade screen, a conversation's scene or a sleep loaded. The activity lane,
in the acceptance's build still one lane for every body, trailed **910 ms (tavern) and 1.97 s (brawl) at the 99th percentile**; the
release build gives every body its own lane: in its check (the same talks, tavern and brawl, fresh after the join) the frame processor stayed **38 ms** behind at the 99th percentile in the tavern, a row of the lane waited **0–15 ms** on average before it ran (0.27 s at worst) and ran 23–52 ms, and the lane's oldest frame was 0.75 s (tavern) and 1.42 s (brawl) old at the 99th percentile — that age also counts each row's own run and the frames the lane handles in line, and stays over the bar.

**The soak's answer.** Sixty minutes joined inside the acceptance (the partner walking the village, two talk rounds, two fights, a loot,
a sleep vote): **no crash, no save fault, no new engine fault, memory flat** (the host's private bytes 18.6 → 19.1 GB, the joiner's
15.9 → 16.2 GB), the joiner's queue under 0.75 s at the 99th percentile in every minute. The installer's own frame-rate soak (the
release gate, the game with the mod against the game without it): **passed** on the release build — with the mod 145.9 fps in the crowd and 135.6 in the fight, without it 146.7 and 139.0; the stat stack 0 in every row, no FAULT line.

## What this build is

| part | what | switch (on) | verdict |
|---|---|---|---|
| A.1 | the batch: one round trip carries every waiting request, at most 2 ms of the main thread a frame | `pipe_batch` | met for the frame processor; the lane's rows wait under 0.3 s, its age not under 250 ms at the 99th percentile |
| A.2 | back-pressure: far, superseded state shed while the queue is over 0.5 s | `pipe_shed` | built; fired only after joins |
| A.3 | the host's own polls ride the same batch | (`pipe_batch`) | met (C.8: get-ups 101–180 ms) |
| B.1 | the fight crash | — | the crash gate passed (6 × 5 min, no crash); no cause found |
| B.2 | our main-thread time | `main_budget` | not met (below) |
| B.3 | the host as a listener | `listen_reassert` | instrument + re-assert; not reproduced |
| B.4 | the soak | — | passed (above) |
| C.1 | one writer: a drifted copy's own posture released | `drift_release` | not met: flips remain (a stronger fix tried, taken out) |
| C.3 | the copy's own action taking a placed activity over is stopped | `takeover_stop` | better, not met |
| D.1 | doors: the joiner never locks one by itself; the host sweeps doors within 60 m | `door_sweep` | met within 60 m of a player (9–10 of 10 uses); doors farther off may differ until a player comes near |
| E.4 | the loot screen's refresh timed in the game | — | met: 96–206 ms |
| E.1–E.3, C.2, C.4–C.7, C.9, D.2–D.4, E.5 | — | — | not built (What was cut) |

## A — the joiner's pipe (`pipe_batch`, `pipe_shed`)

**A.0 — the measure, on the shipped 0.64.4 build.** The packed tavern by night for 5 minutes, then the same tavern with the host's
game minimised, then the brawl; the field's link shape; the DLL's frame-cost meter on.

| | the tavern | the tavern, the host minimised | the brawl |
|---|---|---|---|
| the joiner's frame processor behind (p50 / p99 / worst) | 0 / 6,900 / 8,015 ms | 0 / 112 / 205 ms | 33 / 756 / 933 ms |
| the activity lane (activity rows, extras): mean wait | 1.0 / 1.4 s | 0.5 / 0.9 s | **5.4 / 7.1 s** |
| the joiner's frame rate | 64 fps | 59 fps | 35 fps |
| an NPC animation frame (9–18 a second): the processor's time on it | 41 ms (1.1 round trips) | 33 ms (1.0) | 103 ms (1.0) |
| an activity extra: the lane's time on it | 85 ms (2.6 round trips) | 137 ms (4.0) | 212 ms (2.2) |
| a pipe round trip | 15–16 ms | 16 ms | 29 ms |
| the host's pipe: main-thread requests served a frame | 0.92 | — | 0.85 |

(WO-217's acceptance tavern, the same scene after 40 minutes of others with the joiner at 36 fps, had the processor 100 s behind.)

**Where the time went.** A request of an agent to its DLL was one round trip, and every round trip waited for one frame of the game
(the DLL runs the request on the game's main thread and answers when that frame has run it): 15–30 ms each. The agent's lanes — the
frame processor, the activity lane, the conversation lane — and its own background polls each waited at one gate for their turn, one
request at a time; the host's pipe was measured at 0.85–0.92 requests a frame, its ceiling. An NPC animation frame held the joiner's
processor for one round trip plus its wait at the gate; an activity extra held its lane for two to four. In the brawl the activity
lane fell 5–7 s behind.

**The bench's share and the design's.** With the host's window minimised (its frame cap did not take; nothing of it was drawn) the
joiner ran at the same 59 fps as beside the host's drawn window (64 fps): in this run the GPU was not the joiner's limit, and the
host's game, minimised, sent a third fewer state frames — so the throttled tavern's clean queue is the lighter load, not a faster
joiner. The joiner's frame rate sets the pipe's rate (the bench's late tavern at 36 fps halved it), but the queue grows at any frame
rate once the demand passes one round trip a frame: in the brawl the activity lane needed about 2.4 round trips per frame of its own.
**The design's share: one request a round trip. The bench's share: the frame rate it runs at.** A joiner on its own PC at about twice
the bench's frame rate halves each round trip; it does not change their order.

**A.1 — the batch.** One round trip now carries every request waiting (up to 3.8 KB and 64 requests); the DLL runs a batch in order in
one main-thread turn of at most 2 ms a frame (at least one request a frame; the rest the next frame) and answers once. One batch is in
flight at a time; what arrives meanwhile rides the next. The batch takes no place in the single requests' sequence, and its answer is
written from the main thread, so the pipe thread keeps answering the single requests (the player's position read, the NPC position
feed) while a batch waits for its frame. Latest-wins state is coalesced before it is sent: the host's NPC states (the animation stream)
are posted, never awaited (their answer was never used), and merged per body while they wait — only a body's newest state is ever in a
batch; the activity lane takes every frame waiting at once, keeps each body's newest row of a state kind (every one-shot, in order) and
runs the bodies' rows at the same time, so their requests ride one batch. Damage, deaths and the position read stay single requests.

**A.2 — back-pressure.** While the frame processor is more than 500 ms behind, a superseded state of a body beyond 40 m is dropped at
dequeue (a newer one of the same body already waits); over 1 s a far body's state at most once a second; over 2 s the 20–40 m band too.
Never an event. When the queue is back under 500 ms every body shed meanwhile has its whole-body state asked again of the host
(`MP-PIPE shed …`). In the runs below it fired only in the seconds after a join (the world load).

**A.3 — the host.** The host's polls (its NPC state read ten times a second, its route and opponent reads, its activity capture) are
requests of the same families and ride the same batch.

**The test** — the village's talks, the packed tavern 10 minutes, the brawl; the build with the batch against its twin (the same build,
`pipe_batch` and `pipe_shed` off); the joiner's queue is the frame processor's lag (the activity lane's mean waits beside it):

| | the batch on | the twin (off) | 0.64.4 (A.0) |
|---|---|---|---|
| the tavern: p50 / p99 / worst | **0 / 111 / 291 ms** | 8,449 / 27,366 / 28,350 ms (57 % of the time over 1 s) | 0 / 6,900 / 8,015 ms |
| the tavern: the activity lane's mean waits | **39 / 9 ms** | 190 / 221 s | 1.4 / 1.0 s |
| the brawl: p50 / p99 / worst | **0 / 141 / 382 ms** | 0 / 237 / 524 ms | 33 / 756 / 933 ms |
| the brawl: the activity lane's mean waits | **5 / 6 ms** | 416 / 416 s | 7.1 / 5.4 s |
| the joiner's talks: p99 / worst | 593 / 1,259 ms | 2,717 / 4,883 ms | — |
| a knocked-down copy's fall after its host NPC's | 373, 108 ms | 611, 530, 235 ms | 768, 129 ms |
| its get-up after its host NPC's | 68, 85 ms | 629, 366 ms | — |
| hit verdicts sent to the joiner / accounted for there | 7 / 7 | 39 / 39 | 12 / 12 |
| the DLL: requests served a frame (mean / max) | 1.6–2.6 / 31 | ~1 | ~1 |

**Against the bar:** the tavern's p99 (111 ms) and the brawl's (141 ms) are under 250 ms and never over 1 s; no reliable event was lost
(every hit verdict the host sent is on the joiner's side, applied or — one blow, one judge — judged by the joiner's own engine). Not met:
one of the two knockdowns fell 373 ms after its host NPC (the bar 300 ms); the joiner's talks touched 1.26 s twice, each at a 2.5–2.9 s
batch answer — the joiner's own main thread stood still while the trade screen and the forced dialogue's scene loaded, and what waited
for that frame waited with it. The twin shows the minute: 27 s in the tavern, its activity lane 3–7 minutes behind.

**In the acceptance** (every default as shipped, after 45 minutes of other scenes; the joiner at 29–35 fps in the tavern and the fights):

| scene | the frame processor, p50 / p99 / worst | the activity lane (one lane), p50 / p99 / worst | the joiner's fps |
|---|---|---|---|
| the joiner's talks and trade | 0 / 389 / 2,606 ms | 137 / 683 / 2,887 ms | 54 |
| the host's talks and trade | 0 / 227 / 759 ms | 123 / 639 / 2,758 ms | 45 |
| the smith's house (three windows) | 0 / 1,207 / 5,321 ms | 205 / 1,148 / 3,244 ms | 34 |
| **the packed tavern, 10 minutes** | **0 / 88 / 4,616 ms** | 155 / 910 / 1,269 ms | 31 |
| **the brawl** | **0 / 130 / 163 ms** | 265 / 1,966 / 2,073 ms | 29 |
| the ambush | 0 / 1,513 / 3,609 ms | 190 / 1,058 / 1,138 ms | 38 |
| the loot, the doors, the village walk | 0 / 56–100 / 94–142 ms | 117–128 / 579–802 / 742–996 ms | 39–56 |
| the soak's two fights | 0 / 69–99 / 71–114 ms | 426–524 / 2,038–2,040 / 2,040–2,563 ms | 34–35 |
| the sleep vote | 0 / 1,358 / 1,735 ms | 180 / 1,672 / 1,763 ms | 41 |

The frame processor met the bar in the tavern and the brawl; its worst moments elsewhere (2–5 s) are each a load on the joiner's own game — the trade screen, a conversation's scene, the sleep — when the game's main thread stood still and every request waited for that frame. The activity lane did not meet it: one lane took the bodies' rows in turn, and a body whose row waited for its round trips held every other body behind it (a fight's many state rows most). The release build runs every body's rows on that body's own queue (24 bodies at once; a newer state row replaces the waiting one of its kind, a one-shot waits in order). Its check, against the acceptance (which ran after 45 minutes of other scenes; the check fresh after its join):

| | the acceptance's build (one lane) | the release build (a lane per body) |
|---|---|---|
| the tavern: the frame processor, p99 / worst | 88 / 4,616 ms | **38 / 64 ms** |
| the tavern: the lane's age, p50 / p99 / worst | 155 / 910 / 1,269 ms | 158 / 748 / 977 ms |
| the tavern: a row's wait (mean / worst), its run (mean / worst) | 47 / 1,769 ms, 53 / 1,358 ms | **15 / 266 ms**, 28 / 257 ms |
| the brawl: the frame processor, p99 / worst | 130 / 163 ms | 949 / 1,002 ms (at the bench's two screenshots) |
| the brawl: the lane's age, p50 / p99 / worst | 265 / 1,966 / 2,073 ms | 247 / 1,424 / 1,955 ms |
| the brawl: a row's wait, its run | 15 / 166 ms, 31 / 200 ms | 8 / 124 ms, 33 / 462 ms |
| the joiner's talks: the frame processor p99 / worst; the lane's age p99 | 389 / 2,606 ms; 683 ms | 128 / 1,188 ms; 659 ms |

The rows themselves no longer wait for each other; what is left of the lane's age is each row's own run (one to three round trips of the joiner's frame, more while its game loads something) and the lane's other frames, handled in line.

Reliable events: every hit verdict the host sent (32) is on the joiner's side — 7 applied there, 25 judged by the joiner's own engine (one blow, one judge); none missing. The DLL served 1.8–2.5 requests a frame at the mean (16 at most), drained in 58 µs a frame on average; no sub-request faulted, none was cut. The shed fired 12 times, all in the seconds after the join.

**What the batch exposed.** With the activity rows on time, the joiner's copies show a family of "flips" the backlog had hidden — a copy
placed alternately at two spots every frame for seconds to minutes, most often after a fight (249 windows of 10 s in the batch's run
with a crime in the tavern, 25 in the twin's, 6 in A.0's); the field's 0.64.0 / 0.64.1 sessions show the same family (31 of 981 and 180
of 3,606 windows), so it is not the batch's: a copy whose own activity places it at its object while the stream writes it at its host
NPC. One case traced end to end is C.1's fix below.

## B — stability

**B.1 the fight crash.** The bench's crash reports since WO-213 hold three joiner crashes: WO-215's navigation crash (fixed then: every
route call on the AI update's own thread), WO-216's route crash (the joiner's encounter pool deleting a copy while a route request of
ours ran on it — only with `mp_route on`, which ships off and which this work order leaves alone, C.10), and WO-214's one crash in a
fight with copies attacking the player, of WO-207's freed-memory family, whose dump did not survive on the bench. With no dump to read, B.1
is the crash gate on the release build: six staged fights of five minutes in one session (the host and the joiner against two or three bandits each, the bandits on both players — the joiner's health down to half — while the copies fought them): **no game lost, no crash report on either machine**, the real saves unchanged. The joiner's own swings were not sent (the bench's keys never take a window's focus, and the joiner's window was behind); the case WO-214's crash came from — copies attacking the player — is the one this covers. No cause was found, since none was caught. Page-heap checks were not used: they are a system setting (the image's global flags),
which this work does not change.

**B.2 the main thread's time.** Per module, from the new meter (`mp_main_cost on`: `WO218-COST` per area of the module tick, and the
frame line's 95th percentile): on the build before the budget the largest item on both machines was WO-206's digest sweep (the bug
report's divergence checkpoints: 120 quest-value reads a frame, a pass every 2 s) — 1.4–1.6 ms a frame on the host, 0.8–1.0 ms on the
joiner, and the host's 95th percentile of our time 12.6–13.5 ms (the frames the sweep's slow reads fell in). Seven research file probes
of earlier work orders read a file in the game's folder every frame (40–65 µs each). `main_budget` (on): the digest spends at most
0.3 ms of a frame (the rest of its share the next frame; a pass takes longer, the checkpoints stay 5 s apart) and the file probes look
every 2 s. In the acceptance our time on the host was 1.6–1.7 ms a frame on average and **9.4–9.9 ms at the 95th percentile** (the joiner 2.0–3.4 ms, 4.9–7.5 ms) — **not met** (the bar: 1.5 ms at the 95th percentile in the brawl). What is left at the top, by the meter: WO-206's movement probe (the bug report's record of the bodies near the player, ten times a second: 0.7–1.3 ms a sample, up to 10 ms when it gathers the bodies), the pipe's own work (0.4 ms a frame at 0.75 requests a frame on the host) and the health sample (0.1 ms, 1.9 ms worst). Against the twin (the budget off, the same plan): our time on the host 2.2–2.3 ms a frame on average and 9.4–9.9 ms at the 95th percentile, the joiner's 3.6–4.1 / 7.9 ms — the budget took about half a millisecond a frame off both machines' average (WO-206's area: the host 0.96–1.02 → 0.57–0.61 ms, the joiner 1.69–1.75 → 0.91–1.03 ms) and nothing off the 95th percentile, which sits in other frames. The host's frame rate from the tavern to the brawl: 52.4 → 47.7 fps as shipped, 53.5 → 49.6 in the twin — the few frames a fight costs the host remain. What can move off the main thread moved: the batch's decoding, merging and the decision of what to send are the
agent's; every engine call stays on its thread (the audit).

**B.3 the host as a listener.** Not reproduced in this work order (WO-213 saw 7 of 37 host listens across 12 talk runs begin with the HUD
in another state and the listener's keys doing nothing; the acceptance's talks are one player's). WO-213's named next step is built: the
listen's start writes which HUD clips were visible and the player's state (`WO218-LISTEN start`), and the one cause the symptom points
at — another exclusive key map taking the listener's keys — is answered by asserting the listen map again once a second while
listening (`listen_reassert`, on; off: once at the start). Not proven: no failing listen was staged.

**B.4 the soak.** Inside the acceptance, 60.7 minutes from the soak's start: the partner walking a 25 m circle through the village (six walks), one talk and trade round for each role, two fights, a loot (the bench's staging failed: the body had been looted empty in the loot scene), a sleep vote. **No game lost, no crash report, no save fault**, no new engine fault on either machine (the host's 32 were one read site at the join, guarded and counted, before the soak). The host's private bytes 18.6 → 19.1 GB over the hour (19.13 at most), the joiner's 15.9 → 16.2 GB (16.31 at most): flat. Frame rates by minute: the host 26–67 fps (the first minutes walking with the partner), the joiner 31–56 fps (the village against the tavern). The joiner's queue: 327–724 ms at the 99th percentile a minute (the one-lane activity lane of the acceptance's build), 2.6 s at worst. The curves are in the work order's notes. The installer's own frame-rate soak (copy A alone on the throwaway save, a crowd of three around the player from the start and a staged fight beside them from minute 3, then the same without the mod): **passed** on the release build, 5 minutes each — the mod's first and last two minutes 145.9 / 135.6 fps against 146.7 / 139.0 without it, the stat stack 0 in every row, no FAULT line, 29 rows each. Its first two runs of 10 minutes each ended with the bench's Henry dead about six minutes in, with and without the mod alike: a staged pair's attacker turned on him when its victim fell between two of the soak's looks, and a villager who found a fighter's body took him for the killer (the game's own log: the villager's skirmish attack on the player). The soak's guard now looks once a second while a pair fights (a victim near the ground ends the pair, the player hurt ends the fight), and the gate ran 5 minutes, before the village's reaction.

## C — the bodies' open items

| item | what was done | the bench | verdict |
|---|---|---|---|
| C.1 seated guests, one writer | the flips above traced: an activity owning the copy's position applied while its host NPC was 36 m off; WO-209's drift handed the body back to the stream but left the copy's own posture placing it. `drift_release` (on): the drift also releases the copy's own posture the game's way (again every 3 s while it drifts). | the acceptance: 88 flip windows of 3,078 (10 s of one copy each), 74 of them one woodcutter; the tavern 4 of 548, the brawl 0 of 95; 150 releases (the twin: 323 of 1,933, the tavern 47 of 433). A stronger fix (C.1 below) did not lower them on its check (66 of 613) and was taken out | not met |
| C.2 work bound to an object | not built (the smart object's use for a suspended brain needs research this work did not reach) | the smith's framed pair (b3, by day): the host's smith hammers a glowing blade; the joiner's copy works at the anvil in a working pose, without the blade | not met (better than WO-217's idle stand; the object's prop missing) |
| C.3 a placed activity taken over | `takeover_stop` (on): while the host's line holds an activity the copy showed, the copy's own idle movement, variation, callback or movement transition is stopped the game's way; the same fragment stopped twice in 10 s (the activity's own variation, re-queued at once) is left alone for 60 s | the smith's house (b3, straight after the join): shown within 1 s day **22/22**, midday **21/21**, night 9/14; placed-then-lost 7 / 6 / 0. The acceptance (after 20 minutes of scenes): day **75 %** of 76, midday **56 %** of 32, night **100 %** of 91 (WO-217's acceptance 67 / 72 / 20 %); 157 stops, 61 fragments left alone after two | better, not met (bar: none lost) |
| C.4 looks, C.5 paired attacks | not built | the ambush's attacks: 9 of 9 host rows played on the joiner (natively); paired moves not staged | not met |
| C.6 recoils and clinches with blocks | not built (the scripted partner's block was not reached) | — | cut |
| C.7 a copy bound without physics | not built (the owner has no physics condition; the cause sits before it — not reached) | — | cut |
| C.8 the late get-up | shown: at WO-217's 1.1 s get-up the joiner's queue was clean (0–15 ms) and the host's states for that body reached the joiner every 200 ms at the median, up to a second apart — the host's state poll went through its saturated pipe (0.85–0.92 requests a frame) on a link with 300–900 ms stalls. Not the joiner's queue; the host's pipe. | with the batch: get-ups 68 and 85 ms after the host's (the twin 366 and 629 ms). The acceptance, six knockdowns: the copy fell **106–245 ms** after its host NPC and got up **101–180 ms** after it; none got up first, none fell twice | proven and met (bar 300 ms) |
| C.9 running brains left | not built | — | cut |
| C.10 `mp_route` | stays off; nothing of the route touched | — | as asked |

## D — the world's open items

**D.1 doors (`door_sweep`).** Both of WO-217's paths, closed in the mod: (1) the joiner's world never locks or unlocks a door by itself in
a session — its own lock is refused and its own unlock undone at once, outside the player's own use, a lockpick success and the host's
apply; (2) the host sweeps the doors within 40 m of its player and each partner's figure once a second and sends any whose state differs
from what it last sent (a door a villager opened by a path that never passed the door's animation call is sent within a second), and the
joiner keeps the host's last word for every door and puts its own door back when it differs (never during the player's own use or within
5 s of it). On the bench (b3, by the tavern): 10 of 10 uses the same on both after; over the watch no door differed (0 mismatched
samples; WO-217's acceptance: one door locked on the joiner's side in every sample); the 40 differences present when the scene began were
all put right by the first sweep; the joiner refused its own lock three times and put one door back. The acceptance (b5): 10 of 10 uses the same on both after (5 by each player), none differing at the start; over the 5-minute watch 3 of 15 samples had one door differing — doors 52–53 m from the players, past the 40 m sweep. The release build sweeps 60 m. Its check: 9 of 10 uses the same after (the tenth, a door the joiner shut, sampled while the host's villagers had it open again); the 40 doors differing when the scene began were put right by the first sweep; over the watch one door differed in every sample — 137 m from both players (the bench watches the doors around the scene's spot while the players stand in the village's middle), outside the 60 m sweep by design.

**D.2 loose items.** Not staged (cut for time); named in Known Issues and taken off the README's claims.

**D.3 cart horses.** Not built. The acceptance's caravan: the copy of the cart within 0.57 m of the host's at the 95th percentile (751 samples of the mod's own measure; 0.75 m at most), its riders seated in 922 of 923; the framed pair shows the caravan far down the road on both screens — too far to tell the horses.

**D.4 long names.** Not shown (cut for time); Known Issues keeps it.

**D.5 the trade.** In the A test's runs the joiner's trade screen opened every time (WO-217's acceptance miss did not come back). The sale
stalled on the game's own quantity dialog ("1 / 3 — E Confirm", a stack of three onions in the frame) that the bench's keys never
confirmed — a staging fault, fixed in the scene (one more E). In the acceptance the joiner's trade screen opened in 1 of 2 rounds: in the second the game ended the conversation about 3 s after it began ("waiting for twins"), right after the innkeeper's copy's own bar job failed — named in Known Issues. The host's opened in 1 of 2 (the first: the bench's use key missed). No sale went through in four tries: the frame shows the seller's sell list empty (the bench's Henry had nothing in that list) — staging, not shown.

## E — the proofs

**E.1 / E.2 a road encounter on demand.** Not built (cut for time): the encounter manager's trigger was not reached; the acceptance's
corpse check stands in: the host's dead villager lay dead on the joiner's screen 12 s later, 0.31 m from the host's (not at once).

**E.3 stolen items.** The bench's staging failed in WO-217 because the owner write took nothing; the game's documentation spells the
binding two ways (`SetItemOwner`, `SerItemOwner`) and takes WUIDs — the staging now tries both and reads each item's owner back: **neither is bound in this game build** ("0 no-bind" in the acceptance). The mod sends the owner and the stolen mark with a looted or dropped item, but no script can set them on the other side; the README's and the release notes' claim was taken out and Known Issues says so.

**E.4 the loot screen's refresh, timed in the game.** The host's mod emits a body's new list the moment its poll sees the change; the
joiner's mod emits the moment the items the host's body no longer holds leave the copy's body (the row of its open screen goes with the
move); both agents stamp them on one clock (`WO218-LOOT host-list` / `joiner-out`) — never a console round trip. The acceptance: **124, 206, 127 ms** in the loot scene and 96 ms in the soak (bar 300 ms: met). The loot ledger: 8 items, each removed from the body once, 5 to the host and 3 to the joiner, each of the two contested takes to exactly one player, 4 refusals, no duplicate, no loss.

**E.5 the ghost fight in the field's shape.** No change built. The acceptance's far-ghost scene: the joiner was in the fight 2.3 s after the bandit took on the partner's figure; the far bandit, 48 m from the host and 41 m from the joiner, was kept out of it, and the joiner stayed in combat for the scene's window (no end seen).

### C.1: what the flips are, and a stronger fix taken back out

The acceptance's flip windows (10 s of one copy with a pull over 5 m and moves in over 40 % of its frames: 88 of 3,078) were mostly one
woodcutter: WO-209's drift ended its activity's hold and `drift_release` released the copy's posture; 25 s later the owner placed the
host's chopping on the copy again — at the joiner's own chopping block, 8.5 m from the host's NPC — while the writer streamed it at the
host's NPC, for 15 minutes. A build that held the drift block (at least 60 s, doubled per repeat), caught two writers in the writer
itself and kept the owner from re-installing a position-owning activity on a blocked copy was put to a flip check (the acceptance's
second morning, then a 10-minute village walk): 66 flip windows of 613, its catch never fired, and the windows read closer were mostly a
few centimetres every other frame plus one far jump a window (one body 33–115 m once a window) — not the two alternating spots the change
aimed at; during one copy's block the game's own requests (an aligned one-shot, the watchdog's reseat) placed it all the same. With no
gain shown and a visible change to every drifted copy for a minute or more, it was taken back out; the release build keeps the
acceptance's `drift_release` (the posture released at the drift, again every 3 s while it drifts). The flips stay in Known Issues. The
next lead: the writers that still place a drifted copy (aligned one-shots, the reseat, the seat) and the far single jumps.

## The acceptance

Two runs of one plan on the field's link shape: every default as shipped (the release candidate's acceptance build, before the per-body
lanes and the 60 m sweep) and every WO-218 switch off (the twin). Framed pairs are bench windows only.

| scene | as shipped | the twin (every WO-218 switch off) |
|---|---|---|
| the caravan (horses) | the cart's copy within 0.57 m (p95), riders seated 922 / 923; frames: the caravan far on both screens | the cart's copy within 0.63 m (p95), riders seated 496 / 496 |
| the corpses | the host's dead lies dead in place on the joiner's screen within 12 s (0.31 m) | the same: dead in place within 12 s |
| the talks (both roles) and the sale | talks 4 / 4; trade screens 2 / 4 (one ended by the game, "waiting for twins"; one bench miss); no sale (the seller's list empty) | talks 2 / 2, the forced talks 2 / 2 (one cut short by the game); trade screens 1 / 2; no sale |
| the smith's house | shown within 1 s: 75 / 56 / 100 % (day / midday / night); frame: the joiner's smith at the forge, not at the anvil, no blade | shown within 1 s: 60 / 54 / 96 % |
| the packed tavern, 10 minutes | the frame processor 0 / 88 / 4,616 ms; the lane 155 / 910 / 1,269 ms; 113 pull windows over 50 cm and 4 flip windows of 548; frame void (the guards were after the bench's Henry) | the frame processor **31 / 53 / 54 s** behind; the lane 105 / 224 / 229 s; 304 pull windows over 50 cm and 47 flip windows of 433 |
| the brawl, its knockdowns | the processor 0 / 130 / 163 ms; the lane 265 / 1,966 / 2,073 ms; falls 106–245 ms, get-ups 101–180 ms after the host's | the processor 155 ms / 7.3 s / 8.0 s; the lane 290 / 316 / 318 s; falls **4,672** and 485 ms, get-ups **2,480** and 412 ms after the host's |
| the ambush, the far guard | 9 / 9 host attacks played on the joiner; the far guard kept out | 18 / 18 host attacks played |
| the far ghost | the joiner in the fight after 2.3 s; the far bandit kept out | the joiner in the fight after 2.3 s, out of it again 2.3 s later (the bandit 39 m off) |
| the loot, a stolen item, the refresh | 8 items, no duplicate, no loss, each contested take to one player only; refresh 124 / 206 / 127 ms; stolen: not settable (no bind) | the same ledger: 8 items, no duplicate, no loss |
| the drops, before and after | 6 / 6 picked up by the other, none doubled or lost | 6 / 6 |
| the doors, the loose items | doors 10 / 10 the same after; 3 of 15 watch samples one far door off (52–53 m); loose items not shown (two of four not in the picker's world) | doors 8 / 10 the same after; 40 differing at the start and a door off in every watch sample (no sweep) |
| the soak | 60.7 minutes: no crash, no save fault, memory flat | (not run in the twin) |

The twin's first run (on the acceptance's build) lost its host game four minutes in, in the game's own web client (below); the twin was run again on the release build with every WO-218 switch off, start to end, without a crash. Over the whole twin, 1,349 of 1,933 copy windows (70 %) held a pull over 50 cm and 323 a flip; as shipped 482 of 3,078 (16 %) and 88 — a copy minutes behind its host NPC is pulled every time its stale state lands.

## The bench's crashes in this work order

- **The twin's host, 237 s in** (every WO-218 switch off, the caravan scene): an access violation on one of the game's own worker
  threads, in the game's web client (its statically linked HTTP library; the function's neighbours are the library's transfer and
  future/promise code) — a virtual call through an object already freed. No frame of the mod's on the stack, no address of the mod's
  near it; the mod's only hook in that module family is its own crash capture. The game's online services' own fault as far as the
  report shows; first seen on the bench; not reproduced.
- **The crash gate on the release build** (six 5-minute fights in one session): none.

## What was cut

The work order's order is its priority: the pipe, then stability, then the bodies, then the world, then the proofs, then the release.
Cut, last first:

- **E.1 / E.2** a road encounter on demand — not built. Found on the way: the player module's own encounter controls (cooldowns on/off,
  the global cooldowns, the place and variant overrides, the debug switch); WO-217 tried the place override alone. The next work order's
  first staging.
- **E.3** stolen items — the owner cannot be set from a script in this game build (both spellings of the binding are absent).
- **E.5** the ghost fight's kept half — the scene ran (above); no change built.
- **D.2** loose items, **D.3** cart horses, **D.4** the long-named horses — not built or shown.
- **C.2** object-bound work, **C.4** looks, **C.5** paired attacks, **C.6** blocks for the scripted partner, **C.7** copies without
  physics, **C.9** running brains — not built.
- **B.3** the host as a listener — the instrument and the key re-assert only; no failing listen staged.
- **B.2** the main thread — the digest's budget and the file probes' pace; the movement probe and the owner's work stay on the main
  thread; the bar not met.
- **B.1** the fight crash — no dump to read; the crash gate ran instead of a fix.
- **A.0's** joiner-alone estimate — the host's frame cap did not take on the bench; the minimised host's run stands in.

## The safety gates and the release build

**The release build.** `KingdomComeTogether-Setup-0.65.0-4a99ac0-2611110652.exe` (107,950,244 bytes; SHA-256 `96100c5a8a120a7a12fada1d7567a79e5ab9c772c9f5af024561aacb72059359`), build id `4a99ac0-2611110652`, built by `tools/Build-Installer.ps1 -Version 0.65.0` from a clean clone of the commit as a **release build** — the soak demanded, not waived — and kept local (not published, not pushed, not tagged). Its gates, every one hard:

| gate | result |
|---|---|
| no input capture (`tools/Check-NoInputCapture.ps1`), with its self-test | PASS — in the installer build, and first and last over every binary and script built in this work order (the dev builds, the rig, the harness, its tools) |
| the payload (no input tooling, WO-175) | PASS — 1,036 files, no hit |
| no screenshot in what ships (WO-192) | PASS — 1,036 files, no hit |
| publishing (`tools/Check-Publishing.ps1`) | PASS — the 8 local commits over the remote's main, nothing on the denylist |
| the save round trip, live (WO-175) | PASS — on the bench's copy with the release build |
| the frame-rate soak (`tools/perf/soak.py`) | PASS (B.4) |
| the setup cases (WO-150) | 41 / 41 |
| the relay's smoke and fault-mode suites | PASS |
| the real saves | unchanged — their hash list matched before and after every bench run of this work order |

The offline gates over the release tree (`7ef4bc4`'s code): native unit tests 10,462 passed; the agent's 2,093, the setup's 77, the log bundle's 98; every Lua synthetic suite (they load `kdcmp.lua`, the parse check); the static gates; the digest analyzer's 62 and the rig's 42 tests; the relay message table. The relay's round-trip and fault-mode suites failed 3 timing tests while the bench's two games ran beside them and passed 230 / 230 and 37 / 37 on a quiet machine (the relay and its tests are unchanged since 0.64.4; the shared protocol only gained this work order's switch names).

### The thread and lock audit

Standing rules: an AI call runs on the AI update's thread through the entry hook; no engine call that may take an engine lock runs under
our fault guard. Every engine call or write this work order adds, with its thread, its guard and what it may lock:

| # | where | the call or write | thread | under our fault guard? | may it take an engine lock? | verdict |
|---|---|---|---|---|---|---|
| 1 | pipe_server.cpp `w218_drain` (A.1) | the generic families' `woNNN::handle()` (0x21..0x2B) -- the exact handlers the single path runs on the main thread through `run_sync_bounded` | main (a posted task, as the single path's) | yes: `g_siteBatchSub`, as the single path's `g_sitePipeTask` | as before: every one of them already ran on the main thread under the single path's guard (their own audits) | ok, same calls, same thread, same kind of guard |
| 2 | pipe_server.cpp `w218_run_sub` (A.1) | `wo203::handle_off_main` for a 0x2B sub (the conversation camera's samples) -- no engine call, its own leaf lock | main (it ran on the pipe thread before) | no (as before) | no: its own leaf mutex, no engine call while held | ok |
| 3 | pipe_server.cpp batch reply (A.1) | `send_big` -> `WriteFile` on our pipe under `g_write_lock` (our CRITICAL_SECTION, the lock every unsolicited frame from the main thread takes) | main | no | no engine lock; our own leaf lock (no engine call while held) | ok |
| 4 | pipe_server.cpp `w218_on_batch` / `w218_on_disconnect` (A.1) | our own deque under our own mutex; `main_thread::post` | the pipe thread | no | no | ok |
| 5 | wo208.cpp `own_apply` C.3 takeover_stop | `rttr::stop_action` (`IAction::Stop`) on the copy's own RUNNING variation / callback / idle movement / movement transition on its full body -- the stop the owner already makes on its own actions (`own_retire`) and WO-217's unclog makes on the copy's own pending ones | main (the owner) | as the owner's own stop | as `own_retire`'s (WO-208 A.2's audit) | ok, same call |
| 6 | npc_drive.cpp WO-209 drift C.1 drift_release | `wo208::ask_release`: pushes `{1, name}` on the owner's ask queue (no engine call); the agent's next poll turns it into the owner's existing release (WO-217 0.2's seat rule uses the same) | main (the writer's tick) | no | no | ok |
| 7 | wo206.cpp `digest_tick` B.2 main_budget | `QueryPerformanceCounter`; fewer `wo200::read_value` calls a frame (each under its existing guard) | main | as before | as before (WO-206 C's audit) | ok, fewer of the same call |
| 8 | dllmain.cpp `w218_slow_watch` B.2 main_budget | `GetTickCount64`; the same research file probes, every 2 s instead of every frame | main | as before | as before | ok |
| 9 | wo203.cpp WO218-COST B.2 | `QueryPerformanceCounter` around each registered area, only with the cost meter on | main | no | no | ok |
| 10 | frame_meter.h B.2 p95 | arithmetic on the frame meter's own window | main | no | no | ok |
| 11 | kdcmp.lua D.1 door_sweep | `System.GetEntitiesInSphereByClass(…, "AnimDoor")`, `System.GetEntityByName`; WO-151's own `setDoor` (`door:Unlock`, `DoPlayAnimation`, `door:Lock`) and `KCD2MP_W151DoorChanged`; the wraps return without calling the original Lock | main (Lua; the agent's 1 Hz `KCD2MP_W131Tick`) | no (Lua) | Lua binds | ok |
| 12 | kdcmp.lua B.3 listen | `UIAction.IsVisible`, `player.human:IsInDialog`, `player.soul:IsInCombatDanger` (reads), `ActionMapManager.EnableActionMap` (the call the listen already makes at its start) once a second while listening | main (Lua timer) | no (Lua) | Lua binds | ok |
| 13 | kdcmp.lua E.4 | `KCD2MP_EmitEvent` (our own log line) | main (Lua) | no | no | ok |
| 15 | npc_drive.cpp C.1 drift_release (b7): the two-writer catch and the lasting block | arithmetic on the position the writer already reads before its write (`read_pos`, as WO-209's drift); `set_activity_hold(false)` (our own set); `wo208::ask_release` (our own queue, as row 6) | main (the writer's tick) | no new call under a guard | no: no engine call added | ok |
| 16 | wo208.cpp `own_apply` C.1 drift gate (b7) | none: the owner skips its full-body install of a position-owning activity on a drift-blocked copy (the same `continue` as the one-shot hold) | main (the owner) | -- | no | ok, one call fewer |
| 14 | agent (A.1, A.2) | the batch, the posts, the merges, the activity lane's concurrency, the shed: no engine call (the agent's own process) | agent | -- | -- | no engine call |

The batch drain runsthe same handlers as before, on the same thread, under the same kind of guard; what changes is that several run in one main-thread turn (at most 2 ms of a frame, at least one a frame). A sub that faults is answered with the fault reason and the next sub runs; none can take an engine lock our guard could then leave held that the single path could not.
