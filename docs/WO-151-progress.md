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
| 1.1 — the same fight | built; live (L2–L4) | findings §1.1 |
| 1.2 — friendly fire host → joiner | **not fixed** (the damage arrives; no reaction route) | findings §1.2, §F |
| 1.3 — wolves | the knockdown fixed; the bite's row offset unknown | findings §1.3, §F |
| 1.4 — the animation-queue flood | fixed (hysteresis); synthetic | findings §1.4 |
| 2 — riding | built; live solo (J1, L4) | findings §2 |
| 3.1 — catch-up | built; live (J2), unit | findings §3.1 |
| 3.2 — living quest carries | built; synthetic | findings §3.2 |
| 3.3 — conversation progress | partly built (no applies into an own scene's positioning) | findings §3.3 |
| 3.4 — the scene guard | built; synthetic and unit | findings §3.4 |
| 3.5 — whistle / voice / barks | the whistle built and live (J2); voice and barks not built | findings §3.5 |
| 3.6 — holds | built; synthetic | findings §3.6 |
| 3.7 — weather | built; live (L5, J2) | findings §3.7 |
| 3.8 — the join pause | built; live (L5) | findings §3.8 |
| 3.9 — doors | built; live (J2, L6) | findings §3.9 |
| 4.1 — a guaranteed exit | the anvil fixed, `mp_unstuck`; the bed not reproduced | findings §4.1 |
| 4.2 / 4.3 — the forge, smithing on the avatar | fixed; live (J2, frames) | findings §4.2 |
| 5.1–5.3 — crime | built; synthetic | findings §5 |
| gates, docs, the soak, the installer | see the work log | findings §G, §0.3 |

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

## Live sessions L2–L7, J1–J2 (2026-10-02, solo)

Every session on throwaway playline4 saves (autosave111, the field save's copy), a save lock
except where a host join needed a save (L5, L6: the joins' saves were written to playline4 and
removed at the end); relay on 7779/5274; the maintainer's launcher, agent and relay were not
running; the game never in front (one push-down after J2's load). Between runs `savecheck`:
357 of 357 real save files unchanged. Details and frames: findings §L and each phase.

* **L2–L4** (host): TakeDamage's arguments; copy contexts; hit-row capture and replay; the
  sampler's freed-soul fix; the GetHorse WUID fix; avatar adoption.
* **J1** (joiner): the joiner's own mount of a host horse; the J1 incident (findings §D 12).
* **L5** (host): the weather hook; the engine join hold; the door names' real shape (the key
  fix); no sync attack in a 70 s fight.
* **J2** (joiner, `mp_join_henry` pinned to playline4): catch-up both paths, the weather gate,
  doors from the host and the joiner's ask, the whistle, smithing on the avatar; the save
  list's scan faults (fixed).
* **L6** (host): a joiner's door ask applied and sent; the avatar-as-opener bug (fixed).
* **L7** (host): two soak attempts with the mod that do not count. The first stopped at 72 s on
  one dropped console connection (the tool now retries it). The second fell from 68 to 25 FPS
  late in the run, after the game window was alt-tabbed out of (the maintainer's report; the
  DLL's own cost stayed at 0.7 ms).
* **L8** (no mod: `Mods\kdcmp` moved to the scratchpad and back, sha1s checked): the soak without
  the mod, window focused: 71.7 → 70.7 FPS.
* **L9** (host): the soak with the mod, window focused: 70.9 → 71.4 FPS; the verdict PASS
  (`tools/perf/soak-record.json`, the trees of `b38a59d`).

## Work log (continued)

9. **Phase 1.** Copy contexts (pipe op 5), the 4-argument TakeDamage, NpcHit capture
   (`motion.cpp`, per-class row paths, RTTI-checked) and replay, the sampler's alive check,
   gallop hysteresis.
10. **Phase 2.** `mp_ride_owner`: the ridden horse leaves every NPC path; the WUID fix.
11. **Phase 3.** Holds (3.6); weather (`weather.cpp`, BlendToProfile gated pass-through, hook
    prologue table + test); the engine join hold (`wo138.cpp` source 2, the DLL's deadline);
    doors (DoorState 16 host-only, DoorAsk 17; pivot + name hash; the game's own Open/Close
    shape); the whistle (Emote 3); the scene guard (the agent's stage parser, resume, rescue
    save, the end edge at the release, the forced-dialogue resume); the living carry
    (`living` on the carry wire); the catch-up (the mirror's hold through a host reload, the
    caught-up gate, the passed-step verdict, the seen set's rebase, stashed parts, no applies
    into an own scene's positioning).
12. **Phase 4.** Minigame tutorials local; `mp_unstuck`; avatar minigames never aligned at a
    station's object.
13. **Phase 5.** Joint crime, one report per take, the forgetting.
14. **Gates.** Every synthetic suite 0 failed; agent 824; relay 62; native 368; the three
    static checks; the soak (findings §0.3); the payload smoke (the installer build).
15. **Docs.** The findings, this page, the README (the weather line corrected; 0.43.0's rows),
    `docs/TWO-PLAYER-CHECKLIST.md` §WO-151 (91–103), `docs/TEST-0.43.0.md`,
    `docs/releases/RELEASE-NOTES-0.43.0.md`, `Verify-Install.ps1` markers.
