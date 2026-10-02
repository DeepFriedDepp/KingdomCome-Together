# WO-151 — the 2026-10-01 live session, and the safeguards an open beta needs: findings

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **(observed)** seen live in a run of this WO; **(log)** read in the field
logs; **(code-verified)** read in the code or the binary; **(unit)** a unit or
synthetic test; **(inconclusive)**; **(not built)** with the reason.

## The answer

(written last; see the sections)

## E. The evidence, confirmed line by line

The two field zips (the host is this machine, the joiner is the partner's) were
copied to the scratchpad and every line of the WO counted from the logs. Where a
log disagrees with the WO's text, the log wins; the disagreements are marked
**differs**.

| WO line | the logs | mark |
|---|---|---|
| the joiner's 26 blows reached the host and were applied (`MP-ATTRIB … result=applied`) | 26 `result=applied` in the host's agent log: 10 on `tbuk_man_1`, 7 `tbuk_man_3`, 3 `tbuk_man_4`, 4 on the quest wolves, 2 on `tvid_huntsman` | (log) |
| 56× `re-asserted sheathed (local brain fought back)` on `tbuk_man_1` | 56 on `tbuk_man_1` (171 in all: also `tbuk_man_4` 37, `_3` 29, `_5` 27, `_2` 22) | (log) |
| 7× `native=refused reason=not-living` | 7 on `tbuk_man_1` (`MP-NPCWRITE`), 49 in all (riders' horses, villagers) | (log) |
| 6× `WO131-STANDUP why=not-living` | 6 on `tbuk_man_1` | (log) |
| its health jumped (4.0 → 24.8 → 12.0) | **differs**: the agent's correction lines show the copy's own value far from the host's at each correction: copy 64.6 / host 29.8, copy 1.0 / host 29.2, copy 28.7 / host 15.3 (`MP-WO131 follow tbuk_man_1`) | (log) |
| WO-141/143 gave it a temporary tool mid-fight | `WO141-APPLY tbuk_man_1 … hands L=… R=…` 60 times | (log) |
| friendly fire on (`MP-FF host sent friendly_fire=on`, joiner `ff_from=host`) | 183 heartbeats; the joiner's `MP-WON cfg … ff_from=host` | (log) |
| the host's hits do nothing to the joiner | **differs**: all 24 of the host's hits were sent and applied on the joiner (`WO121-HITS friendly-fire hit on the player … applied`), and the joiner's own position lines show his health falling by exactly each hit's amount (92.23 → 86.58 → 82.95 → 81.00 → 77.70 for 5.66, 3.62, 1.96, 3.30) and his stamina by 30–40 at each of the 17 blocked ones (health 0). What did not happen is any visible reaction: the damage arrives as the game's scripted hit (`TakeDamage`), which plays nothing | (log) |
| wolves churn (`NPC-SYNC puppet start` / `release (stream silent)`), `interrupt_animal_attack failed because it required aliveness` | 232 puppet starts in all; 12 aliveness failures | (log) |
| 1,562 `Animation-queue overflow` on the joiner's avatar, a bow 291, an NPC 259 | `kcd2mp_1` 1,562 (747 `relaxed_idle_lookposes_torso`, 715 `crouched_idle_lookposes_torso`, 56 `relaxed_idle_both`, 44 `1d_relaxed_idle_bigturns_nw`), `bow_b_b001018` 291, `tbuk_man_2` 259 (both `bow_load_1st_over`) | (log) |
| the quest horse driven by three systems; 469 overflows; `ForceIdleState` 108 times | `tvid_huntsmansHorse`: `GHOST_HORSE swapping proxy for world horse` 3, `NPC-SYNC anim … walk/idle/gallop` 306, `horse_grazing` applied, 469 overflows, `ForceIdleState` 108 | (log) |
| riders' horses `bind refused: not-living` | the line reads `MP-NPCBIND npc=dummyWanderer_horse_1 result=refused reason=not-living … no physics` (16 on rider horses) | (log) |
| `MP-CARRY local carry of a living NPC (tvid_huntsman, a quest's carry)` in both logs | host 6, joiner 3 | (log) |
| `hunterCarriable`, `pickUpHunter`, `hunterIsCarried`, `SetTrue 2->1` applied on the host | the host's agent: 3 / 16 / 14 / 6 | (log) |
| `WO137-HOLD … ttac_blacksmith held_s=71.7`; `w139-stop` holds up to 240 s | `held_s=71.7` (one of three on the blacksmith); five `w139-stop` holds on `tvid_huntsman`: 52.0, 92.8, **240.5** (`w139-stop-nostop`), 162.4, 3.5 s | (log) |
| the host sent `semicloudy_clear_B` 46 times; the joiner applied it once | 46 sends; but the profile was the host agent's own pick at session start (`[weather] arbiter picked`), applied on the host too; the game's own weather is never read | (log) |
| `paused the world … paused_s=76.2`; `WO138-WORLD FROZEN … 0.46 s`; time scale 1.14–1.23 | yes; and the DLL's own status lines during those 76 s read `world=running scale=1.000`: the world was never paused (the only freeze is the 0.46 s world-save hitch) | (log) |
| `WO140-DROP the kept picker (a sleep from a bed he lay on stays lying)` | at 20:13:05; the held picker was a **wait** picker (id 1); the host lay until the joiner's hit at 20:23 | (log) |
| `WO139-CRIME theft … item=b38c…` ×2, `…928463…` ×2 | yes, and the WO-134 chest ledger shows both were stacks of two taken at once (`n=2`): one report per piece of one take; the host counted five thefts for three takes | (log) |
| the markers `body_pickup_quest`, `mark_weirdquest_Wine` | both in the host's `kcd.log` as unknown commands | (log) |

## 0. Phase 0 — the required safeguards

### 0.1 No silent fault guards

* **Built.** `native/KCDMP/fault_guard.{h,cpp}`: `fault::guarded(site, body)` and
  `guarded_or(site, fallback, body)`. A site is a named static (`KCDMP_FAULT_CALL`,
  `KCDMP_FAULT_READ`, or the expression forms `KCDMP_SITE_CALL/READ` for a site
  handed to a helper). The filter records the exception code, the faulting
  instruction (module+offset) and, for an access violation, the address touched;
  the handler counts and logs:
  * `FAULT <site>: 0xC0000005 reading 0x10 at KCDMP.dll+0x9b432 (1st)` at once, the
    10th and the 100th;
  * `FAULT-SUM 60s: …` every 60 s while any count is non-zero;
  * a **Call** site (a call into the game's code) is switched off at its 8th fault in
    a session: `FAULT <site>: switched off after 8 faults this session …`; its guard
    then returns false without calling the game;
  * a **Read** site (our own read or write of game memory, where no game code runs)
    is counted and logged the same way and never switched off: a stale pointer
    changes nothing in the game, and several are probes that expect to fault.
  * `mp_fault_switchoff on|off` (default on, the DLL's default too) via pipe `0x2A`.
* **All 286 guards converted** (the WO's "303" counts the lines that name `__try`;
  17 of them are comments): 223 by a converter for the two plain shapes, 63 by
  hand. The reflection layer names a site per property read and per invoke
  (`rttr::sample_health/GetState(stamina)`, `rttr::apply_damage_soul/TakeDamage(attacker)`,
  …); every engine service has its own (`engine::entity_name/GetName`), so one
  service that keeps faulting cannot switch off the others; each repeating
  main-thread task has its own (`main::task/<name>`); one-shot queued work (the
  pipe's commands) has one never-switched-off site (switching it off would stop the
  agent's whole channel). **(code-verified)**
* **The gate.** `tools/Test-NativeGuards.ps1` (no raw `__try` outside
  `fault_guard.h`, no `build_argument(`) runs in `native/Build-Native.ps1` before
  every build and in `Build-Installer.ps1`; planted violations fail it. **(unit)**
* **Tests.** `native/tests/wo151_tests.cpp`: deliberate read and call faults (the
  counts, the three lines and their text, module+offset, the switch-off at the 8th
  and a switched-off body never running, the switch off keeping a site running, the
  60 s sum, the reset). **(unit)**
* **Live (L1).** `mp_fault_test read` → `FAULT wo151::test/read: 0xC0000005 reading
  0x10 at KCDMP-w151a.dll+0x9b432 (1st) [read site …]`; nine `mp_fault_test call` →
  the 1st logged, the 8th `switched off after 8 faults this session`, the 9th not run
  (the count stayed 8); `mp_fault_status` round-trips (`faults=10 sites=3 off=1`).
  **(observed)**
* **What the guard found at once.** Its first line in the live game was a fault that
  had been silent in every earlier build: `FAULT local_state::read_ptr: 0xC0000005
  reading 0xffffffff00007777` — the local-state read's entity-hop probe tried a
  candidate slot of the player's actor that holds a non-pointer and read through it.
  Harmless (a read), but it ran at every hop scan. Fixed: a candidate that cannot be
  a pointer (non-canonical, the null page, misaligned) is skipped unread.
  **(observed; fixed in source)**

### 0.2 Reflection arguments own their value

* **Built.** `rttr::Arg<T>` and `rttr::StringArg` (`rttr_abi.h`): the value inside the
  argument object, beside the 24 argument bytes that point at it; neither copyable nor
  movable (so the compiler refuses any use outside the scope that holds both). The
  pointer-taking `build_argument` is gone; all 21 sites converted (the stamina
  argument of WO-148 §7.4 among them). **(code-verified, unit: the argument points
  into its own object)**
* **Live (L1)**, solo with the scripted partner:
  * health and stamina reads in a crowd: three commoners 3 m around the player, six
    souls tracked, the game's stat stack at 0;
  * a blow and a stamina-only blow (`mp_test_hit`, the WO-147 stand-in for the
    player's swing): `PIPE: LocalHit 5.00 st 10.00 by the player` and, on the
    **second** soul near the player (the path the 0.42.5–0.42.7 bug broke),
    `LocalHit 0.00 st 30.00`;
  * the partner's damage applied: `MP-ATTRIB npc=w151_c3 … result=applied`, the
    commoner 100 → 94;
  * a faction read: the avatar's faction attach read back (`WO131-FACTION: avatar
    joined the player's faction … (match)`). **(observed)**

### 0.3 The frame-rate soak before every installer

* **Built.** `tools/perf/soak.py` (`run`, `verdict`, `check`), `tools/perf/statstack.py`
  (the game's stat stack read from outside; pinned to the Modding Tools 1.5.5
  RPGModule by its PE identity, refusing any other build), `tools/perf/README.md`.
  The scene is input-free: three commoners 3 m around the player (no AI, never
  saved), then from minute 3 a fight beside them — two AI commoners, one sent the
  game's own attack interrupt at the other (`crime:attackInitiatedByConcept`),
  re-sent every 15 s, a fresh pair when the victim goes down or stalls. Nobody fights
  the player (no Game Over can stop the run). Every 10 s: the frame rate over those
  10 s, the stack depth, the `FAULT` lines since the start.
* **The gate.** `Build-Installer.ps1` runs `soak.py check` first: the committed
  `tools/perf/soak-record.json` must say PASS for the same git trees of the code that
  runs in the game (the DLL, the mod's Lua and tables, the agent, the relay, the
  protocol).
* **Dry run (L1, 4 minutes):** 71.1–71.8 FPS, stack 0, no `FAULT`; the fight was real
  (16 hits, the victim down to 8). The real soak: §0.3 table (final build).

### 0.4 The frame rate in the native log

* **Built.** `native/KCDMP/frame_meter.h`: one `FRAME` line every 60 s with the frame
  count, the frame rate, the mean and worst frame time, the frames slower than 50 ms,
  what this DLL's own work cost, and the fault totals. The WO-148 per-task meter stays
  behind `mp_main_cost` (off by default). **(unit: the arithmetic; observed: `FRAME
  window_s=60.0 frames=4299 fps=71.6 … ours_us_mean=760 … faults=1`)**
