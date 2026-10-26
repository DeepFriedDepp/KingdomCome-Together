# WO-207 — Shared conversations on a real pair, the queue that never drains, and the bench's own open items

WO-206 built the bench (two real copies of the Modding Tools game on one PC, joined through our own relay, started the launcher's
way). This WO used it: the joined conversation that failed on every real pair, the agent's queue that fell minutes behind in fights,
the clocks after a shared sleep, the divergences WO-206's soak named, memory over an hour, a grown pair smoke, and the animation
detectors' calibration. Every fix is proven on the pair and ships with a twin (the same build, the fix switched off) whose test
fails. Nothing here reads input: every tool built for it passes `tools/Check-NoInputCapture.ps1`, first and last.

Version **0.63.1**. No installer: the maintainer releases after the one-owner WO.

## Part 0: the bench

| item | what changed | result |
|---|---|---|
| 0.1 the second copy's "start another instance?" box | `tools/rig/dialogs.py`: the windows of the second copy's own process only (by pid), the box matched by its class and its Yes/No buttons, `BM_CLICK` to Yes by control id (a posted `WM_COMMAND` as the fallback), logged, 30 s timeout | 10 of 10 unattended launches, answered 0.4 s after the copy started, never the fallback; every launch since (40+) the same |
| 0.2 the real saves | `pair.py` refuses a copy whose user folder resolves into (or holds) the real saves folder, before it starts; `pair.py guard` dry-runs it (tests: the real name, a `(1)` suffix, a parent, empty) | the hash list matched before and after every run |
| 0.3 two scenarios | `c1fight` (the host and the joiner against three bandits for 5 minutes, the joiner swinging with its own mouse and keys, the bandits set on the joiner's figure every 30 s); `c2tavern` (the tavern at 21:00 of the next day, 10 minutes) | both on fresh seed copies every run |
| the bench's own input | the input helper refused a button-up because Windows reports no foreground window for a moment after a click's down; the left button stayed down for a whole fight. It now waits up to 0.5 s for the game to be in front again (only when no window is in front; another program in front still stops everything) | the joiner's mouse swings land: the host applied 23-25 of its blows per fight |
| a step whose game exits | fails (`copy-exited`, with the crash report's path); a fight had "passed" with the host's game gone a minute in | |

## A: the joined conversation on a real pair

**Why no line reached the listener (A.1).** Five causes, one behind the other, each found on the pair:

1. The listener's agent ran every conversation frame (camera samples, lines, options, votes, the end) through its one frame
   processor, 1.7-5 s behind its NPC traffic; its DLL ends the view after 3 s without a camera sample. `mp_conv_lane`: the reader
   hands them to their own lane (max wait 2 ms).
2. The starter's camera pump and its options and lines polls are pipe calls behind ~45 background polls a second (the agent's pipe
   to the DLL carries one request at a time, answered about once a game frame): samples arrived 0.2-2.5 s late, in bursts.
   `mp_conv_pipe_first`: the conversation's calls go first at the pipe's gate, and the DLL answers the starter's camera poll off the
   game's main thread (from its own sample ring, no engine call). With it: mean sample age 40 ms, max 179 ms, every cut followed.
3. The starter's DLL copied the HUD's option rows only from the first listener's join on; a talk that opens straight at a decision
   had set its rows before. `mp_conv_options_early`: the copy is armed as soon as a partner is connected.
4. The starter's own view leaves the dialogue's shots at some decisions and screens (the sampler never sends a first-person view),
   and 3 s later the listener's view ended. `mp_conv_cam_hold`: while the talk runs, a second with no new sample re-sends the last shot.
5. The bench itself: its decision stand-in (`wh_dlg_ForcedDecision` set mid-decision) does nothing, so the host's conversation stood
   at its first decision; the bench now plays the players' own keys (W/S, E, Esc). And the joiner's talk with a copy starts only
   through the class's talk (`BasicAIActions.OnTalk`, which the mod wraps to give the paused copy its brain back), as the player's
   key press does.

**The listener stuck at the pause menu (A.4, the maintainer's report from watching the pair).** The listener's lock was the game's
own `dialog` key map; that map includes `open_pause_menu`, and the game's Back keys (Esc, Backspace) carry both the pause menu and
the mod's listen-cancel. A Back that cleared a vote opened the pause menu, which ends listening (`why=menu`), while the starter's
talk went on. `mp_listen_map` (on): the keys pak adds the mod's own exclusive map, `kcd2mp_listen` (the game's dialogue priority,
the four listen actions only, nothing included), right after the game's dialog map; the listen actions are bound in both maps on the
game's own keys (none added), and listening enables the mod's map. The twin (`mp_listen_map off`) is the old dialog map. The bench
now builds each copy's keys pak with the build's agent before the game starts (the launcher's order); it had run a template's pak
from an older build.

**No line was ever voiced on the listener (A.5).** The listener got each line's subtitle, and its voice was refused every time
(19 of 21 at the Semin camp). Three causes, one behind the other:

1. The agent's calls for the conversation's options (op 91), the vote mark (92) and a line's voice (93) carried their op number as
   their first argument, while the pipe's call already puts it first: the DLL read the op as the first argument. 93 and 92's
   requests were refused as malformed; 91 worked only because its "arm" flag read the op as non-zero. `conv_op_frame` (on).
2. The listener's bodies (the starter's figure, the host's NPC copies on a joiner) had no speak manager: the engine makes an actor's
   speak manager the first time its own dialogue or barks need one (`C_Actor` +0x210, the slot after the getter), and these bodies
   never speak in a dialogue of the listener's machine. With `conv_voice_make` (on) the DLL asks for it the engine's way, the
   get-or-create the engine's dialogue calls, after checking at runtime that the getter and the maker read the same field.
3. The host listening to the joiner's talk had no body id for its own NPC (the agent's map holds the copies a joiner has). With
   `conv_voice_by_name` (on) the DLL finds the speaker's entity by name.

**The listener's view ending before its first shot (A.6).** Three of the pair's silence drops (d17) came 3.0-3.3 s after the view went
on, before any camera sample: the DLL measured the 3 s silence from the view's start, and a talk's first dialogue shot can come
later. `cam_first_grace` (on): until the first sample the view waits 10 s (the 3 s rule after it, as before); a later first sample
is logged (`WO207-CAM`). The twin is the old rule (`KCDMP_TEST_W207_OFF=cam_first_grace` in the game's own environment).

**One key press, two listen actions (A.7).** In the final smoke the host listened after talks of its own, and every key press
reached the mod twice: each vote was sent twice, one Down moved the vote two rows, and one Back cleared the vote and then left. The
game's own dialog map was still on beside the mod's listen map (the listen actions are bound in both: the dialog map's binding is
the `mp_listen_map` twin's). What leaves the dialog map on after a talk is not found. `mp_listen_once` (on): the same listen
action pressed again within 120 ms is one press (`WO207-LISTEN ... arrived twice`); tests and twin in the Lua suite.

**A 6-13 s freeze the first time a session asks for the save readiness (found through A).** The joiner's view of its own talk went
dark for 13.5 s, its figure stopped on the host, and the listener left for silence. The DLL's WO-189 resolvers (the save readiness
service, the scene positioning manager) ran on the game's main thread on first use: a pointer search through `CSystem` and a scan of
XGenAIModule's whole `.data`, one memory query per pointer-looking value. Measured on the pair: 6.4 s (d13, the Semin camp) and
13.7 s (d15, the bench save), once per session, on whichever machine asks first (a joiner's own talk asks, through the scene
positioning). `w189_fast` (on): the slots the research read are tried first, each verified by its object's vftable; a scan (another
build) runs on a thread of its own, reads only. The bench now fails a run whose main thread stalls 2 s or more in the 20 s after the
first readiness read. The twin (`KCDMP_TEST_W207_OFF=w189_fast` in the game's own environment, the bench only) is the old scan.

**A on the pair.** The bench plays the players' own keys in both roles; each talk's decisions cover the cases followed (the starter
picks the listener's vote), not followed, changed (the vote moved), cleared (Back clears the vote; the listener stays) and leave
(Back leaves). Vote times are from the listener's key to the icon in the starter's own options.

| run | starter | cases | vote icon (ms) | lines to the listener | voiced there | silence drops |
|---|---|---|---|---:|---:|---:|
| d16, the bench save | host | changed, cleared, not followed, followed: all pass | 111-183 (changed 133) | 10 | 8 | 0 |
| d16, the bench save | joiner | not followed, changed, cleared, followed: all pass | 72-217 (changed 156) | 22 | 21 (by name) | 0 |
| d17, the bench save | host | the five, leave included (`why=cancel`, the starter told): all pass | 95-184 | - | - | 0 |
| d17, the Semin camp | host | all pass | - | - | - | 0 |
| d17, the Semin camp | joiner | not followed: pass; the camp's talks open at a decision and end on the pick (no line spoken) | 91 | 0 | - | 0 |
| **d18, the bench save** | **host** | **the five, leave by Back (`cancel`): all pass** | **120-158** | **9** | **8** | **0** |
| **d18, the bench save** | **joiner** | **the five, leave by Back (`cancel`): all pass** | **107-172** | **22** | **21 (by name)** | **0** |
| d19 (+ A.7), the bench save | host | the five: all pass | 110-204 | - | - | 0 |
| d19 (+ A.7), the bench save | joiner | fail: the host, still weapon-drawn from its own talks (guards alerted), took none of the listener's keys (open item 4) | - | - | - | 0 |

The twins, each the same build with one fix off, each failing its own test:

| twin | what failed |
|---|---|
| `mp_listen_map` off (d16) | cleared: the listener left for `menu` -- the pause menu opened; the joiner then sat at the pause menu (no talk, no stats) |
| `conv_op_frame` off (d17) | 22 lines in, 0 voiced (`no-line-voiced`) |
| `conv_voice_make` off (d17) | 9 lines in, 0 voiced, 8 "no speak manager" |
| `conv_voice_by_name` off (d17) | the host listening: 8 lines of its own NPC "no body known here"; the partner's figure still voiced |
| `w189_fast` off (d17) | the joiner's main thread stalled 9.3 s after its first readiness read (`w189-stall`); with it on: 0.0 s |
| `cam_first_grace` off (d18) | the listener dropped for silence in both roles, 3.0 and 3.1 s after its view went on, before any shot (`listener-dropped-silence`); with it on: none in 7 talks |

Timed decisions (A.3): the starter's decision timer is mirrored to the listener's HUD (`WO207-TIMER`, set/update/clear), but no
timed decision is reachable on the bench saves (every `TimeLimit` dialogue there is quest-bound); unproven on the pair.

The bench's own faults found here (each fixed): a double Back with no vote left the talk and opened the pause menu (the maintainer
saw the joiner's copy paused); a talk the listener had left ran on into the next step, its keys went to it and the bailiff fought
the host; the game deleted the host copy's seed save at its 100-save limit; the joiner's lone "earlier" line was counted as unvoiced.


## B: the queue that never drained

**What the queue held (B.1).** Every frame now carries its kind through its handler (`Wo207Trace`): wait in the queue, handler time,
its Lua calls (queued and answered) and its pipe calls (gate wait and round trip), printed every 10 s (`MP-W207-QK`) and the queue
once a second (`MP-W207-Q`). On the joiner in a fight (main's processing): the activity rows and extras (0x6B, 0x6F) are native
writes through the pipe, 80-100 ms each, 1,400 of them; combat actions 55 ms each, mostly waiting at the pipe's gate; ~53
background polls a second filled the pipe (one request in flight, the DLL answers about one a frame); and the processor awaited the
console whenever its batch passed ~1,900 characters.

**The fixes (B.2, B.3), each its own switch, on:**

* `mp_queue_drop_stale`: a combat action already older than its use is dropped at dequeue, before any handler (counted per kind).
* `mp_queue_decouple`: the console's batches go from their own flusher (nothing waits for the game's console), a frame's pipe call
  goes before the background polls at the gate, and the activity frames (0x6B, 0x6F) run in their own lane.
* `mp_queue_measure` (B.4): the host's "34 s behind" through a join hold was the measure -- lag against every frame read, while the
  processor sat idle and only pings and leash frames arrived. Lag is now 0 when nothing waits, and the queue's count is taken before
  the write (a frame taken at once had left a kind counted for the whole session). The host: 0 through the hold and after it.
* The measure also counts what the processor hands on: `lua_ms` and `pipe_ms` (the oldest frame-driven console entry and pipe call
  waiting now); the bench judges the worst of the three. During a load the console's queue holds what the game cannot take yet (up
  to 46 s); it drains before the join completes.

**B.5 on the pair (c1fight, the joiner's game in front):**

| run | after the join (60 s) p99 | the fight, p99 per minute | after the fight (60 s) p99 | combat actions dropped stale |
|---|---:|---|---:|---:|
| main (first baseline; the mouse button stuck down) | 5.2 s | 0, 23, 77, 106, 127, 145 s | 139 s | - |
| main (again, the input fixed) | 5.7 s | 5.2, 1.4, 2.0, 0.6 s | 1.3 s | - |
| this build | **16 ms** | 0.05-0.49 s (worst sample 0.89 s) | **63 ms** | **0** |
| twin (B.2, B.3 and the measure off) | **5.0 s (fails)** | | 0.8 s | 0 |

What still waits: the activity lane's own frames (0x6F avg 139 ms, max 2.5 s) -- looks, gaits and hands, never in the processor's way.

## C: after a shared sleep or wait

**C.2, the background copy kept awake.** The game slows an unfocused window (`sys_SleepIfInactiveWH 1`, 20-34 fps; the agent's
queue fell 18-90 s behind). `mp_keep_awake` (on; the launcher's "keep the game running in the background" box): the agent sets it
to 0 for the session and the mod hands the player's own value back 20 s after the agent goes quiet; nothing is written to disk.

| run (the mirrors' own value 1) | the joiner's game in the background | the cvar | written anywhere? |
|---|---:|---|---|
| d17, keep-awake on | 80 fps (median of 4 reads) | 0 on the host; the bench's read of the joiner's came back empty once (now asked again) | no: both mirrors' system.cfg still 1, no user-folder file names it |
| d17, twin (off) | **23.5 fps (fails)** | 1 / 1 | - |
| **d18 (this build), keep-awake on** | **81.4 fps** | **0 / 0** | no |

**C.1 and C.3, the clocks after a skip.** 20 skips per build (sleep 9 h and wait 3 h, alternating), the game-time gap between the
host and the joiner 1, 10 and 60 s after the skip ends (game seconds):

| run | the background copy | +1 s (median / worst) | +10 s (median / worst) | +60 s worst | failed |
|---|---|---|---|---:|---|
| d17 | sleeping (cvar 1) | 4 / 632 | 4 / 45 | 4 | 1 (the host was never asked) |
| d17 | awake | 4.5 / 3,420 | 5.5 / 758 | 13 | 1 (+10 s: 758) |
| d17, twin (`skip_end_exact` off) | awake | 7 / 977 | 4 / 45 | 10 | 1 (the host was never asked) |
| main | either | the bench's skip flow never got the joiner's Yes on main (every skip `no-yes`); main's earlier smoke: 396 game-s apart at +10 s after a 9 h sleep | | | |

C.3 is **not proven**: `mp_skip_end_exact` (the skip ends on the host's clock at once) and its twin give the same gaps. Most skips
land within a few game-seconds; the first skips of a session and some 9 h sleeps are tens to hundreds of game-seconds apart at
+1 s and +10 s, with or without it, and all agree by +60 s. Open.

**C.4, a link cut during a rejoin.** The relay cuts the joiner's link at the second world transfer of an in-world rejoin (the host
reloads its world with the joiner in it). Before: the joiner's relay-lost leave raced its own join load and it stayed in its own
world. `rejoin_recover` (on): a relay drop during a join arms an in-world rejoin; once the link is back and the leave's own load has
landed (read from the game when its "Gameplay started" fell in the outage), the joiner asks for the host's world again.

| run | the cut | back in |
|---|---|---|
| d15 | at the second world chunk | yes, 186 s after the reload |
| d17 | at the second world chunk | yes, 86 s after the reload |
| d17, twin (`rejoin_recover` off) | at the second world chunk | **no, 240 s (fails)** |

**The tavern (c2tavern).** 21:00 of the next day, 10 minutes. d17: pass -- the clocks a minute apart at most (23:56 / 23:57), the
joiner's queue p99 16 ms. Main: the joiner's queue **346 s** behind (p50) at the end, the clocks **45 game-minutes** apart.


## D: the divergences WO-206's soak named

* **D.1, the host-only fog timers.** The fog is two `TimeOfDayWatch` nodes playing ping-pong (05:00, 07:00) that pulse four
  `FogStreaming` instances; a `DistanceTrigger` and the pulse set each instance's `streamedFog` State, whose layer turns a fog
  profile on. The joiner's clock pulses are quiet by design (WO-200: the host's clock decides), so its watches rest after their first
  pulse. Read on the pair at 05:00 and 07:00: the host's States went Streamed and back while the joiner's stayed Unstreamed -- but
  the fog itself, the four `fogControl_*` game profiles, reached the joiner through the profile mirror (WO-189) 0.6-2.5 s after the
  host both ways. Known-local with that evidence: `tools/digest/analyze.py` names the fogControl clocks (clock values only) as
  known-local, with the reason in its report; on WO-206's 30-minute soak it explains the two fog findings and leaves the State ones.
  Test + twin (the rule off: unexplained) + narrowness (a fog State, another module's clock: still unexplained).
* **D.2, kovar / zaby.** `KovarJeVKovarne` and `henik_hlida_majku.state27` are written by `AreaTrigger`s on named NPC souls -- on the
  joiner those NPCs are copies of the host's, so its own triggers fired on them and wrote its own value. `mp_joiner_npc_triggers`
  (on): the agent finds every AreaTrigger bound to an NPC's soul in the quest files (185 on the bench save) and the WO-200 tap keeps
  their pulses quiet on the joiner (`WO207-QUIET`); the host's value arrives as a mirror.

* **D.3 and E (the soaks) were not run:** the maintainer, mid-run: "SKIP the soak". D.2's fix is proven offline (the trigger list,
  the quiet tap's tests) and by the digests of the pair runs, not by a soak; E's 60-minute memory curve is not measured.

## F: the grown smoke

The pair smoke now covers join, the queue after the join, walk, sleep, wait, a fight, the queue after the fight, and a joined
conversation started by each role.

| step | d17 | d18 | d19 | d29 (the crash fixes, this commit) |
|---|---|---|---|---|
| join | pass (64.6 s paused) | pass (66.0 s) | pass (72.6 s) | pass (64.1 s) |
| queue after the join, p99 | 31 ms | 16 ms | 16 ms | 16 ms |
| walk (p90 each way) | 0.08 / 0.09 m | 0.08 / 0.09 m | 0.09 / 0.09 m | 0.10 / 0.07 m |
| sleep 9 h (gap at +1 / +10 / +60 s) | pass (38 / 9 / 3) | pass (38 / 20 / 3) | **fail: 22,675 / 20,134 / 3,213 game-s** -- the joiner's clock stayed behind | logged only: 30 / 19 / 0 game-s (pass) |
| wait 3 h | pass (2 / 4 / 4) | pass (4 / 4 / 3) | pass (3 / 3 / 2) | logged only: 4 / 4 / 4 (pass) |
| fight | pass (6 verdicts) | pass (9 verdicts) | **fail: the joiner's game crashed** (CombatModule -> XGenAIModule data) | pass (11 verdicts) |
| queue after the fight, p99 | 103 ms | 187 ms | - | 531 ms |
| talk, the host starts | pass | fail: a talk whose every dialogue camera is invalid (the engine says so) gives the listener no shot | - | fail: the listener dropped for silence |
| talk, the joiner starts | fail: one line, the "earlier" one (bench check, fixed) | fail: the host's keys arrived twice (A.7, fixed in d19) | - | fail: the listener dropped for silence |
| a timed decision | fail: none reachable on the bench save | fail: the same | - | pass (the David/Michal talk) |
| loot | fail: the bench picked a buried stash (fixed) | fail: the bench's new pick found none (fixed) | - | fail: the take not seen (Stash19) |
| digest | fail: 45 short-lived quest differences | fail: 16 | - | fail: 72 unexplained quest differences |

**The release gate does not pass on d29.** No game crashed (both fight crash groups are fixed, H). Join, walk, sleep, wait, the
fight, both queues and the timed talk pass; the sleep and wait clock gaps are logged, never a gate (the maintainer: one copy is
always the inactive window, a bench artefact). The talks started by each role (the listener dropped for silence), loot (the take not
seen) and the digest fail, as on earlier builds.

## G: the animation detectors

* **G.1, calibration.** The probe now records each queued action's flags, age and priority (`qd`). "Stuck" is judged only when a
  queued action outranks the running full-body action (or none runs) for more than 2 s on a living, non-idle body; flag 4 and an
  action queued below the running one are normal waiting. On the final candidate's fight (d17 smoke) the host's own NPCs: 1 finding
  on 1 body (main's rule judged none: its probe had no queue age). Tests + twins in `Wo206AnimTests` (the first probe's queue age
  never judged; an action queued below the running one is not stuck).
* **G.2, the measure.** "The copy runs a different full-body action" now compares action families within 300 ms of the stream
  delay (idle variations and standing are one family; locomotion by speed band). d17's smoke fight: **9.8 %** (main's fight: 27.6 %;
  WO-206's exact class+fragment: 70.3 % here, 65.3 % on main), position p95 **0.13 m** (main 20.3 m), stream delay 418 ms, weapon
  drawn 0 %.
* **Crashes.** See H: both fight crash groups were the mod's own and are fixed.


## H: the fight crashes (the crash work)

Every dump kept (the host 18, the joiner 10) read with the bench's own minidump reader and the game's DLLs on disk; the crash report
now names the entities the faulting registers lead to. Grouped by cause:

**1. The host, `RPGModule+0x64afe4` (18 dumps, AI worker thread, reading 0x28) -- the mod's own, fixed.** The function is the target
follower's step (XGenAI `NPC:Update:TargetFollower` -> RPGModule). It reads a bandit's requested target (its combat actor's vt+0x50:
the model's target wrapper at +0x1110) with no null test; the wrapper is null. The soul is a **bandit** fighting the joiner's figure,
never the figure (the report names tapo_kasparBandit_2/3/6). How it got there: a bench-only probe on the engine's
`C_Skirmish::RemoveSoul` (`KCDMP_TEST_SKIRMISH_PROBE=1`, the caller's stack unwound) shows the figure taken out of the bandits'
skirmish 49-76 times per session by **WO-136's `leave_and_add`**: to change a fighting soul's opponent it calls
`RemoveSoulFromSkirmish` then `AddSoulToSkirmish` -- on every joiner swing at a bandit that is not the figure's opponent (191-198
swings per fight), and again from its pending queue every 0.6 s (up to 3 redos), and on each bandit at the host's hand-over. Each
removal clears the target of every bandit fighting that soul; the re-add gives the skirmish a target before the bandit's combat actor
has one, and the AI worker reads the missing one. The crash lands **8 ms** (d25) and **15 ms** (d24) after a WO-136 redo removed the
figure (`KCDMP+0x84db6 <- +0x80724`, wo136 tick). Main's build crashes the same way (it predates this WO).

The fix: **`mp_skirmish_keep`** (on): a soul already in a skirmish is never taken out of it to change its opponent -- the add alone;
a fighting NPC still turns by its forced target (`combat_forcedTarget`, which WO-136 sets first). The DLL holds it (op 15 of 0x22);
the twin is `KCDMP_TEST_W207_OFF=skirmish_keep` in the game's environment. The figure's removals per session: 49-76 before, 6 with the
fix (the joiner's own deaths, WO-132). The fight is unchanged: the host applied 25 of the joiner's blows in the fix's first fight
(18 before), 9-35 per fight over the fix's runs.

**2. The joiner, `CombatModule+0x55b065` / `+0x559d32` / `+0x8ff0a` (7 dumps, one the twin's) and `XGenAIModule+0x25542c0` via `+0x8ff0d` (1)
-- the mod's own, fixed.** The local player's dodge or perfect-block multi-trigger (owner `C_CombatPlayer`): a copy's slot planner
(its own update, `C_CombatActorSlotPlanner` vt+0x38) creates a sub-trigger on the player's multi-trigger (`0x8fdd0`: push {attacker,
trigger}, set up its window, then Start, vt+0x60); the player's own update cleans that multi-trigger (`0x576090`: every sub-trigger
not yet started is stopped and deleted). The sub-trigger is freed between its creation and its Start (the pair zeroed in the dump,
the trigger's memory reused). No mod frame on any stack, and the mod's main-thread tick never runs during the entity jobs (in 605 ms
with a worker creating a trigger the main thread logged nothing). Vanilla has the same two paths on different threads (the host
player: 40 of 128 creations had a cleanup on another thread within 2 ms) and never crashed this way: its attackers are always in
the player's skirmish. The copies here were not: the attacker recovered from the dumps (its combat actor, saved by the creator's
frame) was a copy that WO-132 had released from the player's skirmish 28 s (d26) and 2.5 min (the d26 proof) before -- yet WO-178's
copy engine state still ran its wind-up with the player as its opponent (WO-187 writes the wished opponent back) and planned the
player's windows with PlanSlot. A bench-only probe on the creation and on two trigger destructors (`KCDMP_TEST_TRIGGER_PROBE=1`)
showed every deletion going through the player's own multi-trigger update, never through the mod.

The fix: **`mp_plan_in_fight`** (on): a copy plans the local player's defence windows only while the engine has the copy and the
player in a skirmish fight (the skirmish manager's own relation test, read-only, that frame; unanswered plans nothing). A copy in the
fight still gets its three windows (9 wind-ups planned 3 slots in the proof's first minutes); one the joiner's world let go of gets
none. The DLL holds it (op 16 of 0x22); the twin is `KCDMP_TEST_W207_OFF=plan_in_fight`.

**3. Single samples, not reproduced:** the joiner `XGenAIModule+0x104e0cf` once during a talk step after 40 copies re-bound (d13);
main's build, the joiner executing 0x0 in a CryAction worker once in the tavern.

**The 30 s re-set is not needed:** with the bandits set on once and no teleport (x-d19-noreset) the host crashed in the second fight
the same way. The trigger is the joiner swinging at bandits -- what players do.

### The bisect (c1fight on the pair, one pair session per row, fights of 5 minutes until a game exits)

Each row is one pair session: c1fight back to back (the host and the joiner against three bandits for 5 minutes, the joiner swinging,
the bandits set on the joiner's figure every 30 s unless said) until a game exits. "host" / "joiner" = the groups above.

| run | what is off / on | fights | crash |
|---|---|---|---|
| this WO's final build | nothing | 1 clean | host in 2 |
| the same, no 30 s re-set, no teleport | one provoke per fight | 1 (the joiner not in the fight) | host in 2 |
| main (0.63.0, before this WO) | -- | 1 clean | host in 2 |
| every WO-207 switch off (agent and DLL: the camera poll off the main thread, conv_pipe_first, queue_decouple, queue_drop_stale, w189_fast, conv_voice_make, conv_voice_by_name, joiner_npc_triggers, the rest) | | 1 clean | host in 2 |
| a figure-target gate (tried, removed) | on / off | 2 clean / 1 clean | joiner in 3 / host in 2 |
| mp_copy_engine_state off | | 0 | host in 1 |
| the figure guard off, combat automation on | | 0 | joiner in 1 |
| automation on + copy engine state off | | 0 | host in 1 |
| WO-121 avatar combat off | | 0 | host in 1 |
| the figure's AI paused every 0.5 s | | 1 clean | host in 2 |
| the attack message only to bandits not fighting | | 1 (bench) | host in 2 |
| every bandit set on the host only, the joiner beside, swinging | | 0 | host in 1 |
| the crash report names the registers' entities | | 1 clean | host in 2: the soul is a bandit |
| the skirmish probe (stack scan / unwound) | | 1 clean each | host in 2, 15 ms / 8 ms after a WO-136 removal of the figure |
| **mp_skirmish_keep on** (the host fix) | | 1 clean | **joiner** in 2 (no host crash) |
| mp_skirmish_keep **off** (its twin) | | 1 clean | **host in 2** (76 figure removals) |
| the host fix + trigger probes | | 10, no crash (7 pass, 3 bench faults) | -- |
| the host fix + copy engine state **off** | | **10 of 10, no crash** | -- |
| the host fix only (copy engine state on) | | 5 bench faults (the joiner window refused the foreground), 0 clean | **joiner in 6**: the attacker a copy released 2.5 min before |
| **both fixes** (the final tree) | | **10 of 10, no crash** | -- |
| mp_plan_in_fight **off** (its twin), mp_skirmish_keep on | | 8 clean, the 9th ended by an outside stop | none in 8 |
| the same twin again, up to 20 fights | | 1 clean | **joiner in 2**: the attacker a copy released 4.3 min before |

### The proof

* **Both fixes, 10 bench fights in a row on the pair: 10 of 10 pass, no crash on either side.** The joiner swung 186-202 times per
  fight, the host applied 7-43 of its blows per fight (verdicts 20-45), the joiner's copies fought every fight; the real saves' hash
  list matched before and after.
* **The host fix's twin** (`mp_skirmish_keep` off, the same build otherwise) crashed the host in the second fight, the same
  `RPGModule+0x64afe4` on a bandit.
* **The joiner fix's twin** (`mp_plan_in_fight` off) ran 8 fights clean, then, run again for up to 20, **crashed the joiner in the
  second fight**: `CombatModule+0x8ff0a`, the player's perfect-block multi-trigger, the attacker (from the dump) a copy WO-132 had
  released from the player's skirmish 4.3 minutes before -- the same shape as the two crashes before the fix. The race is rare
  (3 joiner crashes in 28 fights with copies planning out of a fight, none in the fix's 10).

## Open items

1. Both fight crash groups are fixed and proven, each with a twin that crashes again (H).
2. **The shared sleep's clocks (C.3)**: not proven; the twin gives the same gaps. The maintainer: the gap is a bench artefact (one copy is
   always the inactive window) -- logged in the smoke, never a pass/fail.
3. **A talk with no valid dialogue camera** gives the listener no shot (the view ends after 10 s): the sampler only sends dialogue
   shots.
4. **What leaves the game's dialog map on after the host's own talks** (A.7 guards the doubled keys; the cause is not found), and the
   host's weapon drawn between bench steps (guards react to it).
5. **Timed decisions** (A.3): mirrored, not reachable on the bench saves.
6. **The digest's long-session differences** (village behaviours, 15-minute clock edges): short-lived, not explained.
7. **D.3 and E** (the soaks) not run, on the maintainer's word.
8. Two single crashes not reproduced: the joiner in XGenAIModule during a talk step (d13), and main's build executing 0x0 in a
   CryAction worker in the tavern.

