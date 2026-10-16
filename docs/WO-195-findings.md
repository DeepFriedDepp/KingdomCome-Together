# WO-195 — Dead copies fall and end the fight, stealth for the joiner, a body's key (no version bump)

Code: agent `Wo195.cs` (the rules) and `GameBridge.Wo195.cs`, hooks in `GameBridge.cs` (every death apply, the joiner's own fatal blow),
`GameBridge.Wo132.cs` (no engagement of a dead copy), `GameBridge.Wo139.cs` / `Wo139.cs` (the report's stealth field),
`GameBridge.Wo141.cs` (the host's sleepers), `GameBridge.Wo134.cs` (the loot screen's take); native `wo195.cpp` / `wo195.h` /
`wo195_rules.h`, gates in `motion.cpp`, `wo187.cpp`, `wo132.cpp`; `kdcmp.lua` (the switches, the witness rule, the ragdoll, the
loot-screen item). Tests: native `wo195_tests`, agent `Wo195Tests` and `Wo195ReplayTests` (the joiner's 0.60.5 bundle), Lua
`Test-WO195Synthetic` (58) and `Test-WO195LootSynthetic` (17). Harness: `tools/wo118/run195.py`.

All live evidence is single-machine: the Modding Tools game with a synthetic partner through a local relay, on the `playline4` throwaway.
Nothing here is two-player evidence.

## 1. A copy that dies stays in the fight and stays standing (`mp_dead_release`, on)

**0.60.7–0.60.9:** nothing in this path changed (WO-190's engage hold only acts while the host's NPC fights; no death handling was added to
the opponent wish, the guard hold or the writer).

**What the bundle shows** (more than the WO's reading of the agent log):
- The skirmish *was* left at both deaths: the DLL logged `WO132-ENGAGE eid=0x9136 off: … skirmish left` at 21:03:27.280 and `eid=0x5722 off`
  at 21:03:46.711. 0.60.5 removed the engagement quietly inside the death apply, so the agent log had no `engage off` and `engage_off`
  was never counted.
- What kept going was WO-187's opponent wish (alive 4 s after the agent's last word, re-applied every re-check): the engine cleared the dying
  copy's target and the DLL wrote it back — `WO187-OPP npc=tzel_woman_8 overwritten … written back` at 21:03:27.544, after the death — and on
  `tzel_man_13` it also pinned `combat_forcedTarget` in the death's own frame (21:03:46.710). The replay finds both.
- The standing body has a second, separate cause, found live: the mod's `ApplyDeath` kills the soul without the game's death reaction. A
  copy killed that way stays upright, frozen in the dead action's first pose, whether or not it was fighting (live: a copy fighting the
  joiner and a copy killed 99 m away both stood; their physics was still a living entity). A villager killed by a real blow lies as a
  ragdoll. Distance is not the cause; the far copy got the host's death on its first packet (WO-122's owner death, `applied=dead`) and
  stood like the near one.

**Fix:**
- At every death apply of a copy (owner death, 0x31 FATAL, 0x27 transition or resync, this player's own fatal blow) the agent releases
  everything before the apply: WO-132's engagement (now logged `MP-W132 engage off … the copy is dead`), WO-147's own engagement, WO-187's
  guard hold (op 39 off), its wish and pin (op 36 clear), and the DLL's combat mode on the body (op 56). The copy is marked dead and is never
  engaged, guard-held or wished again until the host's stream says it lives (a reload).
- The DLL refuses a body that reads dead (WO-172's `copy_dead`) everywhere a fight hold is made: the writer's per-frame combat hold, the
  wish's apply, re-check and wind-up, a new wish, the guard-hold op and the engage op. Refusals are logged once per body (`WO195-DEAD`).
- 0.4 s after the release the agent reads the body (op 57). A copy that reads dead while its physics is still a living entity gets the
  game's own ragdoll (`Entity:RagDollize`, `WO195-RAGDOLL`), and is read again (`WO195-FALL … living=no(ragdoll) ragdolled=yes`). Two
  seconds after the release this player's own combat state is logged (`WO195-PLAYER`).
- `KCD2MP_W195Check("<npc>")` logs the DLL's read of any body (dead, combat mode, ragdoll, a fight hold) — `WO195-CHECK` in the agent log.

**Live (the game as the joiner, a synthetic host):** a copy 2 m away engaged with this player (`MP-W132 engage on`), its guard held and its
opponent wished, then killed by the host's stream: `WO195-RELEASE … wish=yes guard=yes native_held=combat`, `WO195-FALL after_ms=922 dead=yes
combat_mode=off living=no(ragdoll) fight_held=no ragdolled=yes`, `WO195-PLAYER … combat_mode=false` at 2 s, no WO187-OPP write after the
death. The same body photographed before the fix stood slumped; after it, it lies. A copy killed 99 m away, then approached: dead and a
ragdoll. WO-136's own path ended the engagement first in both runs (`MP-W136 … the engagement ends FIRST`), so the release found
`engaged=no` and the guard hold, the wish and the combat mode still on.

## 2. Stealth: the host's witness rule ignored it (`mp_stealth_witness`, on)

**0.60.7–0.60.9:** unchanged since 0.60.5 (the rule was geometry and `IsSleeping` only).

**Fix (the mod's own rule, no reverse engineering):**
- 2a. Asleep never witnesses: `IsSleeping()`, or the host's own activity read (WO-141's capture) has the NPC lying on an object — its
  bed. The host's agent sends each change to the Lua (`KCD2MP_W195Asleep`). A lying body with no object (a corpse, a knockout) is not
  a sleeper.
- 2b. The joiner's mod sends its stealth once a second on a change (and every 30 s): crouch, a light in hand, the game's own derived stats
  `vis`, `noi`, `con`, and the stealth skill. Every crime report carries the latest as an optional 8th field (`c=1,l=0,v=…,n=…,s=…,k=7`,
  checked whole on both sides; a 0.60.9 joiner sends seven fields and is judged with the dark and the sleepers only). The host also
  counts the partner's figure's torch.
- 2c. The witness radius = 25 m × clamp(1 + darkness + crouch + visibility, 0.12, 1.3) × (1 − 0.3 × skill / 30):

  | factor | value | source |
  |---|---|---|
  | night outdoors (21:00–05:00) | −0.7 | the game's `VisibilityDecrementNighttimeExterior` |
  | interior by day | −0.3 | the game's `VisibilityDecrementDaytimeInterior` |
  | interior at night | −0.7 | the mod's (the night's value) |
  | a light in hand | the darkness is 0 | the mod's |
  | crouch | −0.5 | the mod's |
  | visibility | (vis − 1.0) × 0.5 | the mod's scale on the game's `vis` |
  | skill | −30 % at skill 30 | the game's `StealthSkillToViewRadiusDecr` 0.3 |

  A noise radius = (4 m standing, 0.6 m crouching) × clamp(noi / 1.0, 0.5, 1.6) × (1 − 0.6 × skill / 30) (the game's
  `StealthSkillToFootstepSoundMult`): an awake NPC within it hears him wherever it looks; a sleeper within it wakes. Every judged NPC is
  logged: `WO195-WITNESS id=… npc=… dist=… asleep=…(IsSleeping|bed) lit=… dark=… crouch=… sight_m=… noise_m=… counted=seen|heard|woken |
  ignored=asleep|far|unseen`.

**Live (the game as host, a synthetic joiner's figure):** at 23:30, crouching (stealth 7) 1.0 m from `ttkc_gerta` lying in her bed (the
host's activity read): `ignored=asleep`; four more sleepers ignored, the one awake NPC at 11.9 m `ignored=far` (sight 2.8 m) — no crime.
At noon, standing with a torch at the same spot: `ttkc_man_32` at 4.8 m `counted=seen` — a crime. Notes: the live check used a theft
report: at that spot WO-157's own check calls the place not private, so a trespass report is refused before the witness rule; the
engine's `System.IsPointIndoors` read false beside the bed, so indoors the night's value applied, not the interior's.

**What the joiner's game really sends** (the joiner runs): `c=0,l=0,v=-99.00,n=0.00,s=0.60,k=7` — `vis` reads nothing on this build (no
such derived stat name; −99 = unread, so the visibility term is 0), `noi` is 0.00 standing still (the noise radius then takes its
floor, ×0.5), `con` 0.60. The visibility and noise scales are uncalibrated against the game.

**For an RE WO (not done here):** judge the joiner's crime with the engine's own perception of his figure, the way the engine judges the
host's Henry — and with it the game's real visibility and noise values and the indoor test.

## 3. Keys and loot on a host NPC's body (`mp_body_loot_host`, on)

**0.60.7–0.60.9:** unchanged.

**The cause:** body looting already went through the host's world (WO-134): the loot screen lists the host NPC's items and each take is
claimed there. The field's key was not an item of any inventory: the game's loot screen makes the NPC's home key (`key_home`,
494ed6dc-…) as it opens (`WO134-BODY not-a-put … the loot screen's key`), on the joiner's copy just as on the host's body. The joiner's
take of it asked the host, whose body never held it, and WO-136's verdict `none` took it back off his Henry quietly
(`takeback-applied … (none)` in the bundle). Live: the game's loot screen on a dead copy made the key 215 ms after it opened.

**Fix:** the joiner remembers an item the loot screen made (`WO195-LOOT screen-item`), keeps it in the body through a host update, and its
take goes to the host marked `screen`. The host grants a screen item once per body and class (`ok`; `mine` for his own repeat; `gone` when
someone else — the host's own looting included — took it first), notes it in the body ledger, and removes the copy its own loot screen
makes of it afterwards (`WO195-LOOT host-dup`), so there is never a second one. An item really in the host body's inventory is the normal
take, out of the host's body. No quest item is held back by these rules; the home key is not a quest item.

**Live:** the joiner's half with the game's real loot screen (the screen made the key, its take went out marked `screen`); the host's half
in the game's own world: a key placed in a villager's inventory came out of his body on the take (`ok`, gone from the body), another
body's loot-screen key was granted once and refused to a second joiner (`gone`).

## 4. Tests

| check | result |
|---|---|
| native (KCDMP_NativeTests) | 9,592 passed |
| agent (KcdMp.Client.Tests, with Wo195Tests and Wo195ReplayTests) | 1,781 passed |
| Lua `Test-WO195Synthetic` / `Test-WO195LootSynthetic` / `Test-WO139Synthetic` / `Test-WO134Synthetic` | 58 / 17 / 131 / 61 passed |
| replay: both engaged deaths released ahead of 0.60.5's post-death writes; the taken-back key was the screen's | pass |
| live item 1 (engaged copy killed; a copy killed 99 m away and approached) | pass (on the ground 0.92 s, combat over at 2 s) |
| live item 2 (sleeper in bed at night, crouched: no witness; noon, standing, lit: a witness) | pass |
| live item 3 (the screen's key on the joiner; the host's key take and screen grant) | pass |
| regression WO-191 stale actions (a relaunched partner's swings) | 6 + 6 accepted, 0 stale |
| regression WO-191 attacker bind | pinned (`WO191-BIND … already streamed`), the partner got its stream |
| regression WO-192 host autosave | three host saves, each verified |
| regression WO-184 save round with items | not run as its own harness; the build's live save round trip instead |
| regression WO-187 echo test | not run: it needs real input with the game in front |
| frame rate in town (WO-190's scenario, mp_dead_release on/off/on) | 77.0 / 74.2 / 77.4 fps (WO-192: 73–74) |

**Validation build** (local only, not a release; header unchanged, 0.60.9): `tools\Build-Installer.ps1 -SoakWaiver` from a clean clone of
`f6b064f`: every gate passed, the live save round trip PASS; `KingdomComeTogether-Setup-0.60.9.exe`, 107,127,866 B, sha256 1f589a4c…
(the first attempt stopped on WO-154's game suite: the new witness rule read `Calendar` unguarded — fixed in `f6b064f`).

The live saves went to the `playline4` throwaway; the saves folder was backed up first and is byte-equal to the backup afterwards (the six
test saves moved out).

## 5. Open

- Two-player: the checklist's §WO-195.
- Item 2's visibility and noise scales against the game's own (the RE item above).
- The WO-187 echo test and WO-184's item round were not run here.
