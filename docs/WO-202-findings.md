# WO-202 — Crashes, and quests that fail without the players doing anything

> **Actions go to the authority; values come back.** The host's world decides. The joiner's game shows, and asks; it never decides.

Two field sessions on 0.61.9 (session A: a host and a joiner, an evening of bandit camps; session B: Semine's camp and the hired hand)
left three kinds of trouble: crashes with no evidence at all, a quest that failed while the players stood by, and fights the joiner saw
out of step. This WO makes every crash leave its evidence, fixes what the field's logs could prove at its cause, and measures desync as
one scorecard from now on.

All live evidence is one machine: the Modding Tools game as the host of a synthetic joiner, or the joiner of a synthetic host, through a
local relay, on the `playline4` throwaway (copies of the field host's own saves). The saves folder was backed up byte-equal first and
checked byte-equal at the end. Nothing here is two-player evidence; the tester script in `TWO-PLAYER-CHECKLIST.md` (411-421) is.

Every new mechanism has its own switch, all on; `mp_w202_status` prints them all with their counts.

## 1. Crashes (Act I)

**I.1 Every crash leaves its evidence** (`mp_crash_capture`). The DLL installs an unhandled-exception filter chained to the game's own
(never replacing it; the game's crash reporter blocks `SetUnhandledExceptionFilter`, so the filter sits on the system's own
`UnhandledExceptionFilter`, its prologue checked first). On a crash it writes, beside the game:

* `kcdmp-crashes\crash-<date>-<time>-<pid>\crash.txt` -- the exception, the faulting module and offset, the thread (main or not), the
  mod's hooks on the stack, the DLL's last 200 log lines;
* `crash.dmp` -- a minidump with thread and module data;
* `agent-statements.txt` -- the agent's last 50 Lua statements (the agent adds them when the game process ends with a crash code; a
  fast-fail or a stack overflow the filter cannot catch still gets this file, in a folder of its own).

The log bundle collects that folder, the game's own crash files and the Windows Error Reporting entries for the game from the last
24 hours, and its `BRIEFING.md` (at the top of every bundle now) puts the crash first. Acceptance: two deliberate crashes in a research
build (one on the main thread, one on a worker) each produced the report and the dump.

**I.2 The four field crash contexts.** The field left no dump, no fault line and no engine report: the logs just stop. What could be
found and fixed:

| context | what the logs show | the fix | runs here |
|---|---|---|---|
| the host, 24-26 s after a quicksave (session A, twice; three host crashes in 20 minutes) | Every crashed host launch had the WO-201 "nearest player" substitution running **off the main thread** (about 45 % of its evaluations), with a figure object read once a second and node pointers kept up to 5 s. | The substitution runs only on the main thread, inside the engine's own evaluation, with the figure looked up in that call and used only once it has been the same object for 2 s; no stored node is woken. | 20 quicksave rounds with it off, 20 with it on, then a 31-minute host session with the synthetic partner (42 rounds, 43 saves): no crash, no fault line. The tavern scenario evaluates on the main thread only, so it cannot prove the off-thread case either way. |
| the figure rebuilt in a skirmish | not reproduced | -- | open |
| the joiner, after his death | not reproduced | -- | the joiner's own death and respawn had no scenario; the nearest ones run here -- the joiner flying over 77 of the host's copies at 150 m/s (3 runs x 3 legs), dead copies with the host's body lists (6 runs) -- never crashed |
| the joiner, at the leash countdown | not reproduced | -- | open |

The capture is the real fix for the rest: the next crash of any kind names itself.

## 2. Quests that fail on their own (Act II)

**The hired hand (session B)** failed by **its own timer**, not by a frozen NPC. Semine sent the players after him at 19:08:13; the
third join held the host's world 19:09:30-19:11:07; the timer ran out at 19:15:52 -- 459 s in all, 362 s of it outside the hold: the
game's own six-minute timer of running time (the hold paused it). The hand stood at his hiding spot with his wounded leg, the players
stayed at the camp, the host ran toward him about 30 s before the end, and the hand moved off again when he came near. The join's NPC
pause did leave the engine's "Node status inconsistency" error on his behaviour tree -- removed below -- but he was not frozen by it.

| | What it was | The fix | Switch |
|---|---|---|---|
| **II.1** | A join suspended the brains of the NPCs within 120 m (the hand among them): the engine's tree errors on resume. | No NPC is suspended at a join; the clock and the engine's own hold keep the world still. | `mp_join_no_npc_suspend` |
| **II.2** | A join held the host's world through the joiner's whole load: 96-98 s each in session B. | The world is held only while the save is written and sent, then runs again; the joiner loads into a moving world and catches up the existing ways. Q2: **1.8-2.0 s** held (62 s before). The first run found the early release cancelling the joiner's join (its own "resumed" message read as a cancel) -- fixed; the join then completes with the joiner's ready. | `mp_join_short_hold` |
| **II.3** | "Dead but walking away" (session A's host: 4 marks; its joiner: "dead for me, not for him"). Not a death the mod made up: every one died the engine's way, from a joiner blow the engine took whose **death reaction was refused** -- the victim could not react (fleeing) or no reaction was found. The soul died; the body kept its living physics and its last motion (bodies moved 5.9 and 8.2 m after death in the host's own stream; never more than 0.4 m for any host kill, NPC kill, or joiner kill with a queued reaction). | A joiner blow without a queued reaction is followed by one read of the body; a body the engine reads dead and still upright is laid down by the **game's own ragdoll** (WO-195 did the same for the joiner's copies). D1 (the synthetic joiner kills a villager who flees after the first blow): before, 2 of 2 dead and upright with living physics; after, **4 of 4 down**. Also: a death goes out only when the engine's soul reads dead -- the DLL's death announcement was a health reading (`hp <= 0`) and is now gated on the soul's own `IsDead`; and a reload clears the observer's "already announced" marks (the same NPC killed again after a load was never announced). | `mp_death_engine_only` |
| **II.4** | The next field failure must name its cause. | Every quest step on the host that fails, aborts, cancels or sets a "player bailed" value logs `WO202-QUESTFAIL` with the node whose output caused it (a timer, a watch, a trigger, a behaviour, a conversation), how long it had run, where both players were, and whether a join, rejoin, reload or pause fell inside; the briefing lists each in plain words. | `mp_quest_fail_trace` |

Two of the WO's premises were the logs' columns, not the game: the "health back from 0, 26 times" is the stamina column of the host's
value stream (by health: 2 rises, both across a save load), and the "26 raw health writes" are the engine's own `TakeDamage` with the
attacker set (the fallback when no swing window or row exists).

**II.5** (area triggers counting both players) is not built. The quests' area triggers are `C_AreaTrigger`, `C_AreaTriggerAnonymous`
and `C_AreaLabelTrigger` (RPGModule); their activation (the vtable slot that traces "no soul(s) or area(s) on input") resolves each
input soul to its NPC ("no NPC found for soul '%s'") and watches those NPCs' presence. The partner's figure would be added there when
the player's soul is among them -- the next WO's step, with a live area to prove it. No field failure was an area's.

## 3. Desync, part 1 (Act III)

| | What it was | The fix | Switch |
|---|---|---|---|
| **III.1** | The joiner's pings ran 35-107 s, draining in shrinking bursts: the agent's single frame queue waited on the game. | The network never waits on the game: what goes to the game waits in its own bounded queue, a newer position or state replacing an older one of the same body. | `mp_net_game_queue` |
| **III.2** | The ping went through the game loop; the toast blamed the network when the game was in the background. | The ping is answered and measured by the agent; the toast says "your game is in the background" when it is. | `mp_ping_network` |
| **III.3** | A late rejoin: the host aborted after 20 s; the joiner stayed "reloading" forever and was sent to the main menu. | The host's join waits while the joiner shows progress and aborts only after 60 s of silence; a failed rejoin clears the joiner's states and tries again by itself (3 times, 20/40/80 s); the main menu only after "The host's reload didn't reach you." (F11 try again, F12 leave). | `mp_rejoin_recover` |

## 4. Desync, part 2 (Act IV)

| | What it was | The fix | Switch |
|---|---|---|---|
| **IV.1** | 53 of 182 host attacks never animated on the joiner's copies. All 53 timed out queued; 29 came 0.6-1.5 s after the copy's previous swing. The host's NPC chains attacks the engine's way: its Attack lowers the attack's animation from priority 5 to 3 at the Withdraw (WO-197 read it), and the next one installs. A copy's swing is queued straight into its animation controller at 5 and nothing lowers it: the next swing waited out the whole previous fragment (and the trace showed one never ending). | The copy's previous swing steps down 5 -> 3 when the next arrives, as the host's Withdraw does. F1 (a copy attacking in the field's rhythm): **before 6 of 24 animated, after 46 of 46**, the swing on the copy 8-29 ms after its row arrived. | `mp_anim_shown` |
| **IV.2** | The weapon pops into the copy's hand. Measured: at the host's draw the copy gets the hold-item actions only; no draw action plays on any scope. | not built. Next: the NPC state machine's own draw (`NPCState::C_DrawWeaponAction` / `C_HolsterWeaponAction` in XGenAIModule), driven on the copy the way WO-141 drives activities | -- |
| **IV.3** | Recoils did play (3 of 3 on the joiner). The host's clinches reached the joiner (4) and none played: "his figure has no opponent body here" -- the NPC the host clinched fought only the host, so it was never sent as a fight record and the joiner did not know which copy it was. | The clinch carries the body its player clinched (his engine's own opponent, read every 0.5 s); the partner plays it on that copy. | `mp_recoil_clinch` |
| **IV.4** | A fighting NPC's position was 1.0 s old at the 90th percentile on the joiner (1.7 s at the 99th). | The host sends a drawn, living NPC within the engage range of *any* player every tick, moved or not; the joiner runs a fighting copy on for at most 0.3 s past its newest sample, then holds. | `mp_fight_pos_fresh` |
| **IV.5** | Walkers glide (their step cycle locked at its slowest example). | not built | -- |

**The desync scorecard** (`mp_desync_score`): every bundle's `BRIEFING.md` now has the table below for its machine, and the joiner logs
each host swing's delay to its copy's swing (`WO202-SWING`).

| measure | field (session A) | target | here, after |
|---|---|---|---|
| host attacks animated on the joiner's copy | 129 of 182 (71 %) | >= 95 % | 46 of 46 (F1) |
| delay from a host attack to its swing on the copy | not measured | p90 < 250 ms | 8-29 ms after the row arrives (F1) |
| fighting NPC position age | p90 1.0 s, p99 1.7 s | p90 < 200 ms | not measured two-player |
| deaths both machines agree on | 0 false deaths found; 4 bodies walked | 100 % | every announced death the engine's; 4 of 4 bodies down (D1) |
| weapon drawn on the copy | never shown | the draw played | not built (IV.2) |
| recoils and clinches played | recoils 3 of 3; the host's clinches 0 of 4 | every one | the clinch names its body (IV.3) |
| walker foot slide | 0.6-0.8 m/s (game 0.16) | within 2x | not built (IV.5) |
| joiner message latency | 35-107 s backlog | p99 < 1 s | the agent no longer waits on the game (III.1); two-player only |
| shared cutscene hard seeks | 125 in one scene | <= 2, never backward | the field's stream replayed: 0 backward, at most 2 forward (V.1) |

## 5. Shared experience (Act V)

* **V.1** (`mp_cutscene_clock_v2`): session B's joiner made 125 hard seeks in about 40 s; the host's samples arrived bunched and late and
  each set the copy's clock, back and forth. The host's clock is now the median of its last 8 samples (out-of-order ones not kept, late
  ones dropped, the one-way delay from the clock sync); small drift is closed by the movie system's own playing speed (0.9-1.1,
  `SetPlayingSpeed`, read from CryMovie's vtable and checked by its bytes); a jump only forward, only after 1 s more than 1.5 s behind,
  and only while no voice line plays. Replaying a stream like the field's: the old rule made more than 10 sets with backward ones; the
  new one none backward and at most 2 forward.
* **V.2** (`mp_speech_share`, WO-201's): 0 lines captured on every machine: the capture took only paths ending in `.ogg`, and the
  engine's voice paths have no extension. The capture takes the engine's own form now (the `Localization` dialog prefix and a file
  name); its captured / dropped / played counters were already in `MP-W201-STATS`.
* **V.3** (`mp_keys_per_player`): keys are each player's own. A key one player takes (from a body, a chest, an NPC) is made in the
  other's inventory too (the game's keys: the item table's key rows; quest keys follow the quest mirror). A chest the host unlocks is
  unlocked in the joiner's world the game's own way (the joiner's unlock already reached the host; doors sync both ways since WO-151).
  Chest contents stay shared, one taker.

## 6. Smaller fixes and the tester tools (Act VI)

* **The false "could not repair" toast**: on the joiner, the snapshot pipeline moves a QuickSave out of the playline, and the save check
  then read the missing file. A file the mod itself moved out is never checked or reported.
* **The partner bare after changing clothes** (`mp_outfit_atomic`): the figure's new pieces now go on in one call (one frame) before
  the old ones come off (it was removals first, then one call per piece); and for 30 s after a change the figure's real outfit is
  checked every 2 s instead of 10 (the field: the figure was found wearing one piece three times within a minute of a change).
* **One mark key** (`mp_mark_key`): the period key marks the moment on both machines with a "Marked" toast. Every letter is a key of the
  game's own "haste" action map, the F keys are the game's or the mod's, and H, F1, F3, F10 and the numpad are the Modding Tools
  build's debug keys; the period key is named in neither of the game's keybind files. Live press (the WO-6 rule): one real press of the
  period key into the focused game logged `WO202-MARKKEY pressed (period)`, the `MP-MARK` line and the agent's `mp_mark` event at the
  same game time, and showed the "Marked" toast.
* **"Teleported 7k meters"** is the game's own warning when the inventory or the map closes: in the tester notes.
* **Fly mode stays** (the maintainer's decision): fly-ins are not a fair test of combat -- in the tester notes.
* **The README's opening summary** is the maintainer's own wording now.
* **`BRIEFING.md`** at the top of every bundle: the session, the crash first, every mark from both players in order with what each
  machine was doing, every quest failure in plain words, the desync scorecard, and what stood out against the launch before.
* **The publishing gate** also fails on any player name or handle found in the testers' bundles kept on this machine (the
  repository's own URL is the only exception).

## 7. Release checks

* Every offline gate (88: the native, agent, relay, setup and bundle tests, every Lua suite, the static checks, the save tools): pass.
  They caught two real faults before any release: the mark key's XML comment held "--", which made the keybind merge refuse the whole
  patch (every mod key would have been missing after Setup), and the key's action was missing from the profile patch.
* The no-input and no-screenshot gates over the assembled payload (1036 files, the staged mod included): pass.
* Frame rate (the soak: 10 minutes, a crowd and then a fight, the same unfocused window in every run): the final build against 0.61.9 (main's DLL with the mod installed before this WO): mean 74.4 vs 74.5 fps (-0.3 %), median 74.4
  vs 74.4, the first two minutes +0.7 %, the last two -0.9 %; no fault line, the stat stack 0 throughout.
* The 31-minute host session with the synthetic joiner: no crash (above).
* The live save round trip: pass (the final build, `playline4`: saved, checked, reloaded).

## 8. What is open

* The four field crash contexts were not reproduced here; C2 (the figure rebuilt in a skirmish) and the leash countdown had no
  scenario. The capture names the next crash.
* II.5 (area triggers), IV.2 (the copy's draw), IV.5 (walkers' step cycle): not built.
* The mark key on the partner's machine: the live press was single-machine (no partner connected); the partner's toast is covered by
  the offline tests only, and is in the checklist as its first two-player use.
