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

### Stage A solo live check — not run

No game was started in this session so far. What the live check needs, and what it will read:

* the Modding Tools build (never retail), a **throwaway** playline loaded (the playlines were backed up wholesale before any launch:
  727 files, 1.3 GB, the copy checked equal; the loaded save is read back before any test action);
* the new `KCDMP.dll` injected, the new agent running, `kdcmp.lua` re-packed (`tools/Build-And-Install-Mod.ps1`: a Lua edit needs the
  pak rebuild and a game restart);
* a scripted fight with one NPC (the host's own combat automation, `mp_w154_check hostfight <npc>`), then: `MP-WO163-STATS` /
  `MP-ACTION ... table=combat_action_sync_attack` lines show a sync attack resolved (**`dropped-unknown-row` = 0**), `MP-FIGHTSNAP`
  lines appear for a bound puppet, the frame rate is recorded;
* the game window brought to the front by the maintainer (never by this session): an unfocused window runs at ~26 fps whatever the mod does.

<!-- STAGE-A-LIVE-RESULT -->

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

<!-- STAGE-B-RESULTS -->
