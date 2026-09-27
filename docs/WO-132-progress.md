# WO-132 — join fix, damage safety, combat engagement: progress

Unattended session. Decisions are recorded with their reason as they were made.
Evidence marks: (observed), (code-verified), (synthetic), (inconclusive).

## Phase 0 — the repo and the version

- `origin` = `DeepFriedDepp/KingdomCome-Together`, `main`, clean, level with
  `origin/main` at `ffc4405` (observed).
- `VERSION` read **0.30.5** (the maintainer's 0.30.5 commits are on
  `origin/main` now; WO-131's 0.30.2 note is stale). Set to **0.30.7**, the
  maintainer's string. The README badge and the release notes follow the
  0.30.5 precedent (same commit shape as `5340545`).

## Decisions

1. **Phase 1: match the writer's rule, not its zlib.** The engine keeps a
   deflated block only when it is smaller than 0x8000 bytes and stores it
   otherwise; that rule is copied exactly. The level (the game's 3) is not:
   .NET 8 has no level-3 setting, the reader inflates any valid zlib, and every
   limit the reader checks is a size. The Python reference tools use level 3.
2. **Verify checks what the reader refuses, nothing more.** The description
   length is left alone (the reader enforces no limit there that could be
   found; the writer trims descriptions itself).
3. **An inventory record without an item list is an empty inventory.** Found
   by the re-splice sweep: 16 early-game saves refused, as host and as joiner.
4. **Real hits come from the hit chokepoint.** The DLL already hooked the
   engine's per-hit entry for every hit on an avatar; an NPC attacker's hit is
   now measured there (the same landing-window watch the players' hits use) and
   is the only forward. The Lua sampler stays as the fallback without the DLL,
   with drops under 1 hp dropped as ticks.
5. **The down signal is the downed bit, not the death packet.** With WO-113's
   death guard a dying joiner is floored and never sends a death packet before
   the wake; the host sees the downed bit in its vitals first. Both count.
6. **Leave the skirmish, don't stop the fight.** `RemoveSoulFromSkirmish`
   (found by its `__FUNCTION__` string, vtable `+0x18` beside the
   `AddSoulToSkirmish` the DLL already used) replaces StopFight for the avatar
   and for the host's own death. The knockdown keeps StopFight (it wakes in
   place).
7. **Engagement = the combat actor only, the brain stays paused.** The first
   live probe showed the joiner's own combat mode against a paused copy in a
   skirmish with him while the copy's combat state is held; waking the brain
   was never needed, and would bring WO-131's split back.
8. **Only the host's bound copy engages, only near him.** Guarded + natively
   bound + the host NPC in combat + within 15 m (checked natively on the
   joiner's own positions), released after 3 s without host state.
9. **No protocol bump.** `NpcCombat` is a new kind on the action channel,
   which the relay forwards verbatim; the relay only gains a host-only gate for
   it. Mixed releases are refused at the handshake anyway.
10. **Live-check levers stay in the build, fenced.** Native ops 7 (the
    player's block through SetBlockMode) and 8 (a test NPC fights) and the
    agent's `w132_check` event are diagnostics: op 8 and the hostile-faction
    check refuse anything not named `wo132_…`.
11. **No real enemy could be made to fight solo** (spawned souls; the world's
    remote bandit groups are hidden quest groups). Stopped after three levers;
    everything that needs a real hit went to the two-player checklist.

## Log

- Phase 1 (code): writer/reader rules found in CryAction and Framework;
  `WhsSave` Deflate/Inflate/Verify, Python tools; tests. Sweep of every save;
  the stripped-Henry fix; commit `2f78875`.
- Phases 2–3 (code): native `wo132.cpp` (pipe 0x22 → 0x99; frames 0x9A,
  0x9B, 0x9C), `hits.cpp` (NPC-hit watch, discard watch, skirmish remove,
  override rule), `motion.cpp` (NPC engagement hold, combat read), respawn
  narrow leave; agent `GameBridge.Wo132.cs` + `Wo132.cs`; `NpcCombat` wire;
  relay gate; Lua `KCD2MP_W132AvatarDown` / `Heal`; synthpeer `ncombat`, `hold
  … drawn`; avatarpeer `vitals … downed`, prints 0x22 / 0x13.
- Run J1 (joiner, plan host): first the copy at z = 0 (the plan's height) was
  refused as far (correct); the synth restart needed an agent restart (the
  action channel's sequence state for sender 0); then engaged, the indicator
  over 6 frames, swing, release. Commit `981e3a4`.
- Run J2 (joiner, Host125): join from the main menu (the choice by the
  launcher's POST), world 1 loaded; the host switched worlds; world 2 (a
  stored block in its splice) loaded.
- Run J3 (joiner, plan host): guard, host block, guard-zone change, the
  player's block, position hold, release, CSV.
- Runs H1–H3 (host, avatarpeer): the watch armed; the enemy attempts (§2.4 of
  the findings); the peer's down and wake.
- Gates (below), docs, installer.

## Gates

`tools\Build-Installer.ps1` for 0.30.7, every gate green (observed):

| gate | result |
|---|---|
| relay round trip | 50/50 |
| agent unit tests | 404/404 (new: `Wo132Tests.cs`, `Wo132RulesTests.cs`) |
| synthetic suites | 28 suites, all green (new `Test-WO132Synthetic` 9/9) |
| static checks | console placeholders, Lua locals: pass |
| native unit tests | 47/47 |
| local publish + payload smoke | pass |
| installer | `release\KCDMP-Setup-0.30.7.exe`, 96.4 MB (local only, not on GitHub) |

## Side effects on this machine (disclosed)

- **Autosaves the host agent (or a briefly-host agent) wrote into
  `playline1`** — `autosave078`/`079` (J1), `078`–`080` (H1), `078` (H2),
  `078` (H3) — moved into the session scratchpad; `playline1` and `playline2`
  are back to their original file sets (checked by listing).
- **Join files:** `playline2/mpworld524ce9cd`, `mpworld44003547`, placed and
  deleted by the agent after each load (checked). The agent's Henry store and
  staging live in the scratchpad's data folder, never the repo.
- **Test bodies:** `wo132_cuman`, `wo132_soldier` (autotest souls) spawned in
  the throwaway worlds and removed (or gone with the quit); nothing saved.
  The stand-in bandit is the WO-131 stand-in (never saved).
- The test game's installed `kdcmp.pak` is the 0.30.7 build (the 0.30.5 copy
  is in the scratchpad).
- No key or mouse input; the game window stayed off the foreground on every
  launch (`foreground=False` after each load). Quit with `System.Quit()`.
