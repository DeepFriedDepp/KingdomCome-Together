# WO-195B — Stealth and thievery that feel like single player for the joiner (no version bump)

Code: `kdcmp.lua` (the witness rule, the joiner's stealth reads, the engine wake, the pickpocket judge, the knockout wake, the interior
state), the chest sync (`mp_chest_sync`, on the DoorAsk transport) in `kdcmp.lua`, `GameBridge.Wo151.cs`, `Protocol.cs`, `Wo151.cs`.
Tests: Lua `Test-WO195Synthetic` (118, with the live calibration rows), `Test-WO195BChestSynthetic` (71), `Test-WO135Synthetic` (45),
`Test-WO151Synthetic` (159); the other 67 Lua suites unchanged and green; agent `Wo195BChestTests`. Harness (never shipped):
`tools/wo118/run195b.py`, `live195b.py`, `calib195b.py`, `lua/w195b.lua`. The calibration table: `docs/WO-195B-calibration.csv`.

All live evidence is one machine: the Modding Tools game, the host's own Henry and a synthetic joiner's figure, on the `playline4`
throwaway. The saves folder was backed up first and is byte-equal to the backup afterwards (the session's eleven test saves moved out).
Nothing here is two-player evidence.

## 1. The engine as the reference

What the engine itself says about the host's Henry (all read live, all on this build):

| read | value | meaning |
|---|---|---|
| derived stat `vis` | nothing | no such stat (this is why WO-195 read -99) |
| derived stat `vib` | day standing 1.19, day crouching 0.69, night standing -0.95, night crouching -1.45; up to 3.5 in a lit spot; +0.5 while walking | the game's own visibility: stance, light, the time of day and equipment are all in it |
| `con` | 0.60 standing, 0.10 crouching | conspicuousness. The game's suspicion formula uses it (with the stealth skill, reputation and distance); it is not its sight |
| `evi` | 0.2 | equipment visibility (inside `vib`) |
| `fsm` | 0.43 standing, 0.129 crouching | the footstep multiplier |
| `noi` | 0.00 always, walking included | not a noise reading on this build |
| an NPC's `vir`, `fov` | awake 18.7 .. 21.4 m (28.1 when alerted), 91 .. 100 deg; **0 asleep** | its own view radius and field of view |
| area label `interior` (`XGenAIModule.IsPointInAreaWithLabel`) | true beside the bed spot where `System.IsPointIndoors` reads false | the game's own interior state |

The engine's own verdicts used (each a row of the table):

- **A sleeper's wake.** `XGenAIModule.ProduceSound(3, pos, mult)` (the engine's movement sound) made at 1.1 .. 6 m from sleepers, Henry
  beside them: at 1.0 a sleeper gets up within about 1.5 m, at 0.43 within about 1 m, and never beyond 1.6 m. That is a square-root law
  (1.5 x sqrt(mult)). Henry's own footsteps (the game's walk key, standing, fsm 0.43) woke sleepers at 1.1 / 0.7 / 0.7 m, and none at 3.0 .. 1.2 m
  on the approach: the same as the sound at 0.43. A sleeper that wakes gets up and walks away (5 m in 5 s).
- **A torchless Henry at night in front of the awake night guard** (the guard's torchless-player bark is the engine's "noticed"), standing,
  facing: not noticed at 20, 16, 12 m, noticed at 9 m (within 10 s). Behind the guard: not noticed at 10, 7, 5, 3, 2 m.

What did not work as a reference (stated so it is not repeated): the engine raises nothing for an innocent Henry (no recognition, no
awareness for a man standing in front of people); the torchless bark is only raised for a standing Henry (a crouching one at 14 .. 2 m, even 30 s
at 5 and 3 m, was never barked at: those rows are in the CSV, not scored); the drawn-weapon offence gave one barker per run (a global cooldown).
So the sight side rests on one night guard and one visibility level (`vib` about -0.8).

## 2. The calibration table

All 80 scored cases (the full list is `docs/WO-195B-calibration.csv`; rule before = WO-195 as it stood, with the readings an unchanged joiner
would send; rule after = this build's rule on the same row; the rule after is the real Lua function, scored in the unit test B10):

| group | cases | rule before | rule after |
|---|---|---|---|
| sleeper wakes (engine sound x1.0 and x0.43, Henry walking) | 69 | 56 (81 %) | 65 (94 %) |
| awake sight (the night guard, standing) | 11 | 9 (82 %) | 10 (91 %) |
| **all** | **80** | **65 (81 %)** | **75 (94 %)** |

The sight rows (the engine · rule before · rule after):

| case | engine | before | after |
|---|---|---|---|
| night, standing, no torch, in front, 20 m (vib -0.75) | no | no | no |
| ... 16 m (-0.76) | no | no | no |
| ... 12 m (-0.78) | no | no | no |
| ... 9 m (-0.79) | **yes** | no | yes |
| ... 3 m, lit spot (3.50) | yes | yes | yes |
| behind it, 10 m (-0.84; line blocked) | no | no | no |
| ... 7, 5, 3, 2 m (3.50) | no | no | no |
| ... 1.2 m, it has turned to him (3.50) | no | yes | yes (the one miss: it had been alerted for 40 s) |

The wake rows by distance (cases / the engine woke it / before agrees / after agrees):

| source | distance (m) | cases | woke | before | after |
|---|---|---|---|---|---|
| engine sound x1.0 | 1.0 - 1.5 | 4 | 3 | 3 | 3 |
| engine sound x1.0 | 1.5 - 3 | 5 | 0 | 0 | 5 |
| engine sound x1.0 | 3 - 6 | 12 | 0 | 11 | 12 |
| engine sound x0.43 | up to 1.0 | 4 | 3 | 3 | 2 |
| engine sound x0.43 | 1.0 - 1.5 | 2 | 0 | 0 | 2 |
| engine sound x0.43 | 1.5 - 3 | 7 | 0 | 5 | 7 |
| engine sound x0.43 | 3 - 6 | 25 | 0 | 25 | 25 |
| Henry walking | up to 1.0 | 2 | 2 | 2 | 2 |
| Henry walking | 1.0 - 1.5 | 2 | 1 | 1 | 1 |
| Henry walking | 1.5 - 3 | 6 | 0 | 6 | 6 |

Not scored: 11 rows (the crouching series, and the 8 m row of the first series, whose window was 8 s: a "no" needs the 10 s the 9 m "yes"
took).

## 3. The rule, before and after

| | WO-195 | now |
|---|---|---|
| visibility | `vis`, unread: night -0.7, interior, crouch -0.5, light cancels the dark (the mod's guesses) | his own `vib`: factor = 1 + (vib - 1.19) x 0.25, 0.10 .. 1.35 (1.35 = the engine's `VisionToViewRadius`); the old terms only for a joiner whose `vib` does not read |
| sight radius | 25 m for everyone | the NPC's own `vir` (18.7 .. 21.4, 28.1 alerted; 20 m when it does not read) x the factor x the skill factor (-30 % at 30) |
| field of view | about 210 deg | the NPC's own `fov` (91 .. 100 deg, +5); anyone within 0.6 m sees him whichever way it looks (a pickpocket works from behind at 1 m, not at arm's length) |
| noise | 4 m standing / 0.6 m crouching x the unread `noi`: a still man 2 m from a sleeper woke it | 1.5 m x sqrt(the 2 s PEAK of his footstep level), the level = `fsm` x his pace (walking = 1, capped 2.5); 0 when he stands still; capped 4 m |
| noise reading | `noi`, the instant value (always 0) | the peak over the last 2 s, sampled at 5 Hz from his own positions (`KCD2MP_W195PollNoise`) |
| interior | `System.IsPointIndoors` (false beside a bed) | the game's own `interior` area label, then `System.IsPointIndoors`, then a roof within 6 m only when the engine gives no answer |
| asleep | `IsSleeping` or the bed read | also the engine's `crime_sleeping` context; a sleeper never sees him |

The joiner's wire text is unchanged (`c=,l=,v=,n=,s=,k=`): `v` is now `vib`, `n` the 2 s peak, `s` `con`. A 0.60.9 peer reads the same
fields (an old host sees `vib` as `vis`; an old joiner sends -99 and is judged with the old terms, silent).

The skill factor is applied to sight only: the engine's `fsm` is already a derived stat (it is not known whether it carries the skill).
The one number that is only bracketed: the visibility slope (0.25; the night guard's 9 m "yes" and 12 m "no" allow 0.2 .. 0.28).

## 4. The checks

**2a. Locks (live, the host's side).** A claim from the joiner (`KCD2MP_W195BChestAsked`, the door's `KCD2MP_W151DoorAsked`) unlocks the
host's chest or door with the game's own `Stash:Unlock` / `AnimDoor:Unlock`: chest `chest30` and door `door_village_left55` went from locked to
unlocked, their controls (`chest31`, a house door) stayed locked. A host save (the game's ManualSave through the mod's own path) and a reload
(`LoadGame 4 save037`, `save038`): both still unlocked, both controls still locked. The chest sync is new here (a joiner's picked chest was
unlocked only on his machine): the DoorAsk transport with a stash bit (flags 3), `mp_chest_sync` (on). Not checked live: the joiner's
side (the wrap of the real lockpick / key path) and the wire; they are covered by the 71-check synthetic suite and the relay tests.
A lock picked in sight is judged by the rule like any crime (the WO-139 report carries his stealth).

**2b. Takedowns (live).** The host's avatar `RequestKnockOut` does not take on a puppet; the fallback `unconscious` buff did knock the NPC out
(`IsUnconscious` true, never dead) but it never ended (the knocked-out guard lay 2.5 game hours): the game's own knockout wakes after its depth
(400) at the NPC's fade-out speed (`ufo` 1.6). The fallback now arms that wake (`WO135-KOWAKE`: 250 game s, 17 s at x15): the NPC woke after 17 s
(the game's `remove_unconsciousness`, which also stood the guard up at once). The takedown's crime report carries no stealth: the host now
judges it with what it sees of the figure (its stance, its pace, its torch), instead of "standing, no light".

**2c. A woken sleeper wakes (live).** A walking figure (n = 0.43) 0.7 m from a sleeper: `counted=woken`, `WO195-WAKE` (the engine's movement sound
at his spot), the sleeper got up and walked 5 m. A still figure at 0.7 m beside another: `ignored=asleep`, it slept on. A crouch-walking figure
(n = 0.129, 0.5 m radius) at 0.7 m: ignored, asleep. `mp_wake_engine` (on) makes the sound only when the rule says "woken".

**2d. Pickpocketing in the dark (live, the rule's side).** An item taken out of a living NPC (the host's WO-134 take) is judged by the rule at
the victim's spot, the victim's eyes and ears included (`WO195-PICK`). The night guard, the figure 1 m from him: from behind, crouching or
standing, still: `witnesses=0 ... nobody saw it: no crime`; face to face: `witnesses=1 guards=1 ... a crime in this world`. Not done: a comparison
with the odds of the host's own Henry in the same spot (the game's own pickpocket minigame cannot be driven from outside); the joiner's own
minigame still runs on a paused copy.

## 5. Regression (live, this build's Lua)

| check | result |
|---|---|
| WO-195 item 1 (an engaged copy killed; a copy killed 94 m away) | pass (on the ground in 0.89 s, combat over at 2 s) |
| WO-195 item 2 (a sleeper in bed at night: no witness; noon, standing, lit, in front of an awake NPC: a witness) | pass (the harness's stealth strings now carry his real reads) |
| WO-195 item 3 (the host's key take; the screen's key on the joiner) | pass |
| WO-191 stale actions (a relaunched partner's swings) | 6 + 6 accepted, 0 stale |
| WO-192 host autosave | two host saves, each verified |
| the other 67 Lua suites, WO-135 / WO-151 re-run | all green (the WO-151 guard now faces the crime spot in its test) |

## 6. Still unlike single player

- The sight scale has engine verdicts at one visibility level (a standing Henry at night, `vib` about -0.8) and from one guard. Crouching,
  daylight, a torch and heavy armour are in `vib`, `fsm` and `evi` by construction but have no engine verdict of their own (the torchless-player
  bark is not raised for a crouching Henry; a torch drawn from Lua did not register as one). The slope is bracketed, not fitted.
- The engine notices over time (the guard's 9 m "yes" came in the 10th second); the rule decides at the moment of the crime.
- The noise reach is the engine's wake radius for its movement sound; whether an awake NPC hears at the same radius is not measured, and the
  noise a lockpick or a fight makes is the engine's own and is not added to the joiner's.
- A joiner's pickpocket that fails inside his own minigame (a paused copy cannot notice) is still caught by nobody.
- The chest sync needs the same chest in both worlds (pivot within 1 m and the name hash); an old host reads the stash bit as a door ask.
- Crouch-walk and sprint rows rest on the square-root law of the sound, not on their own engine rows.
