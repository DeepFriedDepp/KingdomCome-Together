# WO-209 — The owner on every copy, each copy's opponent mirrored, the joiner's own defence

Version **0.63.8**. Everything here was measured on the bench (two copies of the Modding Tools game on one PC, joined through our own
relay, started the launcher's way); every fix ships with its own switch, or a test switch, and a twin (the same build with it off)
whose test fails. **Every change ships on** (the maintainer's rule: on by default, off only by hand) -- the owner and the joiner's own
defence too; where the WO's own ship rule would have said off, the measures are reported and the switch is named. Nothing built here
reads input: `tools/Check-NoInputCapture.ps1` passes on every binary and script, first and last.

## Part 0: the baseline (0.63.7)

`main` (0.63.7) built as the WO's baseline and every offline gate passed (97 of 97); the saves folder backed up byte-equal with its
hash list, checked before and after every run of the WO (every check a match). On the pair, the same scenarios with
`mp_copy_anim_owner` off (0.63.7's default) and on, measured with one log pack tool:

**The fight** (the camp's bandits on the joiner's figure; the owner off: three runs, on: two):

| measure | owner off | owner on |
|---|---|---|
| copies running a different full-body action / fragment | 20.9 % / 26.2 % | 18.2 % / 16.7 % |
| position error p95 | 11.9 m | 28.6 m (two camp bandits' copies 19-32 m from their host NPCs, p50: Part C) |
| T-poses / stuck queues / queue overflows | 0 / 0 / 2 | 5 / 4 / 0 |
| host swings shown on the copy; their start after the host's, p90 | 70.3 %; 342 ms | 60.4 %; 899 ms |
| failed work one-shots | 66 of 152 | 15 of 59 |
| foot slide, copies / the host's own walkers (s per body-minute) | 3.3 / 1.2 | 1.5 / 0.8 |
| blows on the joiner judged by his own game / host fallbacks | 2 / 195 | 0 / 190 |

The copies' opponent was the host's own player, not the joiner, in 0.63.7: lock-on took a copy that swung at the joiner in 1-6 of 34-39
windows. **The tavern, the village walk and the smoke columns** run on the bench after this release (four of the WO's village runs
had the joiner away from the host -- a bench fault found and fixed late -- and the bench's time went to the release candidate's
crashes); they will be added to this document.

## Part A: each copy's opponent mirrored from the host's NPC

**Built:** the host reads each tracked NPC's opponent (nobody, the host's player, a partner's figure, another NPC) and sends it on the
NPC state stream when it changes, and again every 1.5 s while it has one (NpcOpp 0x7C / 0x7D, in the relay table). On the joiner the
host's player becomes the host's figure, the joiner's own figure becomes the joiner's own player, another NPC its copy. A copy whose
host NPC fights the joiner's figure joins the joiner's fight (the game's skirmish call, adds only: nothing takes it out until the fight
ends), takes him as its opponent (the engine's own target write, pinned) and is held in combat mode; the copy's own AI never picks.
Switch `mp_copy_opponent_mirror` (on).

| | the mirror on | the twin: the mirror off |
|---|---|---|
| the copy's opponent is the host's NPC's (translated), share of samples | 99.7 % of 29,759 (defence), 98.9 % of 22,744 (a fight) | 79.8 % of 26,648 (WO-187's older wish alone) |
| the copy follows a change of the host's NPC's opponent | p50 13-14 ms, p90 20-456 ms | -- |
| blows on the joiner judged by the joiner's own game (B rides on A) | (B below) | 0 of 85: the host judged every one |
| fights in one session without a crash | 10 in one session (rc6), after two crashes on the way (Part F) | -- |

**Lock-on** (the joiner's own opponent 0.3 s into each copy's own attack at him): the engine picks the lockable enemy best placed in
the player's view, and in a fight the player keeps facing the one he is locked on. On the release candidate's defence run, with the attacking copy within 30 degrees of the joiner's facing the lock was on it in
**40 of 40** windows; over all windows 33 % (40 of 120). Every window where the lock was
elsewhere had the attacking copy outside his view: the bench's player never turns to an attacker the way a player does (it writes his
facing; the fight's own facing turns him back). **The bench** -- the WO's 90 % over all windows is not met; the mod's part (the copy
lockable and in front: taken) passes. In the twin no copy attacks him with its own attack: no window to measure.

## Part B: the copy's swing is its own attack

**Built:** a copy whose host NPC attacks the joiner starts its own combat attack at him through the engine's own AI attack (the zone
set the engine's way, the attack built by the copy's own attack factory, handed to its own action director), from the host's swing
row as it starts and, as a backup, from the host's streamed state. His own engine resolves the blow -- block, perfect block, parry,
dodge -- and the host takes that outcome; the host holds its own verdict for the swing window plus one round trip, at most 450 ms.
Switch `mp_copy_attack_ai` (on); the outcome's path is WO-208's `mp_defence_local` (on).

What it took on the bench (each found in a run, fixed, run again): the joiner's verdict wait ran in the relay's reader loop and held
the next swing; a refused attack (the copy still in its last one) is asked again each frame for half a second; one host attack is
one copy attack, whoever started it (a combo's first swing had started twice, the rest a second behind); the copy's own attack owns
its full body while it runs (the owner's state, a stream delay behind, put the host's footwork over it and cut it); a fight's first
swing waits up to half a second for the mirror's word. Found through B: the owner's copies refused the activity rows (stuck at a
camp's waiting spot, Part C) and a "stop writing it" message overtaking the "write it" after it (Part C).

40 blows on the joiner per phase (two 1-v-1 phases had no fight: the bench's move of the joiner to the camp was lost); the
bench's defender holds block, times blocks and perfect blocks near the blow, dodges, takes some
open:

| run | phase | judged by the joiner's game | host fallbacks | outcomes (the joiner's game = what the host took) | the host saw the figure block |
|---|---|---|---|---|---|
| first build (rc1): defence | 2-v-1 | 92.3 % (48 of 52) | 4 | hit, blocked, perfect block, missed; 0 mismatches | 10 lines |
| first build: defence-2 | 1-v-1 | 92.7 % (38 of 41) | 3 | hit, blocked, perfect block, guard broken, missed; 0 mismatches | 27 |
| first build: defence-2 | 2-v-1 | 89.1 % (41 of 46) | 5 | the same; 0 mismatches | 16 |
| first build, the owner off | 1-v-1 / 2-v-1 | 87.8 % / 90.5 % | 5 / 4 | 0 mismatches | -- |
| **the release candidate**: defence | 2-v-1 | 86.0 % (37 of 43) | 6 | hit, blocked, perfect block, missed; 0 mismatches | 12 |
| the release candidate: defence-2 | 1-v-1 | 87.5 % (42 of 48) | 6 | hit, blocked, perfect block, missed; 0 mismatches | 8 |
| the release candidate: defence-2 | 2-v-1 | **95.3 % (41 of 43)** | 2 | 0 mismatches | -- |
| the release candidate: a fight (three bandits, nobody defending) | -- | 89 % (67 of 75) | 8 | -- | -- |
| **the twin** (B off) | 1-v-1 / 2-v-1 | **0 of 43 / 0 of 47** | 43 / 47 | the host judged every blow | -- |

The dev builds before the candidate passed the 1-v-1 phase three times in a row (one fallback a run or none). What is left, by cause
(the host's blow against the joiner's outcome for it): a combo's finishing swing (the copy's own finisher lands ~150 ms later than
the host's, past the 450 ms hold with the stream's delay), the first swing of a fight (the copy not yet in the joiner's fight when the
host's row arrives; fixed for the row path), and moments the joiner's game stopped answering for about a second (the bench). A
parried blow plays the copy's own parried reaction on the joiner (it is the copy's own attack); on the host, WO-208's recoil plays
only for a blocked stab, and none came.

**Ship decision: `mp_defence_local` on** (as in 0.63.7), with `mp_copy_attack_ai` on. B's acceptance is **not met** (86-93 % judged in
five phases against the WO's 95 %, 7-14 % fallbacks against 5 %; one 2-v-1 phase passed with 95.3 %), and the WO's rule would ship it off; every change ships on: a blow the joiner's
game does not judge in time takes the host's verdict, as every blow did in 0.63.7, so on cannot make a blow worse than off.
`mp_defence_local off` on both machines gives the host every verdict.

## Part C: one owner on every copy

**Built:** the owner on every copy (WO-208's owner, `mp_copy_anim_owner`, **now on**; 0.63.7 had it off), and on it:

- **C.1 paired actions the game's way** (`mp_copy_paired_engine`, on): a clinch a copy starts against the joiner is started through
  the engine's own sync start with the joiner's own player as its partner; a copy's move inside one is its own attack request; the
  slave halves stay held (the joiner's engine plays his own). No paired action of a copy with the joiner came up on the bench (its
  fights never clinched): built, not exercised -- the tester script asks for one.
- **C.2 work one-shots as the game's request** (`mp_copy_oneshot_request`, on): an owned copy takes WO-143's one-shot request (the
  game's own NPC-state request: an innkeeper's serving) and the owner keeps off its full body while the shot runs. Twin (off): 10 of
  35 one-shots failed, on: about half failed but 135 played against 25 -- mixed, reported.
- **C.3 the fight's T-pose:** WO-208's fight T-pose was a measuring artefact -- bandits 846 m from the joiner before the bench moved
  him, counted by the detector; it now counts only bodies within 150 m of the player.
- **C.5 the old writers:** none deleted (0 lines). The owner is a switch, and with it off the old writers are what animates a copy
  (the twin, and any player who turns it off); and the measures said the opposite of retiring: the owner's exclusion of the activity
  rows (WO-208's "every old writer leaves an owned copy alone") is what left camp bandits' copies standing at their spots.

Fixed on the way, found by the owner's own runs (each a test switch for its twin):

- **The neutral rule on owned copies** (`owner_neutral`): the activity rows were refused for every copy the owner owns, so a copy
  bound at a camp's waiting spot was never told it had left it (0.63.7 with the owner on: two camp bandits' copies 19-32 m from their
  host NPCs, p50). Its twin did not bring the stranded bandits back on the bench (the same camp, the fix off): the original evidence
  was most likely the bind race below; kept, unproven.
- **Binds and unbinds in order** (`native_bind_order`): the game's "stop writing this copy" went out at once and the "write it" after
  it from its own task; the two crossed, and the copy stayed unwritten while the game thought it was written. Twin (off): 5 of 6
  bandits' copies stranded 10-28 m from their host NPCs; on: none.
- **The owner's queue cap** (`owner_queue_cap`): once owned copies took the neutral rule again, a copy seated by the game had its own
  actions (its movement, its look pose) at the head of its action queue, and they never finished; the owner's installs never started
  behind them, the owner withdrew each as stale and installed the next, and they piled up -- 11,004 queue overflows on one copy in a
  tavern night, 59,993 on seven the next, 125,205 in the smoke (the joiner's frame rate fell from 44 to 33 fps there, and its console
  answered a minute late). Leaving seated copies to the game instead stopped the overflows but left the sleepers standing (lying
  postures 0 %: the owner's own install is what lays them down). The fix: the owner queues nothing while a copy's queue already holds
  six actions (the game's limit is ten). Two tavern nights: 1 overflow each, lying postures 85 % and 75 %. **Twin** (the cap off):
  218,619 overflows and the joiner's queue 2.2 s behind.

**C.6 the owner on every copy against Part 0's owner-off column** (the same scenarios, the same log pack tool):

| scenario | measure | the owner off | the owner on (the release candidate) |
|---|---|---|---|
| the fight (three camp bandits) | different full-body action / fragment | 20.9 % / 26.2 % (0.63.7, 3 runs) | **11.0 % / 10.1 %** |
| | position error p95 | 2.3-29.5 m | **0.36 m** |
| | T-poses / stuck queues / queue overflows | 0 / 0 / 0-2 | 2 (1.3 s and 0.3 s) / 2 / 1 |
| | host swings shown on the copy | 64.9-80.6 % | 78.1 % (90.1 % with any attack) |
| | failed work one-shots | 66 of 152 | 8 of 56 |
| a tavern night | different full-body action / fragment | 20.0 % / 93.3 % | 72.8 % / **21.5 %** |
| | sleepers lying on the joiner's screen | **0 %** of 6,185 samples | **80.2 %** of 37,653 |
| | seated people seated there | -- | 0 % of 1,573 (they stand, as in 0.63.7) |
| | position error p95 / T-poses / overflows | 0.68 m / 0 / 0 | 7.0 m / 0 / 0 |
| the village walk (a walking villager, the joiner with the host) | different full-body action / fragment | 7.5 % / 45.6 % | 64.4-68.0 % / 25.3-53.5 % |
| | seated people seated / sleepers lying on the joiner's screen | 0.7 % of 2,125 / 0 % of 1,572 | **71.6-76.8 %** / **100 %** |
| | position error p95 | **0.19 m** | 18.3 m and 1.2 m (two runs: copies held seated while their host NPC walked off, one 46 m away) |
| | T-poses / stuck queues | 0 / 0 | 0 / 0 and 10 / 15 (copies 130-148 m from the joiner, p50: beyond where the game animates a body) |
| | foot slide, copies / the host's walkers | 1.0 / 0.21 | 4.6 / 0.64 and 20.4 / 1.0 |
| the smoke (the whole pair test) | different full-body action / fragment | -- (its trace window was empty) | 52.2 % / 36.9 % |
| | T-poses / overflows / frame rate (host / joiner) | 0 / 1 / 59.8 / 50.0 fps | 0 / 0 / 66.6 / 41.6 fps |

The owner-off taverns and smoke are this WO's own build with the owner off (0.63.7's own columns follow the release: Part 0). "A different full-body action"
compares the action's class: the owner plays the host's fragment through its own action, so a sleeper lying in the host's own
fragment still counts as a different action; by fragment the owner is far closer (21.5 % against 93.3 % in the tavern).

**The ship decision: the owner ships on** -- the maintainer's rule. By the WO's own rule (on only if lower on different family, not higher on
T-poses, stuck queues or overflows, higher on posture match, not lower on swings shown) it would ship off: the family measure is higher
in the tavern and the village (the class, above), and the fight had two short T-poses where the owner-off fights had none. It is
better on the fragment, the posture (lying 80 % against 0 %), the fight's position error and its swings shown. `mp_copy_anim_owner
off` on both machines brings back 0.63.7's animation.

**C.7 foot slide:** the copies' foot slide against the host's own walkers, seconds per body-minute: the fight 1.33 against 0.96 (1.4 times: within
the WO's 2 times), the tavern 2.03 against 0.085, the village 4.64 against 0.64 (7.2 times), the smoke 2.83 against 0.76 -- **C.7 is
met in the fight only**. With the owner off the tavern's copies slid 0.64 and the village's (a run whose joiner stood far off) far more.

**A copy the owner lets go** keeps the host's last full-body fragment it was given (withdrawn, not stopped: stopping it leaves the
full body empty, a T-pose, until the copy's own AI acts) until its own AI starts another action -- in the smoke's scene world three
copies looped the host's own idle so for minutes after they were let go: the same idle on both screens, counted by the family measure
as a different action. Open.

## Part D

- **D.1 the joiner's sleep cut short** (`mp_clock_quiet_in_skip`, on): the joiner's clock correction writes no ratio while the game's
  own skip runs (its sleep or wait), and the next write after it is forced. The joiner's own 9-hour sleep: the clocks 73 s apart a
  second after the skip, 8 s a minute later. **Twin** (off): the joiner's sleep was cut about six game-hours short -- 22,091 s apart
  a second after, still 1,797 s a minute later.
- **D.2 the duplicate forced talk** (`mp_forced_talk_once`, on): each player's forced talk is announced with its quest path and when
  it started; a player whose own forced talk is the partner's (the same path, started after it -- a tie goes to the joiner's) ends his
  own the game's way and is offered the partner's, which he joins by himself (WO-208's auto-join). On the bench (the scene world, the
  joiner talking to the Semine camp's Suk): the quest's follow-up started on the joiner and on the host 531 ms later; the host's ended
  and the host listened to the joiner's. **Twin** (off): the host's own ran on and it was never taken into the joiner's ("in a talk of
  his own").
- **D.3 memory.** One bench copy alone on the same save, its memory every 30 s for 10 minutes after the load: with the mod, the
  working set rose from 7.0 to 9.6 GB over the first 3.5 minutes (about 11 MB a second) and then stayed flat to the end (-3 MB a minute
  over the last six); without the mod (its folder set aside, the same save) it stayed at 6.9-7.0 GB. A bounded step, not a growth that
  goes on: nothing to fix by the WO's rule. Its source is not attributed (the two runs load the save two ways: the host's flow and the
  console).
- **D.4 the digest's kinds.** Built: a quest node the host has awake and the joiner has asleep is woken the engine's own way (its
  topmost asleep ancestor, the Wake port's reason) before the host's change is applied (`mp_quest_wake_with_host`, on). Classified in
  the digest's analyzer, each rule with its twin (62 tests): a State at its initial value on one machine only, unchanged all run long
  (**default-only**); a quest timer both machines run, a step apart and then equal again (**timer-local**: timers are never mirrored,
  the States they set are); a host change applied from the initial value where the joiner did not have the key yet
  (**absent-is-initial**). A timer on one machine only stays unexplained on purpose (WO-207's Jaunt shape). WO-208's soak digest: 8
  unexplained -> 0; its smoke: 12 -> 9. On this WO's builds: **the 30-minute soak, 3 unexplained** -- one kind, quest modules awake on
  the joiner and asleep on the host from the first checkpoint (the reverse of what the wake handles; putting the joiner's to sleep would
  be a write into the game's quest graph: not built); **the smoke, 10-18** -- States both machines run themselves converging within 1-4
  checkpoints, host changes where the joiner's own logic had run ahead, the inn brawl's escalation timer on the host only (the Jaunt's
  shape), the clock a 15-minute step apart for up to 18 checkpoints after the sleep and the wait and in talks (gaps the WO logs, never
  gates), one chest row at the run's end. **D.4's acceptance (zero) is not met**; every remaining row is named above.
- **D.5 quest areas for both players: not built.** The game's area trigger (RPGModule's area-trigger node: its activation, its enter
  and leave handlers, the tracker's registration) can be called on the main thread with a tracker record and the area -- but the mod
  has no list of the area triggers that are active, no tested way to read an area's shape and test a figure's position inside it, and
  the bench has no quest area to prove it on. Its own WO; until then a place a quest waits for counts only the host's character.

## Part E

- **E.1 a timed decision on the bench: not reached.** The game's own Lua has no quest bind in this build (the documented quest system
  is not there), so a save cannot be set to a quest's step; and the bench's saves have none in reach (read from the State digest: the
  musicians' quest is past its timed small talk, the stocks quest is finished, the Cumans quest -- with the inn's 6-second timed
  polylog -- has not begun). The timer's mirror (WO-207) stays unproven on the pair. Next: play the Cumans quest to the inn polylog
  through the bench's conversation scenario.
- **E.2 the joined talk away from the tavern brawl.** WO-208's joined-talk runs met the inn's evening brawl (the game's own random
  event: a skirmish that takes in whoever stands near). The joined talk now runs by day with the bailiff, the smith and the miller
  (`convday`), and the brawl is a scenario of its own (`convbrawl`). By day with the bailiff (the release candidate): the host-started joined talk passed every vote case -- away
from the brawl, the listener stayed in the talk to its end; the joiner-started one failed on the bench: the host's join came at 122 s,
2 s past the step's limit (its console answers late right after the bench's clock jump). The smith and the miller were not found by
day. The brawl scenario: the bench's way of starting a fight (a guest set on the joiner's figure) started none with a tavern guest in
two runs -- it needs another trigger.

## Part F: the release gates

**Every crash on the bench** (each investigated: its dump, the mod's log before it and the free VRAM):

| run | build | who | where | free VRAM | verdict |
|---|---|---|---|---|---|
| defence, early dev build | d1 | the joiner | CombatModule+0x559d32 | 6,502 MB | **the mod:** a copy left engaged in the joiner's fight after the joiner's death; fixed (his leave clears the mirror's holds) |
| defence, early dev builds | d2, d3 | the joiner | CryPhysics+0x244543 | 2,636 / 6,824 MB | **the bench:** its turn of the defender wrote an unnormalized direction ("Not-orthonormal matrix" on the player, scale 1.19 -> 2.62); normalized in the bench |
| the crash gate, fight 1 | rc4 (every switch on) | the joiner | CombatModule+0x8ff0a (WO-207's signature), a worker thread | 1,793 MB | **the mod:** both players died in the fight (the bandits' blows are real now); his death's own leave of the fight (the respawn's) did not end the copies' engagements -- the earlier fix sat on the agent's leave only -- so one bandit's copy stayed held from the old fight, was never added to his new one, and its own attacks at him ran from the old skirmish; 9 ms after one of them, the crash. Fixed in rc5: the death's leave ends every engagement too (`death_leave_clears`) |
| the crash gate, fight 4 | rc5 (the death's leave fixed) | the joiner | CombatModule+0x8ff0a, a worker thread | 637 MB (the bench's two copies; a memory-starved crash would be the renderer's, not the combat module's read of -1) | **the mod:** 8 ms after a bandit's copy started its own attack at him -- a copy the game itself held not hostile to him (its own answer to his lock-on picker: no, 16 of 16; the mod answers yes for the lock), while the mod held it in his fight; rc4's crash had the same shape (no, 605 of 605, 9 ms after). Fixed in rc6: no attack of its own at him from a copy the game holds not hostile (`game_hostile_gate`); such attacks before the fix: 104 of 706 in that session, 15 of 159 in rc4's, 21 of 993 in a 10-fight session that did not crash, none in the defence and smoke runs |

The crash gate's first session on rc4 ran 8 fights without a crash (stopped by the maintainer, who needed the PC). **rc6's crash gate: 10 fights in one session, no crash** (8 with the joiner in the fight; in the last two the host's bandits no longer
took his fresh figure after his death in fight 8, by the bench's order: no blow on him). In that session no copy the game held not
hostile attacked him, so the gate never had to refuse: the fix rests on the state both crashes came from, not on a twin that crashed.

**The hard gates:**

| gate | result | evidence |
|---|---|---|
| no crash the bench proves is ours | **pass** on the release candidate (rc6) | the crash gate's 10 fights in one session, no crash; the two crashes on the way were the mod's and are closed (above); the earlier dev builds' three crashes were closed or the bench's |
| join works | **pass** | every run's join step: 67 of 67 |
| no-input-capture | **pass**, first and last | 119 binaries (1,432 imports) and 1,410 scripts (the bench, the harness, the input helper included); the self-test caught every planted capture (8 of 8) |
| every offline gate | **pass** | 97 of 97 on the release candidate: native 10,102, relay 197, agent 2,007, setup 77, log pack 71, 77 Lua suites, 4 static, the relay table 96 of 96, the relay's fault modes 37, the digest 62, the bench 42, the save tools 14 + 7 |
| relay table | **pass** | 96 rules (NpcOpp 0x7C / 0x7D added), every W164 text fits its row |
| payload, publishing, the save round trip | the installer build (below) | |
| the real saves unchanged | **pass** | the hash list checked before and after every run: 270 checks, every one a match; the mirror roots 136 of 136 |
| each fix's twin fails its own test | **pass**, except `owner_neutral` (its twin did not reproduce) | below |

**Each fix's twin:**

| fix (switch) | its test | the fix | the twin (switch off) |
|---|---|---|---|
| A `copy_opponent_mirror` | the copy's opponent is the host NPC's (translated) | 99.7 % / 98.9 % of samples | 79.8 %; 0 of 85 blows judged by the joiner |
| B `copy_attack_ai` | blows on the joiner judged by his own game | 89-93 % | 0 of 43 / 0 of 47 |
| C `owner_queue_cap` | queue overflows, a tavern night | 1 | 218,619 (the joiner's queue 2.2 s behind) |
| C `native_bind_order` | bandits' copies stranded from their host NPCs | 0 of 6 | 5 of 6, 10-28 m |
| C `owner_neutral` | camp bandits' copies at their waiting spot | 0 | 0 on the bench: the twin did not reproduce (unproven) |
| C.2 `copy_oneshot_request` | failed work one-shots | about half, 135 played | 10 of 35, 25 played (mixed) |
| D.1 `clock_quiet_in_skip` | the clock gap after the joiner's 9 h sleep | 73 s -> 8 s | 22,091 s -> 1,797 s |
| D.2 `forced_talk_once` | the host taken into the joiner's forced follow-up | listened | stayed in its own talk |
| F `death_leave_clears` | copies held in the joiner's old fight after his death | released at his death's leave | held: one attacked him from the old fight (rc4's crash) |
| F `game_hostile_gate` | the copies' own attacks at him while the game holds them not hostile | 0: none arose in the 10 fights (it never had to refuse) | 104 of 706 (rc5's crash session) |
| D.4 `quest_wake_with_host`, the analyzer's three rules | unit and synthetic tests with their twins | pass | fail, each |

**The installer:** built from this commit by the installer build, which runs the payload, publishing and save round-trip gates;
its result is added to this document with Part 0's remaining columns.

**Measured and reported:** frame rates in the smoke (the whole pair test), median: the release candidate's host 66.6 / joiner 41.6 fps
against 0.63.7's 63.7 / 43.9 -- no drop; with the owner off 59.8 / 50.0. The clock gaps: the joiner's 9-hour sleep 47 s a second after,
2 s a minute after; the 3-hour wait 3-4 s (logged, never gated). Memory: D.3.

## Switches

Each is a console command on both machines (`mp_<name> on|off`; `mp_w209_status` / `mp_w208_status` list them). Every switch ships
on; the WO's own ship rules (B's 95 %, C.6's comparison) are reported above, they do not set a default.

| switch | default | what it does |
|---|---|---|
| `mp_copy_opponent_mirror` | on | A: a copy of the host's NPC fights whom the host's NPC fights (the joiner's figure there is the joiner here); a copy fighting the joiner joins his fight, is held in combat mode and stays in that fight until it ends |
| `mp_copy_attack_ai` | on | B: a copy's swing at the joiner is its own combat attack (the engine's own AI attack: zone, attack, action director) |
| `mp_defence_local` | on | B (WO-208): the joiner's own game judges a copy's blow on him (block, perfect block, parry, dodge) and the host takes that judgement; a blow not judged in time takes the host's verdict |
| `mp_copy_anim_owner` | **on** (0.63.7: off) | C: one animation owner per copy, driven by the host's NPC's own state |
| `mp_copy_paired_engine` | on | C.1: a clinch or paired attack a copy leads against the joiner plays the game's own paired way (not seen on the bench) |
| `mp_copy_oneshot_request` | on | C.2: a copy's work one-shot (an innkeeper serving) is the game's own NPC-state request on it (with the owner on) |
| `mp_clock_quiet_in_skip` | on | D.1: the joiner's own sleep or wait is never cut short by the clock correction |
| `mp_forced_talk_once` | on | D.2: a talk forced on a player while the partner already has it ends; the player listens to the partner's |
| `mp_quest_wake_with_host` | on | D.4: a part of a quest asleep on the joiner that the host has awake wakes when the host's step arrives |
| WO-208's others, unchanged | on | `mp_target_in_fight`, `mp_defence_recoil`, `mp_conv_cam_player_view`, `mp_listen_own_talk_first`, `mp_conv_autojoin`, `mp_listen_show_head`, `mp_quest_wait_aside`, `mp_clock_stands_with_host`, `mp_joiner_player_triggers` |

Test switches (the bench's twins only, never a console command): `KCDMP_TEST_W209_OFF=owner_neutral` (the owner's copies refuse the
activity rows again), `KCDMP_TEST_W209_OFF=owner_queue_cap` (the owner queues onto a copy whatever its action queue holds),
`KCDMP_TEST_W209_OFF=native_bind_order` (the game's native bind and unbind sent unordered again), `KCDMP_TEST_W209_OFF=death_leave_clears` (the joiner's death
leaves the copies held in his old fight), `KCDMP_TEST_W209_OFF=game_hostile_gate` (a copy the game holds not hostile attacks him anyway).

## Open

- **B's last misses** (the host's verdict for them; B's 95 % not met): a combo's finishing swing (the copy's own finisher is the
  engine's pick and lands ~150 ms after the host's), a fight's first swing, and the joiner's game not answering for a second (the
  bench). The combo's own request (the engine's attack inputs: light, heavy, special; the copy uses the light one) is the next thing
  to try.
- **Lock-on over all windows** (the WO's 90 %): the bench's player never turns to an attacker as a player does. A bench aim that turns
  the view (not the body) is what would settle it.
- **C.1** paired actions: not exercised on the bench (no clinch arose); the tester script asks for one.
- **A copy the owner lets go** loops the host's last fragment until its own AI acts (the same pose on both screens; the family
  measure counts it).
- **The owner in a village:** copies held seated (the activity rows given back to owned copies) stay where they sat while their host
  NPC walks off (one 46 m away for most of a run); far copies (130 m and more) have no full-body action at all with the owner on --
  invisible there; whether one shows a T-pose as it comes into view is not measured. Seated people still stand on the joiner's screen
  in a tavern. The family measure counts the owner's own action class.
- **D.3** the mod's 2.6 GB step in the first minutes after a load: bounded, not attributed.
- **D.4** quest modules awake on the joiner and asleep on the host; the smoke's States both machines run themselves.
- **D.5** quest areas for both players: its own WO.
- **E.1** a timed decision on the pair; **E.2** the brawl scenario's trigger.
- **The bench:** the joiner's first console call after the bench's clock jump is lost (WO-208's runs too); the village walk now
  checks the joiner's place and moves him again -- four of the WO's village runs had him away from the host most of the time and
  were measured again.
