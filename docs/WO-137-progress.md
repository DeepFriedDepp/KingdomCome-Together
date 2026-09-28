# WO-137 — Shared quests, step 1: progress

Answer and evidence: `docs/WO-137-findings.md`. This page is what was done,
what it touched, and what is left.

## Phase status

| phase | status | notes |
|---|---|---|
| 0 — origin, clean tree, fast-forward | done | `origin` = the project's repo; WO-136's findings on `origin/main` |
| 1 — Q1, Q2, Q4, Q5, Q6, Q10; side quests; routes | done | Q1 yes, Q2 yes, Q4 yes, Q5 no, Q6 yes, Q10 inert (all observed). Routes: detector + getters / Set-port apply / scoped resume |
| 2 — host → joiner | done | ordered mirror, load hold, periodic checkpoint + correction, consumers kept / time sets gated (observed h1/j1; the time-gate skip code-verified) |
| 2b — dead is dead | done | corpses never paused or detached; dead stand-ins; three proofs observed (j1) |
| 3 — joiner → host | done | root steps asked; judged against the host's value; applied / already / refused / off observed (h1, h2) |
| 4 — talking | done except surrender | scoped resume + host hold + re-pause observed (j1, h1); outcome counted on both (j1 + h2). **Surrender not built** (findings §9.1) |
| 5 — Find Mutt! + a main-quest step | done | solo method; the live/synthetic split per step in findings §7 |
| 6 — safety | done | H4 audit of every applied port; D2, S2, S3; kill switch observed both ends |
| gates, docs, checklist | done | every gate green (below); tester page, runbook section, checklist section, `Verify-Install.ps1` markers |

## What was built

* **Native** (`native/KCDMP/wo137.{h,cpp}`, `wo137_rules.h`): the detector (the
  concept graph's State setter, real changes only, `seq`, root / cascade /
  mirror, the quest path and its root), the Set-port apply with its refusals,
  batched State reads, the quest getters, the joiner's time gate (quest
  `AdvanceWorldTime` / `PassLongTime` calls never run on a joiner), the HUD
  diagnostic, the research file (solo only, WO-133's gate). Pipe 0x23 → 0x9D,
  unsolicited 0x9E.
* **Agent** (`Wo137.cs`, `GameBridge.Wo137.cs`, `CombatPipe.cs`,
  `ProtocolWo137.cs`): the host's sends, the joiner's ordered apply queue, the
  requests and verdicts, corrections, checkpoints and the post-load resync,
  the talk hold, the kill switch, Godwin and load holds; wire 0x60–0x63 on the
  join channel. Two fixes in older code: the join resets the WO-137 queue as
  its load starts (`GameBridge.Wo124.cs`), the log tail reads the dialogue
  lines.
* **Mod** (`kdcmp.lua`, WO-137 section + three touch points): dead is dead
  (the pause, the detach, the puppet tick, dead stand-ins), talking (wrapped
  Talk/Chat, resume, start/end, timeouts, fallback, forced), the host's hold,
  `mp_quest_sync` / `mp_quest_talk` / `mp_quest_status`.
* **Tools**: `tools/Audit-QuestApplies.py` (H4), `tools/Test-WO137Synthetic.*`,
  synthpeer `quest …` / `questfile` verbs (a synthetic host that judges
  requests like a real one), avatarpeer `quest request|talk|resync` and
  `--quest-rec`.

## Live runs (all solo, one machine, the Modding Tools game)

| run | what | binaries |
|---|---|---|
| r1 | Q1/Q2 on `playline4/autosave088` — that save drops Henry 22 m on load and he bleeds to death (a Game Over hibernates the whole quest graph); moved to `autosave087` | research DLL r1 |
| r2 | Q1, Q2, Q4, Q6 on `playline4/autosave087` | research DLL r2 |
| r3 | Q5 (HUD sink); a `wh_sys_LoadGame 5 …` crashed the game | research DLL r3 |
| r4 | the crash reproduced with no DLL: the engine loads playlines 0–4 only | none |
| r5 | Q10 on a spliced world in `playline4` | none |
| h1 | host: requests (applied / already / refused / off), the cascade, the talk hold, the herbalist step, the kill switch, M05 | r6 |
| j1 | joiner: the join, the host's changes replayed, the herbalist conversation, the checkpoint correction, the host's off/on, dead is dead ×3 | r6 |
| h2 | host: the three steps of the joiner's conversation (recorded in j1) applied on the real host | r7 |

## Side effects (everything touched outside the repo)

**Saves** (`Saved Games\kingdomcome2\saves`):

* Before the first launch all 292 save files were copied to the scratchpad
  with a sha256 list. After every run the save guard checked playlines 0–3:
  **nothing there was changed or added** by any run. Final check at the end:
  all 292 files match their sha256, and no file outside `playline4` is newer
  than the list.
* `playline4` was the throwaway: copies of the maintainer's
  `playline1/autosave087` and `autosave088`. The test games wrote into it only:
  the host's scheduled world saves `autosave089`, `autosave090` (h1) and
  `autosave091` (h2), and the join's placed world `mpworld0c0560b0.whs` (j1,
  removed by the join itself after the load). The r5 splice `mpworld137.whs`
  there was moved by the agent's WO-125 sweep into the scratchpad's data folder
  (h1 start). **`playline4` was deleted at the end** (it did not exist before
  this WO).
* `playline5` existed briefly in r3 (a splice); the engine cannot load
  playline 5 (the r3 crash). Removed the same day.
* No test agent ever saved into a real playline: every run loaded a
  `playline4` copy or joined into it.

**The game** (the Modding Tools install, `D:\…\KCD2Mod`):

* **Two crashes** (r3, r4), both `wh_sys_LoadGame 5 …` (out of the engine's
  range), reproduced with no DLL. **The game's crash-report dialog was closed
  twice without sending** (the privacy-preserving choice).
* **The mod pak**: the maintainer's installed `Mods\kdcmp\Data\kdcmp.pak`
  (sha1 `b7adb756…`) was backed up, replaced by the test build for h1, j1 and
  h2, and **restored** (sha1 checked: `b7adb756…`). `mod.manifest` restored
  from the same backup.
* `kcdmp-quest.txt` (the research file) was created in the game folder for the
  reads and **removed**.
* Test DLLs were injected into the test game process only (research r1–r3,
  `KCDMP-r6.dll`, `KCDMP-r7.dll`). The Modding Tools game was quit with
  `System.Quit()` after every run.
* `kcd.log`: each launch's previous log was copied to the scratchpad first
  (the engine keeps one backup).
* Test cvars set in j1 only, reset before the quit: `wh_dlg_AutoSkip 1` → 0,
  `wh_dlg_ForcedDecision <decision>` → 0.

**Programs**: the maintainer's launcher, agent and relay were not running at
any time; nothing of theirs was stopped or restarted. The test relay ran on
TCP 7779 / HTTP 5274, the test agents on IPC 5911/5912, all stopped after each
run. The test agents' data folder (`KCDMP_DATA_DIR`) was the scratchpad.

**Focus**: no input was sent. After a load the game took the foreground in h1
and h2: one push-down each (to the bottom of the Z order, not activated). In
j1 it did not take the foreground. Frames were `PrintWindow` captures of the
game window only.

**The console command `mp_quest_sync`** changed meaning: it was WO-94's
argless status line for the old readiness prompt (its toggles are
`mp_quest_on/off`, unchanged); it is now WO-137's kill switch as the work
order names it. `mp_quest_status` prints both layers.

## Tests and gates

* `KcdMp.Client.Tests` 497 passed (`Wo137Tests.cs` new; `Wo123Tests`' join-type
  bound widened to 0x63 for the two new pairs).
* `KcdMp.Relay.Tests` 52 passed (a WO-137 round trip new).
* Every `tools/Test-*Synthetic.ps1` suite passed, `Test-WO137Synthetic` 84/84
  new; `Test-WO131Synthetic`'s stand-in check updated to WO-137's rule (a body
  already dead on the host now gets a stand-in created dead).
* The static checks (`Test-WO106ConsolePlaceholder`, `Test-WO110LuaLocals`),
  the local payload publish and smoke, the native unit tests (89 passed,
  `wo137_rules_tests.cpp` new).
* No installer, no GitHub Release, `VERSION` untouched.

## Left for later

* Surrender on the joiner (findings §9.1).
* A two-machine session: `docs/TWO-PLAYER-CHECKLIST.md`, section WO-137.
* The time gate's skip seen live (needs a State-driven quest time set in an
  active quest).
* Holding a forced conversation that is out of step with the host.
