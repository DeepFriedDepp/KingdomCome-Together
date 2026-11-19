# WO-222 — Hits that land like single-player: flinches both ways, fair defence, enemies that stay hittable

Roles only: "the host", "the joiner", "a tester", "the maintainer". Footing marks: [code] read in this repository, [binary] read in the
game's own files, [bench] measured on the bench (two copies of the game on the maintainer's PC, joined through our relay with the
field's link shape — 80 ms ± 20 round trip, 0.5 % loss, two stalls of 0.3–0.9 s a minute), [reasoned] an inference, said as one.
"The twin" is the same build with every WO-222 switch off on both machines but the swing log (an instrument, never part of a twin).
"Shipped" is the final build (`f4`), and where said the builds before it: `f2` (Parts A, B, C, E and F as in `f4`; Part D without the
two flee fixes of §5 (a) and (b), which `f3` and `f4` added) and `f1` (Part A, D and E code as `f2` but WO-131's 3 m gate). Run names
are the bench's own; their outputs, frames, crash records and ledgers are in the WO's notes.

**The field bundles.** The two testers' bundles were not on the bench PC when this work order ran. Everything below is measured on
the bench, with instruments built to show what the bundles could not; the work order's counts from the bundles are quoted where they
are compared.

**In short.** The joiner's blows now get the reaction the host's own blows get, in every situation measured (the twin: none at all
on a bandit fighting the joiner at low stamina, 0 of 34). Its cause: an NPC fighting the joiner's figure is out of combat mode on the
host, and the engine refuses a reaction to a body out of combat mode. The host's reactions now show on the joiner's copies (29 of 29
at 150 ms on `f2`; 0.65.2's way lost one in seven to the copy's own swing). A fleeing enemy stays the joiner's enemy, runs
on its own legs (step 1.25 s against the host's 1.20 s) and takes his blows ("unkillable" was the mod dropping his touches on a runner
as "too far from the host's NPC"). Every swing of either player is accounted for in the bug report. The joiner's defence (Part C) is
not at its bar: the host still waits in vain on one blow in ten or more, because on the joiner's screen the attacking copy often
fights the host's figure instead of him (a known issue this work order measured but did not fix). Every switch ships on.

## 0. The reference (A.0) and the four bars

**A.0 — the host's own blows (the reference) and the joiner's, by the body's situation when the blow landed.** A clean hit (the
engine's verdict "hit"; blocked and parried blows draw no hit reaction for either player and are counted apart). "Reacted": the
engine's hit reaction entered on the body within 0.8 s, or the body stood in its hit reaction right after the blow. The host's own
blows are pooled over every run (WO-222 changes nothing on their path); the joiner's split by build. [bench]

| the body's situation | the host's own blows: clean hits reacted | the joiner's, shipped | the joiner's, twin |
|---|---:|---:|---:|
| in guard (fighting, its own last action not an attack) | 31 of 33 (94 %) | 18 of 18 (100 %) | 10 of 21 (48 %) |
| at low stamina (under a quarter) | 19 of 19 (100 %) | 4 of 4 (100 %) | **0 of 34 (0 %)** |
| mid-attack (its own attack started in the last 1.3 s) | 24 of 24 (100 %) | 21 of 21 (100 %) | 6 of 6 (100 %) |
| out of combat mode, its opponent the host's player | 0 of 12 (0 %) | 0 of 2 (0 %) | – |
| blocked or parried (all blows) | 191 blows, no hit reaction | – | – |

At the start (the first baseline run, every WO-222 switch off) the host's own clean hits reacted 5 of 9 in guard and 0 of 3 out of
combat; `f2`'s runs give the table above. A stricter reading — a *new* hit reaction started by the blow — gives the host
25 of 33 in guard (the other 8 landed on a body already in its reaction) against the joiner's 18 of 18. The joiner's sample is 43
clean hits on the final code; the bench's joiner lands far fewer blows than the host (§6).

| bar | shipped | twin |
|---|---|---|
| **A:** per situation the joiner's blows react at the host's rate within 10 points; `no-reaction-found` 0 | **met:** within 6 points in every situation (strict reading: guard 100 against 76); `no-reaction-found` 0 of 45 (once in 191 over every run, an earlier build) | 48 and 0 % against 94 and 100 % |
| **B:** ≥ 95 % of the host's reactions on copies within 40 m played on the copy within 150 ms of arrival | **met:** 29 of 29 (100 %) on `f2`; 50 of 50 sent, 46 of 50 read as shown on `f1` (the 4 others were torso hits on the upper body, which `f1`'s check did not read) | 24 of 28 (86 %); 6 of 10 at the start |
| **C:** fallback verdicts ≤ 1 % of NPC blows on the joiner; 0 contradicting fallbacks left uncorrected | **missed:** 10.8 % (`f1`) and 38 % (`f2`) of the host's waits ran out; in the defence scene 2 of 48 (4.2 %); contradicting guesses left uncorrected: 0 | 15.9 %; defence scene 3 of 43 (7.0 %) |
| **D:** the chase — every touching swing reaches the host and is applied; the copy runs (step within 10 %); it dies on both screens | **missed in part:** on the final build every touch of the joiner's on a runner reached the host and was applied (3 of 3 sent; the fourth the same swing again, which is never sent); the copy runs as long as its host NPC does (sprint: step ratio 1.04; jog: at speed for as many samples, its step period not measurable by our tool); the runner was knocked down but did not die (the bench joiner's blows do 4.7 health) (§5) | the runner released: 41 swings passed through it ("not hostile here"), 4 touched |

## 1. The instrument (built first)

Nothing in 0.65.2's logs said what the host's own blows do, how often the joiner's blade meets a body, or whether a host reaction
shows on a copy. Built first, read-only, on in every run (the twin keeps it):

- **The blow ledger** (`native/KCDMP/blow_ledger.cpp`). On the host, every melee blow on a body — the host player's own (the
  engine's hit processor) and a joiner's (applied by WO-173's path) — is one `W222-BLOW` line: who struck, the body's situation at
  that moment (its own last actions from the action capture hook: an attack under way, a block or parry, a hit reaction; its
  stamina; its combat mode and its opponent), the engine's verdict, and what the body did next (the engine's own hit reaction
  entering on it within 0.8 s, another of its own actions, or nothing). [code]
- **The swing log** (same file, `mp_swing_log`). On both machines every attack the local player makes — the game's own attack
  action entering, never input — is one `W222-SWING` line: what its blade touched (the hit processor, or WO-210's gate for a copy),
  or what stood in front of it and why it was not touched: out of reach, not hostile here, held, dead, in reach and in this fight but
  nothing touched, cut short by the player's own hit reaction, nothing in front. For each touch the joiner's agent says whether it
  went to the host and if not why (`W222-SEND`). The bug report's briefing has one line for it (made / touched / sent / passed
  through by reason). [code]
- **The reaction rows** (`W222-REACT`, the joiner's agent): every host reaction row that arrives for a copy — its dispatch, and 150
  ms after arrival whether the copy plays it on any scope (op 202: the row's own fragment against the body's scopes). The host's
  agent says each reaction it captured and did not send (`W222-REACTOUT`: none in any run). [code]

## 2. Part A — the joiner's blows get the host's native reaction (`mp_react_parity`)

**A.1 — the causes, measured.**

1. **The body is out of combat mode because its fight is with the figure.** On the host an NPC fighting the joiner's figure reads
   out of combat mode — combat mode belongs to the local player's fight (WO-147) — and the engine's own can-react test refuses a
   reaction to a body out of combat mode. The host's own blow on the same NPC finds it in combat mode, because its fight is with the
   host's player. The twin: 46 of the joiner's 62 blows "victim cannot react", 44 of them on bodies out of combat mode whose opponent was not the
   host's player (in these scenes: the figure);
   the host's own clean hits on bodies in combat mode: 74 of 76 reacted. This is the field's "25 of 75 cannot react". [bench]
2. **The hold.** 0.65.2 held a joiner's blow up to 0.8 s for the figure's strike window before applying it (the field: 44 holds); on
   the bench one held 830 ms, past the start of the body's own next attack. [bench] [code]
3. **`no-reaction-found`** (the field: 7 of 75) happened once on the bench in 191 joiner blows over every run, on an earlier build:
   reaction kind 2 — the kind every queued reaction of the joiner's had — on a body in guard, the engine's CreateHitAction giving no
   action that once. On the final code: 0 of 45. With one case and the same kind as the queued ones, the attacker-side fields of the
   reaction table were not changed. [bench]

**A.2 — what was built** (`native/KCDMP/wo173.cpp`, both under `mp_react_parity`, the host only):

- **No hold.** The joiner's blow is applied the moment it arrives; when the figure's own strike window is not open, the figure
  reads Striking for the processor's call alone (WO-210 C.4's way), as before after a hold.
- **The fight it is in decides, not the combat-mode bit.** When the engine says the body cannot react and the body's opponent is the
  very figure that struck it, the reaction is created and handed to the body's own action director the engine's way — the reaction
  the host's own blow on a body fighting him gets. A body that fights no one, or fights the host's player while out of combat mode,
  is left as the engine says (the host's own blows on such bodies get nothing either: 0 of 12). [code] [bench]

## 3. Part B — the host's reactions show on the joiner's screen (`mp_react_show`)

**B.1 — measured.** Every reaction the host's engine played on an NPC with a copy near the joiner was sent and arrived (50 of 50 on
`f1`, 29 of 29 on `f2`, 28 of 28 in the twin; no `W222-REACTOUT`), all dispatched within 0–31 ms of arrival. What failed was the
playing: the row was queued at the priority of a swing (5), the same as the copy's own swing, so it waited behind it; 150 ms after
arrival the copy still played its swing (the twin: 4 of 28; at the start 4 of 10). The briefing's "recoils 1" in the field counts
WO-197's failed-attack recoils (a blocked stab), not hit reactions. [bench] [code]

**B.2 — what was built.** The host's reaction row plays on its copy at the priority the engine's own hit reactions run at (7) —
above any swing — so it interrupts the copy's swing as it interrupted the host NPC's. The check reads every scope of the body (a
torso hit plays on the upper body). Final build: 29 of 29 shown at 150 ms. [code] [bench]

**Frames.** The three framed pairs (a recoil, a stagger, a blocked-and-pushed, both screens) were not obtained: the duel's frames
were taken right after a host reaction, each viewer turned to the bandit, but the camera does not follow a scripted turn and the bandit
was not in frame on either screen (16 pairs looked at). The placed framing (both viewers put 3–8 m from the subject) was used for the
chase instead (§5).

## 4. Part C — a blow on the joiner is judged on the joiner's screen (`mp_def_wait`)

**What happens.** When a host NPC's blow lands on the joiner's figure, the host waits for the joiner's own engine's verdict
(hit, blocked, parried, missed, or "none": his strike window closed with nothing landed). 0.65.2 waited 450 ms (600–650 in a combo)
and then sent its own verdict; the joiner's game skips that verdict when his engine judged the same attacker a moment before, and
otherwise lets his own engine judge it (WO-165's victim-decides replay) or, when that measure fails, applies the host's numbers.

**Measured.** [bench]

| | the twin (0.65.2's wait) | `f1` | `f2` (as shipped) |
|---|---:|---:|---:|
| NPC blows on the figure, whole session | 536 | 509 | 479 |
| the host's wait ran out | 85 (15.9 %) | 55 (10.8 %) | 182 (38 %) |
| …in the defence scene (two bandits on the joiner) | 3 of 43 (7.0 %) | 7 of 42 | 2 of 48 (4.2 %) |
| his verdicts' delay after the host's blow, p50 / p95 / max | 63 / 406 / 625 ms | 156 / 953 / 1968 ms | 156 / 860 / 1218 ms |
| the host's numbers applied on the joiner without his engine | 4 | 12 | 1 |
| a guess applied there that his engine then contradicted, not given back | 0 | 0 | 0 |

**Why the waits run out.** Two kinds. (1) His verdict comes, but late: in 0.65.2's wait one in five answers left his machine just
before the host gave up (one 44 ms before) and arrived just after; the final wait catches these. (2) His engine never judges the blow
at all: on his screen the attacking copy's swing plays, but the copy is not in his own fight there — the log says it fights the
host's figure (`WO187-OPP … pinned to kcd2mp_0`, `WO208-TARGET … not in a skirmish fighting`) — so his engine processes no blow on
him and no answer ever comes. Of `f2`'s 11 run-out waits in one defence stretch only one answer ever came (2.95 s after
the blow). This is the known "copies retarget to the host's figure" (WO-210 E.1), outside this work order's fixes. [bench]

**What was built.**
- The wait follows how long his verdicts take: the slowest of his last forty answers — answers that came after a wait ran out
  included — plus 250 ms, between 0.8 and 1.5 s (`Wo222Rules.DefHoldMs`). A verdict that comes after the host's guess went is
  counted (`W222-DEF late`) and joins the sample.
- On the joiner, a guess applied there is given back — the health and stamina it took, through the soul's own state — when his
  engine judges the same attacker's blow within 3 s (`W222-DEF corrected`). No run needed it: the joiner's game already skips a late
  host verdict once his engine has judged, and in every run 0 guesses were left that his engine contradicted.

**Three approaches, each measured.** (1) The 95th percentile of his answers plus 120 ms, and a wait kept open while his link was
quiet: 13 % run out (from 29 %), but the quiet-link rule fired whenever the joiner stood still (his position stream is quiet then)
and was dropped. (2) The slowest answer plus 250 ms, late answers counted: defence scene 4.2–4.9 %. (3) A 2 s cap so no answer is
cut off: it changed nothing for blows his engine never sees and kept the host waiting 2 s for them; the cap went back to 1.5 s.
The bar (1 %) is missed by the blows his copies never swing at him; see Known Issues.

## 5. Part D — a fleeing enemy stays a live, hittable enemy (`mp_flee_live`)

**What happened in 0.65.2** (read in our code): the host's NPC that flees leaves combat mode and drops its opponent; the joiner's
copy is then released from his fight, its fight ended (WO-201: the engine's StopFight), held as a plain bound body. Its legs then
belong to the walk driver only when nothing holds it — its fight stance was still held — so it slid. And a touch of the joiner's blade
on it was dropped before it left his machine: WO-131's gate counts a hit only on a copy within 3 m of its host NPC's newest streamed
place, and a running copy trails its host NPC by more (the bench: 3.13 and 3.69 m, both dropped). That is the "unkillable" runner:
his swings met it on his screen and none reached the host. [code] [bench]

Two more causes showed only in shipped chases: the runner was never held as fleeing, (a) because the host read the flight only on
bodies it had seen fighting — and a body whose fight is with the joiner's figure reads "no combat, no opponent" on the host (WO-147),
so the very enemies of the joiner were the ones whose flight went unread — and (b) because the host stops watching an NPC's combat
state when its weapon is put away, and a runner puts its weapon away: the host dropped it from the watch the moment it fled, sent
nothing more, and three seconds later the joiner's copy went stale and was released. [bench] [code]

**What was built.**
- The host reads the game's own flight and surrender on every body that fought in the last two minutes — anyone the host reads, or
  a joiner's figure (the hit hook notes every NPC that strikes a figure; a villager fleeing a crime it never fought in is not
  counted) — at 2 Hz, and keeps watching a body for two minutes after its fight while it lives: the soul contexts its flee
  interrupt puts on it (`combat_flee`, `crime_interruptFlee`) and its surrender's (`combat_surrender`, `crime_interruptMercy`)
  [binary: interrupt_flee, interrupt_mercy]. A flight is one flight while the contexts come and go within 20 s (the bench: 7 s
  without them in the middle of one run). Its combat word says "fleeing" (a free bit of the NpcCombat event).
- The joiner keeps that copy in his fight — his skirmish, its local hits on him discarded — without the combat hold, up to 80 m; its
  fight holds are dropped once so the walk driver runs it as its host NPC runs; the fight's end (WO-201) does not come for a flight.
- A touch of his blade on a copy held as a fleeing enemy goes to the host up to 8 m from its host NPC's place; the host's engine
  judges it there.

**Measured.** [bench] The game decides a flight when a low-health enemy is struck (handleStimulusHit); staging one took from 3 s to
4 minutes of fighting, and three of six chase scenes never got one. In the joiner's chase that did (`f1`, the flight 3 s in): the
host held the bandit as fleeing; on the joiner its copy stayed in his fight and ran — at over 1.5 m/s for as long as its host NPC
(316 against 319 samples at 20 Hz), the step period 1.25 s against the host's 1.20 s (ratio 1.04) — a sprint at 3.4 m/s. His two
touches on it were dropped by WO-131's gate (the fix above is in `f2`). It did not die in the scene's time: the rest of the camp was
on the joiner and he struck them instead.
**On the final build** (`f4`, two chase runs late in the morning, three flights): the host held every runner as fleeing and the
joiner kept each copy in his fight. In the joiner's chase whose swings could be sent, the bandit broke 22 s in; two minutes later
he caught it: his four touching swings on it were three sent (the fourth the same swing again, which is never sent) and all three
applied by the host's engine. None of his swings passed through it as "not hostile here" (the twin's chase: 41). The third blow
knocked it down and its fight ended there; it did not die: the bench joiner's blows do 4.7 health. The first of his blows also ended
the flight on the host — the bandit turned to fight his figure 0.2 s after the mod's retarget to the striker (WO-136), which may be
the cause or the game's own answer to the hit [bench; not traced]. These runners jogged (1.6–1.7 m/s, the camp's bandits); the
copy moved at speed for as many samples as its host NPC (878 against 782 and 849 against 860 over 0.8 m/s), but at a jog our step
tool gives no stable period for the copy (0.3 to 1.1 s by threshold; at its standard 1.5 m/s too few samples for any jog, every
build and the twin alike), so the step bar is measured on the one sprint only. [bench]
**What the bench could not do.** Of 26 chase scenes, 9 staged a flight (2 s to 4 minutes into the fight; the game's own decision)
and none ended with the runner killed by the chaser in the scene's time: the joiner's blows land rarely (§6) and do little damage
on the bench, and the rest of the camp fights on around him. Runs lost: one to the joiner's crash five minutes after a flight
(F.2), four in part or whole to the foreground (another program held it at times, and the rig does not fight for it). 

## 6. Part E — every swing of the joiner is accounted for (`mp_swing_log`)

The bug report now shows what the field's could not. `f2`'s scenes run: 168 swings of the joiner — touched a body 19, cut
short by his own hit reaction 11, in reach and in his fight but touching nothing 14, the copy held for a swing of its own 8, not
hostile here 1, and 115 with nothing in front (the reference and camp phases, where his lock-on took him off the bandit). Every touch
was sent (16 of 16 on `f2`; on `f1` 29 of 31, the 2 others the runner's, dropped by WO-131's gate as above).

Two findings the instrument gave that the work order did not foresee:
- **The joiner lands far fewer blows than the host on the bench** (about one swing in five touches a body, the host's own about one
  in two): his attacks are cut short by the camp's blows on him (a bench joiner without armour takes 20–55 health a blow), and a
  fighting copy blocks and dodges as its host NPC does. A touch-blocking mechanism of ours was looked for in a bisect (eight of the
  joiner's switches off in turn): none; the last window, everything on, touched 6 of 8.
- **The runner's touches were dropped** by the mod's own WO-131 gate (§5).

**The proof — the chase's twin (`mp_flee_live` off, everything else on).** The bandit broke and ran 21 s into the scene. Of the
joiner's swings at it after that, **41 passed through it as "not hostile here"** — on his screen the copy had been released from his
fight the moment its host NPC fled, so his blade went through a body his game no longer counted as an enemy — against 4 that touched
it; the host applied 3 joiner blows on it in all. That is the field's "unkillable" runner, now visible in the bug report line by
line. With the switch on, the chases still showed such swings: 3 at a runner the host never flagged (the figure-fight and the watch
causes of §5, fixed in the final build), and 5 in an early build's chase 15–24 s after the joiner's agent had taken the runner back as
a fleeing enemy, while the DLL read it as not in his fight — that loss was not traced (the early build also let a runner go for the
7 s its flee contexts dropped mid-flight; the final build holds a flight 20 s). On the final build's chases no swing could be sent
(§5). [bench]

## 7. Part F

- **F.1 Bodies left standing (`mp_dead_lay`).** On the joiner, a dead copy that still stands 4 s after its death handling began is
  finished the engine's way again (its ragdoll and its dead state), then — still standing — given the game's ragdoll; at most three
  tries a body. The twin left 1 of 3 dead copies standing in its scenes run; the shipped runs left none, and in the final scenes run
  the sweep found one standing and laid it down. Why 0.65.2 missed it: the death handling decides once (WO-221's single check after
  the game's own death action), and a body that read "lying" at that moment and stood again, or whose ragdoll did not take, was
  never looked at again. [bench] [code]
- **F.2 Crashes.** Three game exits in the night, noted with their dumps. The joiner's game twice in one engine function
  (`CombatModule+0x559d9d` and `+0x559d32`, an access violation reading an invalid pointer, no mod code on either stack): once with
  every WO-222 switch off, during a scene's staging; once on a WO-222 build five minutes into a chase. The same function failing with
  the switches off says it is not WO-222's; it is not WO-221's death crash either (a different site), and it is new to our records.
  The host's game once at the end of a 56-minute scenes run (an exception raised on the renderer's own thread, `CryRenderD3D12`, no
  mod code on the stack). All three in Known Issues' crash line.

## 8. Every bar missed

- **C (fallbacks ≤ 1 %):** missed: 4.2 % in the defence scene, 10.8–38 % over whole sessions. Three approaches (§4). What is left is
  the blows the joiner's copies swing on his screen without being in his fight (the copies retarget to the host's figure).
- **D:** every touch of the joiner's on a runner reached the host and was applied on the final build, and the copy ran as long as
  its host NPC ran; the runner dying on both screens was not shown (knocked down, not killed: the bench joiner's blows are weak and
  land rarely), and the step bar is measured on one sprint only (at a jog our tool cannot read the copy's period) (§5).
- **B's framed pairs:** not obtained (§3).

## 9. The scoreboard against 0.65.2

`tools/rig`'s scoreboard, unchanged, with WO-221's link plan and settings (so its numbers sit beside 0.65.2's), nothing compiled
beside the runs. The final build `f4` had four scenes runs and two checks runs; 0.65.2's own final build was run three times on the same bench the same
morning as a control, because the first two `f4` runs read lower than 0.65.2's published numbers. A fifth `f4` run is void (its
tavern scene had nobody in it, so it scored two scenes of three: place 87.6). `f2` (the same code but for Part D's two flee fixes)
had three runs in the night. [bench]

| | `f4` runs | **`f4`, mean** (spread) | **0.65.2, this morning** (spread) | `f2`, mean (spread) | 0.65.2 as published (spread) |
|---|---|---:|---:|---:|---:|
| place (within 0.5 m, %) | 76.0 · 69.1 · 82.4 · 74.9 | **75.6** (13.3) | **75.4** (2.9) | 78.6 (7.2) | 78.0 (0.5) |
| thing (same activity, %) | 80.9 · 76.1 · 80.4 · 87.5 | **81.2** (11.4) | **81.3** (8.9) | 74.4 (4.5) | 78.1 (4.0) |
| step (copy / host) | 1.00 · 1.00 · 1.00 · 1.00 | **1.00** (0) | **1.00** (0) | 1.02 (0.05) | 1.06 (0.17) |
| slide (% of speed) | 13 · 14 · 16 · 11 | **14** (5) | **22** (30) | 15 (17) | 31 (32) |
| pulls (a body-minute) | 0.24 · 0.21 · 0.14 · 0.11 | **0.18** (0.13) | **0.19** (0.10) | 0.19 (0.13) | 0.20 (0.18) |
| flips (a body-minute) | 0.039 · 0.011 · 0.000 · 0.004 | **0.014** (0.039) | **0.022** (0.007) | 0.028 (0.030) | 0.034 (0.024) |
| lane99 (ms) | 823 · 857 · 814 · 797 | **823** (60) | **869** (160) | 790 (43) | 914 (334) |
| cost95, the host (ms) | 10.5 · 9.1 · 10.2 · 8.6 | **9.6** (1.9) | **8.7** (1.1) | 8.1 (0.6) | 10.2 (0.9) |
| works (of 10) | 10 · 10 (two checks runs) | **10** (0) | — | 8 (one run) | 9.3 (1) |

**Place, pulls and flips are not worse than 0.65.2.** Against 0.65.2 under this morning's bench place is the same (75.6 against
75.4), and so are pulls (0.18 against 0.19) and flips (0.014 against 0.022). Against 0.65.2's published numbers place reads 2.4
lower, outside their spread of 0.5. But 0.65.2's own build read 75.4 this morning too, so the bench moved, not the code: the tavern
scene (the lowest every time) differs most from run to run. Nothing WO-222 added acts on a body that is not in a fight [reasoned: the
flee read and the watch keep are for bodies that fought, the reaction rows for blows, the dead-body sweep for dead copies, the
ledger reads only]. The host's cost (9.6 against 8.7 ms this morning) is no change by the rule (0.9 ms higher, inside this morning's
spread of 1.1), and lower than 0.65.2's published 10.2. The checks: both of the final build's runs passed all ten (`f2`'s one run 8: its trade check got no shop screen, a WO-221 path
WO-222 does not touch, and its knockdown check missed a copy that got up 3.2 s after its fall); 0.65.2 9.3.

## 10. The audit, the gates, save safety

**Thread and lock audit.** Every engine call added, its thread and its lock, is listed in the WO's notes. In short: the capture and
hit hooks only copy numbers under the ledger's own lock; every engine read runs on the main thread in the frame tick, through readers
the mod already uses there; the flee read is the soul context lookup the scripts themselves make (no lock, a re-entrancy flag); the
flee engage makes the existing engage's calls minus the combat hold, plus the existing fight-hold release; the reaction row is
WO-151's row chain with another priority. Nothing added makes a movement or AI request, and nothing added calls the engine under the
fault guard except reads.

**The gates** on the final tree, nothing else using the PC's build or bench: the no-input-capture gate first and last (its self-test
caught every planted capture; every binary built here, the rig, the harness and the input helper passed), the native build and its
10,473 unit checks, the pak, the relay (230), agent (2,121, WO-222's 9 among them), setup (77) and log bundle (104) tests, every
synthetic suite and static gate, the relay table and fault-mode gates, the digest, rig (42) and save tool tests: all pass.

**Save safety.** The real saves were hashed before the first run (1,136 files) and checked after every run by the rig, and once more
at the end: 0 changed, 0 gone, 0 added. Both copies ran on their own user folders and never on the real saves; the copies' saves are
left in their own folders.

**Conduct.** Once in the night a build ran while a bench run was still being taken down (its numbers collected, the games quitting):
a wait for the bench timed out and the next step went ahead. The run's data were already written; the rule was broken all the same,
and the later chains waited up to two hours for the bench. The rig never fought for the foreground: runs that found another program in front lost their key
steps and are counted as lost above.

## 11. The switches

| switch | part | machine | default |
|---|---|---|---|
| `mp_react_parity` | A | the host | on |
| `mp_react_show` | B | the joiner | on |
| `mp_def_wait` | C | the host (the give-back on the joiner) | on |
| `mp_flee_live` | D | both | on |
| `mp_swing_log` | E | both (read-only) | on |
| `mp_dead_lay` | F.1 | the joiner | on |

`mp_w222_status` lists them and the DLL's numbers. The bench's twin: `KCDMP_TEST_W222_OFF=<names|all>` in both games' and both
agents' environment ("all" leaves the swing log on).
