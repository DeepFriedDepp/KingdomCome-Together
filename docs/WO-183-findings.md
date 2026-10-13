# WO-183 — 0.50.9: the 0.50.8 playtest patch (videos only, the joiner's saves, arrests, haggling, block icons, the T-pose)

2026-10-13. Version 0.50.9 (the maintainer's string). Built on `main` 0.50.8 (`3b70912`). Evidence: Holiday's (host) and Mystigan's (joiner)
logs of the 0.50.8 playtest, unpacked privately and never committed. A fourth bundle ("Combat logs (2)", Mystigan, a later launch) was used only
as a second look at item 4a. **No live run was made** (the maintainer's decision, 2026-10-13: skip the live checks); everything below was proven
offline: unit tests, the Lua synthetic suite and replays of the bundles through the real transport filter and the new rules.

## Result

| item | result | default |
|---|---|---|
| 1 shared scenes: videos only | the relocation watch is a log line (`WO179-JUMP`), the 30 m fallback, relocation scenes, the conversation-as-scene bookkeeping, the joiner time-skip exception and the settle window are gone. After a video the partner lands beside the triggerer where he stands when it ends, on public ground only (3, 6, 10, 15, 25 m rings, the game's own area state of each spot), never in an arrest; a held bring-along is dropped after 30 s | `mp_scene_bring_along` on (videos only) |
| 2 the scene guard cut the joiner's talks at 3.5 s | `DialogTwin_` lines are forwarded; a twin, a command of the player's own dialogue or a state past `WAITING_FOR_TWINS` counts as running. Class test added | on |
| 3 the joiner's progress was lost | the mod never saves to unstick a scene (three paths removed); the snapshot is the only save request and goes through one gate; a snapshot the game cannot take is owed, retried every 15 s | on |
| 4a block icons | cause confirmed (flag `0x10` referred every blow against a held block to the host's verdict); `mp_replay_blocked` replays them through the engine | on (see Open) |
| 4b T-pose on a retarget | an engaged copy stays engaged while its NPC fights anyone; released as "left combat" only after 1.5 s of no combat and no target; the talk sweep never runs mid-fight | on |
| 4c "errors on every attack" | not a bug (below) | — |
| 5 animation-queue overflows | cause written, not fixed (below) | — |
| 6 triage signatures | not done: WO-181's tool is not in the repository | — |

## 1 — videos only

The bug (Holiday's log, four relocations seq 1-4): `WO179-FALLBACK scene=Dialogue:conversation moved 43 m` (seq 1) and `135 m` (seq 4), and
`source=jump` bursts in between (seq 2: seven relocations from x=1938 to x=1489 in 9 s, which is the host's own travel; seq 3: two). Mystigan's own
`FALLBACK … moved 58 m` went the other way, and the host's seq 2 and 3 waited in his queue behind the arrest scene (`WO179-QUEUE … runs first`, 15:09:59–15:11:17).
A conversation counted as a scene that can move Henry and stayed open after the talk, so every later jump was sent. Everything named in the WO
is removed, not switched off. Kept as built in 0.50.7/B: both watch, one skip ends it for both, the partner is safe while watching, the false-skip
hold, the scene guard. The triggerer now sends where he stands at the video's end (just before the end message). The landing tests each ring spot
with the game's own area labels (`KCD2MP_W183AreasAt`, WO-157's `KCD2MP_W157AreaAt`); only `public` is a landing (`open`, `private`, `unknown`
are not); none public = not moved, log `WO179-LAND NOT placed (no public spot …)` and the toast "Couldn't bring you to your partner, they're
somewhere private."; the snapped spot is checked again and a non-public one puts him back. Nothing moves a player in an arrest, a crime stop or a
punishment: the landing waits (held, 30 s, then dropped), the leash pull is refused, the shared-travel mirror is not run. "In an arrest" is read from
the game's own words: a `crime_*` scene, a guard's crime stop on this joiner, a punishment dialogue in the last 20 s.

## 2 — the guard

Mystigan's `kcd.log` has the `DialogTwin_` lines; his `agent.log` had 0 (the forward lists never passed one). Replay of the haggling bundle:
2 `ttkc_man_11` haggles run (twin/command/state seen), 2 `ttkc_emerich` haggles show no sign and are still ended (their cause is WO-181's research).
**The class test** (`Wo183Tests.Every_literal_an_engine_line_handler_matches_is_forwarded_by_the_log_tail`) reads the source of every engine-line
handler and its rule class (Wo144/160/164/166/167/170/172/179/183) and checks every literal against the forward lists; a literal that is only a
refinement of an already forwarded phrase is listed with its reason. What it caught: `DialogTwin_` (the bug); the new literals of this WO
(`Interrupting dialog '`, `Readiness observer 'BeforeGameSave'`, `because there is another savegame pending`), which would have repeated it. One more
was found while writing the reason for the `] Dialog ` exception: Wo166's dialogue-state reader never got the `[ID: n] Dialog ending` lines (6 in the
haggling bundle); now forwarded (harmless: the same state, CLEANUP, is on the lines that were).

## 3 — saves

The chain in Mystigan's `Combat_logs` is as the WO says: 36 false `WO179-RELEASE` → 35 `WO167-SCENE stuck … a save request` → the first rescue
QuickSave (mid-arrest) opened `Readiness observer 'BeforeGameSave' … started async waiting` and was never ready → 34 later rescues refused, then
the host's seq 3 and seq 4 snapshots refused (`another savegame pending`, 35 lines in his `kcd.log`).

* 3a: removed the rescue save from WO-151's scene rescue (`Wo151SceneRescueSaveAsync` is deleted), WO-167 T4's stuck-scene release and WO-179's
  positioning lift. Scenes are released by what exists: the bounded positioning lift, the engine's own end, `InterruptDialogs`, the fader restore,
  the 90 s give-up. No case turned up that only the save fixed (none could be tried live).
* 3b/3c: the only caller of `KCD2MP_Wo125Snapshot` is the Henry snapshot (a test pins it). The one gate (`Wo183Rules.SaveBlock`): another save
  pending (read from the engine's own lines; stale after 30 s), a join, loading, a menu, downed, an arrest/crime stop/punishment, a scene, a
  conversation, the screen faded. Blocked = owed: `WO183-SNAP owed` once, retried every 15 s with the newest host save (an older owed one is
  superseded), up to 60 tries, `taken … late` when it runs. The engine refusing a snapshot is owed too.
* 3e: one log line per owed snapshot; one toast ("Your character's progress hasn't been saved for a while…") when still owed after 2 minutes.
* Replay of the bundle: 37 save requests in the game's own log (1 accepted and never ready, 35 refused) and 35 rescues asked by the mod, 36 false
  conversation releases; the new rules ask for the two host-save snapshots only, with nothing pending.
* **What moved Mystigan during the arrest (3d):** the game's own arrest, not the mod. Evidence, 15:09:18–15:11:40 on his machine: 31
  `WO179-SKIP … jump=14–29 m` lines (median 20 m) at about 0.1 s steps, 13 `MP-LEASH … is flying (69–169 m/s, no horse, no fast travel)` notes,
  height 7.7 → 44.6 m in 5 s; and in the same window 0 `WO179-BRING`/`LAND`/`RELOCATE`/`LADDER`, 0 leash pulls (the 13 "pull" hits are the
  flying note's own words), 0 DLL own-teleport marks (kind 4). It is the engine's guard escort / second-arrest scene (`crime_secondArrestFader`,
  `interrupt_arrest`) running the walk to the cell as a fast-forward. The WO-179 relocations only arrived at 15:11:17 (queued, never delivered).
  What the mod now guarantees: nothing of it moves a player in that state (landing, leash pull, shared-travel mirror).

## 4a — block icons: before / during / after the arrest window (Mystigan, `Combat_logs`)

Arrest window = 15:08:34 (stop #1) – 15:10:50 (the scene guard gave up). The first blow of the session came at 15:12:00. "After" is split at
15:23:16, the last stuck-scene release (the pending save never cleared; the later period is simply the quieter one).

| window | blows | engine hit | engine blocked (icon) | perfect block | out of reach (miss) | not replayed: victim blocking | not replayed: copy not engaged |
|---|---|---|---|---|---|---|---|
| before the arrest | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| during the arrest | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| after, to 15:23:16 | 30 | 10 | 0 | 0 | 10 | 7 | 3 |
| after, from 15:23:16 | 22 | 10 | 2 | 0 | 2 | 7 | 1 |
| total | 52 | 20 | 2 | 0 | 12 | 14 | 4 |

The arrest did not cause it: no blow fell in it, and the later bundle (Mystigan, a later launch: 20 blows, 6 hits, 10 out of reach, 4 victim blocking,
0 blocked) shows the same. The 14 blocked-state blows (35 % of the 40 that reached him) never reached his engine, so no icon, no perfect-block
window, no block sound. Step 2: `mp_replay_blocked` (joiner, default on, console `mp_replay_blocked on|off`, Lua + agent) drops the refer-to-the-host flag, so
those blows are replayed too; his own held block still decides (WO-178 own_block, in the DLL). Nothing else in combat was touched.

## 4b — the retarget

Holiday's sender lines for `tvez_man_15` (host clock 16:12:06): `combat=0 target=Host`, `None`, `Avatar:1`, `Host`; `tvez_man_16` at 16:12:19: `combat=0
target=None`, `Host`, `None`. The sender reads combat=0 for an NPC that fights the host or an avatar, so "combat=0 for 1.5 s" alone would still release;
the rule is therefore: an engaged copy stays engaged while its NPC has combat or any target; only combat=0 with no target is a leave, and only after
1.5 s (`Wo183Rules.LeftCombat`; a report of a fight resets it). Replay of the host's 246 sender lines: 17 releases of an engaged copy under the old rule,
7 under the new (8 retarget gaps kept engaged). The mid-fight `WO164-SWEEP` (trigger `focus`, 50 times in Mystigan's session) is skipped for a copy that
is engaged or whose host NPC fought in the last 10 s (the agent and the Lua half); the host-conversation clear, the talk retry and the console are not
affected. The old WO-153 rule is unchanged for a copy that is not yet engaged: it is not engaged for the host's own fight.

## 4c — not a bug

The "errors on every attack" in Holiday's `kcd.log` are the game's own Modding Tools developer notes ("Warning for ViktorB: Please fill animation hit
RPG…" on the clinch attacks; missing lip-sync for combat barks). Left alone.

## 5 — animation-queue overflows (cause written, not fixed)

Mystigan's `kcd.log`: 566 `Animation-queue overflow` (Holiday's: 27, all `ttkc_inkeeper`, so the inkeeper has a vanilla baseline). By NPC: `ttkc_inkeeper`
230, `tzel_rowdy_2` 198, `ttkc_tradersMom` 88, then 26/24. By animation: idle (`relaxed_idle_both`, `1d_relaxed_idle_bigturns_nw`) and the look
poses (`*_idle_lookposes_head/torso`). They come in bursts of 20–60 s on a copy that has been paused (`wh_ai_PauseNPC`, the puppet-start hold) for
hundreds of seconds, near where he stood (the inn), and end when the hold ends (`event=release via=silence`). The reading: the engine keeps feeding the
idle and look-pose layers of a character whose brain is suspended, and the queue (16 entries) is never consumed. Fixing it means resuming or stepping a
paused copy near the player, which is the pause lever's design (WO-107/108): not small, not contained, needs a live probe. Left as is.

## 6 — triage signatures

WO-181's triage tool is not in the repository (no tool, no signature file), so the six signatures were not added. They are: a refused or owed
snapshot (`WO183-SNAP owed`, `another savegame pending`), `BeforeGameSave` never ready (`WO183-SAVE engine: a save is pending` with no `ready`), any save
request from a scene release (should be zero: `MP-W151 … NO save is requested`), a punishment scene not released (`not released … s after its content`),
`WO179-RELEASE conversation why=no-twins` (should be rare), a bring-along dropped after 30 s (`WO179-QUEUE … DROPPED`).

## Tests

* Unit: `Wo183Tests` (the twin signs, the class test, the pending tracker and its stale bound, the gate states, the owed timing, the rings, the public-only
  rule, the 30 s bound, the 1.5 s debounce, a retarget is not a leave, the sweep rule), `Wo132RulesTests`/`Wo147FightAndQuestTests` (the retarget
  case; the old expectation that a copy is released on `combat=0 target=Host` is replaced), `Wo179Tests` (the removed rules out). Client suite
  1631 passed; relay 63, setup 77, LogPack 14, Farkle 59.
* Replays (`Wo183ReplayTests`, run where the bundles are unpacked; they pass with a note elsewhere): haggling (item 2), combat (item 3), host (4b).
* Lua synthetic `Test-WO183Synthetic` (area check, switch, sweep gate, no save from a scene release): 17 passed; WO-164/165/167/179B suites pass.
* **Not run:** every live check of the WO (a shared video and the landing, the trespass test in a private building, an ordinary talk then a 200 m
  walk, a stuck scene released with no save, the host saving after a fight and during a fader with the joiner's inventory after a reload, a duel
  retarget, the town frame rate, the live save round trip). The maintainer waived them for this build.

## Open

* All live checks above; the two-player checklist §WO-183 has them in plain words.
* `mp_replay_blocked` ships on without the synthetic duel harness having shown blocks counted as blocks (the harness was not run): per the standing
  "new mechanisms ship on" rule, with its switch. If blocked blows show up as hits, `mp_replay_blocked off` is the way back.
* A snapshot taken late pairs the joiner's Henry as it is then with the newest host save (the WO's "taken late if owed"); what he did between the
  host's save and the late snapshot is in that Henry. The host-save lag rule (3 s) is measured from the moment the gate opened.
* The landing's trespass check uses the game's area labels; a spot the engine cannot answer for counts as not public, so on a build where the call is
  missing no landing is made (a video still ends for both, the partner just stays where he watched it).
* The 2-minute toast for an owed snapshot is the only message; there is no banner.
