# WO-139 — Crime and guards: progress

Answer and evidence: `docs/WO-139-findings.md`. The research behind it:
`docs/WO-139-crime-reference.md`, `docs/WO-139-guards-reference.md`. This page
is what was done, what it touched, and what is left.

## Phase status

| phase | status | notes |
|---|---|---|
| 0 — origin, clean tree, fast-forward | done | `origin` = the project's repo; WO-138's findings on `origin/main` |
| 1 — research: raising a crime, the perpetrator, the entry points, guards, jail's clock | done | findings §3; the two reference pages |
| 2 — the joiner's crimes detected and sent; the host raises them | done | trespass native (observed); thefts, stashes, locks, horses, bodies in the game's Lua (synthetic); the host's judge (observed); no engine culprit exists, so the host keeps the record (findings §3.2) |
| 3 — guards pursue, the stop on the joiner, execution, horses, punishment clock | done | pursuit and stop observed on both sides; execution via WO-113 (code-verified); horses observed; the punishment clock gated (code-verified, data observed) |
| 4 — no robbing each other; nothing between players is a crime | done | refusal observed on both machines; friendly fire observed |
| 5 — prove it | done | h1, h2 (host + avatarpeer), j1–j3 (joiner + synthpeer); the dialogue's choices need a player (checklist) |
| gates, docs, checklist, tester page, runbook | done | every gate green (below); checklist section WO-139; `docs/TEST-WO139.md`; runbook "Crime together"; `Verify-Install.ps1` markers |

## What was built

* **Native** (`native/KCDMP/wo139.{h,cpp}`, `wo139_rules.h`):
  * the trespass detector (a gate on the HUD's trespass listener);
  * the pursuit (skirmish add + `combat_forcedTarget`, cleared per pair, all
    of them when the pipe closes);
  * the entity-context writer (an allow-list of five);
  * the punishment switch for the WO-137 time hook (`wo137.cpp`: node paths
    under the punishment module are refused while armed);
  * pipe 0x25 → 0xA2, unsolicited 0xA3.

  Elsewhere: the execution branch of the respawn (`respawn.cpp`) now wakes the
  player outside a settlement **within the leash** of the partner.
* **Wire** (`ProtocolWo139.cs`): CrimeAsk 0x64/0x65 (joiner → host) and
  CrimeHost 0x66/0x67 (host → one joiner) on the join channel; the relay rows.
* **Agent** (`Wo139.cs`, `GameBridge.Wo139.cs`, `CombatPipe.cs` + dispatch
  lines in `GameBridge.cs`, `.Wo121`, `.Wo123`, `.Wo132`, `.Wo135`):
  * the joiner's record on the host (per joiner): witnessed or not, known or
    not, fresh, resisted, calmed, the trespass merge;
  * the guard decisions every second (stop or pursue), the hold, the outcome;
  * the joiner's side: reports, trespass re-reports, the stop's outcome, the
    toasts, the legal horses, `Cleared`;
  * the takedown marks and the violent crimes the host sees itself.
* **Mod** (`kdcmp.lua`, WO-139 section + touch points in the WO-131 loot gate,
  the WO-134 pickups, the NPC stream receive and the draw loop):
  * detection (pickups confirmed by the inventory, stashes, locks, horses,
    bodies), the rob refusal;
  * the host's judge (the sight test, guards, settlement), the guard scan, the
    legal horses, the attack starter, the placing of a held guard;
  * the stop on the joiner (unbind, resume, plant the host's list, the result
    wrap, the end with `stopFight`);
  * the skip-time cutscene data (1 s in a session, restored after);
  * `mp_crime_shared`, `mp_crime_status`, `mp_w139_test`.
* **Test peers** (never shipped): avatarpeer `crime report|outcome|resync`,
  prints every CrimeHost; synthpeer `crime stop|judged|pursue|mode|cleared|horses|record`,
  logs every CrimeAsk.

## Live runs (all solo, one machine, the Modding Tools game)

| run | what | build |
|---|---|---|
| s1 | Lua surface survey, the trespass detector, a guard's arrest from an injected theft (answered too late: the escalation killed Henry) | t1 |
| h1 | host + avatarpeer: the rob refusal, a theft judged, known, stopped, paid; an assault, pursuit (forced target alone: no fight; the attack message: a fight), the partner downed → arrest → paid; flee → attack; a knockout takedown; friendly fire | t2, then the t3 agent with the attack starter defined live |
| j1 | joiner of synthpeer: trespass end to end, a failed pick reported as theft (the false positive), four stops (nostop, fled ×2, paid with no money), legal horses, the rob refusal, the clock | t3 |
| j2 | joiner: the failed pick still reported (hidden ≠ taken), the full unanswered arrest to the attack | t4 |
| j3 | joiner: the failed pick not reported, the host's list planted, `stopFight` at the end and a calm copy after, `Cleared` | t5 |
| h2 | host: the trespass merge (3 reports, 1 crime), the packaged attack starter (a guard 34 m away fought), downed → arrest, execution clears | t5 |
| v1 | a menu-only launch with the original pak, to read the cvars back | none |

## Side effects (everything touched outside the repo)

**Saves** (`Saved Games\kingdomcome2\saves`):

* **Backup:** before the first launch all 292 save files were copied to the
  scratchpad with a sha256 list. The save guard ran after every run.
* **Final check:** **all 292 files match their sha256, and no file outside the
  throwaway was ever new** (checked again after v1).
* **The throwaway:** `playline4` held a copy of the maintainer's
  `playline1/autosave087`. The test games wrote into it only: the host
  agent's identify saves `autosave088` (h1) and `autosave089` (h2), and the
  joins' own world files (removed by the joins). Crimes were tested only on
  this copy, under a script save lock and a frozen playline, and never saved.
  **`playline4` was taken out of the saves folder at the end** (it did not
  exist before this work order); its three files were moved to the scratchpad,
  not deleted.

**The game** (the Modding Tools install):

* **The mod pak:** the installed `Mods\kdcmp\Data\kdcmp.pak` (sha1
  `b7adb756…`) and `mod.manifest` (`18de23ef…`) were backed up, replaced by the
  test builds t2–t5, and **restored** (both sha1s checked).
* **Test DLLs** were injected into the test game process only (`KCDMP-t1` …
  `-t5`). The game was quit with `System.Quit()` after every run.
* **`kcd.log`:** each launch's previous log was copied to the scratchpad first.
* **Cvars:** `wh_sys_FreezePlayline` 1 in every run, `wh_dlg_AutoSkip` 1 in
  s1; the v1 launch read both back as **0** (they do not persist).
* **Files created in the game folder:** none.

**In the throwaway worlds only (never saved):**
* s1: the clock moved forward (to 09:52) for an on-duty guard; Henry
  teleported, arrested, and killed by the escalated guard;
* Henry teleported (into and out of a house, to an item, away from a guard);
  200 groschen created on him (j1); 10 hp lost to a guard (j1);
* NPCs: the guard tzel_man_7 fought the avatar (h1, h2); three villagers hit
  for 4–5 hp by the avatar (h1, h2); one knocked out by the avatar's takedown
  (h1); a failed console pick left an axe hidden in the world (j2, j3);
* on the joiner, the guard's copy was resumed and paused by hand, and sent the
  game's own `stopFight` and `crime:forgetCrimesData`, to test the stop's end
  (j1, j3).

**Programs:** the maintainer's launcher, agent and relay were not running at
any time, so nothing of theirs was stopped or restarted. The test relay ran on
TCP 7779 / HTTP 5274 and the test agents on IPC 5911/5912; all were stopped
after each run. The test agents' data folder was the scratchpad.

**Focus:** no input was sent. The game never took the foreground in these
runs, so no push-down was needed. Frames were `PrintWindow` captures of the
game window only.

## Tests and gates

* **Unit and relay tests:**
  * `KcdMp.Client.Tests`: 567 passed (`Wo139Tests.cs` new, 55; the WO-123 wire
    range widened to 0x67);
  * `KcdMp.Relay.Tests`: 54 passed (a WO-139 round trip new: crime messages
    cross joiner → host and host → one joiner only). One run under load (the
    game and a driver running) failed 45 of 54; it passed on every rerun and
    in the final gate run.
* **Synthetic suites:** every `tools/Test-*Synthetic.ps1` suite passed,
  `Test-WO139Synthetic` 113/113 new. `Test-WO131Synthetic` (g): looting an
  avatar is now refused by design, so the check moved to a dialogue stand-in,
  with a new check for the refusal.
* **The rest:** the static checks, the local payload publish and smoke, the
  native unit tests: 178 passed (`wo139_rules_tests.cpp` new).
* The new `Verify-Install.ps1` markers were checked against the built payload
  and pak: all present.
* No installer, no GitHub Release, `VERSION` untouched.

## Left for later

* **The maintainer's checklist:** `docs/TWO-PLAYER-CHECKLIST.md`, section
  WO-139: stealing in view of a guard, paying a fine, jail without a clock
  move, execution respawning outside town, no robbing each other, the host's
  own crimes as before.
* **The joiner's copy of a guard keeps its memory of him** (findings §5.4):
  the game's forget message is there, not wired.
* **Crime expiry by the world clock** (the record has it; not fed).
* **The punishment's fast-travel branch** beside WO-114's refusal of the
  joiner's fast travel (inconclusive).
