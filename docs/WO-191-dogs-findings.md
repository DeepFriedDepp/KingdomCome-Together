# WO-191 follow-up — Village dogs in the NPC stream (`mp_dog_sync`): findings

Code: `kdcmp.lua` (the block after the WO-191 commands, and one-line hooks in the rescan, the resync burst, the scan compare,
`KCD2MP_ApplyNpcState`, `KCD2MP_W131ParkReleased` and `KCD2MP_W136StandInSpec`), `native/KCDMP/npc_scan.cpp` (class `Dog` joins the
animal list). Agent and protocol unchanged. Tests: `tools/Test-WO191DogsSynthetic.lua` (44 checks); live harness `tools/wo118/run191dogs.py`
(the synthetic peer now logs which NPCs and activity rows reach it). No version bump, no installer.

Evidence marks as in the WO findings: **(observed)** live in the game; **(synthetic)** the real game against a synthetic peer, or the mod
under the Lua driver; **(code-verified)** read in the code, not run.

## 1. Why the dogs stood still

A village dog is entity class **Dog**. The host's NPC stream carried NPC, NPC_Female, Horse and the encounter animals Wolf, WildDog and
Boar, in two lists: the native scan's class registry list (it pre-filters the candidates the mod's rescan sees, and is on by default) and
the mod's `w136.ANIMAL`. Neither had Dog, so the host never tracked a village dog, and the joiner's guard paused each one where it stood
(`WO131-GUARD pause ... class=Dog why=not-streamed`, WO-153 2). Field logs name 23 such dogs; a town has up to five (Troskowitz
`ttkc_dog_1`–`5`, Tachov four, most villages one or two).

## 2. What changed (`mp_dog_sync`, both roles, default **on**)

| where | with the switch on | off |
|---|---|---|
| native scan | always reports class-Dog bodies as not-human candidates (no switch in the DLL; the mod decides) | — |
| host rescan, resync burst, scan compare | a class-Dog body is tracked and streamed like an encounter animal (flag 0x80) | 0.60.7 |
| the player's own dog (Mutt: `tvez_vorech`, `player_dogCompanion*`) | never streamed by the host; a host stream naming it is refused on the joiner (`DOG-SYNC refused`) — each world's Mutt follows its own player | 0.60.7 |
| joiner, a dog copy the stream lets go of | paused where it stands and **shown** (`DOG-SYNC released`), the way WO-153 keeps an unstreamed dog; a human copy is still parked (hidden) | parked (hidden) |
| joiner stand-in for a dog this world lacks | the host's own soul in class Dog | none |
| WO-191's attacker pin | unchanged (it still streams an attacking dog when the switch is off) | unchanged |

Dog is deliberately **not** added to `w136.ANIMAL`: that set also means "an enemy" (`KCD2MP_W147Hostiles` reports an encounter animal as
-1, which drives the joiner's hostile handling). A village dog keeps the game's own relationship, like a villager.

The DLL's `body_kind` now returns 2 (animal) for a Dog, so a dog copy whose physics is not a living entity is written through its entity
like a wolf (WO-144 2.3) instead of being refused `not-living`.

## 3. Evidence

| check | result |
|---|---|
| synthetic suite `Test-WO191DogsSynthetic` | **44/44** (synthetic): the switch; the class gate (Mutt by both names, other classes, off); the rescan tracks a dog and streams it with flag 128, never Mutt, lets it go when switched off, the WO-191 pin still works off; the joiner refuses Mutt's stream and makes a copy of a village dog; a released dog is paused, shown, kept by the guard's sweep and never hidden (a human is still parked); the enemy list reads a dog's relationship (0.30) and a wolf's -1; the stand-in class |
| all mod suites and static checks | 68 synthetic suites and the 4 static checks pass (with the new one) |
| host role, Troskowitz (real game, synthetic joiner) | **(synthetic)** `ttkc_dog_1`–`5` tracked; each streams with flag 128 (`npc_state ... 128`); the synthetic joiner received all five (`SYNTH got npc ttkc_dog_N ... flags=128`). Mutt was not nearby, so its exclusion is proven by the suite only |
| a dog's activity on the host | **(observed)** dogs have an NPC-state context and WO-141 captures it: `ttkc_dog_2: stance=lying unstance=animal_dog_sleep2` (lying asleep), `ttkc_dog_3: stance=sitting`, then `none` when it walked. So lying down travels on WO-141's activity rows, like an NPC's bench or bed |
| frame rate, host, Troskowitz (`run191dogs.py h2`) | **(synthetic) A/B, pass.** `mp_dog_sync` on / off / on, 90 s each, one game session, the window as it was: tracked 80 (5 dogs) / 76 (0) / 78 (5); fps 72.3 / 71.8, 72.6 / 70.9, 67.1 (60 s `FRAME` windows): on/off **0.971**; the DLL's own time `ours_us_mean` **696 µs on vs 684 µs off** (+12 µs, about 2 %). Only 1–2 windows per phase, so the fps ratio is coarse; the DLL time is the steadier number. The native walk costs the same either way (it visits every entity); the dogs add 5 bodies to about 75 |
| joiner role, `ttkc_dog_2` (`run191dogs.py j1 ttkc_dog_2 2 0 582 0`) | **(synthetic) pass.** The synthetic host walked the dog 8 m, sent `lying` + `animal_dog_sleep2` (unstance id **582**, from the DLL's research verb `unst`), then stopped streaming it while staying connected. The copy bound (`MP-NPCBIND ... result=ok`, its physics is a living entity), followed the walk (2 samples inside the 6.7 s walk, at most 0.44 m off; REST reads are slow then), and took the activity: `WO141-APPLY ttkc_dog_2 -> ok`, and the copy's own state reads back `stance=lying unstance=animal_dog_sleep2`. It held the spot exactly while lying. When the stream stopped: `DOG-SYNC released npc=ttkc_dog_2 why=silence`, still shown, and it did not move in 11 samples over the next 30 s |

The first live session ended early: at 14:49 the game was quit from its own menu (`MenuOpen` → menu focus moves → `C_UISaveLoad::ExitGame`)
by someone at the machine. The maintainer then said the machine was free, and h2 and j1 ran in a second session. In both sessions the saves folder
had been backed up byte-equal first (958 files) and was 958/958 equal afterwards. The three saves the runs made in the throwaway `playline4`
(the mod's manual host saves) were moved out, and the Mods folder was restored from its backup.

Seen on the way (j1's first try): when the synthetic host's plan ended and it disconnected, the joiner became the host and the guard gave
every copy back (`WO131-GUARD state=off`); the dog then walked off under its own brain. That is the existing partner-left path, not the
release. The harness now ends only the dog's stream (`window`) while the host stays.

## 4. Open

- **On screen:** j1 was checked through the engine's own state (position, the copy's NPC-state). Nobody looked at the copy on screen, so whether
  it plays a walking gait rather than sliding, and the lying pose, is still to check. Two real players are needed for the end-to-end view.
- The synthetic peer saw no activity rows at all in the host run (not even humans'), so its new activity log line is unproven. The
  host's capture lines are the evidence above.
- A dog name the joiner's world lacks and the soul tables do not know still falls back to WO-136's species soul (`dog` → WildDog). Only
  a runtime-spawned dog can hit this; level dogs exist in both worlds.
