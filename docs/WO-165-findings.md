# WO-165 — Shared combat with one human: lock-on, victim decides, the attacker's recoil, fight snapping (0.48.0)

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L]** live, on this machine's Modding Tools build (1.5.5), throwaway playline only; **[L-field]** counted from the testers' logs on
disk (no line copied; entity names such as `ttkc_man_3` are the game's own); **[syn]** the synthetic peer or a Lua synthetic suite; **[unit]**
the engine-free .NET suites; **[native]** `KCDMP_NativeTests`; **[disasm]** read in the game's machine code this session (the design source
is `research/WO-162/combat-RE.md`, cited by section); **[code]** read in our code; **[not run]**; **[needs two players]**. The version is the
maintainer's (**0.48.0**, given 2026-10-09). The partner was always the synthetic peer (`tools/wo118/synthpeer`); the maintainer was the only
person at a keyboard.

## Summary

| Item | Result | Mark | Default |
|---|---|---|---|
| Stage 0.1 synthetic-peer verbs (`swingat`, `verdict … pb\|broken hid=`, `avatarfight`, `flee`, `teleport`, `fightz`) | packet self-test 32/32 | [syn] | harness |
| Stage 0.2 the keeping measure (`fightsnap165.py`) | 0.47.0 joiner, fight windows, jumps excluded: resume p90 **67.9 cm**, post-hold p90 **70.3 cm** | [L-field] | — |
| Stage 0.3 synthetic baseline fight | 40/40 shown + applied once; fight p90 resume 8.6 / post-hold 18.7 cm | [L][syn] | — |
| The instrument (`wo165.cpp`): a blow through `RPGProcessHit` | built, 30 + 13 calls, **0 faults** | [L][native] | probe only |
| P2 what the chain raises | damage only: no fight, no skirmish event, no bark, no crime | [L] | — |
| P4a repeat filter, 20 calls | filter works; 27 calls no crash | [L] | — |
| P3 the player's own block | **FAIL**: a held block is not honoured (state 0x80, not 0x100) | [L] | — |
| P4b perfect block | not run (moot after P3; see why) | [not run] | — |
| P5h host capture of sync attacks | not run | [not run] | — |
| P6 host lock-on | **PASS** (engine selector picked the guard; it stayed on the partner) | [L] | — |
| P7 the host's first blow | no crime (the 0.46.x rule); one witness bark; relation read "not hostile" | [L] | — |
| P8 a queued failed-attack row | dispatched 4/4, no crash; its look **not determined** | [L] | — |
| P9 3-copy fight, window in front | 40/40; fight p90 resume 12.3 / post-hold 13.1 cm | [L][syn] | — |
| **C1** host lock-on | built; live gate set at 3.5 m facing, removed past 10 m | [L][native] | **on** (`mp_host_lock`) |
| **C2** victim decides | built; live end-to-end: 4/4 `applied=engine`, nothing applied twice; a player holding block is referred to the host's verdict (P3) | [L][unit][native] | **on** (`mp_victim_decides`) |
| **C3** attacker's recoil | built; not judged by eye (P8) | [unit] | **on** (`mp_block_recoil`) |
| **C4** fight snapping | C (hold) −10 %/−19 %, B (pose) +5 %/−13 %: **neither kept**; A not measurable here | [L][syn] | none shipped |

## Inputs (read)

`research/WO-162/combat-RE.md` whole; `docs/WO-163-findings.md` (Stage A, P1 FAIL, P5 joiner half, why P2–P4 were not built);
`docs/WO-161-findings.md` (the verdict path, the three snapping causes, the pocket designs); `docs/WO-164-findings.md` second pass (the
joiner-in-world harness, the plan verbs, the throwaway routine, the frame-cost method).

## Stage 0 — harness and baseline

### 0.1 Synthetic-peer verbs [syn]
`tools/wo118/synthpeer/Wo165Verbs.cs` builds each verb's packets; `SynthPeer --selftest-wo165` decodes every one with the **shipped**
decoders (32 checks): `swingat <t> <npc> local <row> <lag_ms> <hit|blocked|pb|broken> <hp> <st>` (the row, then the host's 0x72 verdict after
the row's own lag, swing id known); `verdict … [hid=N]` (the WO-165 values, an explicit id for duplicate tests); `avatarfight <t0> <t1> <npc>
<every_s> <hp> <st>` (an attributed NpcDamage 0x30, flag 0x04 — the host's own lever that sets its NPC on the avatar); `flee`; `teleport <t>
<npc|ghost> <dx> <dy>`; `fight … [phase]` and `fightz` (the ground height every 30° round the circle: no copy floats on a slope).

### 0.2 FIGHTSNAP p90, teleports excluded [L-field]
`tools/wo118/fightsnap165.py`: a window is excluded when it contains a load (the agent's `paused: load` … `running` + 10 s, a join's load,
`level loaded`), a fast travel, a mount/dismount, a ghost `TELEPORT` over 20 m, the "flying" leash line, or any single step over 20 m (the
line's own `step_max_cm`, `resume_max_cm` or `post_hold_step_max_cm`). The game log's Lua clock is mapped to the wall clock by the three
join-load anchors (offset spread 8 ms). On the 0.47.0 joiner log (9,034 lines):

| population | windows | resume_max_cm p50 / p90 / max | post_hold_step_max_cm p50 / p90 / max |
|---|---|---|---|
| fight (`fight_frames > 0`) — **the keeping measure** | 549 | 1.11 / **67.9** / 1,140.7 | 3.38 / **70.3** / 1,850.8 |
| any hold | 6,283 | 1.80 / 93.2 / 1,594.0 | 5.83 / 94.9 / 1,853.7 |

681 windows excluded (load 283, teleport 339, step > 20 m 41, mount 19). The unfiltered maximum, 106.7 m, is a join load — as WO-164 said.

### 0.3 Synthetic baseline fight [L][syn]
Joiner role (`KCDMP_TEST_JOINER_IN_WORLD=1`, a synthetic host), the throwaway, 3 copies, 40 swings, verdicts from the synthetic host, no input,
the window behind others: **40 / 40 `WO161-HIT … shown=yes applied=yes`, 0 duplicates**, `MP-WO161-STATS hit=16 blocked=16 …` at the
minute mark; fight p90 resume **8.61 cm**, post-hold **18.65 cm**; `ours_us_mean` 587–643 µs. (This run's copies were written at one fixed
height on a slope — see "Harness errors" — so the C4 comparisons below use their own baseline on terrain.)

## The instrument — a blow through the engine's hit processor [disasm][native]

WO-163 stopped because the result struct's constructor/destructor, the sub-hit vector builder and the collision-details record were unread.
Read this session in the collision handler (`OnCollision`, found by its `__FUNCTION__` label) and `RPGProcessHit` (found by its label):

* `this` is the pointer **stored at** combat actor + 0x478 (not the address), validated by RTTI `C_CombatRPG` and its back pointer (+8 = the
  combat actor). The offset is read from the handler's own `mov rcx,[rcx+disp32]` before the call.
* The result struct is **plain data**: the engine's own constructor (the call before the processor, `lea rcx,[rbp-10h]; call`) and no
  destructor; the caller gives it 0xA0 bytes. Our buffer is 0x100, built by that constructor.
* The sub-hit vector is **one 16-byte entry** the caller copies from a provider (factors A, B, C, condition) and frees itself; ours points into
  our own buffer (factors 1.0, condition 0) and the engine never grows it.
* The collision details: +0..+8 position, **+0x24 is the victim's entity id** (the repeat filter's key — per attacker and victim, not per
  contact), +0x64/+0x68/+0x6C subparts/material (−1 = none); the victim actor rides in the hit-in.
* The processor returns **at once with `true`** when the attacker's AttackType holds the engine's "no attack" sentinel — a copy's −1 (WO-163 P1).
  So the four attack fields are written first (raw, each property block named first; put back after the call).
* Flags out: +0 **blocking** = the victim's State is 0x100 *and* the zone / perfect / force test passed; +3 = the victim's PerfectBlockState;
  +2 ("damaged") stays 0 even when damage lands — not used.
* The damage is **applied inside the call** (the core hands the event to the RPG utilities) and lands one frame later.

Anchors are port-aware: the two functions are found by label, accepted only when their prologues match the bytes read (the build-identity
test for functions we *call*), the call sequence must occur exactly once in a 4 KB window of the handler, and the called target must be the
labelled function. `WO165-REPLAY armed:` names them (`CombatModule.dll+…`) at first use. Main thread, `fault::guarded`, every pointer looked up
in the frame it is used; any fault switches the replay off for the session (logged, said on screen). The slot hook (`hits.cpp`) opens a
**replay window** for exactly that attacker and victim on that thread: never discarded (WO-162 Q1.3 #9), the engine-built record captured,
the victim's damage measured (nothing put back), reported by sequence number. Native tests: the call-site parser on the real 80 bytes and on
moved / doubled / truncated / far variants, the structs' layouts, the outcome rule.

## Stage B — the probes (maintainer present, 2026-10-09)

The maintainer sat for P2–P3 by chat, then — after two GPU crashes when switching away from the game during the tests and one full system
crash (below) — for P9, P8, P6 and P7 as **one scripted sitting** (`tools/wo118/sit165.py`): the instruction line in the game's top-left corner,
timers, the logs read afterwards, no switching windows.

### P2 — what the chain raises [L]
*Setup:* solo, two townsmen (`ttkc_man_30` attacking `ttkc_man_31`), the replay through `RPGProcessHit`. *Steps:* none. *Reads:* the
victim's health/stamina (the hook's watch), `kcd.log` for skirmish, bark, crime and dialogue lines, both models after. *Result:* a slash
(attack type 1) placed 1.2 m away and facing took **hp −21.07, st −40.0**, landed one frame after the call. **No fight started, no skirmish
event, no assault bark, no crime line**; the two greeted each other afterwards. *Verdict:* the replay applies damage and nothing else; there is
nothing to suppress for C2 (the reaction, listener signals and the hit action are the collision handler's tail, which the processor never runs —
WO-162 Q1.5). The *slot-alone* half of P2 was not built: the reading above shows the slot attaches the stat change and the core applies it, so
a slot-only call applies nothing by construction [disasm].

### P4a — repeat filter, 20 calls [L]
Two calls 100 ms apart with the filter on: the second **filtered** (`returned=0`, the core never ran). 27 calls in all (20 at 0.7 s), **0
faults**, the game unharmed. Damage varied: a punch from a townsman holding a weapon does **0** (the damage-type map); a slash lands only when the
attacker is within reach and facing; stamina takes a blow before health. *Verdict:* PASS — P3 may touch the player.

### P3 — the player's own block [L] — **FAIL**
*Setup:* joiner role, one enemy copy (`ttkc_man_3`, weapon drawn) engaged on the player 1.6 m north (`MP-W132 engage on … skirmish vs me
added`), the player's Opponent = that copy. *His steps:* (1) stand, no block — GO; (2) hold block toward the copy — GO; (3) block facing away —
not run (moot). *Reads:* `model me` (State, BlockZone, Opponent), the engine's flags and record, health/stamina; Henry restored after each step.

| step | his state | engine | damage (two replays each) |
|---|---|---|---|
| 1 no block | Guard (2), opp = the copy | hit, hit | hp −16.73 st −40.0, hp −16.73 st −40.0 |
| 2 block held | **PreparingToParry (0x80)**, BlockZone 2 = AttackZone 2, opp = the copy | hit, hit | the same |

*Verdict:* **FAIL.** The engine counts a block only in **ParryInPlace (0x100)** (WO-162 Q1.4), and a held block enters it only in answer to an
attacker the engine sees **Striking** — a copy never is (WO-163 P1). The replay reads the player's block as not yet raised, every time.
*Consequence:* C2 refers a player holding block to the host's verdict and ships **on** (the maintainer's decision, below); P4b (the perfect block's window also opens on the attacker's real
strike) is not run. Earlier zero-damage replays in this probe were the harness's fault (the copy held 0.7 m above the ground; 2.5 m away).

### P5h — not run
No throwing / combo NPC was staged; the remaining sitting was spent on P6/P7/P8/P9 without switching windows. Stage A's host capture stays
as WO-163 left it.

### P6 — the host's lock-on [L] — **PASS**
*Setup:* host role, the synthetic joiner's figure (`avatarfight`, stamina-only blows) fighting the local guard `ttkc_man_3`; the explicit pair
`skirmish_add(host, guard, 1)` set by the probe lever. *His steps:* walk within 6 m, face the guard, lock on, no blow. *Reads:* the engine's own
line **`Player: Opponent change from '<none>' to 'ttkc_man_3'`** (4×), `model me` (opponent = the guard at 3.35 m), `model ttkc_man_3`
(opponent = the figure in every read from the engagement to after his blow). The maintainer: "the guard was targeting the host [the
figure], and then turned to me when I hit him." *Verdict:* PASS — C1 is built on and ships **on**. (The script's reads were ~12 s apart, a
fault of the script; the maintainer's account closes the gap.)

### P7 — the host's first blow on that guard [L]
The blow: the guard's health 100 → 96.6. **No crime**: `WO154-JUDGE src=1 assault on ttkc_man_3 -- not a crime: it fights 5 s later`
(the 0.46.x rule). One witness assault bark and several "what was that" barks. The engine's relation read 5 s after the blow: **not hostile**
(unexpected with the pair set and the lock-on working — pocket). The guard turned to the host and killed Henry with one blow (Henry was at
~55 hp after P9): the WO-163 A6 rule compares recent *damage*, and this harness's figure dealt **0 hp** (stamina only, so P7's health check could
see the host's blow alone) — any host blow out-damages 0. The mod's own death handling woke Henry ~300 m away; no Game Over. `mp_hostile_crime`
stays on (fail-closed); the README wording is unchanged.

### P8 — a queued failed-attack row on a copy whose swing is in flight [L]
`swing2 ttkc_man_31 300 <the punch> | <CombatAttackFailed, short swords / longsword>`, four times: every row dispatched **ok**, no crash. Whether
the swing *looks* interrupted into the recoil is **not determined** (the maintainer did not see it as a bounce; the townsman's weapon is neither
a short sword nor a longsword, the only rows that exist besides halberd and sword-and-shield — WO-162 Q4). C3 ships **on** (the maintainer's decision, below), unjudged by eye.

### P9 — snapping, window in front [L][syn]
The 3-copy fight round the player on open ground (terrain rings), 3 minutes, the window in front: **40 / 40** shown and applied once, 0
duplicates; fight p90 resume **12.27 cm**, post-hold **13.12 cm** (max 41 cm); 73 fps; `ours_us_mean` **579–583 µs**.

## Stage C — what was built

### C1 — host lock-on (`mp_host_lock`, default **on**) [native][L]
`wo165.cpp lock_tick`, called from `wo136::tick` (host only) at 4 Hz with the NPCs WO-136 knows in a partner's fight. The pure rule
(`wo165_rules.h lock_rule`, 22 native checks): **set** `skirmish_add(host, npc, 1)` when the NPC fights an avatar (opponent link or combat mode)
and the host is within **6 m** and facing it (60°); **removed** past **10 m**, when the fight ends, the NPC dies, or it has been over 30 m from
every player for 20 s (the WO-164 D2 rule); **forgotten — never removed — when the NPC turns on the host** (his own fight goes on). The host
leaves its skirmish only when no other pair of ours remains and no NPC fights him. The NPC's own target is never written. A missed lock says
why (`pair=not-set why=farther-than-6m|not-facing|npc-not-fighting|…`, once per 5 s). Three failed adds switch it off for the session (logged).
`WO165-LOCK npc=… pair=set|removed|forgotten|not-set why=… dist_m=… facing_cos=… skirmish=done|FAILED`. Pipe op 8 carries the switch.
*Found live:* a guard beating the figure read **CombatMode 0** with its Opponent on the figure — the first build required combat mode and never
set the pair; the rule now treats the opponent link as fighting (the signal WO-136 also ends a fight on).

### C2 — victim decides (`mp_victim_decides`, default **on**) [L][unit][native]
Joiner, in the verdict path (`GameBridge.Wo161.cs` → `GameBridge.Wo165.cs`): preconditions in order — switch, missile, attacker named, a copy
here, engaged, the row it played known, the player not down — else `fallback=<why>` and the WO-161 path. The replay writes the row's attack type
and zone (strength 1.0: lower strengths round to 0), reads the damage back after 0.8 s, and **one side applies the blow**: the engine
(`applied=engine`), or — when the engine applied nothing for a hit, or the measure never came — the host's verdict (`Wo165Rules.Decide`, tested
over every outcome × state × damage: never both, never neither). The outcome goes to the host on W164 kind 4 (`WO165-OUTCOME … host_said=`).
`WO165-REPLAY hid=… by=… engine=… dmg=… host_said=… fallback=-|<why>`; `MP-WO165-STATS`.
**A player holding block is referred to the host's verdict** (replay flag 0x10, native: the victim's State is PreparingToParry 0x80 or
ParryInPlace 0x100 → `refused=victim-blocking`, `fallback=victim-blocking`): P3 showed the engine never honours such a block from a copy, so
without the referral every blocked blow would land as a full hit; with it a block costs what the host's game says, as in 0.47.x.
*Live, after the switch-on (2026-10-09, the shipping build, a copy's four blows on the joiner, no input):* `WO165-REPLAY … engine=hit
dmg=4.7/27.2 … fallback=-` ×3 and `dmg=16.7/33.8` ×1, each `WO161-HIT … applied=engine`, **no WO-161 application for those blows** (no
`[playerhit]` line), `MP-WO165-STATS replays=4 applied_engine=4 fallback_apply_host=0 agree_host=2 disagree_host=2` — the host had said
"blocked" twice (the synthetic verdicts), the joiner was not blocking, his engine said hit. The referral itself was not exercised live (it
needs a held block: checklist 208).

### C3 — the attacker's recoil (`mp_block_recoil`, default **on**) [unit]
Joiner: a blocked / perfectly blocked outcome plays `combat_action_failed_attack` (action 27 / 15) chosen by the swing row's weapon tags
(`ActionRowCatalog.FailedAttackRow`: every weapon tag of the failed row in the swing's, most tags first, then the GUID) on the copy that swung;
none for unarmed and other weapons (`recoil=none why=no-row-for-this-weapon`). The installed tables give a row for exactly the four weapon sets.
The host-side recoil of the avatar's own blocked swing was **not built** (the host engine already plays its real NPC's answer; the avatar's swing
reaches the host as an attributed hit, not a verdict).

### C4 — fight snapping, one cause at a time [L][syn]
The same 3-copy fight (`tools/wo118/plans/plan.wo165.c4.txt`, `c4run165.py`), a fresh load each run, measured by 0.2's measure; keep only if
**both** p90s fall by ≥ 30 % with no new `WO161-HIT` / `MP-DMG` anomaly.

| run | resume_max_cm p90 | post_hold_step_max_cm p90 | WO161-HIT dup / anomalies | kept |
|---|---|---|---|---|
| baseline (none) | 4.86 | 12.19 | 0 / 0 | — |
| **C** the hold ends with the swing (start + hit + withdraw, 300–900 ms; the punch 690 ms) | 4.37 (−10 %) | 9.82 (−19 %) | 0 / 0 | **no** — reverted |
| **B** the swing waits the 120 ms playout delay (the row's age cannot be mapped to this clock) | 5.08 (+5 %) | 10.64 (−13 %) | 0 / 0 | **no** — reverted |
| **A** the state block applied at its ring sample | — | — | — | **not built**: a copy's combat state comes from the host's per-NPC event, which this harness sends unchanged once a second, so a fix that times *changes* of state cannot move the measure here |

`WO165-SNAP cause=C before=4.86/12.19 after=4.37/9.82 kept=no` · `cause=B before=4.86/12.19 after=5.08/10.64 kept=no` · `cause=A … not measurable`.
The synthetic fight snaps little to begin with (p90 ≈ 5 / 12 cm against the field's 68 / 70 cm): the field's snapping comes from what this peer
does not produce — network jitter, a host who moves, state that changes. The next measurement needs the peer's `--jitter-ms` / `--spike-*` and
changing `ncombat` rows, or a two-player session.

## Stage D — gates

* **Offline:** client **1,320** (0.47.5: 1,282), setup 77, farkle 59, relay 63, native **590** (537), Lua synthetic **52** suites (51 + WO-165's
  11 checks), synthetic-peer self-test 32. `Test-NativeGuards` clean.
* **Live, joiner (the shipping defaults, 3-copy fight):** 37 verdicts in the window, all `applied=yes`, **0 `applied=dup`**, no blow both
  engine-applied and WO-161-applied (C2 off: 0 engine); fight p90 resume **4.46** / post-hold **11.09 cm** (baseline 4.86 / 12.19);
  `ours_us_mean` 540–607 µs.
* **Live, host (C1):** `WO165-LOCK npc=ttkc_man_3 pair=set why=near-and-facing dist_m=3.5 facing_cos=0.79 skirmish=done`, then after 19 m
  `pair=removed why=host-left-10m skirmish_leave=done`; `lock_set=1 lock_removed=1 lock_failed=0`; the guard stayed on the figure.
* **Frame cost:** the DLL's `FRAME ours_us_mean` — fight 540–607 µs, P9 fight 579–583, town (idle, no session) 532 (0.47.5: 642–674): within
  10 %, lower. The menu cannot be sampled by script (timers halt). fps 70–76 with the window in front.
* **Soak:** waived under the standing rule (below); C2 is off, so the WO's 15-minute C2 soak does not apply.

## Incidents

* **A full system crash, 16:45, during the P3 setup.** The game's frame loop stopped at 16:44:46 (the DLL's pipe saw the main thread hang) with
  no fault logged; Windows reset ~40 s later (Kernel-Power 41, no bugcheck, no dump). **No replay ran in that game session** (0 `WO165-REPLAY`
  lines; the instrument was not even resolved). The maintainer later reported the GPU crashing for a moment whenever he switched away from the
  game during the tests — the sitting was redesigned so he never had to.
* **Saves:** the whole saves folder was backed up before the first launch (753 files, byte-equal). After the crash and at the end: every real
  file byte-identical; only the throwaway `playline4` gained autosaves (023–028 …). Henry died once in the throwaway (P7). Steam was running all
  session (started 08:55; restarted with Windows after the crash); no new cloud restore seen in the comparison.
* **Harness errors (mine), fixed:** the stand-in figure flipped north/south every second (a 1 cm ping-pong path); copies held at a fixed height
  floated 0.7 m on the slope (hence `fightz`); the P6 reads were 12 s apart.

## The defaults (the maintainer's decision, 2026-10-09)

The first 0.48.0 build shipped C2 and C3 **off** (the WO's "a failed probe ships switched off"). The maintainer: *"Testing cannot happen
WITHOUT a real partner. This should be enabled by default with a switch to disable it."* — his standing rule (new mechanisms ship on, each
with its switch). Both are **on** in the second 0.48.0 build; `mp_victim_decides off` and `mp_block_recoil off` turn them off. C2 got the
blocking referral above so that "on" cannot make blocking worse than 0.47.x.

## Pocket list

1. **A held block on the joiner** — the engine honours it only against a Striking attacker. Two ways on: (a) the hit-in's "victim blocking"
   byte set by our code from the player's own state (PreparingToParry/ParryInPlace, BlockZone = AttackZone, Opponent = the copy), the engine
   still computing the defence, stamina and weapon — our code would judge *whether* he blocked, which WO-162 ruled out as a principle; or (b)
   put the copy's combat model into Striking for the row's hit window (Level C, `OnCollision`) — unread. A decision for the maintainer.
2. **Bows** wait on (1): a missile on the joiner is the same question.
3. **The relation read** said "not hostile" 5 s after the host's blow while the pair was set and the lock-on had worked (WO-163 pocket 3: the
   relation object's layout was accepted either way, not recorded which).
4. **C4 cause A** and a jitter-bearing snapping measure (above).
5. **A parked (hidden) NPC still offers a talk prompt** — seen when the synthetic host streamed only three NPCs; in a real session a hidden
   local body next to the host's real one could do the same.
6. **The guard turning on the host after one blow** follows A6 because the harness's figure dealt 0 hp; with a partner who hurts it, the WO's
   expectation (it stays on the partner unless the host clearly out-damages) is the checklist's to confirm.
7. The WO-137 `set_send_callback` fault flood on a loaded world (34,754 in one run) — guarded and counted, recorded since WO-160; not touched.
8. The maintainer's GPU crashing when the game loses focus during these tests (and the system crash): worth a driver check; the WO-164 frame
   note ("soak needs focus") is the same family.

## The 0.48.0 build

From a fresh clone of `ccdfcb1` (`release\c0480`) with the three git-ignored start saves (sha1 equal to the 0.47.6 build's), `tools\Build-Installer.ps1
-SoakWaiver "The maintainer's standing rule, stated in WO-161 on 2026-10-08 and applied to WO-165 on 2026-10-09: release candidates are built
without the soak test (0.48.0)."` The header says **0.48.0**. **`release\KingdomComeTogether-Setup-0.48.0.exe`, 106,434,966 bytes, sha256
`562c722961fb6ca017d0c247cf948d7a8515efe3c2fbcdb4592c52b344b2b157`.** Local only; not tagged; **unsigned** (no signing settings). Transcript
`release\BUILD-0.48.0.log`, waiver `release\SOAK-WAIVED-0.48.0.txt`. Inside the build: relay 63, agent 1,320, setup 77, native 590, **all 52
synthetic suites**, the static checks, the installer cases, the payload smoke (`protocol=v10 release=0.48.0`); no FAIL line; no account or
machine name in the payload. The maintainer's installed `Mods\kdcmp` was restored after the live runs (three files, hashes equal to the copy
taken before).
