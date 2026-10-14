# WO-185 findings: the staged duel session -- from an A/B test to 0.60.3

What started as one interactive A/B run (`mp_replay_blocked` on vs off, "are the joiner's blocks and combat animations broken?") became an eight-hour
investigation. It found and fixed a real perfect-block defect, found a harness artifact that had been distorting every duel run, and mapped how the game's
own perfect-block and riposte timing works. Three local release candidates came out of it (0.60.1, 0.60.2, 0.60.3). Nothing here is two-player evidence:
every run used the synthetic host (tools/wo118) or the game alone.

## 1. Summary

| # | Finding | Status |
|---|---|---|
| 1 | The harness's per-swing `r_GetScreenShot` stalled the game about 0.3 s at every wind-up (game time read 0.25x, then 1.9x). It was the "laggy wind-up" and it distorted the block verdicts | harness default now off; the mod itself takes no screenshots (except the tester's `mark_odd`, left on) |
| 2 | `mp_replay_blocked` on/off is not what breaks blocks; the earlier A/B verdicts were contaminated by finding 1 | closed |
| 3 | The pushback and the "hit reaction on a block" were the player's own hit-reaction tail (`mp_player_recoil`, on by default) running on every in-reach replay, blocks included | understood; the switch exists; default left on |
| 4 | A perfect block the game credited still took the full hit: the copy's perfect-blocked sequence ran from the press and cut the player's `PerfectBlockState` short of the verdict | fixed in 0.60.1 (`mp_pb_at_contact`, default on) |
| 5 | Vanilla's own timing was measured (slot formula, console variables, attacker states, riposte slot, per-swing recovery times) | recorded below |
| 6 | After a perfect block the player's attack waits for the attacker's 0x10 phase; vanilla's length depends on the swing, the copy used a fixed 0.42 s | 0.60.3 gives the vanilla value per swing |
| 7 | At night the harness stand-in T-poses (the game's night routine takes his sword) | harness-only so far; a night check is on the two-player list |

## 2. How the session went

1. **Build check.** The A/B plan demanded that the installed 0.60.0 files match the installer. The coding shell sees a stale, sandbox-redirected copy of the
   user-profile install folder (old DLLs, old dates), so hashing there was meaningless. The maintainer hashed in his own shell and the values were compared with
   the installer payload (`release/c0600c/release/KCDMP/`): `KCDMP.dll`, `KcdMpClient.dll` and `kdcmp.pak` matched. The harness agent was started from that
   payload folder (byte-identical files) with its own relay on port 7790 (the launcher's relay holds 7778 and its HTTP port, so the test relay needs `KCDMP_TEST_URLS`).
2. **A/B (runs A, B, C).** 20 swings each at 4 s: swings 1-4 no block, 5-14 held block, 15-20 perfect block.
   * A (`replay_blocked=on`, screenshots on): 6 hit, 5 pb, 9 out-of-reach; green shield 9 of 10; the bandit visible on every swing.
   * B (off, screenshots on): 4 hit, 4 blocked, 3 out-of-reach, 9 swings not replayed (`fallback=victim-blocking`); shield 6 of 10; wind-up "laggy".
   * C (on, screenshots off): `WO138-WORLD SLOWED` 0 against 20 in A and in B, worst frame about 40-48 ms against about 250-270 ms, verdicts 5 hit, 12 pb,
     3 out-of-reach. "The lag was completely gone."
3. **Pushback and block-as-hit (runs D, E, F).** With `mp_player_recoil off` the pushback and the hit reaction on blocks were gone. Health only ever dropped on `hit`
   verdicts: blocks cost stamina, not health. Some "no damage" swings were out-of-reach misses (the harness seats the bandit 2.2 m away and the stab's contact reach
   is 2.0 m), not blocks.
4. **The perfect-block defect.** In D-F, 14 of the 15 in-reach swings the game had credited as perfect blocks (`WO178-PBHIT`) still ended `hit` (19.4 hp). The
   `mp_w178_trace` timelines (runs G, I) showed why (section 4). Fixed and verified in run J.
5. **Vanilla traces.** Single-player fights against real NPCs with the same read-only trace gave the engine's own timeline (section 5).
6. **Riposte timing.** The maintainer felt a delay after a perfect block on the copy; the vanilla traces gave the per-swing recovery times (section 6).
7. **Builds.** 0.60.1 (the fix), 0.60.2 (extra trace fields), 0.60.3 (per-swing recovery times); all local release candidates.

## 3. The screenshot stall

`tools/wo118/run178.py` took `r_GetScreenShot 1` at the middle of every striking window (`W178_SHOTS` defaulted to on). The game writes the image on the main
thread: every swing showed a hitch of about 0.3 s right after the swing was queued (6-9 frames, game time 0.25x, then about 1.9x to catch up). Runs A and B each
logged exactly 20 `WO138-WORLD SLOWED` lines; run C, the same plan without screenshots, logged none. The verdict counts moved too (9 out-of-reach in A, 3 in C).

Now `W178_SHOTS` and `W176_SHOTS=mid|rows` default to off, and `tools/wo118/README.md` names the trap. A run that took screenshots must never be used to judge
timing or a block. The mod's own `mark_odd` screenshot (one picture when a tester presses "Something's wrong here", `mp_mark_shot`) stays on.

## 4. The perfect-block defect (fixed in 0.60.1)

How the engine does it (CombatModule 1.5.5, found by RTTI and structure; see section 8): the attacker's attack action plans the defender's perfect-block slot (the
green shield) with `PlanSlot`: start = max(0, (countdown - tuning) - window), end = countdown - tuning. With `wh_cs_StandardSlotDuration` 1, `wh_cs_PLSlotDurationMod`
0.5 and `wh_cs_SlotEndHitOffset` 0.15 the shield is about 0.5-0.65 s long and ends about 0.1-0.15 s before the blow. A perfect block is resolved by the engine's hit
core at contact, from the victim's `PerfectBlockState` (model +0x8A8), which the player's perfect-block action holds while it runs.

What the 0.60.0 copy did: the swing is an animation on the joiner. The mod set the copy to its wind-up (0x4) and strike (0x8) states and wrote the countdown, so the
engine opened the shield (+0.44..0.47 s after the swing was queued, closing about +1.13 s) and credited the press (`WO178-PBHIT`, +0.5..0.65 s). It then put the copy into
0x10 (perfect-blocked) at the press and into 0x20 0.5 s later, and the player's `PerfectBlockState` ended about 0.52 s after the press, in the same frame the copy's
sequence ended. The damage verdict (`RPGProcessHit`) arrived at +1.23 s and read the player's state at that moment: not blocking, so a hit. Any press earlier than about
+0.70 s lost its perfect block, and the shield invited presses from +0.45 s. In the traces the state ended 0.52-0.54 s after the press in six of six swings; the swings
where the copy happened to stay in 0x20 past the verdict were judged `pb` by the engine's own hit core.

Fix (`mp_pb_at_contact`, default on; `off` is the 0.60.0 behaviour): the press only arms the step (`WO185-PBARM`; a copy the engine dropped to Guard at the press is put
back to striking). At the blow's contact (`hitAt`) the copy goes 0x10 with OpponentHit if the player still holds the perfect block (`WO178-PBHIT ... [at contact]`), then
0x20, then its prior state. A perfect block that ended before contact logs `WO185-PBLOST` and the verdict is the engine's. Run J (0.60.1): in every armed, in-reach
swing the player's `PerfectBlockState` stayed on from the press to +1.76..1.81 s and the verdict at contact was `pb` (5 of 5 in reach), the copy's sequence matching
vanilla's.

## 5. Vanilla's timeline (read-only `mp_w178_trace`, real NPCs, single player)

All times from the attacker's wind-up start (state 0x4) unless noted.

* The perfect-block slot opens at +0.01 (fast swings) and closes 0.14-0.15 s before contact.
* Attacker 0x4 -> 0x8 (strike) at +0.29..0.60, 0x8 -> 0x10 at contact (+0.63..0.82), 0x10 -> 0x20 after the recovery times of section 6, back to Guard 0.25-0.35 s after
  that. The attack action and the 0x4 state start in the same frame; contact is the row's `attack_time_to_hit` from that moment (0.69-0.80 s for the NPC measured over 17 swings).
* The player's press came 0.15-0.41 s before contact. His `PerfectBlockState` ran 0.7-0.95 s and ended about 0.05 s after the attacker entered 0x20 (or in the frame his own
  attack began).
* The riposte slot (the blue swords: `RiposteSlot` property 1, cursor icon 11) opens exactly at contact (46 of 46 perfect blocks) and closes 0.04-0.15 s later; the player's
  `RiposteState` turns on at that close. The harness copy opens it at contact too (+0.01 s) but kept it open 0.24-0.5 s.
* The player's attack after a perfect block (state 0x4) starts when his perfect block ends: 0.27-0.59 s after contact in vanilla (median about 0.48), 0.43-0.58 s on the
  copy (0.60.3). A press made before contact does not start it earlier (`RequestedInputClass` was already 1).

The charged stab the harness plays has start 0.533 s and hit 0.7 s in its table row, and the engine's own blade collision on the copy lands at +1.16..1.21 s (8 of 8
logged): contact there is start + hit. The ordinary rows of the NPCs measured put contact at hit from the action start. The mod's contact model (start + hit) was only
exercised on that charged row; see the open items.

## 6. The riposte wait (0.60.3)

The wait after a perfect block is the attacker's 0x10 phase, his recovery. In vanilla it depends on the swing. Medians of 0x10 -> 0x20 from contact, one NPC with a
one-handed sword, 46 perfect blocks:

| swing (type, zone) | 0x10 -> 0x20 |
|---|---|
| stab, zone 5 | 0.41 s (0.40-0.42) |
| slash, zone 0 | 0.50 s (0.49-0.51) |
| slash, zone 1 | 0.29 s (0.27-0.29 often, 0.36-0.39 otherwise) |
| slash, zone 2 | 0.41 s (0.38-0.41 or 0.47-0.49) |

0.60.2 used a fixed 0.42 s: right for the stab, up to 0.14 s long for the fast slash. `pb_contact_stage2(type, zone)` (`native/KCDMP/wo166_rules.h`) now returns those values
(0.42 s for a swing not measured), and the copy returns to its prior state 0.30 s after 0x20 (vanilla 0.25-0.35 s). The harness only throws the stab, so its pass (run M,
8 perfect blocks: 0x20 at +0.41..0.43 s, the player's attack at +0.43..0.58 s) equals 0.60.2 and matches vanilla for that swing; the slash values are untested live. The
maintainer's verdict: good enough for now, polish after the retail port.

## 7. Builds

| version | what | installer sha256 |
|---|---|---|
| 0.60.1 | `mp_pb_at_contact`, harness screenshots off by default | `2384deb9e15822779777a2d3dec8d3a4588fd506bec5c354cf73d4770a45fe63` |
| 0.60.2 | plus read-only trace fields (`RiposteState`, `OpponentHit`, `CanHitByAttack`, the attack request, `ComboState`, `Preblock`, `FreeBlock`) | `561389279c76132482c380aabdbf12071b8d212a1cc8f6fad38728795272fa3c` |
| 0.60.3 | plus the per-swing recovery times | `466009d088b44d26b28efab5a3b68aa66e48aba7893fc75ac9eab9e5978de8f7` |

Each is a local release candidate (`-ReleaseCandidate`: the soak skipped, every other gate stood, including the live save round trip on a `playline4` throwaway),
unsigned, never uploaded. Gates per build: native 9,480-9,481, agent 1,665, relay/setup/LogPack suites, all Lua synthetic suites, static checks, payload smoke,
installer checks, the no-input-tooling gate. New switch: `mp_pb_at_contact on|off`. The trace command `mp_w178_trace <secs>` is unchanged.

## 8. Reverse-engineering notes (CombatModule.dll 1.5.5; RVAs are image offsets; the file was read with capstone, no game memory written)

* `PlanSlot` `0xe07b0` (called from the attack action's `EnterImpl` for slot types 4, 0xA, 8) reads the attacker's countdown (model +0x5C0), `window` (`0xa2d50`:
  `wh_cs_StandardSlotDuration` times the blocker's mod; type switch through vtable +0x68) and `tuning` (`0xa2eb0`: `wh_cs_SlotEndHitOffset` 0.15 or `wh_cs_SyncSlotEndHitOffset` 0.1),
  and registers {type, start timestamp, duration}. `C_CombatSlotTrigger::Update` (`0x559ba0`) starts and ends a slot at those timestamps; the perfect-block trigger (`0x55bab0`) also ends
  it early when its keep-open predicate (`0x55bb10`) fails (defender already in `PerfectBlockState`, in state 8 or 0x200, attacker not in 4|8, and others).
* Console variables read live: `wh_cs_StandardSlotDuration` 1, `PLSlotDurationMod` 0.5, `AISlotDurationMod` 0.5, `SlotEndHitOffset` 0.15, `SyncSlotEndHitOffset` 0.1,
  `RiposteSlotDuration` 0.5, `ComboSlotDuration` 0.5, `AttackTimeToHitEst` 0.4, `OpenSlotFOVBiasPlayer` 35, `OpenSlotFOVBiasNPC` 45, `PerfectBlock_SpatialVolumeRadius` 0.1. The game also
  logs "<Slot> Slot started with planned window length: N s" to kcd.log (riposte 0.500 s in all 27 cases; perfect block 0.65 s on the copy).
* The player's perfect-block action (`C_CombatActorActionPerfectBlock`): `EnterImpl` `0x92860` sets `PerfectBlockState`, `ExitImpl` `0x93270` clears it, `UpdateImpl` `0x93120` exits
  on animation end or when the owner's State reads 0x20 (0x20 is written by `C_CombatActorActionHit::OnAnimActionExit`, `0x4f1d0`). Its table rows (`combat_action_perfect_block`)
  last 1.2-1.5 s with `block_time_to_hit` 0.6-0.97 s.
* The riposte trigger (`C_CombatTriggerRiposte`, vtable `0x6518b0`): `StartSlot` `0x55be70` sets the owner's `RiposteSlot` property (+0xAD8) and a window from a skill factor; `Update`
  `0x55bf60` keeps it open while `PerfectBlockState` is on, `RiposteState` (+0x4C0) is off, an RPG check passes and the actor's availability check (`0x714e0`) holds.
* Model state values (property `State`, +0x40): 0x2 in combat/guard, 0x4 wind-up, 0x8 striking, 0x10 perfect-blocked, 0x20 after perfect-blocked or after a hit reaction, 0x40 hit
  reaction, 0x80 preparing to parry, 0x100 parry in place, 0x200 dodge, 0x400 transition. The state setter is `0x80d40` (property at model +0x40, value in edx).
* Tables (`Data/Tables.pak`, `Libs/Tables/combat/*.xml`): attack rows carry `attack_time_to_start`, `hit`, `end`, `withdraw`, `animation_duration` and `hit_distance`. The
  failed-attack-on-perfect-block rows (`CombatAttackFailedPB`, four weapon sets) last 0.5-0.7 s, but the attacker's 0x10 phase is shorter and swing-dependent (section 6).

Not found: the game code that decides the per-swing 0x10 length (only its measured values), the per-actor factor behind the slot window, and what a real master-strike riposte looks
like at the animation level (the state traces never showed one; the Mannequin tag watcher only logs locomotion tags).

## 9. Tooling traps found (each cost time)

* The coding shell's view of the user-profile install folder is stale (sandbox redirection): never verify an installed build from it. The agent's own log lines are current.
* A stale `bin/Release/*/publish` folder from earlier builds kept an inbox `System.Text.Json` 8.0 beside a 10.0 `deps.json`, and the publish step's start-save validator died
  ("could not load System.Text.Json 10.0.0.0"). Delete the project `publish` folders before a build.
* `tools/Build-Installer.ps1` needs `KCDMP_PUBLISH_DENYLIST` (the private denylist's path) in the build process, and a game with a `playline4` throwaway loaded for the live round trip
  (the gate refuses any other playline and a closed game). The game's last-loaded save is read from kcd.log, and a quick-load can silently pick another playline.
* The harness's test relay needs its own HTTP port (`KCDMP_TEST_URLS`) while the launcher's relay runs, and the launcher's own agent must be closed (two agents on one game).
* In a joiner session a synthetic host streams no NPCs, so a joiner has none: a real enemy through the joiner pipeline needs a real second game.
* At night (in-game hour about 20-6) the harness bandit T-poses: the game's routine gives him a torch and the longsword swing finds no sword. Reload the throwaway for daytime.
* kcd.log has no clock; align it with the native mirror log through the `t=` and position lines. `WO132-HITS` logs a collision only when the blade hits (not when blocked).

## 10. Open items

* Two-player: a real host's enemy, slash swings (the 0.60.3 table), other weapons (the table is one weapon class), a fight at night (does the copy animate?).
* The contact model (start + hit) was only exercised on the charged stab; a normal row's blade contact on the copy was not measured (the pass that tried it was blocked).
* `mp_player_recoil` stays on by default (hit reaction and knock-back on blocks too); off removes both. Not judged again after 0.60.1.
* The vanilla "instant riposte" the maintainer remembers was not captured as an animation; the traces show the same attacker-recovery wait.
