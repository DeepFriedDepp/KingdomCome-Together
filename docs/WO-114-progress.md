# WO-114 progress: the leash (refreshed)

Session 2026-09-26, solo, unattended. Findings: `docs/WO-114-findings.md`.
No installer (no version string given): `VERSION` stays 0.30.0.

## 1. Phases

| phase | state | where |
|---|---|---|
| 0 repo | `origin` = KingdomCome-Together, clean, level with `origin/main` | — |
| 1 the leash | built; host side live with a synthetic joiner, joiner side live with a synthetic host | findings §2, §5 |
| 2 respawn within the leash | built; three live deaths + one beside-the-partner | findings §3 |
| 3 fast travel together | built; host fast travel live (after two fixes); joiner refusal live (console route) | findings §4 |
| 4 prove it solo | 10 items, all pass (item 8 on the console route) | findings §5 |
| 5 gates and docs | green; tester page and runbook "Staying together"; decisions rows | findings §6 |

## 2. Code

* Protocol v10 (`ProtocolWo114.cs`): Leash 0x58/0x59, LeashState 0x5A/0x5B as
  JoinWire rows; `LeashCommand` / `LeashState` codecs.
* Agent:
  - `LeashLogic.cs` (new, pure): thresholds, hysteresis, the countdown, holds,
    fast-travel pull, failures/disarm, the words.
  - `GameBridge.Wo114.cs` (new): the host tick (250 ms), host holds, the
    fast-travel sources (engine lines, settled clock jump, 200 m jump), the
    joiner's state/pull/dismount, the fast-travel switch, SetPartner.
  - `LogTailGameTransport`: `FastTravelStateChanged`, `FastTravelRefused`;
    a fast travel's end clears the stale `ApseOpen` pause.
  - `GameBridge.cs`: wiring; a joiner in the host's world never reports its
    own clock jump.
  - `CombatPipe.SetPartnerAsync` (pipe 0x20).
* Native: `wake_pick.h` (new, pure), `respawn.cpp` (partner, the pick, beside
  the partner, last spot), `join_native.find_beside`, pipe 0x20 `SetPartner`
  (cleared when the pipe closes).
* Lua: `KCD2MP.w114`, `mp_leash` / `mp_leash_warn_m` / `mp_leash_pull_m`,
  the countdown row, busy/dismount answers, the fast-travel switch, the queued
  refusal line; presets; `WO114-BUILD`. Pak rebuilt.
* Tests: `Wo114Tests.cs` (38), relay tests (+2), `native/tests/wo114_wake_tests.cpp`
  (14), `Test-WO114Synthetic` (59); WO-123/WO-108 counts updated.
* Tools (never shipped): avatarpeer `leash on|flags|obey`; synthpeer
  `--join-host125` control lines `pos`, `leash …`, `timeskip`.
* `tools/Verify-Install.ps1`: WO-114 markers.

## 3. Decisions taken (unattended)

* **Protocol bump to v10.** Two new message types on the join channel; a v9
  relay drops unknown types, so the WO-123 way (refuse at Handshake) is the
  safe one. The release string is untouched.
* **Distance is 2D**, the recorder's measure the maintainer's numbers came
  from.
* **The host runs the whole state machine**; the joiner only reports its
  reasons and executes. The joiner also refuses a pull when busy (a race with
  its last report).
* **Respawn filter follows the leash**: off → the WO-113 rule. Only deaths;
  executions and knockdowns keep their own rules.
* **Fast travel detection from the engine's log lines**, after the live run
  showed the travel is a simulated walk. The jump rule stays for teleports.
* **Fast-travel block = `wh_pl_FastTravelEnabled 0`** (A/B-proven, not saved,
  given back on leaving). Not the `EnableFastTravel` scriptbind: its field is
  saved with the character and would ride into the joiner's Henry snapshots.
* **Mounted = dismount, leave the horse**: no reachable teleport-with-horse.
* **Three failed pulls disarm the pulls** (fail closed, warnings stay), per
  the ship-it-on rule.
* **No fade around the pull** (not asked); carried.
* **Screenshots in `docs/wo114-shots/`**: game UI only.

## 4. Method (reproducible)

* Game: Modding Tools build, started minimized, kept at the bottom of the Z
  order, never activated; captures are window-only (PrintWindow). No key or
  mouse input; everything through the console API. Quit with `System.Quit()`.
* The maintainer's own relay was running on the default port; the test relay
  ran on 7779 (HTTP 5274) and the test agent on IPC ports 5911/5912.
* DLL injected from a scratch copy; agent, relay and peers from scratch build
  folders; the pak installed with `Build-And-Install-Mod.ps1` and the original
  put back afterwards (checksum equal).
* Host runs: the throwaway save, `--hosting` agent, avatarpeer as the joiner
  (it must `stand` within 30 s of connecting or the relay's idle timeout drops
  it). Deaths via the REST lethal hit (WO-113's method).
* Joiner runs: the game at the main menu, synthpeer `--join-host125` with a
  re-seeded copy of a throwaway save as the host world, "Bring my character"
  through the agent's `/join-choice`, the WO-125 join (≈55 s), then synthpeer
  control lines. The joiner was mounted with a spawned horse and
  `ForceMount` (in memory only; the joiner cannot save).
* Fast travel: console `wh_pl_FastTravelTo x y z` between two wake-spot
  points 375 m apart.

## 5. Side effects

* The host agent's scheduled world saves wrote five autosaves into the
  throwaway playline during the host runs; all five were moved out to the
  session scratchpad (the playline's newest save is again the one from before
  this session). No other save was left: the joiner's snapshots were swept to
  the scratchpad store, the transient join files deleted by the agent.
* Four deaths in the throwaway world (its graves live only in the moved-out
  autosaves). One spawned test horse (never saved). No townsfolk touched,
  daytime throughout, `mp_spawn_armor` never used.
* A failed console fast travel leaves the map screen open (the game paused);
  those games were quit with `System.Quit()`.
* The installed pak and manifest restored; the maintainer's relay and master
  server were left running and untouched.

## 6. For the next WO

* The two-player items: `docs/TEST-0.30.2.md` section 4 and the runbook's
  leash lines; predictions in findings §7.
* A fade around the pull; a real dialogue/cutscene hold on the joiner.
