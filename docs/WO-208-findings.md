# WO-208 — One owner per copy, the joiner's own defence, and the release

Version **0.63.7**. Everything here was measured on the bench (two copies of the Modding Tools game on one PC, joined through our own
relay, started the launcher's way); every fix ships with its own switch and a twin (the same build, that switch off) whose test fails.
Nothing built here reads input: `tools/Check-NoInputCapture.ps1` passes on every binary and script, first and last.

## Part 0: the baseline (current main)

`main` (WO-207 with both crash fixes) built and every offline gate passed (96); the saves folder backed up byte-equal with its hash list,
unchanged after every run of the WO. On the pair: three fights, two tavern nights, two joined-talk runs with both roles starting, the
full pair smoke (the WO's run notes hold every number).

| measure (main) | fights | joined talks | smoke |
|---|---|---|---|
| copies running a different full-body family from the host's NPC | 14.1-18.1 % | 44.8 % (copies standing in a talk with an empty full body) | 21.5 % |
| queue overflows on copies | 0-2 per fight | 0 | 1,119 |
| T-poses on copies | 0-1 per fight | 3 | 10 |
| host swings shown on the copy | 85-93 % | 64 % | 85 % |
| swing start on the copy after the host's, p90 (traces) | 159-804 ms | — | 984 ms |
| the same posture on both screens (sitting, lying in bed) | 0 % | 0 % | 0 % |
| failed activity one-shots | 8-12 of 27-33 | 79 of 223 | 306 of 752 |
| blows on the joiner judged by the joiner's own engine | 0 | — | — |
| the joiner's game crashed | 1 of 3 fights (CombatModule, WO-207's signature) | — | — |

Two things the WO's measures could not see, found on the way: copies of villagers asleep in bed on the host stood beside the bed on
the joiner (the action family calls a posture "idle"; a posture measure from the game's own fragment names was added), and every
joined-talk failure of the baseline smoke came from one talk the game shows with no dialogue camera (C below).

## Part A: one animation owner per copy

**Built:** the host streams its NPCs' full and upper body as state (each scope's action class, fragment, tags, priority and status, 10
times a second, sent on change, through the pipe lane; NpcAnim 0x7A / 0x7B in the relay table); on the joiner one owner per copy
installs only the differences through the copy's own action controller, one stream delay after they arrived, withdraws its action when
the host's scope is back at its base, removes one that has not started in 300 ms with the game's own stop, never keeps two pending, and
gives an empty full body the host's base. Every old writer leaves an owned copy alone. Switch `mp_copy_anim_owner`: **off** (below).

**A.1, the probe** (one copy taken from every other writer and driven only from the host's state, 5 minutes each,
the WO's design notes):

| | the fight (a bandit) | the tavern (the innkeeper) | the village (a villager) |
|---|---|---|---|
| queue overflows / T-poses | 0 / 0 | 0 / 0 | 0 / 0 |
| different full-body family (the WO's measure) | 12.6 % | 26.7 % | 10.9 % |
| the same, by fragment | 6.8 % | 0.3 % | 5.1 % |
| postures the same | — | sitting 99 %, lying 97 % (main: 0 %) | — |

It fails A.1's "under 3 %". What the probe found, each an engine rule: a paired action (a sync attack, a clinch) installs only with its
partner's half, and that partner was the joiner's own player, whose body the mod never drives; a paused copy never queues its own base
again (its full body stood empty for seconds); an activity one-shot (the innkeeper serving) is a request to the copy's NPC-state machine,
not an action; the village NPC was bound as a copy and released again five times (the NPC sync's churn). The design was changed for
each (paired actions held, the walk's start installed, the host's base given to an empty body, pending actions stopped the game's way)
and probed again on the release candidate:

| | the fight (a bandit) | the tavern (the innkeeper) | the village |
|---|---|---|---|
| different full-body family, first probe -> again | 12.6 % -> **8.2 %** | 26.7 % -> **4.8 %** | 10.9 % -> not measured (the probe's trace never held the walker the bench picked) |
| the same by fragment | 6.8 % -> 3.1 % | 0.3 % -> 2.8 % | 5.1 % -> -- |
| queue overflows / T-poses | 0 / 0 -> 0 / **1** | 0 / 0 -> 0 / 0 | 0 / 0 -> -- |

Better, and still not under 3 % (and one T-pose in the fight). **A.2 was not started on a failed probe, so the owner ships off and no
old writer was deleted.**

**A.3** was not run (A.2 not started). Its "before" is Part 0's table; the A.1 probes above are the owner's only numbers. **No old
writer was deleted:** each one leaves an owner-driven copy alone (the check in WO-167's swing path, WO-143's one-shots, WO-141's
placement, WO-190's toggles and T-pose watch, WO-187's planner, WO-197's pairs, WO-132's engagement for the probe), and they all
still drive every copy while `mp_copy_anim_owner` is off.

## Part B: the joiner's own defence

**Built (`mp_defence_local`, on):** on the joiner, a copy swinging at the joiner inside the joiner's fight is watched: a blow the joiner's own engine lands is
judged by it (hit, blocked, perfect block, guard broken: the hit record's own verdict bytes) and kept, and the outcome goes to the host
(`DefenceOutcome`, appended); the host holds its own verdict for that blow up to the swing's window plus one round trip (at most
450 ms) and sends none when the joiner's outcome came; otherwise its verdict goes as before (`WO208-DEF fallback`). B.4
(`mp_defence_recoil`, on): a stab the joiner's engine blocked that the host's had as a hit makes the host's NPC play the game's own
failed-attack recoil.

**What the bench found:** the copies' swings on the joiner are the row path's animation, and their blows land on the joiner's own
player about once a fight -- on main as well (one local hit against 86-120 host verdicts per fight). The first design also told "missed"
for a window whose blow did not land; on d9 that was 30 of 32 blows (9 of 10 in the 2-v-1), the host then sent no verdict, and **the
joiner took no damage at all**. Fixed (d11): a miss is told only with the joiner's own dodge seen in the window; a window with neither
a landed blow nor a dodge is no judgement and the host's verdict applies, as before B. That alone left the host holding every such
verdict the full 450 ms for an outcome that never came (damage on the joiner arriving late); so the joiner now tells the host "none"
when such a window closes, and the host applies its own verdict at once (d13). With both, the joiner's engine judges only the few
blows that land; the rest are the host's (the 95 % below is missed). The engine rule behind it: a copy's swing played as an animation
row is not the combat AI's attack action, and only the latter's blow tests its target; making the copy's swing a real attack of its
combat AI on the joiner is the next step.

**B's acceptance on the release candidate (d14), the bench's 1-v-1 and 2-v-1, holding block, timed blocks and dodges:**

| run | step | host blows on the joiner | judged by the joiner's engine | host fallbacks | the host's hold, p50 / p90 | figure seen blocking (host lines) | lock-on: the swinging copy is the joiner's opponent |
|---|---|---|---|---|---|---|---|
| d14-defence | 1-v-1 | 28 | 0 | 28 | 0 / 468 ms | 15 | 2 of 18 windows |
| d14-defence | 2-v-1 | 46 | 0 | 46 | (the run's) | 3 | 0 of 2 |
| d14-defence-2 | 1-v-1 | 38 | 0 (one blow landed and the joiner's engine blocked it; it matched no host blow) | 38 | 0 / 453 ms | 7 | 1 of 6 |
| d14-defence-2 | 2-v-1 | 44 | 0 | 44 | (the run's) | 1 | 0 of 1 |
| d9-deftwin (`defence_local` off) | 1-v-1 | -- | 0 (the host judges every blow, no hold) | -- | none | 8 | 0 of 17 |

The 95 % is missed (0 %), fallbacks are 100 % instead of under 5 %. The "none" rule works: 221 of 360 host blows over both runs
went at once (the joiner's window had closed with nothing landed), 139 waited the 450 ms cap. No fight crash in these runs or in
the 10-fight crash gate (Part F).

**B.1 (engage early) and B.6 (lock-on) were not achieved, on main or here.** On the joiner the copies of the host's attackers are
rarely in the engine's own fight with the joiner: WO-187's planner found the joiner as their opponent in 10-12 of 140-386 samples per
run (main 12/142 and 10/151, d9 7/99 and 10/386, the F.1 twin 11/130). The joiner's own opponent at the midpoint of a copy's swing
(what lock-on shows) was that copy in 1-6 of 34-39 sampled windows, another body in 10-27, nobody in 6-22, with or without this WO's
changes. The reason is on the joiner's own screen: **the copies' combat AI fights the host's figure there.** In the defence run the
host's NPCs landed 107 blows on the joiner's figure; on the joiner's machine their copies landed 166 blows on the host's figure and
about one a fight on the joiner. WO-187 asked for the joiner as their opponent 15 times (the host's NPC targeting the joiner) and for
the host's figure 6 times; WO-132 added the copies to the joiner's skirmish 42 times; their AI kept the host's figure. B.1 is therefore the copy's opponent mirrored from the host's NPC's (the joiner's figure there is
the joiner's own Henry here) through the engine's own target calls; lock-on and the joiner's engine judging most blows follow from
it. Not built in this WO.

## Part C: the joined conversation

**C.1 / C.2 — the listener dropped for silence.** Every drop on the d29 smoke and on the baseline smoke was the same talk: the
bailiff's, whose dialogue cameras the game itself refuses ("Dialog camera type BONE_MEDIUM is not valid (valid:0, colision:0)", every
shot). The starter's camera sampler then stays on the player's own head view for the whole talk (579 frames "in head", 0 sent) and the
listener's view ends after its 3 s silence rule. **Fix (`mp_conv_cam_player_view`, on):** a running talk that has sent no dialogue
shot for 1.2 s sends the starter's own rendered view instead, until a dialogue shot comes back.

On the release candidate (d14): `jointalk` both roles three times, **zero silence drops** (two runs pass every voting case in both
roles; in the first the tavern's own scripted brawl began as the joiner's talk started and the listener left for combat, as designed:
the bench's world). One talk of the second run had no dialogue shot at all, naturally: the host sent its own view (693 frames) and the
joiner listened to its end. **No-camera talks on demand:** the game's own `wh_dlg_CollisionRadius` set very large makes every dialogue
camera fail its collision test (the bailiff talk's exact failure). With it, three of the host's four talks had no dialogue shot; the
host's view went out instead (360 / 4,110 / 540 frames), every talk was listened to its end, every voting case passed, 0 silence
drops. **The twin** (`mp_conv_cam_player_view` off, the same cvar): the listener dropped for silence twice.

**C.3 — the game's dialogue key map left on.** On the baseline smoke the host's dialogue keys reached the listen actions twice (and a
Back meant to clear a vote opened the pause menu) because the host was still inside its own talk: the bailiff talk above stayed open at
its first decision after the listener dropped, the bench moved on, and the host later listened to the joiner's talk from inside it.
**Fix (`mp_listen_own_talk_first`, on):** a player in a talk of their own does not join the partner's; the bench now closes a talk it
gave up on (the game's own `InterruptDialog`). The listen-once guard is kept as a check.

**C.4 — conversations that start by themselves.** Built (`mp_conv_autojoin`, on): the starter's announce now says when a talk started
by itself (the engine's own origin in its "New dialogue" line: a quest's `ForceDialogNode`, or an NPC opening a full dialogue with the
player, `AI::RequestDialog`); the partner within 30 m who is free (no talk of their own, no fight, not listening already) listens in
without a key press, the same join as the prompt's.

On the bench's scene world (Semine's camp; Gnarly's questions are a quest-forced talk after the second talk with him): **the host's
forced talk ran on the host's machine and the joiner was listening 0.77 s after it began** (`WO208-AUTOJOIN ... listening in`; the
host: "partner 1 listens to conversation 439 (auto-joined)"), and stayed listening, without a silence drop, for the minutes it stood
at its decision. With the joiner talking first, **the joiner's forced talk started on the joiner's machine** (the WO's B7 point
holds). **Not solved: the host started the same forced talk for its own player within the same second.** The joiner's talk had been
replayed on the host (WO-200) and set a camp State; the host's own quest flow then forced the follow-up for the host's player, in a
context the node gate does not guard (WO-203's gate keeps forced dialogues out of the replay itself, not out of what a State it set
wakes later). The two copies did not collide: the joiner's auto-join of the host's copy was refused by C.3's rule the moment its own
talk began, and the host did not join the joiner's; each player had the talk on their own machine. **The twin**
(`mp_conv_autojoin` off, the same scene): the host's forced talk came, the joiner was offered it (the prompt) and never joined.

**C.5 — the listener's own head** (`mp_listen_show_head`, on): while listening, the listener's own character keeps its head in the
shots (the game's own head-visible setting, put back after).

## Part D: loot and the digest

**D.1 — the loot take not seen: the bench's pick.** The level has stashes that share a name (`Stash19`, `Stash45`). The bench found
the nearest stash by its class, moved the joiner beside it, then took the item through the stash's *name* -- which the game resolves
to another stash of that name, 931 m away (d29 and the d14 smoke took the very same item, handle `0200000000005E88`, from it). Nothing
was taken from the stash the joiner stood at, so the mod rightly saw no take. The bench now keeps the stash (or body) it picked as the
entity itself, moves the joiner to the stash's own height and checks the distance before taking. On the way three more of the bench's
picks showed, never the mod's ledger: a body the joiner was 44 m from, a crime scene's corpse that the scene's own bandit looted on
the host meanwhile (the joiner's next open showed the host's 0 items, as it should), and a stash "460 m away" that was in fact the
namesake. **On the fixed bench, five in a row passed**, each a stash and a body (the stash reached at 0.8-0.92 m and its take seen
by the mod; the body's item taken on the host and confirmed back, five times).

**D.2 — the digest's unexplained differences, by kind:**

| kind | real or the measure | what changed |
|---|---|---|
| States under no quest: tutorials, the open-world combat chatter, a random event's state, the fog machine (the game's own files declare them `<Gameplay>` / `<Module>`, not `<Quest>`) | the measure: each machine runs its own, and the State mirror carries quest States only (WO-137) | the analyzer names them known-local, from the game's own Scripts.pak (`NOT_A_QUEST_RULE`; unreadable files: off) |
| the world clock a 15-minute step apart for minutes, during the host's talks | real: the host's clock stands in its dialogue, the joiner's was set to stand too, and the joiner's gentle clock correction wrote a running ratio over it every 2 s (up to 917 game s ahead, then pulled back in jumps) | `mp_clock_stands_with_host` (on): no correction while the clock stands with the host's |
| host quest changes late on the joiner (one 2 min 8 s) | real: the joiner's apply queue stopped 30 s behind every change whose node sleeps there | `mp_quest_wait_aside` (on): such a change waits aside, the later ones apply in the host's order |
| nodes asleep on the joiner (part of a quest hibernated there) | real, open: a hibernated node drops the port's pulse; hibernation is not mirrored, and writing the value into the State would write into an engine object the mod does not own | reported |
| the 9 h sleep's clock: the joiner's own sleep ran 56.5 s past the host's | real, open: WO-200's clock correction was running when the sleep began and reset the ratio 3 s into it ("a skip or a load", over the game's own sleep time-lapse) | reported (pre-existing since WO-200; minimal fix: no ratio write while the game's own skip runs) |
| quest timers one phase apart (the tavern brawl's), two applied-stale values | not yet classified | reported |
| keys on the joiner (at their first value) never on the host for a whole session, all from the other map region (8 in the soak) | not yet classified (the joiner's own save before the join is the likely source) | reported |

**On the release candidate:** the full smoke's digest has 12 unexplained (main's smoke: 49), the 30-minute soak's 8 (all the last
kind; its clock differences all explained). D.2's "zero" is missed.

## Part E: carry-overs

**Not built.** **Gliding** (a copy's walk started through IdleToMove) is one of the owner's
rules (the host's walk transitions installed on the copy), so it ships with the owner, off; built outside the owner it would be one
more writer on the copies. **Area triggers for both players:** the game's quest area node is event driven -- at activation it registers
the player's own entity with the area tracker, and the tracker calls the node back when that entity crosses. Unlike the distance
node WO-204 answered, there is no evaluation for the partner's figure to answer, so WO-204's value override has nothing to override.
A build needs the mod to watch the figure against each active player area and call the node's own enter / leave handler (no node
writes), with WO-204's quiet rules; that and a bench scenario with such an area are a WO of their own.

## Part F: the release gates

**Every crash on the bench** (each investigated: its dump, the mod's log before it and the free VRAM):

| run | build | who | where | free VRAM | verdict |
|---|---|---|---|---|---|
| m0-c1fight-2 | main | the joiner | CombatModule+0x8ff0a (WO-207's signature) | 2,598 MB | **the mod:** WO-187 wrote the joiner back as the opponent of a copy WO-132 had released 17 ms before |
| d9-deftwin (2-v-1) | d9 (F.1's first gate) | the joiner | CombatModule+0x559d32 <- +0x8ff0d (the same walk) | 2,797 MB | **the mod:** the same write-back 490 / 54 ms after the release; F.1's first gate asked only the engine's relation test, which still answered "fighting" |

**F.1 (`mp_target_in_fight`, on):** the joiner is written as a copy's opponent (WO-187's wish, WO-166's strike) only while the mod
itself holds the copy in the joiner's fight (WO-132's engagement) and the engine's relation test agrees; a copy found with the joiner as its opponent
outside that is counted (`WO208-TARGET violation`). The test is the crash's own state -- the joiner written as a copy's opponent while
the mod does not hold the copy in the joiner's fight (counted from the joiner's log):

| runs | build | the joiner written as an opponent | of them while the copy was not held |
|---|---|---|---|
| m0-c1fight-1, -2 (crash 1), -3 | main | 95 | 68 |
| d9-probefight, d9-defence, d9-deftwin (crash 2) | F.1's first gate | 39 | 35 |
| d9-f1twin | F.1 off | 44 | 31 |
| **d14-f-c1x (10 fights), d14-defence, d14-defence-2** | **the release candidate** | **52** | **0** |
| d14-f1twin | the release candidate, F.1 off | 7 | 7 |

**The hard gates:**

| gate | result | evidence |
|---|---|---|
| no crash the bench proves is ours | **pass** on the release candidate | 10 fights in one session, 2 defence runs (1-v-1 and 2-v-1), the smoke's fight, the loot runs' fights, F.1's twin: no crash. The WO's two crashes (above) were the mod's, on main and on F.1's first gate, both closed by F.1 as built |
| join works | **pass** | every run's join step: 39 of 39 |
| no-input-capture | **pass**, first and last | 119 binaries (the candidate's included), 1,364 scripts (the bench and the harness included); the self-test caught every planted capture |
| payload | **pass** | the installer build's payload smoke (the published relay and agent round trip, release 0.63.7), no-input-tooling, no-screenshot (1,036 files, 0 hits), no-input-capture on the payload (963 binaries and the pak's script) |
| publishing | **pass** | the pending commits against the denylist and the field logs' player names |
| relay table | **pass** | 95 rules, every W164 text fits its row |
| the save round trip | **pass**, live | inside the installer build, against a bench copy on the candidate: weapon drawn, a rest save (healthy, 0 stuck fights), reloaded, drawn again. Its first attempt found the copy gone: alone on its save it had grown from 7 to 20 GB in its first minutes and, with the build's own load, ran the 32 GB PC out of memory (the game's own "Out of memory" box) -- **the bench**; a fresh copy passed |
| the real saves unchanged | **pass** | the hash list checked before and after every run (160 checks, every one a match) |
| each fix's twin fails its own test | **pass** | below |

| fix (switch) | its test | the fix | the twin (switch off) |
|---|---|---|---|
| F.1 `target_in_fight` | the joiner written as a copy's opponent while not held in the joiner's fight | 0 of 52 | 7 of 7 |
| C.2 `conv_cam_player_view` | silence drops in talks with no dialogue camera | 0 | 2 |
| C.4 `conv_autojoin` | the partner listens in to a forced talk | in 0.77 s | offered, never joined |
| B `defence_local` | blows judged by the joiner's engine, at least 95 % | 0 % (B's acceptance not met) | 0 %: the host judges every blow again, as the twin must |
| B.4 `defence_recoil`, C.3 `listen_own_talk_first`, C.5 `listen_show_head`, D.2 `quest_wait_aside`, `clock_stands_with_host`, `joiner_player_triggers`, B's dodge rule | unit and synthetic tests with their twins (the agent's, the Lua suite's, the native suite's) | pass | fail, each |

**The installer:** `KingdomComeTogether-Setup-0.63.7.exe`, 107,613,730 bytes (0.62.7: 107,660,104), the three start saves inside,
unsigned as before; built from the commit the bench tested, every gate of the build passed. It stays with the maintainer.

**Measured and reported:** the queues (p99 16-251 ms after the join and the fights),
the conversation steps and votes (above), the timer (no timed decision on the bench's save: **the bench**), loot (D.1, five in a
row), the digest (D.2), the animation targets (A.1), B's rate (0 %: **the mod**, B.1 not built), the clock gaps (the 9 h sleep's:
**the mod**, pre-existing; the 3 h wait's 7 / 9 / 2 s), frame rates (no drop against main: the smoke's host 62.9 / joiner 43.9 fps
median against main's 63.9 / 38.6; the soak's host 80.5 / joiner 104.8).

## Switches

Each is a console command on both machines (`mp_<name> on|off`; `mp_w208_status` lists them).

| switch | default | what it does |
|---|---|---|
| `mp_target_in_fight` | on | F.1: a copy of the host's NPC takes the joiner as its opponent only while the game has them in a fight together |
| `mp_defence_local` | on | B: a copy's blow that lands on the joiner is judged by the joiner's game; the host takes that judgement |
| `mp_defence_recoil` | on | B.4: a stab the joiner blocked (the host's game had it as a hit) makes the host's NPC recoil |
| `mp_copy_anim_owner` | **off** | A: one animation owner per copy, driven by the host's NPC's streamed state (missed A.1's bar) |
| `mp_conv_cam_player_view` | on | C.1 / C.2: a talk with no usable dialogue camera shows the starter's own view |
| `mp_listen_own_talk_first` | on | C.3: a player in a talk of their own does not join the partner's |
| `mp_conv_autojoin` | on | C.4: a talk that starts by itself (a quest or an NPC starts it) takes the partner in within 30 m |
| `mp_listen_show_head` | on | C.5: the listener's own character keeps its head in the shots |
| `mp_quest_wait_aside` | on | D.2: a host quest change whose node sleeps on the joiner waits aside; later changes keep the host's order |
| `mp_clock_stands_with_host` | on | D.2: no clock correction on the joiner while its clock stands with the host's (the host in a talk) |
| `mp_joiner_player_triggers` | on | D.2: the joiner's own player-watching quest areas stay quiet; the host's values decide |

## Open

- **B.1: the copy's opponent mirrored from the host's NPC's** (the joiner's figure there is the joiner's own Henry here): on the
  joiner the copies' AI fights the host's figure; until that changes the joiner's engine judges almost no blow and lock-on rarely
  takes a copy (B, B.6).
- **The single animation owner** (A.2 / A.3): it missed A.1's bar after one round of design changes (fight 8.2 %, tavern 4.8 %, one
  T-pose); the village probe needs a bench pick the trace holds.
- **C.4 point 3:** a State the joiner's talk set on the host wakes the host's own quest flow, which forces the same follow-up talk
  for the host's player.
- **The joiner's sleep cut short by WO-200's clock correction** (pre-existing): no ratio write while the game's own skip runs.
- **D.2:** hibernated nodes on the joiner, quest timers a phase apart, other-region keys on the joiner only.
- **E:** gliding ships with the owner; quest areas for both players need a WO of their own.
- **The bench:** no timed decision on its save (the timer case), the tavern's scripted brawl in the jointalk runs, a village walker
  the probe's trace holds.
