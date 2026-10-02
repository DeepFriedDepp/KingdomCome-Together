# WO-151 — progress

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

The answer and the evidence: `docs/WO-151-findings.md`.

Unattended run (the WO's rule): no stop to ask; the conservative option taken
and recorded under "Decisions made unattended" in the findings.

| part | status | where |
|---|---|---|
| 0 — the bookmark | done | the tag `v0.42.8` on `37ccea0` (lightweight, like `v0.42.5`), pushed before any code |
| the evidence | confirmed line by line | findings §E |
| 0.1 — no silent fault guards | built, unit-tested, live-checked | findings §0.1 |
| 0.2 — arguments that own their value | built, unit-tested, live-checked | findings §0.2 |
| 0.3 — the frame-rate soak | the tool and the gate built; dry run live | findings §0.3 |
| 0.4 — the frame rate in the native log | built, unit-tested, live | findings §0.4 |

## Work log

1. **The bookmark.** `v0.42.8` → `37ccea0`, pushed.
2. **The evidence.** Both zips copied to the scratchpad and every WO line
   counted from the logs (findings §E). The host is this machine; the joiner
   is the partner's.
3. **Phase 0.1.** `native/KCDMP/fault_guard.{h,cpp}`: one guard
   (`fault::guarded` / `guarded_or`) with a named site; all 286 raw guards in
   36 files converted (303 lines named `__try`; 17 of them are comments). 223
   by a converter for the two plain shapes, 28 by hand, the reflection layer
   (`rttr_abi.cpp`, 35) by hand with a named site per property read and per
   invoke; each engine service its own site. The static gate
   `tools/Test-NativeGuards.ps1` (no raw `__try`, no `build_argument(`) runs in
   `native/Build-Native.ps1` before every build and in `Build-Installer.ps1`;
   planted violations fail it (exit 1).
4. **Phase 0.2.** `rttr::Arg<T>` and `rttr::StringArg` (`rttr_abi.h`): the value
   inside the argument object, neither copyable nor movable; all 21
   `build_argument` sites converted, the builder removed.
5. **Phase 0.4.** `native/KCDMP/frame_meter.h` and the `FRAME` line every 60 s
   (`main_thread.cpp`), with the fault totals; the WO-148 per-task meter kept
   behind `mp_main_cost` (off by default); each repeating task its own fault
   site.
6. **The switches.** Pipe `0x2A` (`wo151.{h,cpp}`): `mp_fault_switchoff`
   (default on), `mp_main_cost` (default off), `mp_fault_status`,
   `mp_fault_test read|call`; the agent (`GameBridge.Wo151.cs`, `CombatPipe`)
   and the mod (`KCD2MP.w151`, `w151_cfg`).
7. **Native tests**: 367/367 (39 new in `native/tests/wo151_tests.cpp`).
8. **Phase 0.3.** `tools/perf/soak.py` (`run`, `verdict`, `check`) and
   `tools/perf/statstack.py` (the game's stat stack, read from outside, pinned
   to the Modding Tools 1.5.5 RPGModule by its PE identity);
   `Build-Installer.ps1` runs `soak.py check` first: no installer unless the
   committed `tools/perf/soak-record.json` says PASS for the same code trees.

## Live session L1 (2026-10-02, host role, solo)

Setup: every playline backed up first (362 files, sha256 list). The throwaway
is `playline4` (see the findings: the game lists only `playline0`–`playline4`;
`playline4` holds WO-148's throwaway copies, kept; four copies of `playline1`
saves added and removed again at the end). Save lock in every session. The
Modding Tools `Mods\kdcmp` backed up (SHA-1s recorded) and the test pak
installed with the game closed. Relay on TCP 7779 / HTTP 5274; the
maintainer's ports 7778/5273 were free and untouched.

* The game, started minimized, took the foreground once on its own; one
  push-down (bottom of the Z order, no activation); later starts stayed in the
  background.
* A `wh_sys_LoadGame 9 …` and a `wh_sys_LoadGame 5 …` (a playline index the
  game's save list does not hold) did nothing / **crashed the game** (BugSplat,
  08:21:55): recorded in the findings; no save was written.
* Results: findings §0 (the guard's first live line found a real silent fault;
  every check green).
