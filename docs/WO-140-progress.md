# WO-140 — Sleep voting, the "own world" trap, the checklist: progress

Answer and evidence: `docs/WO-140-findings.md`. This page is what was done,
what it touched, and what is left.

## Phase status

| phase | status | notes |
|---|---|---|
| 0 — origin, clean tree, fast-forward | done | `origin` = the project's repo, clean, even with `origin/main`; WO-136, 137, 138 and 139's findings all on `origin/main` |
| 0b — the "own world" trap | done | the launcher's joiner line; the separate state (agent), its drops, the message (game every 5 s, launcher window), the log line every minute, the host's leash skip; proof run j2 (observed) |
| 1 — the levers | done | the bed (`BedTrigger.ReportUse`), the picker and skip (`C_SkipTime`), the UI controls, the rest, the calendar (findings §1) |
| 2 — the vote | done | wire 0x68/0x69; native gate + edges + start + stop + pull; the agent's vote; the mod's hold, prompt and lines |
| 3 — prove it | done | h1 (host + avatarpeer): yes/yes, no, timeout, the host accepting (+ rest save), the gate alone; j1/j3 (joiner + synthpeer): yes/yes both ways, no, timeout, the fall-safe; two fixes found and re-verified in j3 |
| 4 — the build | done | every gate green; `docs/TWO-PLAYER-CHECKLIST.md` rebuilt (setup, most-wanted first, WO-136 → 140, markers); the tester page draft `docs/TEST-WO140.md`; the runbook section; `Verify-Install.ps1` markers. No installer (WO-141 builds it) |

## Live runs (all solo, one machine, the Modding Tools game)

| run | what | build |
|---|---|---|
| p1 | Lua probe: the beds, the lie-down, the picker, `TurnRight`/`Confirm`, a real sleep's rest, `wh_pl_ForcedSkipTime` | the installed pak, no DLL |
| p2 | the DLL levers through a pipe script: the gate hold and replay, the states, `Start` id 2 (outdoors: sleep + rest) / 14 (screen only), `Stop`, the clock pull; a guard killed Henry for trespass (the throwaway was reloaded) | t1 |
| h1 | host + avatarpeer: steps A–E (findings), the separate-leash check | t2 DLL/pak; t3, t4 agent + peer |
| j1 | joiner of synthpeer: yes/yes both ways, no, timeout, the fall-safe (+3 h) — found the two clock issues | t4 |
| j2 | the joiner in its own save, connected to a shared-world synthpeer: the trap | t5 |
| j3 | joiner again: the two fixes re-verified (full rest, no early pull, the pin) | t5 |
| p3 | `Start` id 1 (the accepter's wait): the game's "Waiting" screen, 1 h, no bed (observed) | t5 |

## Side effects (everything touched outside the repo)

**Saves** (`Saved Games\kingdomcome2\saves`):

* **Backup:** before the first launch all 292 save files were copied to the
  scratchpad with a sha256 list; the save guard ran after every run.
* **The throwaway:** `playline4` (it did not exist before this work order) held
  a copy of the maintainer's `playline1/autosave087`. The runs wrote only
  there: the host agent's saves and the game's rest save `autosave088`–`095`
  (h1, moved to the scratchpad after the run), and the joins' own world files
  (removed by the joins). Probe runs used a script save lock.
* **Final check:** all 292 files match their sha256; `playline4` was taken out
  of the saves folder at the end (its files moved to the scratchpad, not
  deleted).

**The game** (the Modding Tools install):

* **The mod pak:** the installed `Mods\kdcmp\Data\kdcmp.pak` (sha1 `b7adb756…`)
  and `mod.manifest` (`18de23ef…`) were backed up, replaced by the test builds
  t2, t4, t5, and **restored** (both sha1s checked).
* **Test DLLs** were injected into the test game process only (`KCDMP-t1`,
  `-t2`, `-t4`, `-t5`); the game was quit with `System.Quit()` after every run.
* **`kcd.log`:** each launch's previous log was copied to the scratchpad first.
* **Cvars:** none set. **Files created in the game folder:** none.

**In the throwaway worlds only (never kept):** Henry teleported in and out of a
Zelejov house to use its bed, slept and waited many times (the clock moved by
hours), rested; in p2 a guard killed him for the trespass (the save was
reloaded); in j1 his clock was pushed +3 h from the console and pulled back; in p3
he waited one hour standing outdoors. After p3 `playline4` was taken out again and the
292 files re-checked (all match).

**Programs:** the maintainer's launcher, agent and relay were not running at
any time, so nothing of theirs was stopped or restarted. The test relay ran on
TCP 7779 / HTTP 5274 and the test agents on IPC 5911/5912; all were stopped
after each run. The launcher was not started (it would take focus).

**Focus:** no input was sent; the game never took the foreground (checked at
every launch), so no push-down was needed. Frames are `PrintWindow` captures of
the game window only (`docs/wo140-shots/`).

**Found in the working tree, not mine:** `docs/WO-141A-activity-census.md`
(untracked, written 11:46 by another session); left untouched and not
committed.

## Tests and gates

* `KcdMp.Client.Tests`: **596 passed** (`Wo140Tests.cs` new, 29; the WO-123
  wire range widened to 0x69).
* `KcdMp.Relay.Tests`: **55 passed** (a WO-140 round trip new: the vote crosses
  joiner → host and host → one joiner).
* Every `tools/Test-*Synthetic.ps1` suite passed; `Test-WO140Synthetic`
  **61/61** new.
* The static checks (console placeholder, Lua locals), the local payload
  publish and smoke, the native unit tests: **204 passed**
  (`wo140_rules_tests.cpp` new).
* The new `Verify-Install.ps1` markers (agent, DLL, wire, launcher, pak) are
  literal strings of this build.
* No gate was left red by a previous WO (the WO-100.5 suite's extra
  "0 passed, 0 failed" summary line is its own output, as in WO-139's run).
* No installer, no GitHub Release, `VERSION` untouched.

## Left for later

* **The two-machine checklist**, section WO-140 (both screens at once, the
  wake together, the Wait key, reading in bed, a three-player vote).
* A natively-held sleep (from a bed one sits on) answered no leaves the player
  lying in the bed, as the game's own Back does (findings §3).
* WO-141: its checklist section, `docs/TEST-0.41.7.md`, the installer.
