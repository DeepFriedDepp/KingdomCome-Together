# WO-131 — combat and bodies: findings

Unattended, solo, one machine: the Modding Tools game, a throwaway save, a
local relay on its own port. The real game ran as the **host** with
`tools/wo121/avatarpeer` as the joiner (run A), and as the **joiner** with
`tools/wo118/synthpeer` as the host (runs B–E). Evidence marks: (observed),
(code-verified), (synthetic), (inconclusive). Animations are judged only by
consecutive frames on the joiner's screen. No installer: `VERSION` is left as
it is in the repo (**0.30.2**; the prompt names 0.30.5, which is not on
`origin/main` — see the progress file). Screens: `docs/wo131-shots/`.

## 0. Answer first

**Priority 1 — the joiner is seen, and every fight is in one place.**

| item | result |
|---|---|
| 1a no split NPCs | **fixed** (observed). On the joiner every host-owned NPC is the host's stream (bound by identity wherever it was, placed at the host's position) or **parked: suspended and hidden**. A released copy is parked, never resumed. Joiner CSV: **0** human rows with `suspended=0 driven=0` over run C (249 s) and run E (§4.1, one 1 s transient at a save load, three far bodies) |
| 1b hits on the right body only | **fixed** (observed). Forwarded only when the DLL says the copy is driven by a host sample ≤ 3 s old and the body is ≤ 3 m from it. A hit on a free copy: `reason=wo131-not-bound`, not sent |
| 1c host-only deaths and bodies | **fixed** (observed). Copies carry the imm guard (a 500 hp local blow leaves 1 hp), follow the host's health, die only on the host's order at the host's position (0.9 m off); a local death is never sent. **Looting and pickpocketing host-owned NPCs is blocked on the joiner** with the game's own toast (observed) — loot-as-a-request was not built |
| 1d NPCs perceive the joiner | **partly** (observed / inconclusive). On the host of a shared world the avatar is never AI-ignorant and is re-parented onto **the player's faction** (read back: `FactionNode/Parent/Name = player`); wolves targeted and bit it and it joined Henry's side of their skirmishes (observed). A human hostile attacking it unprovoked was **not** produced: spawned bandit souls ignore even Henry, and a live bandit camp's reactions (warn, chase out) are scripted on the player only — they reacted to Henry at 4 m, not to the avatar at 4 m (observed). Guards: not tested (no guards outside towns) |
| 1e the joiner's armor counts | **not measured** (inconclusive). The direct defect is fixed: the avatar is immortal (imm guard) and was never healed, so after the first hits it sat at 1 hp and later NPC hits measured little or nothing; its health is now restored after every measured hit (code-verified; no damaging NPC hit on the avatar happened in the runs — the wolves' bites did 0 health) |
| 1f runtime-spawned NPCs | **fixed for humans** (observed). A name the host streams that has no body on the joiner gets a **stand-in under that exact name** (a road bandit wears a bandit soul), bound, guarded, removed when its stream stops. Horses carry a new stream bit 0x80 and never get one; animals are still per-machine |
| 1g fights end with a death | **fixed** (observed). The joiner's death/respawn → the host StopFights its avatar: the wolf fighting it dropped the fight at once (`SkirmishVictory`). The local player's own death StopFights too: in a real fight two camp bandits killed Henry, he woke 487 m away, `WO131-STOPFIGHT`, `SkirmishVictory`, nobody chased (observed, run F) |

**Priority 2 — animation.**

| item | result |
|---|---|
| 2a NPC combat moves reach the joiner | **fixed end to end** (observed). `npc_rows_out=0` because the host's NPC capture never produced a name (the owner at `ca+0x2D8` is a `C_Actor`, the name helper wanted a `CEntity`). Fixed: in a real fight on the host, `cap_npc=5`, no drops, `npc_rows_out=5`. Those five captured rows, replayed into a joiner game (`tools/wo131/replay.py` → synthpeer), make the joiner's copies of the same two bandits **swing**: an overhead mace blow over ten consecutive frames and an axe blow over fourteen (sheets 7–8). Limit: rows picked from the table by hand for a stand-in whose loadout did not match them gave a bind pose, not a swing (sheets 5–6): a stand-in's approximate loadout can refuse the host's real rows |
| 2b no stuck poses | **fixed** (observed). Reproduced the field's "phasing" exactly: a local blow ragdolls a copy, the writer holds it at standing height, only the head shows. Now the native writer drops a bound body whose physics stops being a living entity (`reason=not-living`) and Lua calls the engine's `actor:StandUp()`: standing 0.7 s after the blow in every frame (sheets 3–4) |
| 2c the players' swings | **capture fixed (code-verified), live swing (inconclusive).** The field's `owner-not-a-combat-actor … +0x612B28` is **`C_CombatPlayer`** (the player's combat actor is a subclass; resolved from the RTTI locator) and `no-owner-name … +0x5BF030` is a plain `C_CombatActor` (the NPC case above). Both accepted now. No input-free route to a real swing exists on this build: `RequestAction(attack|freeAttack)` returns null on the player and on NPCs (observed) |

**Also found:** an NPC hit by the avatar (attributed) targets it but still never swings at it on the host — with spawned souls and with a live camp bandit mid-fight (observed, runs A and F); and in a real fight the campers go for Henry, never the avatar beside him, although it is on Henry's side of their skirmish (observed). Fight-back against the avatar stays open (§8).

**Phase 3 — leash.** The countdown is cancelled only back under 630 m (650 −
20); a dialogue hold needs a conversation the player is in: `IsInDialog`
**and** the engine's `DialogTwin_<player>` stand-in (synthetic; no live
dialogue run).

## 1. Priority 1 in detail

### 1a — the copy guard (joiner)

Root cause of the field's split NPC (code-verified): (a) a puppet whose stream
went silent (the host culls past 60 m) was **resumed** after the 10 s dwell —
a free brain at a stale spot; (b) an NPC the host never streamed was never
touched on the joiner. Now, while this machine is connected as a
non-authority under host authority in a shared world, and the agent's 1 s tick
is fresh (`KCD2MP_W131Tick`):

- every NPC/NPC_Female within 220 m that is not a puppet is paused
  (`wh_ai_PauseNPC`) and hidden (`Hide(1)`) — corpses, horses, avatars, dialogue
  stand-ins excepted, and nothing new while the player is in a conversation;
- a stream for a parked name unparks it (shown, paused, bound);
- a release (silence, or the reconcile sweep) parks instead of resuming;
- a load, or a body found shown again, re-pauses and re-hides; every parked body
  in range is re-paused every 10 s anyway;
- the agent silent for 10 s (crash) or disconnected, `mp_npc_guard off`, host
  authority off → everything given back (unhidden, resumed).

**Why hidden, not parked in view:** a parked body stands where the joiner's own
world put it, which is not where the host's NPC is. The host therefore streams
its tracked NPCs past the 60 m cull out to **150 m of any player on a 2 s
heartbeat** (far band), so the joiner binds the host's real NPCs around it and
only NPCs the host has elsewhere vanish.

| check | result | mark |
|---|---|---|
| guard on, 52–60 bodies parked, 4 bound (run B) | `WO131-GUARD state=on`, `parked=52` | observed |
| identity binding: `ttkc_man_28` streamed 60 m from its local spot | the copy read back at exactly the host position, natively bound | observed |
| only host-streamed NPCs visible on the joiner | sheet 1: the host avatar, the stand-in bandit, the streamed villagers; everyone else hidden | observed |
| a stream stops → park, not resume | `ttkc_woman_11`: released after silence, then `hidden=true parked=true`, no resume | observed |
| a save load | reset → re-parked about 1 s later (`WO131-GUARD reassert`) | observed |
| the agent killed | `WO131-GUARD state=off why=agent-silent parked=60` after 10 s; re-parked on reconnect | observed |
| CSV, run E | §4.1 | observed |

Run D found one defect, fixed before run E: a parked name whose body the load
re-created and that entered the radius after the load's single re-assert was
only re-hidden, not re-paused (hidden, brain running; 5 names in the CSV).

### 1b — the hit gate (joiner)

`MP-DMG dir=drop … reason=wo131-<why>` for: no answer, `not-bound`,
`stale-stream` (> 3 s), `far-from-host` (> 3 m), `dead-on-host`. A refused hit
on a guarded copy puts its health straight back to the host's.

| check | result | mark |
|---|---|---|
| 15 hp on a bound copy near its host position | `SYNTH got hit npc=ttkc_man_26 hp=15.0` at the host | observed |
| 10 hp on a free copy (the field's case, guard off for the test) | `reason=wo131-not-bound`, nothing sent | observed |
| a parked (hidden) copy | the sampler never reports it at all | observed |
| unit rules (distance, age, no answer, dead, floor) | 12 tests | synthetic |

Found live and fixed: the gate first dropped every hit as `no-answer`. The pipe
reader **awaits** the LocalHit handler, so a pipe request made inside it waits
for a reply only that same loop can deliver (a 5 s deadlock). On a joiner the
handler now runs off the reader loop. Also: resolving a soul by GUID
(`rttr::find_soul_by_guid`, a full reflection walk) was too slow for a hit path;
the checks now go by the bound body's entity id.

### 1c — deaths, bodies, loot

| check | result | mark |
|---|---|---|
| 500 hp local blow on a guarded copy | hp 1, alive, not knocked out | observed |
| … forwarded | `measured 99.0, forwarded 100.0 (the copy sat at the imm floor)`: the host's own health decides | observed |
| host health follows | host 40 → copy 40 | observed |
| the host says dead | guard off, `ApplyDeath`, corpse 0.9 m from the host's position; the local FATAL echo dropped, not sent | observed |
| loot the host-killed body on the joiner | blocked, toast "Only the host can loot bodies in co-op for now." (sheet 2) | observed |
| pickpocket, horses, avatars, the host | unit tests | synthetic |

Loot-as-a-request (the joiner takes the host copy's items) needs an item
transfer protocol and a host-side item move; not built. Blocking is the
minimum the WO allows; duplication is closed either way.

### 1d — perception (host)

| lever | before | now |
|---|---|---|
| `AI.SetIgnorant` on the avatar | 1 (ignorant), 0 for 30 s after an attributed hit | **0 all the time** on the host of a shared world (re-asserted every 2.5 s) |
| avatar faction | orphan node (or the hostile donor under `mp_enable_aggro`) | **the local player's faction** (`WO131-FACTION … read back … match`; REST `FactionNode/Parent/Name` = `player`) |
| avatar crime/skirmish contexts, native combat automation off | set | **unchanged** — they keep it from barking, starting fights or reading as a crime victim when the players spar |

`mp_avatar_perceive off` restores the old rule and detaches the faction.
Observed: wolves (the game's own spawn) targeted and bit the avatar
(`TargetChanged … (target kcd2mp_1)`, `Attack`, `HitTarget`) and it was added to
Henry's side of their skirmish (`SoulAdded on Dude` → `SoulAdded on kcd2mp_1`).
The A/B with perception off still saw wolf attacks: wolves attack any NPC, so
they do not discriminate (inconclusive for the lever itself). Not produced: a
human hostile engaging the avatar unprovoked (see §0). Guards: untested.
Shared crime (fines, jail, the joiner's crimes) is the next WO; nothing here
blocks it — the faction attach is what that design's "mirror Henry's standing"
needs.

### 1e — damage to the joiner

The forward is the avatar's measured health loss (0x21 → the joiner's Henry).
Fixed: the avatar's health is restored after every measured hit (native op 5;
it was imm-floored at 1 with no restore). Not done: the ~10 % comparison. The
avatar wears the joiner's real items (appearance sync), but on a roster soul
with its own stats (the log shows strength 2, agility 2). No damaging NPC hit
on the avatar happened in these runs, so neither option was measured
(inconclusive). The two-player checklist asks for it.

### 1f — stand-ins

`WO131-STANDIN spawn npc=prepadeniNaCeste_bandit_9 soul=29f8bb4d kind=bandit`
— bound natively (WUID check passed), guarded, visible with its weapon drawn
(sheet 1), re-created after a load, removed when its stream stops (observed).
A stand-in is an approximation of the look: the host's soul is a runtime soul
the joiner does not have.

### 1g — a death ends the fight

| check | result | mark |
|---|---|---|
| the joiner dies (peer 0x23) with a wolf on its avatar | `StopFight on its avatar -> sent`; at once `SoulRemoved on Wolf`, `TargetChanged (target -)`, `SkirmishVictory` | observed |
| … and respawns (0x3E) | StopFight again | observed |
| the local player's death | `WO131-STOPFIGHT the local player's death -> StopFight sent`, grave, woke inside the leash | observed (no fight around it) |

Crime is untouched. (The wolves re-engaged 20 s later because the test's avatar
never left: avatarpeer sent no vitals, so the host kept it in the dead state at
the fight — a harness artifact, fixed in avatarpeer for later runs.)

## 2. Priority 2 in detail

### 2a — NPC combat moves

- **Why 0 rows:** above (the capture's owner name). Also, `npc_rows_out` only
  ever counts on the host; a joiner's log shows `npc_rows_in`.
- **Host, run F (observed):** a real fight at a bandit camp (Henry trespassing,
  the avatar beside him). `cap_npc=5`, all drop counters 0, `npc_rows_out=5`,
  `MP-ACTION section=outbound kind=NpcAttack npc=tpod_bandit_2 row=…`; the
  recording peer received all five.
- **Joiner, run G (observed):** the recording's five rows replayed onto the
  joiner's copies of the same two bandits (bound by identity, placed in open
  ground, weapons drawn by the stream): both swing, with their own shields and
  weapons — sheet 7 (f20–f33: guard, mace up, overhead, blow, recovery) and
  sheet 8 (f66–f79: wind-up, axe overhead, strike, back to guard). This is the
  WO's protocol: record the host side, replay into a joiner game, judge frame
  by frame.
- **What did not animate (observed):** rows picked by hand from the table for
  the club-carrying stand-in (sheet 5; with its brain resumed, sheet 6): an
  arms-out bind pose. The real rows carry the attacker's own loadout tags
  (`l_shield+…`); a body whose loadout does not match them does not play them.
  A stand-in wears an approximate soul, so a road bandit's real rows may not
  fit it (inconclusive until a live road encounter).

### 2b — no stuck poses

Sheet 3 is the field's phasing reproduced (a local blow, a ragdoll held at
standing height) and the same copy after `StandUp`. Sheet 4 is the automatic
path in run D: drop `reason=not-living` 0.5 s after the blow, `WO131-STANDUP …
ok=true`, on its feet from the first frame (0.7 s) (observed). A copy the host
has knocked out is left down; a death is the host's (unit tests).

### 2c — the players' swings

Both field vtables identified offline from CombatModule's RTTI (`C_CombatPlayer`
primary at +0x612B28, `C_CombatActor` at +0x5BF030). The capture accepts both
classes (primary or +8), names NPC owners through `C_Actor` GetName / entity id,
and the drop line now prints the RTTI class name. Live: no real swing
(`RequestAction` null for attack types on player and NPC; mouse input is
forbidden) (inconclusive).

## 3. Phase 3 — leash

Unit tests: a countdown started past 650 m survives 648/652/645/655/640/631 m and
is cancelled at 629.9 m; custom settings keep the 20 m margin. The dialogue rule
is pinned by the Lua suite (a bark without a twin is no hold).

## 4. Live runs

| run | role of the real game | what |
|---|---|---|
| A | host (avatarpeer joiner) | faction, perception (wolves, spawned bandits, a live camp), StopFight on the peer's death, capture attempts |
| B | joiner (synthpeer host) | guard, identity binding, stand-in, hp follow, host death, loot block, first CSV |
| C | joiner | rows → no swing; hit gate (after the deadlock fix); lethal/floor; phasing reproduced + manual StandUp |
| D | joiner | automatic stand-up; save load; release → park; CSV (found the re-pause defect) |
| E | joiner | the re-pause fix; save load; the local player's death; long CSV |
| F | host (avatarpeer joiner) | a real fight at the bandit camp: Henry killed by two campers, StopFight on the wake; the host's NPC rows captured and sent (`npc_rows_out=5`) and recorded; the avatar in the skirmish but never attacked; the avatar's attributed hit: targeted, no swing |
| G | joiner | the recorded real rows replayed: the copies swing (sheets 7–8) |

### 4.1 CSV (joiner, `mp_leash_trace`)

| run | span | names | human rows `suspended=0 driven=0` |
|---|---|---|---|
| B | 194 s | 61 | 1 (at the 200 m edge before a sweep; the guard radius is now 220 m) |
| C | 249 s | 59 | **0** |
| D | 355 s | 63 | 736 (the re-pause defect + the load transient) — fixed |
| E | 528 s | 72 | **6** — three bodies at ~195 m for one sample at a save load (the transient); none otherwise, including after the joiner's own death and wake 380 m away |

Horses are free by design (excluded) and are not counted.

## 5. Screens (`docs/wo131-shots/`)

| file | what |
|---|---|
| `1-joiner-one-world.jpg` | the joiner's view: the host's avatar, the stand-in bandit (weapon drawn), streamed villagers; unstreamed NPCs hidden |
| `2-loot-blocked.jpg` | the game's toast over the host-killed body |
| `3-phasing-and-standup.jpg` | the copy in the ground after a local blow; the same copy after `StandUp` |
| `4-auto-standup-strip.jpg` | run D, 0.5 s apart from 0.7 s after a lethal local blow: standing throughout |
| `5-npc-rows-no-swing.jpg` | run C, 80 ms apart around three attack rows: no swing, arms-out pose |
| `6-npc-rows-brain-resumed-no-swing.jpg` | the same with the copy's brain resumed |
| `7-real-host-row-swing-a.jpg` | run G: a row captured from a real host NPC attack, replayed: the joiner's copy swings a mace overhead (f20–f33, ~100 ms apart) |
| `8-real-host-row-swing-b.jpg` | the other bandit's captured row: an overhead axe strike (f66–f79) |
| `9-replay-scene.jpg` | the replay scene on the joiner: both copies, shields and weapons drawn |

## 6. Gates

Every `Build-Installer.ps1` gate, run without Inno Setup (no installer):
relay round trip 50/50; agent unit tests 387/387 (12 new in `Wo131Tests.cs`,
one WO-114 leash test moved to the 630 m cancel line); all 27 synthetic suites
green (new `Test-WO131Synthetic`, 79/79; WO-114's dialogue case now includes
the conversation stand-in; WO-118's harness exposed an unguarded `StandUp`
call, now guarded); both static checks; native unit tests 47/47; local publish
+ payload smoke. `Verify-Install.ps1` knows the WO-131 markers.

## 7. Two-player checklist (the next session)

Each item says what the **joiner** looks at.

1. **A guard notices the joiner.** Joiner stands near a guard outside town
   while the host watches: on the host, `MP-WO131 avatar N -> the player's
   faction: joined`; joiner: does the guard look at you / react when you draw?
2. **An enemy attacks him.** Ride to bandits or wolves together; the host stays
   back. Joiner: they come at you, and you can see every one that hits you
   (road ambushers are stand-ins: `WO131-STANDIN spawn` in your kcd.log).
3. **An NPC he hits fights back, and swings.** Joiner hits a bandit: does it
   fight you back (host's screen), and does it **swing on your screen**? (World
   NPCs' copies swung in the replay test; road-ambush stand-ins may not.)
4. **No phasing.** Hit an enemy hard: it never sinks into the ground; it lies
   down only if it lies down on the host's screen too (`WO131-STANDUP` lines).
5. **A body and its loot in one place.** Kill one: the body lies in the same
   spot on both screens; joiner's loot attempt shows "Only the host can loot
   bodies in co-op for now."; the host loots it.
6. **Fair damage.** Both take one hit from the same enemy, joiner in the same
   armour as the host: compare the health lost (joiner kcd.log / agent log
   `PlayerHit`, host's own drop). Note both numbers.
7. **One world.** Walk a village apart: nobody shows up twice; the joiner's
   `mp_w131_status` shows parked bodies.
8. **A death ends the fight.** Die in a fight; after waking nobody chases you.

## 8. Carried forward

1. Stand-in loadouts vs the host's real rows (2a): a road bandit's rows on an
   approximate stand-in may not play.
2. The ~10 % damage comparison (1e); if the avatar is far off, forward the hit
   (weapon, zone, strength) and let the joiner's engine compute it.
3. An unprovoked human hostile engaging the avatar; guards (needs a live world
   NPC, not a spawned soul).
4. A real player swing (2c) in the next two-player session.
5. Loot as a request to the host (1c).
6. Animals are per-machine (a joiner's local wolves are local fights).
7. Save-load transient: bodies are free for about a second until the next sweep.
