# WO-133 — quest safety patch: progress

Findings: `docs/WO-133-findings.md`. Started from `3b76a70` (WO-132 on
`origin/main`, its findings doc present, tree clean, fast-forward only).

## Log

- Read WO-126A §2 / §6.3, WO-114 findings (the joiner's clock-jump withhold),
  WO-132 findings. Audit re-checked in code: every quest piece lives in
  `kdcmp.lua` (prompt, keys, `mp_quest_*`, proximity, divergence, gap) and
  `GameBridge.cs` (divergence, fingerprints, time skips); `port_watch` in
  `native/KCDMP/concept_read.cpp`.
- Decision: one gate, computed by the agent (it alone knows the role and the
  host's mode) and pushed to the mod; the mod checks it at the chokepoints
  (`QuestFire`, `QuestAnswer`, `QuestShowPrompt`, `QuestDivergence`,
  `QuestObjectiveGap`, the proximity tick) so no key or command can go round
  it. The agent also stops comparing and sending on its side. A fresh Lua
  starts with the gate off (solo unchanged); the heartbeat (5 s) re-pushes it
  after a game restart.
- Decision: `port_watch` refuses a trigger whenever a session runs or an agent
  is attached (not only in a shared world): the WO says "never armed in a
  session", and the file needs no role to be dangerous on a host.
- Decision: the host drops every phase of a non-own skip (sync too), and does
  not record it as the peer clock for reload convergence.
- Commit `5b99136` (phases 1–2 + tests).
- Run H1 (real game = host, avatarpeer): gate, keys, commands, peer skips,
  host skip to the peer, divergence / fingerprint, port gate,
  `mp_shared_world off`. avatarpeer had to be restarted once (its `VERSION`
  file was missing beside the build).
- Run J1 (real game = joiner in its own world, synthetic host): gate, keys,
  commands, marker / fingerprint withheld, divergence skipped. Found: a joiner
  applied a third client's skip, and the fingerprint-compare skip line was
  throttled by a counter shared with the send skip. Fixed both (joiner applies
  only the host's skips; per-kind log counters); a third client's skip then
  dropped, the host's applied.
- Run J2 (real join into the synthetic host's world from the main menu, the
  launcher's choice by POST): the joiner's +3600 s clock jump not reported
  (WO-114, confirmed live); the quest commands refused there too.
- Test tools: avatarpeer `timeskip …` / `story …`, synthpeer Host125 `story …`
  (never shipped).
- Docs: findings, this file; `docs/TEST-0.30.7.md` now warns not to press
  F11/F12 or type `mp_quest_…` (0.30.7 still has the hazard; keep the warning
  until WO-134's installer ships); README's Shared Quests row says it is off in
  a shared world. No runbook told players to use F11/F12 or `mp_quest_*` in a
  shared world (checked: the WO-124 shared-world runbook, WO-121, WO-118, the
  0.30.x tester pages), so nothing was taken out.

## Gates

Every `tools\Build-Installer.ps1` gate, without Inno Setup (no installer
built), all green (observed):

| gate | result |
|---|---|
| relay round trip | 50/50 |
| agent unit tests | 419/419 (new `Wo133Tests.cs`, 15) |
| synthetic suites | 29 suites, all green (new `Test-WO133Synthetic` 48/48; WO-90 70, WO-94 101, WO-95 32, WO-96 160, WO-98 50, WO-99 39) |
| static checks | console placeholders 7/7, Lua locals 6/6 |
| native unit tests | 53/53 (new `wo133_port_gate_tests.cpp`, 6) |
| local publish + payload smoke | pass |

`Test-WO1005Synthetic` prints a second, empty `RESULT: 0 passed` line after its
33/33 (exit 0); pre-existing, not touched here.

`Verify-Install.ps1` knows the new markers: agent `KCD2MP_Wo133Gate`, "only the
host's clock moves the world"; native `WO133-PORTGATE`; pak
`function KCD2MP_Wo133Gate`, "Quest catch-up is off in a shared world." (all
present in the built files, checked).

## Side effects on this machine (disclosed)

- **`playline1/autosave078.whs`** — the host agent's join-identify world save
  (run H1) — moved into the session scratchpad; `playline1` and `playline2`
  are back to their original file sets (checked by listing). The J2 join file
  `playline2/mpworld6c215ec5.whs` was placed and deleted by the agent (checked).
- **`kcdmp-concept.txt`** in the Modding Tools folder (a WO-99.5 read-only
  probe line) was overwritten by the H3 test without being read first; its
  content was recovered from the DLL's own log and restored byte for byte in
  meaning (`probe Barbora.trosecko.socky.hibernable.v_hospode.rekniPtackoviOPraci SetDone`),
  and the DLL re-read it as the same probe.
- The test game's installed `kdcmp.pak` is the WO-133 build (the previous one
  is in the scratchpad). The repo's `kdcmp/Data/kdcmp.pak` is left as
  committed (WO-134 builds the installer and its pak).
- Throwaway worlds only (`quicksave036` and a copy of it); nothing saved by the
  game. No key or mouse input; the window stayed off the foreground
  (`foreground=False` after each launch and load). Quit with `System.Quit()`.
