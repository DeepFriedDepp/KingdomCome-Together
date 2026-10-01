# WO-147 — The joiner can fight, and the leash pulls: progress

Answer and evidence: `docs/WO-147-findings.md`. This page is what was done, what
it touched, and what is left.

## Phase status

| phase | status | notes |
|---|---|---|
| 0 — the bookmark | done | the tag `v0.42.2` on `7bcc28a` (the commit the 0.42.2 installer was built from) pushed before any code |
| 1.1 — the hit reaches the host | done | stamina drops sent; only this player's own blows; the gate 6 s; a copy knocked over stays bound 3 s for the gate |
| 1.2 — the avatar as the attacker | works (existing) | the host applies with the avatar as the attacker; the NPC turns on it (observed, H1) |
| 1.3 — the NPC fights back at the joiner | done | the host watches an NPC fighting an avatar (30 s); the joiner engages when it is his avatar; heartbeat for any opponent |
| 1.4 — lock on to hostile copies | done | public enemies, animals, hostile relationships within 12 m while the weapon is out; friends never (synthetic, J1) |
| 1.5 — no crime on hostile NPCs | works (existing) | `not a crime (a public enemy)` (observed, H1); civilians keep WO-139's path |
| 2.1 — who holds a pull | done | only unsafe states; the host's menu/dialogue/cutscene no longer (synthetic, H1) |
| 2.2 — the hold cap | done | 60 s, then a forced pull; `mp_leash_cap_s` (synthetic, H1) |
| 2.3 — spikes and jumps, their causes | done | the stuck skip-time flag; the frame backlog (leash lane); the flights and the quest teleport classified; stale positions not used |
| 2.4 — the pull lands | done | no ground → the host's own spot, the fall hold until the landing, then beside the host; 3 failures pause 3 min (observed, J1) |
| 2.5 — mounted | decided | dismount cleanly, the horse stays (code) |
| 2b.1 — destructive requests | done | from the quest data; held 10 s; conversation outcomes applied; refused otherwise (synthetic, H1) |
| 2b.2 — knockouts, not deaths | done at the source | guards re-made after a load; the host's non-fatal hit never kills a copy (synthetic, J1/J2) |
| 2b.3 — the correction rule | done | the port producing the host's value; re-checked before firing; no-op corrections remembered |
| 3 — README | done | the feature list with evidence levels; the old detail table in `docs/FEATURE-HISTORY.md` |
| + the frame backlog | done (asked for during the work) | `mp_npc_catchup`: superseded samples skipped behind; a stream's silence is the agent's word; a stall is no silence (mod and DLL); after the live A/B: the render lag and every NPC through every 300 ms (J2–J6) |
| 4 — gates, docs, build | done | every gate green; the installer from a fresh clone of `origin/main` (section "Build") |

## The field bundles

The 0.42.2 evening: the host's and the joiner's Report-a-bug zips (the joiner
played three games; the host two, with an agent restart). The 0.42.0 evenings:
two host and two joiner zips. Copied into the scratchpad; no name, Steam name,
user path or address from them is in any committed file.

## Live runs (all solo, one machine, the Modding Tools game)

| run | what | build |
|---|---|---|
| H1 | host + scripted partner, on a copy of a save of this machine: relationship calibration; the partner's blows on a real wolf of the save (the pack attacked the avatar) and on a bandit-soul NPC (it turned on the avatar, drew, attacked; killed, credited to the avatar); the crime judge; the leash with the host "in a menu" (700 m → pulled), the 60 s cap with the partner "in a dialogue" (forced pull), the partner's 92 m/s flight; the field's destructive requests and the whole fight-club sequence | w147a |
| J1 | joiner of a synthetic host (a reseeded copy): a bandit, a wolf and a villager streamed; the hostile list and the engagement; `mp_test_hit` blows (the `not-living` race found and fixed after); the host's 150 hp non-fatal blow (1 hp left); the kill (a re-engagement found and fixed after); the leash pull 700 m out with no ground (the fall found and fixed after); a teleport, a flight, a refused fast travel; a 20-minute soak | w147b |
| J2 | joiner again with the fixes: 5 of 5 blows through (the knocked-over copy); the backlog A/B, fix off and its first version (36 NPCs at 10 Hz); the host's kill of the bandit stand-in did not stick (the by-name soul, found here); a pull with no ground (released mid-air at 6 s, the fall killed Henry) | w147d |
| J3 | the soul by its body: the kill `applied=dead requests=1`; the backlog with the render lag (sprint 81); a reload without the reseed: the joiner's leave picked a 1.5.6 save of the retail game ("needs newer game", refused; the leave target fixed after); the pull hung in the air | w147e (its build folder later reused for J7; the logs are kept) |
| J4 | the render lag's spike reaction (sprint 0, idle/walk flicker 369/371); the fall hold until there is ground (held the full 60 s while the body hung); the host's real height: ground found 518 m out on the first try; a reload with the reseed: the old ids kept, nothing re-guarded (the re-report added after) | w147f |
| J5 | the backlog under a sustained lag (walkers starved up to 38 s: the 300 ms guarantee added after); a reload: the stand-ins came back with their OLD entity ids, unreported — the wolf stayed unguarded, the bandit's death went to the old soul for 135 s (found in the logs while writing these pages; fixed and re-run as J7) | w147g |
| J6 | the final backlog A/B (ping 336 ms, no churn, 0 lost batches, 0 animation switches); a reload: the wolf's new body (a new id) reported and guarded again | w147h |
| J7 | J5's reload after its fix, twice: both stand-ins reported again (`id ? -> ...`), guarded, the bandit's death at once (`requests=1`, 0.3 s and 0.5 s; both reloads gave new ids, the same-id case is the Lua suite's) | w147e (rebuilt) |

Frames (`docs/wo147-shots/`, window captures, 1280×720): 1 the host's screen
right after the partner's figure (`wo147-partner`) killed the bandit (H1);
2 the joiner's screen, the bandit copy engaged, the game's enemy marker above
the compass (J1).

## What changed

* **Agent** (`dotnet/KcdMp.Client`): `Wo147.cs` (the rules: positions and
  flight, the pull's fallback spot, the joiner's pull refusal, the hostile list
  and its judge, the hit-forward rules, destructive classes, the correction
  port, the verdict), `GameBridge.Wo147.cs` (the leash lane and positions at
  read time, the host's distances and lag-aware timeout, the joiner's motion
  check, the settings, the 1 s loop and stats, the hostile engagement, the
  fight watch, guards after a load, `mp_test_hit`, quest safety),
  `QuestValueIndex.cs` (the game's own quest value table from `Scripts.pak`),
  `LeashLogic.cs` (blocking holds, the cap, forced pulls, the round-trip timeout,
  the 3-minute pause, the fast-travel lapse), `LogTailGameTransport.cs` (a
  cancelled skip ends the pause; a skip open 3 minutes is cleared),
  `GameBridge.cs` (the leash lane, the own-blow filter, the non-lethal flag on
  inbound host hits, the menu pump survives a console timeout), `GameBridge.Wo114`
  (the host tick and the joiner's pull), `.Wo121/.Wo132` (the fight watch, the
  engagement of a joiner's own avatar's fight, never a dead NPC), `.Wo131` (a
  dead stream noted), `.Wo136` (guards forgotten on a load), `.Wo137/.Wo144`
  (the destructive gate, corrections, barks), `.Wo138` (a flying partner is no
  anchor), `Wo131.cs` (the gate 6 s), `Wo132.cs` (`JudgeEngage` for the joiner's
  own avatar), `Wo137.cs` (barks), `CombatPipe.cs` (op 0x29, the non-lethal flag,
  the typed quest read).
* **Protocol** (`dotnet/KcdMp.Protocol`): the leash's forced bit (0x0100 on the
  reason) and the `flying` flag (0x0200). Append-only; protocol v10 unchanged.
* **DLL** (`native/KCDMP`): `wo147.cpp` (the `mp_test_hit` stand-in, status),
  the sampler's stamina (`rttr_abi`), the player-hit mark (`hits`), the
  non-lethal `ApplyDamage` (`pipe_server`), the heartbeat for any opponent
  (`wo132`), the typed quest read (`wo137`), the exact placement and the fall
  hold until the landing (`join_native`), a knocked-over copy bound for the gate
  (`npc_drive`).
* **Mod** (`kdcmp.lua`, the pak): `KCD2MP_W147Hostiles`, `KCD2MP_W147EndDialog`,
  `KCD2MP_W147TestHit`, the four switches and `w147_cfg`, the host's dialogue
  pause from the dialogue camera, the checklist markers (and the 0.42.2 page's,
  never registered before); the frame backlog's half: `KCD2MP.npcSilence`,
  `KCD2MP_NpcSilenceAgent`, `KCD2MP_NpcStreamSilent`, `KCD2MP_NpcSilenceRelease`
  (the puppet tick's release rule) and the stall rule in the puppet tick.
* **The frame backlog (agent)**: the reader notes every NPC's and avatar's newest
  sample (`Wo147NoteNpcAtRead`); the processor skips a superseded one when
  250 ms behind (`Wo147NpcSkip`, `Wo147GhostSkip`) and notes its lag; the 1 s
  loop gives the mod its word and the silent streams (`Wo147SilenceTickAsync`);
  rules `Wo147Rules.SupersededUnderLag` and `SilenceDue`; stats `catchup=`,
  `lag_max_ms=`, `superseded=`, `silent_notes=` in `MP-W147-STATS`. The DLL's
  writer moves its streams' silence clocks on by a stall (`npc_drive.cpp`).
* **Tools**: `tools/Test-WO147Synthetic.ps1` (+ `.lua`), `Test-WO138Synthetic.lua`
  (a bark is no dialogue), the WO-95/102/104/108/118 suites (each scenario starts
  from a fresh puppet chain: a tick that last ran in another scenario's time reads
  as a stall), the synthetic host (`npchit`, `leash pull forced`,
  NpcDamage logged, an update's hp), `Verify-Install.ps1` markers.
* **Tests**: agent 729/729 (637 in 0.42.2), `Test-WO147Synthetic` 59/59, relay
  59/59, native 298/298, every other synthetic suite unchanged and green.

## Side effects of the runs

* The throwaway playline (`playline4`: copies of two saves of `playline1`) got
  one QuickSave (made on purpose: the joiner's newest own save, so its join
  files land there), the join's own files, and 30 autosaves of the joined world
  (J1's game stayed in it for about 1.5 hours while the session was stopped at
  a usage limit). It is moved out to the scratchpad after each block of runs.
* **No real playline was written.** Checked against the checksum list after
  every block of runs; the last checks, after J6 and after J7: 318 of 318 save
  files unchanged, none missing, none new.
* **J3's leave**: a reload test without the reseed made the joiner leave the
  host's world, and the leave picked `playline2/autosave028` — a save of the
  retail game (1.5.6), which shares the saves folder. The Modding Tools game
  (1.5.5) refused it ("needs newer game"); nothing was written; the agent was
  stopped and the game quit at once. The leave target takes only saves of the
  running world's build now (`GameBridge.Wo125.cs`).
* **The maintainer's own game** (18:09–18:51, the maintainer's real playline)
  ran while the WO-147 test pak was in the Mods folder; nothing of it was
  touched (no console, no injection), and the save check at 18:51 found no
  real save new or changed.
* The Modding Tools mod folder carried the test paks during the runs; the
  original pak and manifest were put back after J6 and again after J7 (SHA-1
  `9ba56db…` and `18de23e…`, as backed up).
* Henry (the joiner) died of the fall twice in the throwaway world (J1, J2: the
  synthetic host's made-up heights); the host's avatar and wolves fought in the
  throwaway world; a quest (the fight club) was failed there on purpose by a
  request.
* No key was pressed; the game started minimized and was never brought to the
  front; frames by window capture only.
* J7's build reused the build folder name of J3 (`w147e`); J3's binaries were
  overwritten, its logs are kept.
* The maintainer's own launcher, agent and relay were not running; nothing of
  theirs was stopped.

## Gates and the build

On the final tree (VERSION 0.42.5): relay round trip 59/59, agent unit tests
729/729, all 40 `Test-*Synthetic.ps1` suites (`Test-WO147Synthetic` 59/59), both
static checks, native unit tests 298/298, the local publish and the payload
smoke — 47 gates, all green. The installer build ran every gate again inside
a fresh clone of `origin/main` (at `7dcd01a`): all green there too (the native
DLL built from scratch, the smoke relay at `release=0.42.5`), and
`release\KingdomComeTogether-Setup-0.42.5.exe` (95.8 MB, SHA-256
`b2b58c0c…6cbb6a`) sits beside 0.42.2's in the git-ignored release folder. No
GitHub release. Its payload (1,026 files) was swept for the field bundles'
player and Steam names, the Windows user names, the addresses, and any profile
path or private address: none of ours in any file. The only profile path is
inside the six NAudio DLLs, which are the NuGet package's own files unchanged
(the library author's build path); the only private-range address is the
documented example in the master server's settings comment.

## Decisions made unattended

* **Defaults.** `mp_hostile_engage`, `mp_quest_safety`, `mp_leash_cap_s 60` and
  `mp_npc_catchup` ship **on**: each ran live solo (a scripted partner or a
  synthetic host) and fails closed (only the host's guarded, bound copies are
  engaged; a destructive step is applied only when the host's world agrees or
  out of a conversation; the cap forces only holds that are not loads; the
  backlog fix changes nothing when the agent keeps up). The work order's
  "default on only when proven" was read as "proven live", which solo runs are.
* **The enemy test** is the game's own public-enemy flag (the relationship read
  0 for a bandit); the relationship threshold is −0.1 for everyone else.
* **Quest safety's wait** is 10 s; the death-name rule leaves out 36 names that
  only speak of a death.
* **Mounted pulls**: dismount cleanly, the horse stays.
* **The fall hold** after an exact pull lasts until the navmesh is within 2 m
  under the joiner and he has stopped dropping, at most 60 s (a body the engine
  holds in the air while the area streams in drops only later).
* **The backlog fix** was asked for during the work and is in this version;
  its thresholds: 250 ms behind, 300 ms per NPC, the agent's word 10 s, the
  mod's stall 6 s (the field's freezes were 9–28 s; the older Lua suites jump
  their clocks 3–5 s between ticks), the DLL's stall 1 s.
* **Five older Lua suites** (WO-95, 102, 104, 108, 118) start each scenario from
  a fresh puppet chain now: their scenarios jump the test clock between
  scenarios, which the stall rule read as a freeze. No check was changed.
* **The leave target** (WO-124/125) takes only saves of the build of the world
  the game runs: J3's leave picked a 1.5.6 save (the retail game shares the
  saves folder), which the 1.5.5 game refused ("needs newer game"). No save
  was written; found because the reload test changed the world's identity.
* **`mp_test_hit`** (the console stand-in for the player's blow) ships in the
  mod like the other test commands.
* **The re-created copies (J4/J5's logs)** were fixed after the planned runs
  and checked in one more live run (J7) before the build: a new body is
  reported whatever its id, and the agent's soul cache never outlives a load or
  a new body. It rides the copy guard and the soul-by-body fix; no new switch.
* **The 0.42.2 tester page's markers** (`mark_rejoin` and five others) were
  never registered; they are now, with this version's.

## Runbook: how the live runs were made

One machine, no second player. The harness is the repo's own tools:
`tools/wo121/avatarpeer` (a scripted partner: positions, `npchit`, `quest
request`, `leash on|flags|obey`, `move`) and `tools/wo118/synthpeer`
(`--join-host125`: a synthetic host that serves a join from a save copy and
takes `pos`, `npc`, `npchit`, `npccombat`, `leash warn|countdown|pull [forced]`).

1. Back up every playline with a checksum list; make a throwaway playline of
   save copies; QuickSave once there (the joiner's newest own save).
2. Host runs: start the Modding Tools game minimized, load the copy, a save
   lock, inject the build's DLL, a relay on a free port, the agent with
   `--hosting`, the avatarpeer; its control lines drive the checks:
   `npchit <npc> <hp> <st>` (the partner's blow), `stand x y z yaw` far away and
   `leash flags in-world[,dialogue]` (the leash), `move 90 0 5` (a flight),
   `quest request <flags> <old> <new> <port> <questLen> <path>`. The host "in a
   menu": `mp_slow_time` (the manual pause, the same hold as the inventory).
3. Joiner runs: the game at the main menu, the synthpeer as the host with a
   reseeded copy, the agent; the join from the menu; then `npc` rows beside the
   joiner (a stand-in is made for a name this world lacks), `DrawWeapon` by
   console, `mp_test_hit <npc> <hp> [stamina]` for this player's blow, `npchit`
   for the host's own blow, `pos` + `leash` for the pull.
4. Read the agent's log, `kcd.log` and the DLL's mirror log; frames by window
   capture (`win.ps1 shot`), the camera by `PlayerSetViewAngles`.
5. At the end: quit the game by console, put the mod folder back, move the
   throwaway playline out, compare every save's checksum with the backup.
