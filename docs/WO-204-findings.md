# WO-204 — Sleep and wait stop crashing, copies' bodies, and the conversation feature (0.62.7)

> **Actions go to the authority; values come back.** The host's world decides. The joiner's game shows, and asks; it never decides.

Session D (0.62.5, a host and a joiner) crashed the host after a sleep and again after a wait, showed the joiner's copies of the host's
NPCs breaking in new ways (T-poses, swings that never animate, sliding while seated), and showed the conversation feature half there.

All live evidence below is one machine: the Modding Tools game as the host of a synthetic joiner, or the joiner of a synthetic host,
through a local relay, on the `playline4` throwaway (copies of saves only; the saves folder was backed up byte-equal first). Nothing here
is two-player evidence; `docs/WO-204-tester-script.md` is the script for the next two-player session.

Every new mechanism has its own switch, all on; `mp_w204_status` prints them with their counts.

## Act I — sleep and wait never crash

**The cause (both crash dumps).** Both host crashes read a null at the same instruction, 0x39 bytes into the engine's
`C_DistanceTrigger::Evaluate` (XGenAIModule), on an AI worker thread. Evaluate had just read the node's origin and then read its
target: **0**. The node in both dumps had its active flag 0, its origin set to the host's Henry's object, and its target, compare
kind and threshold all 0.

The engine registers a distance trigger as a move listener on both of its objects when it activates. Deactivation unregisters the
node from the two objects then in its fields and clears both fields. The WO-201 "nearest player" design wrote the partner's figure into
the node's player field for one evaluation and wrote Henry back after it. Inside that evaluation the engine's own result setter fires
the quest's flow, and that flow deactivated the node. So the deactivation unregistered the **figure**, not Henry, and cleared the
fields, and the restore then wrote Henry back into the dead node. Henry kept a move listener on a node with no target. His first move
after the time skip evaluated it on a worker and read the null target. The swap ran 11 s before each crash (once in the waiting
session, which crashed 2 s after its wait). This was the "restore writing a stale pointer into a node that the engine had torn down",
not a race with a worker and not the forced re-evaluation.

**The rebuild (`nearest_player_v2`, switch `mp_nearest_player_triggers`).** The node swap and the forced re-evaluations are deleted.
The detour on Evaluate now only **reads**: the node's two objects and their positions (the same calls the engine makes, on the thread
the engine evaluates on). When the node is bound to Henry and the partner's figure is nearer to its other end, it computes the
engine's own compare from the figure's distance and calls the engine's own result setter; otherwise the engine's Evaluate runs
unchanged. The figure's position comes from the mod's own main-thread snapshot (never its object, off the main thread). The engine
code's shape is checked at start (the whole function, displacements masked; the six compares; the result setter); a build that
differs keeps the triggers the host's Henry's.

Quiet windows: from a time skip's start until 5 s after it ends, after a load or a new player soul, and while a join or a load runs,
the engine's own Evaluate answers alone.

| S1 (the host, a synthetic partner circling Henry at 3 m) | result |
|---|---|
| time skips with the override on | **60** (30 shared sleeps, 30 waits, 9 h / 3 h, alternating) |
| crashes | **0** (the game up at the end, no crash report) |
| evaluations of player-bound nodes / overrides | 262 / **94**, all 94 on the engine's AI worker threads |

Per the WO the switch shipped off until S1 passed; it passed, so it ships **on**. Not tested on this machine: an escort that only the
partner keeps up with (no escort save), so that check is in the tester script.

## Act II — the copies' bodies

**What the engine does** (static, CryAction 1.5.5): a body's queued actions live in its action controller at +0x50/+0x58. More than
10 queued actions runs the engine's own trim, which drops the newest ones it may drop from the back. That trim prints the "attempting
to queue too many actions" line. Session D's ttkc_man_7 held 8 actions that never started, and every later swing of his waited behind
them.

**The single owner (II.1 / II.2's rules, the streamed state) is not built.** The WO makes a probe with a recorded host state, and a
copy matching it for 5 minutes with zero overflows and T-poses, the gate before the owner replaces eight writers. That probe was not
run tonight, and building the owner without it would be guessing. What was built, each measurable on its own:

| | What | Switch |
|---|---|---|
| II.2 | **The T-pose detector.** A copy or a figure whose full-body scope has stood empty for more than 0.3 s is logged once per stretch (`WO204-TPOSE`: what it last ran, its queue length, the queue's first action) and counted. Its base state is put back at most once a second: a fighting body's guard idle, any other body's locomotion. | `mp_copy_tpose_watch` |
| II.3 | **Seated copies have one authority.** The position stream never moves a copy that is itself seated, lying, kneeling or aligned at an object, whatever the host's row wants at that moment. Before, the hold followed only the host's wanted activity, so a refused or backed-off placement left a seated copy to the stream. `WO204-SEATMOVE` logs a seated copy that moved more than 5 cm. | `mp_copy_seat_hold` |
| II.3 | **The weapon-draw delay is measured.** Copies already take the host's draw and holster on the stream's weapon bit; each one now logs how long after the host's own change it was applied (`WO204-DRAW`, on the host's clock as the stream carries it), and the briefing shows its p90. | (a measure) |
| IV.5 | **A copy stuck in a state the engine cannot leave is released.** 5 failed state searches in a minute: its stance and unstance are reset the game's own way, at most every 2 minutes. | `mp_copy_unstick` |

## Act III — join the conversation

**III.1 Combat barks are never conversations** (`mp_conv_no_barks`). The engine's own dialogue line names each dialogue's kind and
origin. A real talk is kind `normal`, started by the player (`Actor::RequestDialog`), a forced scene (`ForceDialogNode`), a haggle, or
an NPC opening a full dialogue. The hit barks and war cries are kind `ingame` from `CombatShout`; greetings are `chat`. The positive
list takes only the first set and never a bark family (`boj_a_zraneni`, `combat`, `horse`, `crime_reaction_barks`, the monologues).
Replayed over session D's own logs: the host announced **422** conversations before and **5** with the filter, and those 5 are the
real ones (tavern talks, a shop, the duel's forced dialogue); the joiner's log goes from 93 to 5. No offer, no announce and no gestures
for a bark.

**III.2 Removed:** the F-key vote panel (`mp_dialogue_vote`, its F2/F4/F5/F6 handling) and the overheard broadcast with its raw-key
subtitles (`mp_speech_share`). The line capture stays; a 0.62.5 partner's old messages are counted and dropped.

**III.3 / III.4 The listener's lines, options and vote** (`mp_conv_lines`, `mp_conv_voice`, `mp_conv_options`), on WO-203B's session:
* each line the starter's engine voices goes to the listener: the subtitle through the HUD as `@<StringName>`, localized in his own
  language, and the voice on the speaker's body through its own speak manager. A listener who joins late gets the lines so far,
  0.8 s each, marked "(earlier)". Every one of session D's 1,205 voice files is its loc key with a speaker prefix, so the key is exact.
* the starter's options as their HUD showed them (copied as they go to the screen; the game's own rows are never written), in the same
  order with the same marks; the listener moves with their own dialogue keys and votes with their confirm key, through the mod's own
  `kcd2mp_listen_up / _down / _confirm` actions, which live in the game's dialog map inside the game's own Up, Down and confirm
  superactions. A vote shows an icon in the voted row, on their screen and on the starter's (the copied rows re-sent with the icon, the
  highlight put back); voting again moves it, Back clears it, and a second Back leaves. The choice shows as "Your pick" or "Their pick".
* not done: the decision timer is not mirrored to the listener.

| V1 (one machine, a synthetic partner) | result |
|---|---|
| the game as the **listener** (the starter replays a recorded conversation with Barbora: her real lines, options, choice) | joined, the line shown as the game's own localized subtitle, the 4 rows as the starter's HUD had them, moved down-down / voted / up / voted again: both votes reached the starter (row 2, then row 1), the icon in the voted row, the choice shown; the HUD came back when the starter left |
| the game as the **starter** (talking to Barbora, the partner auto-joins and votes row 1 at every decision) | the snapshot, 17 lines, 5 option sets and 4 choices reached the listener (the 5th decision was still open when the run ended); their 5 votes showed as 5 icons in this player's own rows; the icon cleared when they left |

Found and fixed on the live run (each was 0 before): the starter's lines never went out without a partner figure in the world (the
capture's poll still waited for the retired broadcast's figure); the choice never went out (the log tail hands the engine's line over
with a trailing character, so an exact end match failed); a partner's icon stayed in the starter's rows after the partner left.
The listener's own body in the shot shows without its head: III.7, open.

**III.5 Trialogs** (`mp_conv_trialog_roles`): the dialogue's own file in the game's Scripts.pak names its roles and the souls it binds
(`<SelectedSoul Role=".." Soul=".."/>`); with the soul order the engine logs, each role's gestures go to the right body.

**III.6** (forced conversations from the joiner's own trigger) and **III.7** (the listener's own head in a shot) are not done.

## Act IV — joins, combat, the session D marks

| | Finding | Fix |
|---|---|---|
| **IV.1** | 0.62.5 let the host's world run again once the joiner had the file. | Every join pauses the world until the joiner says ready. WO-202's short hold and its switch are deleted. The joiner's agent reports its load every 5 s; 60 s with no message at all from them aborts the join and the world runs again. J1: 3 of 3 synthetic joins held the host's world for the whole load (22 s each, the world clock still) and resumed on ready; a joiner that never says ready was aborted after 60 s of silence and the world ran again. |
| **IV.2** | The joiner's defence judged by their own engine. | Not built (WO-203 II.2's design). The briefing's row stays n/a. |
| **IV.3** | The "not taking full damage until seconds after" bandit: the joiner's blows arrived saying stamina only, and the host's engine re-judged them as hits. | A blow the joiner's own engine judged blocked (stamina only) is applied as their engine judged it (`mp_blow_joiner_values`). Every joiner blow on the host is logged (`WO204-BLOW`), and a blow that came right after a stall of their link (the previous position over 1 s before) is marked late and counted. Why the joiner's outbound stalled is not found: their game's own queue was 1-30 s behind while it was in the background. |
| **IV.4** | The bandit "with no weapon, not attacking, at 0 health". | **Not the mod's, and not 0 health.** The `hp=0.0` in the logs is each blow's health damage. The host's own value stream read the bandit's health at 100 and stamina at 153 the whole time: the blows hit the bandit's pavise shield (7 perfect blocks), and the skirmish's weapon-change events show the bandit switching weapons. The game's own fight. |
| **IV.5** | "Invalid anim ref" (4,391) and `fa_idle_sleep_*` (5,752). | All of them are facial-expression clips (`fa_*`, scope FacialExpression) that the male and female databases name and the skeleton lacks, on every body. They scale with the session's length and the number of faces; nothing here ties them to a mod path, but no vanilla run was made to compare. |
| **IV.5** | ttkc_marusa's 2,575 failed state searches. | **The mod's**, on the joiner only (0 on the host): her copy was stuck in a milking pose holding a bucket, and every mirrored activity (a bench, a bowl) was unreachable from there. Fixed by the release above. |

## Act VI — carry-overs

* **Tutorial pop-ups on the joiner.** WO-200's gate refused the joiner's own tutorials ("encumbered", "broken armour", "several
  opponents") as the host's story. A node from the game's tutorial flow is now the player's own and runs.

## Act V — the briefing tells the truth

* Only this launch's session: an earlier agent log counts only when it was still being written within 30 minutes before this launch
  began. Session D's host briefing was headed by a mark from two days before; it is gone.
* Both players' notes: a mark's note now travels with its ping (a new W164 kind, append-only), and the partner's briefing shows it.
* New scorecard rows: queue overflows on bodies (by body), T-poses, the weapon-draw delay, the joiner's blows judged by his engine,
  blows that arrived late, and conversations announced (with the barks kept out). The network ping and the game's queue are separate
  lines, and a backgrounded game's queue has its own.

## Switches

`mp_nearest_player_triggers` (rebuilt, on), `mp_conv_no_barks`, `mp_conv_lines`, `mp_conv_voice`, `mp_conv_options`,
`mp_conv_trialog_roles`, `mp_copy_unstick`, `mp_copy_seat_hold`, `mp_copy_tpose_watch`, `mp_blow_joiner_values`, all on.
Removed: `mp_dialogue_vote`, `mp_speech_share`, `mp_join_short_hold`. `mp_near_safe` (WO-202) no longer does anything: the rebuilt
override never writes, so it has nothing to guard.
