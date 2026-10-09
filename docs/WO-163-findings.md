# WO-163 — Shared combat, built for real (on the WO-162 contracts)

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L]** live, on this machine's Modding Tools build (1.5.5), throwaway playline only; **[L-field]** counted from
the testers' logs on disk (matched by event; no log line is copied, no player name, address or path is written here —
entity names such as `ttkc_man_23` are the game's own); **[code]** read in our code; **[unit]** / **[syn]** /
**[native]** a test that gates the build; **[disasm]** the WO-162 reading (`research/WO-162/combat-RE.md`, the design
source — every engine fact below cites its section); **[not tested]** no run; **[proxy]** a stand-in for data that does
not exist. The version is the maintainer's (0.47.0); no tag, nothing uploaded.

This file is written stage by stage: a section is appended when its stage ends, so a cut-off session still leaves a
complete record.

## Inputs — and the one that does not exist

**"Tonight's 0.46.5 bundles (both machines)" are not on this disk.** Every zip under `logs/` (and `logs/old/`) was
searched: **0 lines of `WO161-HIT` and 0 of `MP-WO161-STATS` in any of 25 bundles**, and the newest bundles report release
0.45.8 (the afternoon of 2026-10-07) and 0.45.1 (the pair stamped 23:32). 0.46.5 was built locally (WO-161: "no game was
started") and no tester log from it has been put in `logs/`. So the per-blow table the WO asks for **cannot be built as
specified**, and none of its numbers is invented here.

What is built instead is a **[proxy]** baseline from the two sessions WO-162 counted (the 0.45.8 pair whose host log starts 17:34
and the pair stamped 23:32, which reports release 0.45.1): the same 45 host-forwarded NPC blows on avatars, each with the host's verdict (the WO-161 rule applied to the
logged hp/st: `Classify`), the attacker kind, which swing the **old 1.2 s ledger** and the **new lag rule** (A3) pair it
with, and the **host-replayed guard state** of the victim avatar within ±300 ms (`WO121-MOTION body=… block=`, the state
the host's engine decided against). What the proxy cannot give: the joiner's *own* block state (0.45.x logs carry none;
the DLL logs `block=` only on a replayed transition and never logs the release when combat mode ends) and the joiner's
`shown=`/`reason=` (a 0.46.5 line). **When a 0.46.5 bundle pair is dropped into `logs/`, the table is re-run from the real
`WO161-HIT` lines** (the script is a counter, not a copier) and replaces this one.

### The baseline (proxy) — the 45 blows

Two sessions (the 0.45.8 pair whose host log starts 17:34, and the pair stamped 23:32 that reports release 0.45.1), host side only; every host-forwarded NPC blow on an avatar. Counted,
no log line copied; the attacker is named by kind.

| | Count |
|---|---|
| blows | **45** (35 + 10); 39 by men, **6 by an animal**, 0 missiles |
| verdict (the WO-161 rule on the logged hp/st) | **16 blocked** (no hp lost, stamina paid), **29 hit** |
| paired by the **0.46.5 ledger** (newest swing within 1.2 s) | **29**, of which **4 to an unreadable sync row**; **16 "no-swing-captured"** |
| paired by the **lag rule** (A3: the row's own start+hit lag, +-0.35 s) | **33**; 5 `swing-unmatched` (4 rows exist but none fits, 1 only an unreadable sync row); **7 `no-swing-captured`** (the animal's 6 bites and one first blow 84 s after the NPC's last row) |
| lag-fit error of the 33 | median 0.16 s, none over 0.35 s |
| rows sent by the host | 64 and 347; **5 and 71 unreadable sync rows** (8 % and 20 %) |

**How often the host's verdict disagreed with the victim's visible guard** — the number that motivates C.2. With 0.45.x logs the
nearest observable is the host's *replayed* guard on the avatar (`WO121-MOTION body=… block=` within +-300 ms; the state the host's
engine decided against): **blocked while the replayed flag was not up: 13 of 16; hit while it was up: 7 of 29; so 20 of 45 (44 %)**
verdicts did not match the replayed flag. This is a **lower-quality proxy** and is **[not determined]** as the joiner's own guard:
the DLL logs `block=` only on a transition and never logs the release when combat mode ends (`motion.cpp`, the block branch), the
flag is absent from the joiner's own logs, and a blocked verdict with the flag "down" may be a stale-down log. What it does say:
the host's verdict and the replayed guard are not a reliable pair; a decision made on the victim's own machine against the
victim's own block (Stage C.2, if P3/P4 pass) has a real gap to close. **The 0.46.5 `WO161-HIT` re-run replaces this.**

Per blow (the order of the logs; sess S1 = 35 blows, S2 = 10; the guard column is the replayed flag):

| # | sess | attacker | verdict (hp/st) | host-replayed guard (+-300 ms) | 1.2 s ledger | lag ledger | swing age s | row lag s |
|---|---|---|---|---|---|---|---|---|
| 1 | S1 | man | blocked 0.0/24.0 | down | paired-1.2s | paired-lag | 0.25 | 1.07 |
| 2 | S1 | man | blocked 0.0/23.1 | down | paired-1.2s | paired-lag | 0.31 | 1.09 |
| 3 | S1 | man | blocked 0.0/24.3 | down | no-swing-captured | swing-no-timing-fit | 1.35 | - |
| 4 | S1 | man | hit 6.3/29.5 | down | paired-1.2s | swing-no-timing-fit | 0.98 | - |
| 5 | S1 | man | blocked 0.0/23.4 | down | paired-1.2s | paired-lag | 0.4 | 1.1 |
| 6 | S1 | man | blocked 0.0/24.1 | down | no-swing-captured | paired-lag | 1.27 | 1.46 |
| 7 | S1 | man | blocked 0.0/24.3 | down | paired-1.2s | paired-lag | 0.98 | 1.14 |
| 8 | S1 | man | hit 0.5/23.5 | down | no-swing-captured | paired-lag | 1.53 | 1.45 |
| 9 | S1 | man | hit 8.9/0.0 | down | paired-1.2s | paired-lag | 0.45 | 1.51 |
| 10 | S1 | man | hit 8.9/0.0 | down | paired-1.2s | paired-lag | 0.42 | 1.43 |
| 11 | S1 | man | hit 8.9/0.0 | down | paired-1.2s | paired-lag | 0.42 | 1.43 |
| 12 | S1 | man | hit 12.5/0.0 | down | no-swing-captured | paired-lag | 1.56 | 1.79 |
| 13 | S1 | man | hit 8.6/0.0 | down | paired-1.2s | paired-lag | 1.19 | 1.1 |
| 14 | S1 | man | blocked 0.0/24.3 | down | paired-1.2s | paired-lag | 0.17 | 1.03 |
| 15 | S1 | man | hit 1.8/19.3 | down | paired-1.2s(unreadable-row) | paired-lag | 0.07 | 1.42 |
| 16 | S1 | man | blocked 0.0/24.2 | down | paired-1.2s | paired-lag | 0.26 | 1.07 |
| 17 | S1 | man | blocked 0.0/24.2 | down | paired-1.2s | paired-lag | 0.38 | 1.42 |
| 18 | S1 | man | blocked 0.0/23.4 | down | paired-1.2s | paired-lag | 0.51 | 1.42 |
| 19 | S1 | man | hit 7.1/4.7 | down | no-swing-captured | paired-lag | 1.23 | 1.42 |
| 20 | S1 | man | blocked 0.0/23.4 | down | no-swing-captured | sync-unreadable | 2.72 | - |
| 21 | S1 | man | hit 2.1/17.8 | down | paired-1.2s | paired-lag | 0.37 | 1.07 |
| 22 | S1 | man | hit 12.3/0.0 | down | paired-1.2s | paired-lag | 0.46 | 1.42 |
| 23 | S1 | man | hit 8.9/0.0 | down | paired-1.2s(unreadable-row) | paired-lag | 0.3 | 1.09 |
| 24 | S1 | man | hit 32.7/0.0 | down | no-swing-captured | swing-no-timing-fit | 2.21 | - |
| 25 | S1 | man | hit 33.7/80.0 | down | no-swing-captured | no-row | - | - |
| 26 | S1 | man | hit 5.3/40.0 | down | paired-1.2s | paired-lag | 0.29 | 1.17 |
| 27 | S1 | man | hit 5.3/40.0 | down | paired-1.2s | paired-lag | 0.3 | 1.17 |
| 28 | S1 | man | hit 28.6/40.0 | down | paired-1.2s(unreadable-row) | paired-lag | 0.3 | 1.16 |
| 29 | S1 | man | hit 18.7/36.7 | down | paired-1.2s(unreadable-row) | swing-no-timing-fit | 1.0 | - |
| 30 | S1 | man | blocked 0.0/33.6 | down | paired-1.2s | paired-lag | 0.34 | 0.9 |
| 31 | S1 | man | blocked 0.0/33.3 | up | paired-1.2s | paired-lag | 0.16 | 1.55 |
| 32 | S1 | man | blocked 0.0/30.0 | down | paired-1.2s | paired-lag | 0.15 | 1.18 |
| 33 | S1 | man | hit 16.4/40.0 | up | paired-1.2s | paired-lag | 0.3 | 1.17 |
| 34 | S1 | man | blocked 0.0/37.1 | up | paired-1.2s | paired-lag | 0.28 | 1.16 |
| 35 | S1 | man | blocked 0.0/37.2 | up | no-swing-captured | paired-lag | 1.26 | 1.17 |
| 36 | S2 | man | hit 27.4/40.0 | down | paired-1.2s | paired-lag | 0.43 | 1.04 |
| 37 | S2 | man | hit 23.6/40.0 | down | paired-1.2s | paired-lag | 0.11 | 1.23 |
| 38 | S2 | man | hit 50.5/40.0 | down | paired-1.2s | paired-lag | 0.14 | 1.21 |
| 39 | S2 | man | hit 23.6/40.0 | down | no-swing-captured | paired-lag | 1.37 | 1.18 |
| 40 | S2 | animal | hit 23.3/40.0 | up | no-swing-captured | no-row | - | - |
| 41 | S2 | animal | hit 23.3/40.0 | up | no-swing-captured | no-row | - | - |
| 42 | S2 | animal | hit 23.3/40.0 | up | no-swing-captured | no-row | - | - |
| 43 | S2 | animal | hit 23.3/40.0 | up | no-swing-captured | no-row | - | - |
| 44 | S2 | animal | hit 23.5/40.0 | up | no-swing-captured | no-row | - | - |
| 45 | S2 | animal | hit 23.6/40.0 | up | no-swing-captured | no-row | - | - |


## Stage A — certain fixes (code and unit gates done; the solo live check is the next step)

Marks: **[unit]** the engine-free suites below; **[syn]** the Lua synthetic suites; **[native]** `KCDMP_NativeTests`; **[L]** live —
**not yet run** for Stage A (the game is not running on this machine; the live check needs the maintainer's say, and a build that
loads the new DLL, agent and pak).

| Item | What was built | Gate | Mark |
|---|---|---|---|
| **A1** sync-attack capture | `kPathSync` reads the row GUID at descriptor **+0x7C** (Q3.1/Q3.3). The legacy +0x84 read is kept as a check: the DLL sends it as an optional 16-byte tail of the 0x96 frame; the agent logs `WO163-SYNCGUID` if **both** hit its catalog (never expected) | all **24** distinct field sync-row dumps (16 are the two sessions WO-162 counted) resolve at +0x7C in `combat_action_sync_attack` **24 of 24**, the legacy read hits **0 of 24**, `BothResolve` false; the legacy read is half of the same GUID (8 bytes overlap) in 24 of 24 | [unit] client + [native] |
| **A2** master strikes are swings | the DLL now captures an NPC's perfect-block-class action (flags bit 0); the agent sends it as an `NpcAttack` row **only** when the row's table says master strike (action types **55** and **62**, the one that kills), drops the block itself and its sync half (counted) | the real table: 206 perfect-block rows, **90** master-strike rows, none of the 14/23 | [unit] |
| **A3** pairing by time | the host ledger pairs a hit with the swing whose **own `attack_time_to_start + attack_time_to_hit`** (catalog `HitLagMs`) the hit's age matches +-0.35 s, best fit wins, look-back 4 s; a row with no lag in its table keeps the 1.2 s window; a row no table knows is never paired; `no-swing-captured` only when the NPC has no swing at all, else `swing-unmatched`. The victim judges the verdict against the played row **its lag fits** (a played-row log per NPC), so a 3 s sync attack is not "stale" | **the field set: 45 blows -> 33 paired / 5 `swing-unmatched` / 7 `no-swing-captured`** (the 0.46.5 window: 29 / 16); every pairing inside 350 ms | [unit] |
| **A4** generic swing | a verdict that arrives with nothing shown (`no-swing-captured`, `swing-unmatched`, `row-not-received`, `row-stale`) plays **one generic row on the copy before the damage is applied**: an animal gets the attack table's plain bite (`CombatAttack`, `aZ1+bite+attack_heavy+oppMale+oppFemale`), a man the unarmed punch (`FreeAttack`, `l_noweapon+r_noweapon`), chosen by content from the catalog; `shown=generic` (reason keeps why), `generic=` in `MP-WO161-STATS`. Not for missiles, a nameless attacker, a bare legacy hit or a row the copy refused to play. Toggle: `kcdmp-client.json` `GenericSwingEnabled` / `--no-generic-swing` (default **on**) | the catalog holds both rows (the installed tables and a synthetic pak), deterministic; the reason set | [unit] |
| **A5** `MP-FIGHTSNAP` | the line of Q6, one per puppet per 10 s window when it held a hold or was engaged, computed in the writer from `cur`, `pose`, `wrote` (no render hook): `window_s frames fight_frames holds hold_ms_max corr_max_cm corr_p95_cm resume_max_cm resume_mean_cm step_max_cm post_hold_step_max_cm blend_max_cm snaps_gt5m`. Definitions are `tools/wo118/fight118.py`'s, horizontal (x,y); a hold = >= 5 unwritten frames; swing-hold frames read the engine's position (they never reach `write_one`) | on a real writer trace (1026 frames, 6 holds): longest hold 929.8 ms, resume max 8.57 cm / mean 3.85 cm, post-hold step 12.74 cm — **the native accumulator reproduces fight118's numbers within 1 cm** (they agree to the 0.01 cm); the applied position equals the render position in 0 of 1026 frames off by 0.1 mm (the claim that no render hook is needed, confirmed on a second file) | [native] |
| **A6** `decide()` hysteresis | an NPC fighting someone turns only when the challenger's **recent damage (6 s)** exceeds the current opponent's by **25 %** (on top of the count rule and the 3 s hold); a blocked blow (0 hp) or a swing turns nothing; of two challengers the one that hurt it most; nobody-to-be-sticky-to is unchanged. Avatar hits pass their **measured** hp (`apply_attributed`); `WO136-TARGET` logs `damage=<to>/<cur>` | 9 new cases incl. the margin edge (11.1 vs 11.25 stays, 12 turns), 0-damage blows, the hold, the two-challenger pick; the 5 old cases unchanged | [native] |
| **A7** crime window reads the engine | `hits::skirmish_hostile(host, victim)`: the engine's own relation test (`C_SkirmishSituation`, found by RTTI; the object at `C_SkirmishManager`+0x80 checked by its vtable this frame; the function accepted only when its prologue is the one read in the game's code), read-only, fault-guarded, main thread, both souls looked up in the same frame; reached through a new op family **0x2B -> 0xAC** (`wo163.h`). At the 5 s mark the Lua judge asks (`w163_hostile`), parks, and **an answer of "hostile" drops the assault** (`WO163-JUDGE ... not a crime: the engine says ...`); "not hostile", "could not be made" or **no answer in 1.5 s judges exactly as 0.46.5**. Toggle `mp_hostile_crime on|off` (default **on**, fail-closed); counters in `MP-WO163-STATS` | the Lua suite with a stubbed answer: hostile -> exempt, a late answer judges nothing twice, timeout -> judged and said so, unanswered -> judged, off -> no question (`B3/A7`, 7 checks + the switch in `B5`); the vtable offset and the prologue read from the DLL on disk (`+0x08` of the situation vtable is the relation function; its first 33 bytes match) | [syn]; the call itself **[not tested]** in the game: P7 |

### What Stage A does not do, stated so it is not read as done

* **A6's host side:** the DLL marks the host's own blow on an NPC but does not read the NPC's health (only an NPC's blow on an
  *avatar* is measured, by the watch). So an unmeasured landed hit counts a **nominal 10 hp** (a swing counts none); an avatar's
  hit counts its measured hp. The rule and the margin are the WO's; measuring the host's damage on an NPC is a pocket item.
* **A1's "catalog hit" check** needs the catalog, which only the agent holds, hence the tail on the frame (the WO's wording
  "keep the old read behind a one-line check" is met by the agent line; the DLL logs nothing of its own).
* **A5 measures the horizontal step** (x, y) like fight118; the correction (`corr_*`) is 3D.
* **A2 includes action type 62** (master strike that kills) beside the WO's 55: same exchange, same row family.

### Stage A gates (the whole repository, after the changes)

client **1,258** (was 1,236: +22), setup 77, farkle 59, relay 63, native **530** (was 421: +109: A1 offsets 24 dumps x 5 checks, A5's
trace and definitions, A6), Lua synthetic suites **50 of 50**, 0 failures. Native build with `Test-NativeGuards` clean (no raw
`__try`, no pointer-taking `build_argument(`).

### Stage A solo live check [L] — run 2026-10-08, with synthetic peers

**Setup.** This machine's Modding Tools build (1.5.5), started minimized, the new `KCDMP.dll` injected by hand (a copy of the build, a
new file name per rebuild), the new pak installed with `tools/Build-And-Install-Mod.ps1` (the previous `Mods\kdcmp` was copied aside
first), a **throwaway** save (`playline4/quicksave022`, the one earlier sessions used) loaded by `wh_sys_LoadGame 4 quicksave022`
(the log line read back), a local relay, the agent as the **joiner** from a copy of the Release build, and `tools/wo118`'s synthetic
authority peer driving a plan. **The playlines were backed up first (727 files, 1.3 GB, the copy checked equal); afterwards playlines
0-3 are byte-identical to the backup, and the throwaway gained three autosaves (023-025, written by the agent in shared-world host
mode before the harness switched it off) — `quicksave022` untouched.** The game window was never brought forward; it restored
itself after the load and was minimized once (the one push-down after my own load); the frame rate below is therefore the
background-limited one.

| What | Result | Mark |
|---|---|---|
| New pak, DLL, agent | `MOD INIT`; hit slot, attribution and capture armed on the new DLL; the agent connected to the DLL; catalog **1,710 rows** (the two new tables add 222); no Lua error | [L] |
| Probe surface | `mp_w163_probe status` through the console: Lua -> agent -> pipe op 0x2B -> DLL -> back: **`skirmish_relation=armed`** (the relation function's prologue verified on 1.5.5) | [L] |
| `model me` (P1/P3 instrument) | `ca=1 pca=1 model=1 state=1 gz=2 bz=-1 bh=1 bm=0 pb=0 az=2 at=-1 as=0.000 ah=1 cm=0 opp=0x0`: **all eleven property blocks name themselves** (no `!`), widths right (float strength, byte bools). An NPC with no combat actor: `ca=0` | [L] |
| A7 relation read | `relation <npc>` for an NPC in no fight: answered, **not hostile**. The first ask (NPC ~95 m away, the first session) was "could not be asked" — the DLL then said nothing about why; every refusal now logs its reason (`WO163-RELATION ... NOT answered: ...`), and the relation object is accepted whether it lies inside the manager at +0x80 or is held by a pointer there (the layout was **not** recorded which; see the pocket list). The **hostile** answer, and the judge's own 5 s path, are **not exercised** (they need a fight with the host: P6 / P7) | [L] partial |
| **A5** `MP-FIGHTSNAP` | lines appear per 10 s window for the bound puppet with `holds=2-4`, `hold_ms_max` 906-938 ms, `resume_max_cm` 3.0-10.1, `post_hold_step_max_cm` 6.2-18.0, `snaps_gt5m=0`. On the first run the fight118 harness read the **same trace**: holds of 910-938 ms (the line: 931-938), worst resume step 9.5 cm (line: 10.06 in the window that held the bind), a render step of 16.4 cm after a resume (line: 16.44 in a window wholly inside the trace) — **the line agrees with the harness to the numbers it can be aligned on**. Two artefacts found and fixed in the accumulator (native tests): a bind's first write and the two frames the physics body lags it read **tens of metres** as a "correction" (`corr_max` 65 m and 81 m); a correction over 5 m is now a placement | [L] + [native] |
| **A3** + **A4** (victim) | a plan sends one swing row (the unarmed punch, lag 0.22 s) for the bound copy and four verdicts: the row **played natively** (`dispatch=native-row result=ok`); verdict 1, 0.3 s after it: **`shown=yes`** (the copy's row fits); verdicts 2 and 3, seconds later with only that old row behind them: **`shown=generic reason=row-stale`** (the copy lunged once before the damage); verdict 4 names nobody: `shown=no reason=no-attacker`. Each applied **once** (`applied=4`, `dup_ignored=0`; hp 15.0 / st 12.0 = the sum sent); `MP-WO161-STATS ... generic=2 shown=3 not_shown=1 not_shown_why=[no-attacker:1]`. That the lunge is *visible* is **[not determined]** here (the window was minimized): the dispatcher said ok, the same answer a played row gives | [L] |
| A1 / A2 capture | **not exercised live**: a host-side NPC fight (and a sync attack) was not staged — the host's combat automation wants a `w154_` test NPC and the spawn route was not worth a rabbit hole. Covered by the unit gates (24 of 24 field dumps; the frame tail; the master-strike filter on the game's own table). P5 is the live check, with a throwing / combo NPC | **[not tested]** |
| Frame rate | **not recorded** (background-limited, ~24 fps while minimized; ~72 fps while the window was visible between runs): the WO-148 rule — menu, town, 3-enemy fight, before / after — needs the window in front and is for the maintainer's session | **[not tested]** |

**Noted, not mine.** The native log shows `wo137::set_send_callback` read faults at ~11.9 k per minute from the moment the save loaded
(`FAULT-SUM ... 0 switched off`): WO-160 recorded the same flood (the quest reader on a loaded world), guarded and counted; no
`wo163` site faulted. **Harness note:** in shared-world mode (default on since 0.30.0) the agent *claims host* and outranks a plan
peer, so verdicts (host-only on the relay) never reach it; `mp_shared_world off` restores the older "lowest id is the authority"
rule the WO-118 harness relies on.



## Stage B — the eight probes (the instruments are built; **no probe has been run**)

Each probe is run one at a time with the maintainer at the keyboard; the session asks before each; its setup, reads, result lines,
verdict and Stage C consequence are written here before the next. Nothing from Stage C is built before its probe passes.

| # | Instrument (all in `mp_w163_probe`, answers in the agent log as `WO163-PROBE` / `WO163-MODEL`) | Needs |
|---|---|---|
| P1 | `model <copy>` read during a host row on an engaged copy: `state` 8 (Striking) with `az at as ah` set? | a joiner's copy fighting the avatar (WO-147 engages it); read at the row's start+hit |
| P2 | **new instrument, built only after P1** (a hand-built record through slot 0x150 on dummy NPCs) | dummy NPCs, the hp/stamina watcher |
| P3 | **new instrument, built only after P1** (`RPGProcessHit` on the local player under the death guard); `model me` before/after for the block fields | the maintainer holding block |
| P4 | as P3, on dummies first | |
| P5 | none new: a throwing / combo NPC; `MP-ACTION ... table=combat_action_sync_attack` + `dispatch=native-row` on the joiner | a sync-attack NPC |
| P6 | `pair <guard> on 1` while the guard fights the avatar; `model <guard>` for its `opp`; the controller's `Player: Opponent change ...` line; the guard stays on the avatar >= 10 s | a guard beating the partner |
| P7 | the host's first blow; `relation <guard>` at the 5 s mark; the `WO139-JUDGE` / `WO163-JUDGE` lines | as P6 |
| P8 | `swing2 <npc> <gap> <attack row> | <CombatAttackFailed row>` on a copy | a joiner's NPC copy |

P2 / P3 / P4 need native code that calls the engine's combat path (`RPGProcessHit`, slot 0x150) — **not built**: WO-162's contract
names the call but not the result struct's size or constructor (`[not determined]`), so its first instrument is designed from P1's
result, on the main thread under `fault::guarded` with the pointers validated in the frame they are used.

### P1 — does an engaged copy playing a host row enter Striking? **FAIL** (asked, answered 2026-10-08, synthetic peers, maintainer's go)

* **Setup [L].** The throwaway save, Henry standing at (2338, 2048); the plan's host NPC held 3.6 m from him with a host combat state
  targeting the joiner's avatar and five host rows (four unarmed punches and one combo sync attack). No join session exists in this
  harness, so the agent's own engagement does not fire (`JudgeEngage` wants a joiner session); the copy was engaged through the DLL's
  own engage op (`mp_w163_probe engage`), the same call the agent makes: `ok=True first=True skirmish=True dist=3.6 m` — the copy joins a
  skirmish against the player and its combat state is held. (The first attempt read `dist=350.2 m reason=6`: the **agent's leash had
  dragged Henry 350 m toward the synthetic host's avatar** — `mp_leash off` and a teleport back fixed the harness; the leash itself is
  working as designed.)
* **Reads.** `mp_w163_probe modelwatch`: the DLL's model reader every ~45 ms for 26 s, logging only changes, with each host row's arrival
  marked on the same clock. **327 reads, 1 change** (the first): `ca=1 pca=0 model=1 state=2 gz=1 bz=-1 bh=1 bm=0 pb=0 az=-1 at=-1 as=0.000 ah=1
  cm=1 opp=0x7777(me)` — *Guard*, combat mode on, **the Opponent is the local player**, every property block names itself. Through all five
  rows (and a first run with the copy *not* engaged: 331 reads, state 1 / Idle, no opponent) **State never left its value, AttackType stayed
  −1, AttackStrength 0.000, AttackZone −1 (engaged: −1; unengaged: its default 2)**.
* **Verdict: FAIL.** A copy that plays a host row through the cosmetic route does not enter `Striking` (State 8) and its attack fields are not
  set (WO-162 Q1.7 / Q1.4: the hit core *reads* them). **Consequence for C.2:** it must write `AttackType / AttackZone / AttackStrength /
  AttackHandSlot` on the copy's model itself before the Level-B call, from the row it just played (the catalog row carries `attack_type_id`,
  `attack_zone_id`; strength 1.0; the hand slot from the weapon) — "recorded, still allowed" in the WO. **Level C (`OnCollision`) stays a no-go**: it
  needs `State == Striking`, which only a combat action committed by the combat module produces. The engaged copy's `Opponent == me` and
  `State == Guard` are exactly what the block test reads on the *victim* side (Q1.4); on the attacker side they are what a replay would use.
* **Not answered:** whether the fields *can* be written (their property blocks are named and writable by the same reader's offsets; no
  write was made) — that is the first step of the P3 instrument.

### P5 (joiner half) — does a sync-attack row, resolved at +0x7C, play on a copy? **PASS**

A plan sent the catalog row of one of the 24 field dumps (`combat_action_sync_attack`, `CombatAttackComboGen`, a hook combo): the agent
resolved it (`spec="CombatAttackComboGen, r_noweapon+c01+sZ2+leftGuard+eZ1+aZ5+hook+attack_heavy+l_noShield+oppMale"`) and the copy played it:
`dispatch=native-row result=ok` (no `dropped-unknown-row`) — the joiner half of A1's live check. **The host half — the DLL reading the GUID at +0x7C
from a real committed sync attack — is not tested** (needs a throwing / combo NPC fighting the host).

<!-- STAGE-B-RESULTS -->
