# WO-136 — world presence: progress

Findings: `docs/WO-136-findings.md`. Started from `d37ac8e` (`origin/main`,
clean, fast-forward only). No force-push. `VERSION` untouched (0.40.0); no
installer (WO-140), nothing on GitHub but the commits.

## Log

- Read WO-131, 132, 134, 135 findings and `docs/DECISIONS-coop-design.md`
  (horses: world-shared; the rider owns the horse while riding).
- Read the 0.40.0 field session's host and joiner logs (the two log zips on the
  desktop, copied to the scratchpad): the load stall, the dropped wolves, the
  ridden horse still bound, the enemies' surrender (no knockout ever streamed),
  the escape rule at the host's death, the white_red preset's failed unequips,
  the refused belt and hose, the loot screen's home key.
- Extracted Tables.pak (souls, archetypes, items, script contexts, clothing
  presets) and Scripts.pak (behaviour trees); the scriptbind docs; disassembled
  (EntityModule) the crouch action and the "is crouched" query, (CombatModule)
  the combat model's opponent setter and the test module's registrations,
  (WHGame) the context manager's relation slots.
- Built: agent (the frame hold, soul/item index, ride, knockout-first, torch
  bit, preset clear, refusal reasons, loot verdicts), mod (the hold, stand-in
  spec, ride take/return, bare preset, torch, loot provenance), native (animal
  scan, threat table, retarget with read-back, hand-over at the death, the
  forced target, the crouch query), test peers (avatarpeer `ride`, `torch`;
  synthpeer `npcmove`, `npcrow`, `npccombat`, `hstate k=v`, `appearance`).
- Runs: H1 (host: ride, outfit, torch, threat, a real death) → H2 (crouch,
  refusals, overrides) → H3 (harness crash, §8 of the findings) → H4, H5 (the
  retarget read-back and redo; the hand-over order) → H6, H7 (the forced
  target; the real death with two wolves) → J1 (menu join with streams; wolf
  stand-in; knockout; ride; outfit; torch; crouch) → J2 (in-world join).
- Decisions (why):
  * the hold covers both halves: the agent keeps frames, the mod keeps its own
    ticks — either half alone still touched NPCs during the load;
  * stand-ins use the host's soul and class from the game's own tables rather
    than a guess: the name is the soul name on this build, so the by-name paths
    stay unchanged;
  * a horse never gets a stand-in; a ridden horse copy is the rider's (the
    design decision), the host's stream resumes 3 s after the dismount from
    where it was left;
  * the engine's own `combat_forcedTarget` over skirmish calls: every skirmish
    route was unreliable (observed H1–H5), the relation context switched every
    time (H6/H7); only pairs the mod set are ever cleared (refcounted store);
  * the hand-over runs at the start of the host's native death, before the
    engine's escape rule closes the fight;
  * a put is proven by the item's own id leaving the player's pack (not by
    timing: a time-based baseline broke real puts in the synthetic suite);
  * a joiner back in its own world gets no copy guard or stand-ins (found J2).

## Gates (observed, `h\gates.ps1` = every `Build-Installer.ps1` gate without Inno Setup)

| gate | result |
|---|---|
| relay round trip | 51/51 |
| agent unit tests | 451/451 (new `Wo136Tests.cs`, 8) |
| synthetic suites | all green (new `Test-WO136Synthetic` 50/50; `Test-WO131Synthetic` 79/79, `Test-WO134Synthetic` 60/60, `Test-WO135Synthetic` 40/40, drops 55/55, `Test-WO132Synthetic` 9/9) |
| static checks | console placeholders 7/7, Lua locals 6/6 |
| native unit tests | 67/67 (new `wo136_rules_tests.cpp`) |
| local publish + payload coherence + smoke | pass |

`tools/Verify-Install.ps1` knows the WO-136 markers (agent `MP-WO136-STATS`;
DLL `WO136-HANDOVER`, `WO136-FORCED`; pak `KCD2MP_W136Hold`,
`KCD2MP_W136RideTake`, `KCD2MP_W136AvatarTorch`); run against the local
payload and the repo's mod folder, all present (the two install-manifest
checks fail there by design: a payload is not an install).

## Side effects on this machine (disclosed)

- **Saves.** Every playline was backed up to the scratchpad before the first
  launch (and a checksum list of all 292 files taken). The test world was a
  copy of `playline1/quicksave036` placed as `playline4/quicksave036` (a new,
  throwaway playline); the hosting runs' autosaves went there only
  (`autosave037`–`049`); the joins placed their world files there and deleted
  them after loading. `playline4` was **removed** at the end. A guard ran after
  every run; at the end all 292 original files matched their checksums and no
  file was new — no real playline was written.
- **Read, not written:** J1's Bring took the Henry of `playline4/autosave049`
  (the throwaway); after the synthetic host's reload the joiner went back to
  its own world, which **loaded `playline2/autosave027`** (a real save, read
  only), and J2's Bring took that Henry. The Henry snapshots and join staging
  lived in the harness's own data folder in the scratchpad.
- **Focus.** The game was launched minimized and never took the foreground in
  any of the eight launches (the window check after each load:
  `foreground=False`); no push-down was needed or made. No key or mouse input.
  For frames the in-game camera was turned with `actor:PlayerSetViewAngles`
  (a Lua call in the game, not input).
- **Programs.** The maintainer's launcher, agent and relay were not running
  (port 7778 not listening) — nothing of theirs was stopped. My own harness:
  in H3 a leftover H2 relay, agent and peer (my mistake: `stop` takes a kind,
  not a run tag) made the new agent a guest of a dead host and **the game
  crashed**; all my processes were then stopped and the script now refuses a
  busy port.
- **Deaths and fights.** Henry was killed twice by spawned test wolves
  (`wo132_wolf*`, the open field by the test spot, away from settlements; H1
  and H7 — the latter with his health lowered first); WO-113's grave and wake,
  in the throwaway world, not saved. Test wolves were spawned and removed in
  each run; on the joiner a local copy of a world NPC (`tzel_man_6`) was
  knocked out by the synthetic host's stream, in the joined world, not saved.
  Nothing was stolen; `mp_spawn_armor` was never used; every run ended with
  `System.Quit()` except H3 (the crash).
- **The game's mod pak** was the WO-136 test build during the runs; the
  maintainer's 0.40.0 `kdcmp.pak` (sha1 b7adb756…, backed up before the first
  launch) is **restored** in `Mods\kdcmp`. The repo's `kdcmp/Data/kdcmp.pak` is
  the rebuilt WO-136 pak (it needs the WO-136 agent: the new load hold waits for
  the agent to lift it; the TTL releases it after 240 s otherwise).
- Game log backups before each launch are in the scratchpad (`h/prelaunch/`).
