# WO-192 — Saves, simplified (no version bump; ships with the next shared-cutscene build)

Code: agent `Wo192.cs` (the rules) and `GameBridge.Wo192.cs`, with the removals in `GameBridge.Wo184.cs`, `GameBridge.Wo189.cs`, `Wo184.cs`
and `Wo189.cs`, hooks in `GameBridge.Wo122.cs` (a host save restarts the autosave), `GameBridge.Wo140.cs` (the wake), `GameBridge.Wo178.cs`
(the autosave's result), `GameBridge.Wo191.cs` (the fader bound leaves a held wake alone) and `LogTailGameTransport.cs` (the save-lock lines);
native `respawn_actions.cpp` (4a) and `wo190.cpp` / `wo190_rules.h` (4d); `kdcmp.lua` (the switches, the station release, the mark);
`tools/Test-NoScreenshots.py` and `tools/Build-Installer.ps1` (the screenshot gate). Tests: agent `Wo192Tests`, `Wo192ReplayTests` (the
0.60.9 bundles), native `wo190_tests`.

All live evidence is single-machine: the Modding Tools game with a synthetic partner through a local relay, on the `playline4` throwaway.
Nothing here is two-player evidence.

## 1. Why

The latest loss (the 0.60.9 session's bundles, confirmed): after each in-game cutscene WO-189's gate (`WO189-READY not ready: a scene is
still positioning the host's copies`) refused **13** snapshot requests between 19:50:21 and 20:08:56, the ESC-menu and host-reload snapshots
included. WO-189 Part D's 20 s bound never fired (no `WO189-POSITIONING` line). The 20:09 rejoin restored the snapshot of the 19:53 join —
four host saves back — so about 16 minutes of the joiner's progress were lost.

## 2. What was removed

| what | where it was | now |
|---|---|---|
| WO-189 A3: the readiness / positioning / stuck-cutscene gate | `Wo189AskBlockAsync`, before every snapshot request | gone; the reads are one log line: `WO192-SAVE during a scene: ...` |
| the ESC menu's early final snapshot (WO-184 B4's menu half) | `Wo184OnMenu` on the DLL's 0xAD edge | gone; the edge is now only the host autosave's "a menu is open" |
| the joiner's own 10-minute snapshot (WO-189 A4) | `mp_joiner_autosnap` | code and switch gone |
| the host's 15-minute reminder (WO-189 A4) | `mp_save_reminder` | code and switch gone |
| the mark screenshot (WO-172 W1) | `mp_mark_shot`, `r_GetScreenShot` in `KCD2MP_Mark` | code and switch gone; the build now fails if a shipped file takes a screenshot |

The joiner's snapshot is now asked at every host save with two hard blocks only: (a) a save already pending on this machine, (b) not in the
host's world (loading, a join, the host's reload). The engine's own "not now" (its CanSave) is still asked again every 2 s. Kept as they
were: WO-189 A1 and A2 (a stalled save is cancelled and taken again), the leave's final snapshot at the exit dialog, the late-pairing rule,
`mp_save_status`, "Partner saved.". Two reasons that are not waits stay: a host whose player is not Henry, and a playline already holding
100 quicksaves (a snapshot could push out the player's oldest).

The known consequence, accepted by the WO: a snapshot asked while a scene positions the host's copies may cancel their fast-forward. WO-189 C
still releases a cutscene left at "initialized".

## 3. What was added

| part | what | switch (default) | log |
|---|---|---|---|
| 2 | the host's autosave every 30 minutes of play, the Saviour Schnapps way (WO-178's path, no item used). It counts play only in a session and in the world; the game's own rules decide (a dialogue, a scene, a fight, an arranged fight, a mod transaction, a menu, the engine's CanSave, a pending save): then it waits 60 s and asks again. Any host save (the autosave, Home, the player's own) starts the next 30 minutes. The old resting-save world autosave (`mp_world_autosave`) stays off and untouched. | `mp_host_autosave` (on), `mp_host_autosave_min` (30) | `WO192-AUTOSAVE asked / done / waiting why=...` |
| 4a | the partner's map and compass marker moves: each move (over 2 m, at most once a second, the existing cap) is a new pin entity at the new place with the mark made on it. The cause: the mark is drawn from the pin entity's AI linkable, which keeps the place the entity was spawned at; moving the entity (0.60.9: 573 moves) never moved the marker, and re-making the mark on a new picture put it back at the old place. | — (a fix) | `WO192-MAPMARK pin ... moved to` (one in 30) |
| 4b | after a shared sleep or wait, a joiner whose own skip ends first is held behind the game's fader until the host's "woke" arrives (plus 1 s for the clock), 15 s at most. WO-191's fader bound leaves that fade alone. | `mp_wake_hold` (on) | `WO192-WAKE held / released after N s (why)` |
| 4c | the game's own save lock of a station (`waterTub`: the trough's wash and the water resource) held 15 s means the game's interaction never ended (a normal wash holds it ~5 s): the agent ends it the game's own way (`AI.AbortAction`, `OnEndInteractive`, `RemoveSaveLock`). `mp_unstuck` drops the lock too. The field: the lock stayed 14 minutes on the joiner; `mp_unstuck` freed the player but not the lock. | `mp_station_release` (on) | `WO192-STATION ...` |
| 4d | the animation trace's `auto` mode also probes every body that does not fight (its full body, 4 times a second) and traces it for 4 s when its full body has stood empty 0.5 s or its gait class flipped 3 times in 2 s — a T-pose while running now leaves `WO190-ANIM` lines. | `mp_anim_scope_log auto` (unchanged) | `WO190-EVIDENCE body=... why=...` |

## 4. What each test showed

| test | result |
|---|---|
| unit (`Wo192Tests`, native `wo190_tests`) | every rule above; the only blocks are (a) and (b); 1,770 agent tests and 9,582 native checks pass; all 68 Lua suites and the 4 static checks pass |
| replay, Part 1 (0.60.9 joiner bundle) | all 13 refused requests (19:50:21 → 20:08:56) are asked at once under the new rule (no save pending, in the host's world). Model: host saves 3, 4, 6, 7, 8 and 10 pair ~2.5 s after they arrive; at the host's 20:08:56 reload the newest pair is **seq 8 — the host's last save before the reload** (the bundle restored seq 4's join snapshot) |
| replay, 4a (host bundle) | 573 move sends; the DLL logged ~800 position moves across the bundle's launches against 30 pin spawns: position updates did reach the DLL, as entity moves only |
| replay, 4b (both bundles, the host's clock) | the two shared **waits** ended on the joiner first: 6.8 s and 2.3 s before the host (the WO's "about 3 s"); the rule holds them 7.8 s and 3.3 s. The two sleeps ended on the joiner after the host: no hold |
| live, Part 1 (joiner) | a host save with a scene counted open on the positioning watch (staged: the harness opens the count) logged `WO192-SAVE during a scene ...` and **paired in 2.6 s** |
| live, Part 2 (host, interval 1 min) | the autosave was asked after 1.0 min of play every time and written the schnapps way (`MP-W178 save why=autosave ... verified`), each announced to the partner as a normal host save (`WorldSaved seq=N`); a real conversation with Henry logged `waiting why=dialogue` and the save went through 60 s later; a manual save logged `a host save ... after 0.3 min` and the next autosave came 64 s after it. The joiner's pairing was shown in the joiner-role run (any host save pairs); the synthetic partner of the host-role run takes no snapshot |
| live, 4a (host, partner walking a 12 m circle) | the DLL made a new pin entity per move (`WO192-MAPMARK`); on the compass the partner's icon moved across the bar with the partner (five screenshots 10 s apart, camera still; the harness's own, removed from the game's folder at once). The map itself was not opened (no input) |
| live, 4b (joiner, staged through the harness) | host's "woke" 3 s later: **held 4.0 s** behind the game's fader (the engine: "Last fader took 3.73s"); no "woke": released at the **15 s bound**; WO-191's fader bound logged "left alone: the wake waiting for the host" |
| live, 4c (joiner, trough at Troskowitz) | a real wash (the game's own `OnUsed`): lock added and removed in ~6 s, no release. The lock added and left behind: **released at 16 s**, `abort=true end_interactive=true lock_removed=true` |
| live, WO-184 C1 round | pick up 3 items, host save (paired 2.6 s), quit, restart, rejoin: restored the pair, **inventory EQUAL (32 classes)** |
| frame rate in town (WO-190's scenario: joiner, every NPC within 30 m held, the partner walking) | 73.3 / 74.0 / 74.1 fps with `mp_anim_scope_log` auto / off / auto (the probe costs nothing measurable: ratio 0.996); 0.60.9 measured 77.2 in the same scenario: −4.5 %, within the 10 % bound |
| screenshot payload gate | fails on the 0.60.9 payload (its pak calls `r_GetScreenShot`); passes on this build |

**Validation build** (`tools/Build-Installer.ps1 -SoakWaiver`, the standing rule of 2026-10-06; header 0.60.9, unchanged): every gate passed --
the publishing gate, the relay round trip, 1,770 agent tests, the setup and log-pack tests, every Lua suite and static check, 9,582 native
checks, the live save round trip, the payload smoke, the no-input gate and the new no-screenshot gate (1,036 files, 0 hits). The installer is
local only and is not a release.

## 5. Old mark screenshots: where they are

Since 0.48.8 every `mark_odd` (the menu's "Something's wrong here") made the game write one picture into its own screenshots folder:

`%USERPROFILE%\Saved Games\kingdomcome2\screenshots\` — files named `screenshot_YYMMDD_HHMMSS.jpg`

The mod never deletes files on a player's PC. Testers can delete those pictures themselves; the game's own F12 screenshots land in the same
folder with the same names, so keep any they took on purpose.

## 6. Open

- 4b and the Part 1 scene were staged (the harness), not reached through a real shared sleep or a real scene positioning copies; a
  two-player session is the real check (checklist §WO-192).
- Part 2's wait was shown for a dialogue, not a fight (no input is sent to the game, so no fight was staged).
- The map was not opened (no input); the move was seen on the compass. There the partner's icon drew as the game's missing-picture placeholder
  because this test install has no `kdcmp_brand.pak` (the knight pictures ship in it): the position, not the picture, was checked.
- The DLL still sends the ESC menu edge (0xAD); only the autosave reads it now.
