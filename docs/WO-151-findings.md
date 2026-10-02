# WO-151 — the 2026-10-01 live session, and the safeguards an open beta needs: findings

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **(observed)** seen live in a run of this WO; **(log)** read in the field
logs; **(code-verified)** read in the code or the binary; **(unit)** a unit or
synthetic test; **(inconclusive)**; **(not built)** with the reason.

## The answer

**0.43.0 fixes most of what the 2026-10-01 session showed, in order of damage, and adds the
three safeguards an open beta needs.** Each new behaviour has a switch, and §D says why each is
on. Everything below was checked solo on one machine with throwaway saves, with frames where the
fix is visual. What needs two players is in `docs/TWO-PLAYER-CHECKLIST.md` §WO-151.

* **Safeguards (Phase 0).**
  * No native fault is silent any more. Its first live line found a real silent fault, and
    later a second (the save list's scan); both are fixed.
  * Reflection arguments own their value.
  * The frame-rate soak gates the installer.
  * A FRAME line goes into the native log every 60 s.
* **Combat (Phase 1).**
  * A joiner's copy in a fight runs none of its own hit reactions, knockdowns or tools.
  * The host's damage on a copy lands without one.
  * The host NPC's own reaction rows play on the copy.
  * The animation-queue flood around a ride is fixed (gallop hysteresis).
  * The wolves' knockdown is fixed.
  * **Not fixed:** a visible reaction to the host's friendly fire on the joiner (the damage
    already arrives), and the wolf's bite.
* **Riding (Phase 2).**
  * One owner for a ridden horse: the rider's mount path.
  * The `GetHorse` WUID bug is found and fixed.
* **The world (Phase 3).**
  * Catch-up: the mirror holds through a host reload; a joiner acts only after a full
    checkpoint; a step the host has passed is "already".
  * Living quest carries are shown like bodies.
  * The scene guard: the end edge at the release, a scoped resume, the save request that
    released the field's black screen, a reset on load.
  * The whistle.
  * The host's live weather is read natively and gated on the joiner.
  * The engine truly holds the world for a join: `world=FROZEN scale=0.000` where the field
    read `running 1.000`.
  * Doors are owned by the host's world, both ways.
  * **Not built:** dialogue voice and barks; a scene that is initialised and never played.
* **The players' own states (Phase 4).**
  * The anvil's cause: the tutorial's States were mirrored; they are now the player's own.
  * `mp_unstuck`.
  * Smithing shows on the avatar, and no avatar makes a station look taken.
* **Crime (Phase 5).** Joint responsibility (default), one report per take, and the record's
  forgetting.
* **Gates.**
  * Every synthetic suite: 0 failed.
  * Agent 824, relay 62, native 368.
  * The three static checks: exit 0.
  * The soak: PASS (§0.3).

## E. The evidence, confirmed line by line

The two field zips (the host is this machine, the joiner is the partner's) were
copied to the scratchpad and every line of the WO counted from the logs. Where a
log disagrees with the WO's text, the log wins; the disagreements are marked
**differs**.

| WO line | the logs | mark |
|---|---|---|
| the joiner's 26 blows reached the host and were applied (`MP-ATTRIB … result=applied`) | 26 `result=applied` in the host's agent log: 10 on `tbuk_man_1`, 7 `tbuk_man_3`, 3 `tbuk_man_4`, 4 on the quest wolves, 2 on `tvid_huntsman` | (log) |
| 56× `re-asserted sheathed (local brain fought back)` on `tbuk_man_1` | 56 on `tbuk_man_1` (171 in all: also `tbuk_man_4` 37, `_3` 29, `_5` 27, `_2` 22) | (log) |
| 7× `native=refused reason=not-living` | 7 on `tbuk_man_1` (`MP-NPCWRITE`), 49 in all (riders' horses, villagers) | (log) |
| 6× `WO131-STANDUP why=not-living` | 6 on `tbuk_man_1` | (log) |
| its health jumped (4.0 → 24.8 → 12.0) | **differs**: the agent's correction lines show the copy's own value far from the host's at each correction: copy 64.6 / host 29.8, copy 1.0 / host 29.2, copy 28.7 / host 15.3 (`MP-WO131 follow tbuk_man_1`) | (log) |
| WO-141/143 gave it a temporary tool mid-fight | `WO141-APPLY tbuk_man_1 … hands L=… R=…` 60 times | (log) |
| friendly fire on (`MP-FF host sent friendly_fire=on`, joiner `ff_from=host`) | 183 heartbeats; the joiner's `MP-WON cfg … ff_from=host` | (log) |
| the host's hits do nothing to the joiner | **differs**: all 24 of the host's hits were sent and applied on the joiner (`WO121-HITS friendly-fire hit on the player … applied`), and the joiner's own position lines show his health falling by exactly each hit's amount (92.23 → 86.58 → 82.95 → 81.00 → 77.70 for 5.66, 3.62, 1.96, 3.30) and his stamina by 30–40 at each of the 17 blocked ones (health 0). What did not happen is any visible reaction: the damage arrives as the game's scripted hit (`TakeDamage`), which plays nothing | (log) |
| wolves churn (`NPC-SYNC puppet start` / `release (stream silent)`), `interrupt_animal_attack failed because it required aliveness` | 232 puppet starts in all; 12 aliveness failures | (log) |
| 1,562 `Animation-queue overflow` on the joiner's avatar, a bow 291, an NPC 259 | `kcd2mp_1` 1,562 (747 `relaxed_idle_lookposes_torso`, 715 `crouched_idle_lookposes_torso`, 56 `relaxed_idle_both`, 44 `1d_relaxed_idle_bigturns_nw`), `bow_b_b001018` 291, `tbuk_man_2` 259 (both `bow_load_1st_over`) | (log) |
| the quest horse driven by three systems; 469 overflows; `ForceIdleState` 108 times | `tvid_huntsmansHorse`: `GHOST_HORSE swapping proxy for world horse` 3, `NPC-SYNC anim … walk/idle/gallop` 306, `horse_grazing` applied, 469 overflows, `ForceIdleState` 108 | (log) |
| riders' horses `bind refused: not-living` | the line reads `MP-NPCBIND npc=dummyWanderer_horse_1 result=refused reason=not-living … no physics` (16 on rider horses) | (log) |
| `MP-CARRY local carry of a living NPC (tvid_huntsman, a quest's carry)` in both logs | host 6, joiner 3 | (log) |
| `hunterCarriable`, `pickUpHunter`, `hunterIsCarried`, `SetTrue 2->1` applied on the host | the host's agent: 3 / 16 / 14 / 6 | (log) |
| `WO137-HOLD … ttac_blacksmith held_s=71.7`; `w139-stop` holds up to 240 s | `held_s=71.7` (one of three on the blacksmith); five `w139-stop` holds on `tvid_huntsman`: 52.0, 92.8, **240.5** (`w139-stop-nostop`), 162.4, 3.5 s | (log) |
| the host sent `semicloudy_clear_B` 46 times; the joiner applied it once | 46 sends; but the profile was the host agent's own pick at session start (`[weather] arbiter picked`), applied on the host too; the game's own weather is never read | (log) |
| `paused the world … paused_s=76.2`; `WO138-WORLD FROZEN … 0.46 s`; time scale 1.14–1.23 | yes; and the DLL's own status lines during those 76 s read `world=running scale=1.000`: the world was never paused (the only freeze is the 0.46 s world-save hitch) | (log) |
| `WO140-DROP the kept picker (a sleep from a bed he lay on stays lying)` | at 20:13:05; the held picker was a **wait** picker (id 1); the host lay until the joiner's hit at 20:23 | (log) |
| `WO139-CRIME theft … item=b38c…` ×2, `…928463…` ×2 | yes, and the WO-134 chest ledger shows both were stacks of two taken at once (`n=2`): one report per piece of one take; the host counted five thefts for three takes | (log) |
| the markers `body_pickup_quest`, `mark_weirdquest_Wine` | both in the host's `kcd.log` as unknown commands | (log) |

## 0. Phase 0 — the required safeguards

### 0.1 No silent fault guards

* **Built.** `native/KCDMP/fault_guard.{h,cpp}`: `fault::guarded(site, body)` and
  `guarded_or(site, fallback, body)`. A site is a named static (`KCDMP_FAULT_CALL`,
  `KCDMP_FAULT_READ`, or the expression forms `KCDMP_SITE_CALL/READ` for a site
  handed to a helper). The filter records the exception code, the faulting
  instruction (module+offset) and, for an access violation, the address touched;
  the handler counts and logs:
  * `FAULT <site>: 0xC0000005 reading 0x10 at KCDMP.dll+0x9b432 (1st)` at once, the
    10th and the 100th;
  * `FAULT-SUM 60s: …` every 60 s while any count is non-zero;
  * a **Call** site (a call into the game's code) is switched off at its 8th fault in
    a session: `FAULT <site>: switched off after 8 faults this session …`; its guard
    then returns false without calling the game;
  * a **Read** site (our own read or write of game memory, where no game code runs)
    is counted and logged the same way and never switched off: a stale pointer
    changes nothing in the game, and several are probes that expect to fault.
  * `mp_fault_switchoff on|off` (default on, the DLL's default too) via pipe `0x2A`.
* **All 286 guards converted** (the WO's "303" counts the lines that name `__try`;
  17 of them are comments): 223 by a converter for the two plain shapes, 63 by
  hand. The reflection layer names a site per property read and per invoke
  (`rttr::sample_health/GetState(stamina)`, `rttr::apply_damage_soul/TakeDamage(attacker)`,
  …); every engine service has its own (`engine::entity_name/GetName`), so one
  service that keeps faulting cannot switch off the others; each repeating
  main-thread task has its own (`main::task/<name>`); one-shot queued work (the
  pipe's commands) has one never-switched-off site (switching it off would stop the
  agent's whole channel). **(code-verified)**
* **The gate.** `tools/Test-NativeGuards.ps1` (no raw `__try` outside
  `fault_guard.h`, no `build_argument(`) runs in `native/Build-Native.ps1` before
  every build and in `Build-Installer.ps1`; planted violations fail it. **(unit)**
* **Tests.** `native/tests/wo151_tests.cpp`: deliberate read and call faults (the
  counts, the three lines and their text, module+offset, the switch-off at the 8th
  and a switched-off body never running, the switch off keeping a site running, the
  60 s sum, the reset). **(unit)**
* **Live (L1).** `mp_fault_test read` → `FAULT wo151::test/read: 0xC0000005 reading
  0x10 at KCDMP-w151a.dll+0x9b432 (1st) [read site …]`; nine `mp_fault_test call` →
  the 1st logged, the 8th `switched off after 8 faults this session`, the 9th not run
  (the count stayed 8); `mp_fault_status` round-trips (`faults=10 sites=3 off=1`).
  **(observed)**
* **What the guard found at once.** Its first line in the live game was a fault that
  had been silent in every earlier build: `FAULT local_state::read_ptr: 0xC0000005
  reading 0xffffffff00007777` — the local-state read's entity-hop probe tried a
  candidate slot of the player's actor that holds a non-pointer and read through it.
  Harmless (a read), but it ran at every hop scan. Fixed: a candidate that cannot be
  a pointer (non-canonical, the null page, misaligned) is skipped unread.
  **(observed; fixed in source)**

### 0.2 Reflection arguments own their value

* **Built.** `rttr::Arg<T>` and `rttr::StringArg` (`rttr_abi.h`): the value inside the
  argument object, beside the 24 argument bytes that point at it; neither copyable nor
  movable (so the compiler refuses any use outside the scope that holds both). The
  pointer-taking `build_argument` is gone; all 21 sites converted (the stamina
  argument of WO-148 §7.4 among them). **(code-verified, unit: the argument points
  into its own object)**
* **Live (L1)**, solo with the scripted partner:
  * health and stamina reads in a crowd: three commoners 3 m around the player, six
    souls tracked, the game's stat stack at 0;
  * a blow and a stamina-only blow (`mp_test_hit`, the WO-147 stand-in for the
    player's swing): `PIPE: LocalHit 5.00 st 10.00 by the player` and, on the
    **second** soul near the player (the path the 0.42.5–0.42.7 bug broke),
    `LocalHit 0.00 st 30.00`;
  * the partner's damage applied: `MP-ATTRIB npc=w151_c3 … result=applied`, the
    commoner 100 → 94;
  * a faction read: the avatar's faction attach read back (`WO131-FACTION: avatar
    joined the player's faction … (match)`). **(observed)**

### 0.3 The frame-rate soak before every installer

* **Built.** `tools/perf/soak.py` (`run`, `verdict`, `check`), `tools/perf/statstack.py`
  (the game's stat stack read from outside; pinned to the Modding Tools 1.5.5
  RPGModule by its PE identity, refusing any other build), `tools/perf/README.md`.
  The scene is input-free: three commoners 3 m around the player (no AI, never
  saved), then from minute 3 a fight beside them — two AI commoners, one sent the
  game's own attack interrupt at the other (`crime:attackInitiatedByConcept`),
  re-sent every 15 s, a fresh pair when the victim goes down or stalls. Nobody fights
  the player (no Game Over can stop the run). Every 10 s: the frame rate over those
  10 s, the stack depth, the `FAULT` lines since the start.
* **The gate.** `Build-Installer.ps1` runs `soak.py check` first: the committed
  `tools/perf/soak-record.json` must say PASS for the same git trees of the code that
  runs in the game (the DLL, the mod's Lua and tables, the agent, the relay, the
  protocol).
* **Dry run (L1, 4 minutes):** 71.1–71.8 FPS, stack 0, no `FAULT`; the fight was real
  (16 hits, the victim down to 8).
* **The soak of 0.43.0: PASS** (`tools/perf/soak-record.json`). It ran on the commit the
  installer is built from: the code trees match, and `soak.py check` passes. Both runs used
  playline4/autosave111, 10 minutes, the crowd from the start, fights from minute 3 (four and
  five pairs), and the game window focused all the way:

  | window | with the mod | without the mod |
  |---|---|---|
  | the first 2 minutes | 70.9 FPS | 71.6 FPS |
  | the fights, minutes 3–8 | 71.0–71.8 | 70.7–71.4 |
  | the last 2 minutes | 71.4 FPS | 70.7 FPS |
  | the game's stat stack | 0 in all 56 rows | 0 in all 60 rows |
  | `FAULT` lines | 0 | — |

  All seven checks pass. The DLL's own cost (`FRAME ... ours_us_mean`) was 0.69–0.76 ms per
  frame. **(observed)**
* **Two attempts before it do not count.**
  * The first stopped at 72 s, when the game's console dropped one connection; the tool now
    retries a dropped connection.
  * The second held 68.5–72 FPS and then fell to 25 FPS late in the run, and stayed there after
    the scene was removed. The DLL's own cost stayed at 0.7 ms, and the maintainer reports
    alt-tabbing out of the game window at that time. A window that loses focus caps the game's
    frame rate, so the focused reruns of both are the record: the mod held 71.0 at the end
    where it had collapsed. `tools/perf/README.md` now asks for the same window state in both
    runs. **(observed)**

### 0.4 The frame rate in the native log

* **Built.** `native/KCDMP/frame_meter.h`: one `FRAME` line every 60 s with the frame
  count, the frame rate, the mean and worst frame time, the frames slower than 50 ms,
  what this DLL's own work cost, and the fault totals. The WO-148 per-task meter stays
  behind `mp_main_cost` (off by default). **(unit: the arithmetic; observed: `FRAME
  window_s=60.0 frames=4299 fps=71.6 … ours_us_mean=760 … faults=1`)**

## 1. Phase 1 — combat

### 1.1 Fighting the same NPC

* **The copy runs none of its own hit reactions while it fights.** On every copy WO-131
  guards, the DLL sets the game's own three entity script contexts
  (`combat_actorSupressHitreactionAnimation`, `switch_disabledHitReaction`,
  `switch_disabledHitBehavioralReaction`; WO-139 ref. A §6) and clears them when the copy
  leaves the guard or the session ends (pipe op 5 `CopyFight`, `WO151-COPYFIGHT`). Live L3:
  a two-argument `TakeDamage` no longer knocks the NPC down; a replayed reaction row still
  plays on it; armed blows: 5 of 6 with no reaction, one knockdown at 17 hp. **(observed)**
* **The host's damage lands without the copy's own reaction.** Live L2 settled what
  `CombatSoul::TakeDamage` does: with two or three arguments it **knocks down** an NPC that is
  out of combat, and the player too (frames T2a/T2b, T1a/T1b: Henry on his back until
  `StandUp`); with the fourth argument, `SuppressHitReaction = true`, health drops and nothing
  plays ("no valid reaction found", T2c). The copy's damage path now honours the flag the
  sender always sets (`apply_damage_soul_ex`, the invoke's 4-argument export). **(observed)**
* **The host NPC's reactions are shown from the stream.** The host captures its NPC's committed
  hit reaction (`C_CombatActorActionHit`, its row at action+0x58, RTTI-checked
  `S_CombatActionHitData`, the row GUID at +0x7C: 4 of 4 dumps) and sends it as `NpcHit`
  (`ActionKind` 15, forwarded by the relay only from the host). The joiner plays that row on its
  copy (`mp_npc_reactions`, default on). Live L4: rows resolve (`CombatHitGen`,
  `CombatHitTorso`; 1,488 rows in the catalog); L2/L3: a replayed row plays on a copy (frames
  `row1_strip`, `cf2`). **(observed)**
* **Activities, tools and health.** A copy in a fight (and 10 s after its last blow) gets no
  activity row or temporary tool (WO-141/143), is not holstered against this joiner's own
  engagement, gets its health set back to the host's after a local blow, and is not written
  while a local blow has it on the ground (2.5 s: the sinking) (`mp_copy_fight`, default on).
  **(unit; observed L2–L4)**
* **The freed-soul fault found on the way.** L2's first fight showed the health sampler reading
  souls an entity removal had freed (`FAULT rttr::sample_health/GetState(health)`, 8 in 450 ms,
  then switched off). Fixed: before each read the sampler checks the soul's vptr and its own GUID
  (offset learned by content, two souls must agree). Live L3: "a tracked soul is no longer
  itself ... dropped", no fault. **(observed)**
* **Limit.** A copy that has never fought and is suspended has no combat actor: the row replay
  is refused (`body-wrong-state`, J1). Such a copy still shows no reaction of its own.
  **(observed)**

### 1.2 Friendly fire host → joiner — not fixed

The field log disagrees with the WO's premise: all 24 host hits were applied on the joiner,
health exactly (§E). What is missing is a visible reaction. The damage arrives as the game's
scripted hit, and the joiner's `TakeDamage` reaction is refused while he fights: "'Dude':
Reaction cannot be played right now" ×10 and "no valid reaction found" ×5 in his log. The game's
hit-reaction table has no player rows (every `combat_action_hit` row has `player="false"`).
`Actor.CameraShake` and `SetViewShake` show nothing, `GameRules` is nil in this build's Lua
(`SendHitIndicator` is unreachable), and there is no `ExecuteHitReaction`. **(observed; log)**
Follow-up: the combat module's own hit route, synthesising the hit into the victim's hit slot
(`C_CombatSoul`+0x150, `S_CombatHitData`, WO-121), so the game computes and plays the reaction
itself.

### 1.3 Wolves against the joiner

* **The knockdown is fixed**: the host's damage on a wolf's copy, and on the joiner from a wolf,
  goes through the suppressed path (§1.1). **(observed for NPC copies)**
* **The bite is not shown yet.** The capture accepts the paired attack
  (`C_CombatActorActionSyncAttack`, row at +0x60, RTTI `S_CombatActionSyncAttackData`), but that
  row's GUID is **not** at +0x84 (unknown). The first three sync attacks dump the row
  (`WO151-SYNCDUMP`) so a follow-up can find the GUID by its content. A 70 s NPC-vs-NPC fight in
  L5 produced no sync attack. **(observed; inconclusive)**
* `interrupt_animal_attack failed because it required aliveness` comes from dead copies' brains.
  It is harmless. **(log)**
* The churn (`puppet start` / `release (stream silent)`) is the WO-147 catch-up path. It is not
  changed here. **(log)**

### 1.4 The animation-queue flood

The 1,562 overflows on the joiner's avatar came in the three minutes around his ride on the
hunter's horse. The avatar's ride loop picked the gallop from the raw per-tick speed: with
10 ms packets and 20 ms ticks it crossed 3.0 m/s every few ticks, and every crossing restarted a
clip. Fixed: the gallop is chosen from the smoothed speed with hysteresis (in above 3.5, out below
2.5). The engine owns the pose again once at the gallop's end, not on every tick. A horse
puppet's gait has the same hysteresis. **(unit: Test-WO151Synthetic (c); the live recount needs
two players: TWO-PLAYER-CHECKLIST)**

## 2. Phase 2 — riding and horses

* **One owner for a ridden horse** (`mp_ride_owner`, default on). The horse this player sits on,
  from the moment the mount starts (`Horse.OnMount`), and a horse a partner's avatar rides here
  leave every NPC path at once: no puppet, no native writer, no park or hide, no join freeze. The
  host's stream for it is ignored while it is ridden and for 3 s after (WO-136's settle).
  **(unit; observed)**
* **Found on the way.** `KCD2MP_W136MountedHorse` resolved the game's `GetHorse()` with
  `System.GetEntity`, but `GetHorse` returns a WUID: the call returned an unrelated entity
  ("randomEvent[...]"). It now resolves through `XGenAIModule.GetEntityByWUID` only and checks
  `class == "Horse"`. Hot-loaded live in L4, it names the mount. **(observed)**
* **Live, solo:**
  * the joiner's own mount of the host's horse copy takes it (`WO136-RIDE take puppet=true`,
    unbound, its brain back, shown; the agent: "the rider owns it"); frame `j1_ride` (first
    person on the horse); after the dismount the 3 s settle hands it back to the stream;
  * the host's view of an avatar adopting a horse (`HorseAdopt`, `WO151-RIDE avatar takes`): the
    avatar rides it (frame `ride3`) once the partner's height is the saddle's (+1.5 m). The
    harness's ground-level height sank both; that is a harness artefact.
  **(observed)**
* **Not proven here:** riding motion and the gallop (they need input); the other screen's view
  of the joiner riding (two players); NPC riders (no code mounts a rider's copy: a gap for the
  follow-up). TWO-PLAYER-CHECKLIST §WO-151.

## 3. Phase 3 — quests, conversations and the world

### 3.1 Catch-up on every join, rejoin and reload

The field logs (both reloads traced): the joiner's mirror stayed on through both host reloads
(37 s and 42 s). It sent requests from the world about to be discarded, and the host applied
them (`nighttime`, `whatTime`). It also applied post-reload changes there. The host's verdict had
no notion of a passed step:
* `deliverWater 3->2` and `trackHorse 2->1` were **applied**, putting the host's quest back;
* a counter's recount (`resolvedbanditscounter.state3`) was refused 12 times.

The host's seen set kept pre-reload values, so the checkpoints left out exactly the States the
reload changed: 4 of them for 41 minutes. 29 of 109 checkpoints never completed on the joiner,
because a part that arrived while the queue was busy was dropped. **(log)**

Built (`mp_quest_catchup`, default on):
* **The mirror holds** while the host reloads or a join runs (`_rewinding`, a running join), as
  it already did for this machine's own loads.
* **A joiner acts only after a full catch-up.** When the mirror (re)starts, this player's own
  quest steps wait until the host's checkpoint has been compared in full (every part, every
  correction applied). Then each held step goes out only if this copy still has it. A
  conversation with one of the host's people waits too, with a toast: "Catching up with the
  host's world -- try again in a moment."
* **A step the host has passed is "already".** The host keeps each State's values since its
  world's last load. A joiner's step to a value the host has passed is answered "already" and
  never applied again (bools excepted: they go back and forth by design). The joiner then puts
  its copy back toward the host's value where a port is known.
* **The seen set is re-read after a host load.** It takes the loaded values, so the next
  checkpoint compares them.
* **A checkpoint part that arrives while the queue is busy waits for it.** It used to be dropped.

Live J2:
* the gate's timeout path: "caught up (the host sent no checkpoint (nothing changed there to
  compare), 10.3 s)";
* the full compare after a mirror restart: "checkpoint part 1/1 ... compared" → "caught up (the
  host's checkpoint compared in full, 0.9 s)".

The verdict is unit-tested on the field's own steps (`Wo151CatchUpTests`); the talk gate is
synthetic (k). **(observed; unit)**

### 3.2 Living quest carries

A living NPC is carried through the game's own grab-body route when the quest gives it the
entity context `CarryLivingActor` (WO-148A; five quest NPCs). Such a carry is now shown on the
other screens like WO-148's bodies (`mp_carry_living`, default on; it rides `mp_carry_sync`):
* the carrier sends it as `living` (the wire's fourth kind);
* the carrier's avatar picks the copy up with the game's own `RequestGrabCorpse`, only when
  `CanGrabCorpse` allows it;
* the copy is held: the host's stream, the Lua writer and the DLL's per-frame writer let go of it;
* the set-down is the game's own put.

On the host, the joiner's carry moves the host's NPC exactly as the game's carry would: the
avatar carries it. Nothing else about a quest NPC changes. **(unit: Test-WO148Synthetic
a/b; not live: no carriable NPC at the test spot; TWO-PLAYER-CHECKLIST)**

### 3.3 Conversation progress

The field's black screen on the return with the horse: the joiner's own fader
`zranenyLovci_trackingAndCampStreaming` was initialised and never played. It waited for a
stream-profile step that the mirror had applied **20 minutes earlier**, during his previous
scene's end positioning (#949). It was released only by the rejoin load, after 5 min 26 s.
**(log)**

Built: while this player's own scene positions its NPCs (its content over, its release not
yet), the host's changes wait (it rides `mp_quest_catchup`). The catch-up of §3.1 covers the
rest of "the joiner's copy catches up before the next scene". Not built: a guard for a scene that
is initialised and never played (it waits for a quest step, which only the dialogue-as-triggerer
work of WO-149 C settles). **(unit; inconclusive live)**

### 3.4 The scene guard

Built (`mp_scene_guard`, default on). The engine's scene-player lines are read for every type
(`CutscenePlayer::<stage> called for <Type> cutscene '<name>'`). Then:
* **the end edge fires at the engine's release** (`ReleaseScene` / `Interrupt`). It used to
  fire at `OnCutsceneEnd`, 41–60 s early, so the host read a black joiner as free;
* **on a joiner, a positioning wait gets help:**
  * 5 s after the content's end without `OnPositioningFinished`, the paused copies within 40 m
    are resumed and held (never paused or parked) until the release;
  * at 20 s, the rescue: a save request. That is the engine's own way out ("Cancelling FF for
    all NPCs until the save takes place"). It released the field's 87.5 s case, and it goes
    through the WO-125 snapshot path, with the file moved out at once;
  * at 90 s the guard gives up: copies handed back, the end edge sent;
* **a dialogue the game forces on a paused copy resumes it** for its length, through WO-137's
  talk route. 4 of 4 such dialogues waited 20 s for their twins in the field;
* **a load or a disconnect resets it all,** `_localCutsceneActive` included;
* **the scene-queue echo now matches** the engine's "addded" spelling.

**(unit: Test-WO151Synthetic (i), Wo151SceneStageTests; not live: no stuck scene reproduces
solo)**

### 3.5 Dialogue audio, barks, the whistle

* **The whistle — built** (`mp_whistle`, default on). This player's `call` press goes out once,
  on the reserved `Emote` action. The partner's machine plays the game's own ATL trigger
  `v_horse_whistle` (Libs/GameAudio/voices.xml) at this player's avatar, with the engine's
  falloff. Live J2: "WO151-WHISTLE ghost=0 played at its avatar". The engine took the call;
  hearing it needs two players. **(observed; unit)**
* **Dialogue voice — not built.** There is nothing to replay from Lua: voices.xml holds 84
  effort and sound triggers and no line triggers. The lines are played natively by the dialogue
  module. Follow-up: the dialogue module's line player, called natively with the line's key.
* **Barks — not built.** The lead is `DialogModule.StartMonolog(entity, topicId)`. The host's
  engine logs each monologue's path (`... (ingame monolog - Id: N) (AI::DoMonologue)`), but no
  table in the data maps a path to a topic id (the 19 dialogue tables in Tables.pak do not).
  Follow-up: the topic id from the dialogue database, then `StartMonolog` on the copy.

### 3.6 Holds

Done earlier in this WO. A hold blocks only a second conversation: the host's own talk to an NPC
the partner holds is refused with a toast. It never freezes the NPC's activity, and a partner who
leaves gives his holds back (`mp_hold_freeze` off by default; on = WO-137's freeze).
**(unit: Test-WO151Synthetic (e))**

### 3.7 Weather

* **The host's live weather is read natively.** A gated pass-through on EnvironmentModule's
  `C_TimeOfDayBlender::BlendToProfile` (found by its `__FUNCTION__` string, one reference;
  prologue on an instruction boundary) records every blend the game runs. The host's agent
  sends a new one as the session's weather, and the WO-40 random pick stops at the first
  reading. On a joiner, the gate lets only the host's profile blend, and a load re-applies it.
* Live L5: a blend through the game's own `EnvironmentModule.BlendTimeOfDay` → "[weather] the
  host's own game blended to 'semicloudy_clear_B' -- the session's weather (the first reading:
  the random pick stops)".
* Live J2: the host's profile arrives → "gate: only the host's 'semicloudy_clear_B' may blend
  here" and the DLL's "WO151-WEATHER gate on". The gate counts the blends it declines; it does
  not log each one.
* **The README's weather line says so:** it is proven solo, both halves; the two-machine test is
  on the checklist. **(observed)**

### 3.8 The join pause

* The field's "pause" was never the engine's: during the 76 s, the DLL read
  `world=running scale=1.000`. **(log)**
* Built (`mp_join_engine_hold`, default on; the name `mp_join_hold` was taken by WO-123). The
  engine's own `PauseGame` from the ScriptBind source (2, idle on this build and never declined
  by WO-138's levers) holds the world. It runs from the verified join save to the join's end, at
  most the join's timeout + 60 s (the DLL's own deadline). It is released when the agent goes
  away.
* Live L5, with a synthetic joiner: `WO151-JOINHOLD on ... held=0x4` → `world=FROZEN
  scale=0.000` → off after 20.1 s at the joiner's Ready → `running scale=1.010`.
* The world save itself (about 2 s) still runs on the mod's own freeze: the engine hold starts
  when the transfer does. **(observed)**

### 3.9 Doors

* Built (`mp_door_sync`, default on), as the WO describes. The class table's
  `AnimDoor.DoPlayAnimation` (the one choke point), `Lock`/`Unlock` and `OnUsed` are wrapped:
  * **host:** every real change (a player, an NPC, a quest, the engine) goes to the joiners as
    `DoorState`. The relay forwards it only from the host. A load's own restore and a re-lock
    are never news;
  * **joiner:** his own use moves his door at once and asks the host (`DoorAsk`, with "unlock"
    only when his key or lockpick unlocked it). The host's answer settles it.
* **The door key — found live.** Real door names are prefab-instance paths of 100+ characters
  (L5), so a door is keyed on the wire by its pivot plus a 32-bit hash of its name.
* **The opener — found live.** In L6, applying an ask in the avatar's name built no animation:
  the state said open while the door stayed shut. A door now always moves as the game's own
  engine-called `Open`/`Close` do, with this machine's player as the user.
* **Live J2 (joiner):** the host's `DoorState` opened the joiner's door (frames `door_b` →
  `door_c`). His own use closed it locally and the synthetic host received the `DoorAsk`.
* **Live L6 (host):** a joiner's `DoorAsk` was applied and its result sent out (`door state
  out`). The fixed opener opens the host's door (frame `l6door_d`). **(observed)**

## 4. Phase 4 — the players' own states

### 4.1 A guaranteed exit (bed, anvil)

* **The anvil.** The host could not leave the smithing minigame because the joiner's tutorial
  steps of his own forging were mirrored into the host's world: `blacksmithing_minigame.
  tutorialState` and the steps under it (§E; requests #77–#95). A minigame's own tutorial State
  is now the player's own and never mirrored (`*_minigame` paths, `*TutorialProgress` types).
  **(unit: Wo151MinigameTests; log)**
* **The safety net.** `mp_unstuck [hard]` is a way out of any player state through the game's
  own exits: the interrupts, then the stand-up. **(unit)**
* **The bed.** The field's kept picker was a wait picker (§E). It was not reproduced: there is no
  bed near the test spot. **(inconclusive)**

### 4.2 / 4.3 The forge and smithing on the avatar

* The field log names the difference. The grindstone that worked played its loop where the
  avatar stood (no object). The smithing entry was aligned at the anvil's object
  (`BlacksmithingToAnvil at the same object (4808C85A445FCA90)`): it showed nothing on the host
  and floated the avatar on the joiner, and the aligned action made the station look taken.
* Fixed: an avatar's minigame never aligns at a station's object (`mp_minigame_align`, default
  off; on = WO-143's aligned entry). The player's own place is at the station anyway.
* Live J2: "smithing -- the avatar plays BlacksmithingAnvilIdle where he stands"; frames
  `smith_a` (upright with its polearm) → `smith_b` (the hunched anvil idle). No station is
  involved, so none can look taken. **(observed)**

## 5. Phase 5 — crime

* **5.1 Joint responsibility** (`mp_crime_mode joint|individual`, default joint, the
  maintainer's direction). A crime by either player counts for both:
  * the joiner's witnessed crime is planted in the host's own witnesses, so the host's Henry is
    wanted for it natively and the host's save carries it;
  * the host's own crime goes into the joiner's record;
  * a fine or punishment by either clears both;
  * `individual` is WO-139.

  The unattended defaults are in §D. **(unit: Test-WO151Synthetic (g); two players for the
  live check)**
* **5.2 One report per take.** The "duplicates" were stacks of two taken at once (§E). One take
  is now one theft. **(unit (f))**
* **5.3 The wanted icon.** When a record clears, this machine's NPCs forget it
  (`crime:forgetCrimesData`, `self` = the NPC). A paused guard copy gets its brain back for the
  moment it takes. **(unit (g))**

## D. Decisions made unattended

1. **"Default on only when proven"**, read per switch.
   * On, because they were proven live: `mp_copy_fight`, `mp_npc_reactions`, `mp_ride_owner`,
     `mp_join_engine_hold`, `mp_door_sync` (both halves live: J2, L6), `mp_whistle` (the call
     ran; hearing it needs two players), the minigame alignment off.
   * On, because they act only where today's behaviour is proven broken, so they cannot regress
     a working case: `mp_scene_guard` (stuck scenes and dialogues forced on paused copies: 8 of 8
     and 4 of 4 broken in the field), `mp_carry_living` (the quest carry: broken both ways in the
     field).
   * On, because the WO asks for exactly this behaviour: `mp_quest_catchup`, and
     `mp_crime_mode joint` (the WO's own default).

   Every one of them has an off switch.
2. **Friendly fire host → joiner (1.2) is not fixed.** The damage already arrives; the reaction
   needs the combat module's hit route (follow-up).
3. **The NpcHit kind is new** (`ActionKind` 15) and the relay forwards it only from the host. Door
   states (16) likewise. Door asks (17) and the whistle (the reserved `Emote`, 3) come from
   anyone. `living` is the carry wire's fourth kind. Mixed releases are refused by the relay, so
   the protocol number is unchanged.
4. **Copy contexts** are set on every guarded copy, not only on copies in a fight (the field's
   copy fought before any engagement was seen).
5. **The engine join hold** uses PauseGame source 2 (ScriptBind, idle on this build). It is
   called `mp_join_engine_hold` because `mp_join_hold` exists (WO-123).
6. **The weather**: the host's own blends are the session's weather once the first one is read;
   WO-40's random pick runs only until then.
7. **Minigame tutorials are local-only** (the anvil's cause).
8. **Joint crime**, as directed:
   * the guard arrests whoever he reaches;
   * a fine or punishment served by either clears it for both;
   * a punishment moves only the arrested player.
9. **The scene guard's rescue** is a save request through the WO-125 snapshot path (the one save
   type that passes the joiner's lock). The file is moved out at once; nothing is paired.
10. **Doors** are keyed by pivot plus name hash, not by name (the level names do not fit an
    action). They are always moved as the game's own engine-called `Open`/`Close`, with this
    machine's player as the user (the avatar as user built no animation, L6).
11. **The scoped resume covers paused puppets only.** Parked copies (WO-131: hidden and
    suspended) are not resumed for a scene; they are rarely a scene's participants.
12. **The J1 incident.** The first joiner run placed its transient world file in playline1, a
    real playline: the join picks the newest save by SaveTime, and playline1 had one. The file
    was deleted after the load and no real save changed (357 of 357 match). Every later join
    pinned `mp_join_henry` to a playline4 save (J2: the file went to playline4).

## G. Gates

| gate | result |
|---|---|
| every synthetic suite (`Test-*Synthetic.ps1`, 45) | 0 failed (WO-151: 148, WO-148: 120, WO-137: 89 ...) |
| the three static checks (`Test-WO106ConsolePlaceholder`, `Test-WO110LuaLocals`, `Test-NativeGuards`) | exit 0 |
| agent tests | 824 passed |
| relay tests | 62 passed |
| native tests | 368 passed |
| the payload smoke | pass (§B) |
| the frame-rate soak | §0.3 table |

## B. The installer

Built by `tools/Build-Installer.ps1` inside a fresh clone of `origin/main` at `b28ac78`. Every
gate ran again there and passed: the soak check ("soak PASS for this code"), agent 824, relay 62,
the synthetic suites, and the native build from scratch.

* The Setup: `release\KingdomComeTogether-Setup-0.43.0.exe`, 100.5 MB, SHA-256
  `70809b61...2affbfb9`. It sits in the git-ignored release folder beside 0.42.8's. There is no
  GitHub release.
* **The payload smoke** passes (`Test-PayloadSmoke.ps1 -Payload release\KCDMP`): coherence over
  1,065 assembly entries (16 informational version differences, as before), `RELAY-SMOKE ok
  ... protocol=v10 release=0.43.0`, and no load failure in either log.
* **`Verify-Install.ps1`** against the payload: every WO-151 marker is present. Only the two
  layers that Setup itself writes (the dice keys and Setup's own verify file) fail, as on any
  folder no Setup ran on.
* **The privacy sweep** covered all 1,026 payload files, ASCII and UTF-16. It found none of the
  field bundles' player or Steam names, Windows user names, Steam IDs, addresses or profile
  paths of ours. The only hits are in stock files:
  * "MooseCree" is a language name in a Microsoft culture table;
  * `ToBinary...` and `AttemptingToBind...` are .NET method names;
  * the NAudio author's own build path sits inside the six NuGet DLLs;
  * `10.0.0.2` is the master server's documented example;
  * the other 10.x "addresses" are assembly version numbers.

## L. Live runs (solo, throwaway saves, one machine)

| run | role | what it proved |
|---|---|---|
| L1 | host | Phase 0 live; the guard found `local_state::read_ptr` |
| L2–L4 | host | TakeDamage's arguments; copy contexts; hit-row capture and replay; the sampler fix; the GetHorse WUID fix; avatar adoption |
| J1 | joiner | the joiner's own mount of a host horse copy; no combat actor on a suspended never-fought copy; the J1 incident (§D 12) |
| L5 | host | the weather hook; the engine join hold; the door names' real shape; no sync attack in a 70 s fight |
| J2 | joiner | catch-up (both paths), the weather gate, doors from the host, the joiner's own door ask, the whistle, smithing on the avatar; the save-list scan faults found |
| L6 | host | a joiner's door ask applied and sent; the avatar-as-opener bug found and fixed |
| L7 | host | two soak attempts with the mod, neither counted (a dropped console connection; the window alt-tabbed out of, §0.3) |
| L8 | — | the soak without the mod, window focused: 71.7 → 70.7 FPS |
| L9 | host | the soak with the mod, window focused: 70.9 → 71.4 FPS: PASS |

Saves: every playline backed up first (sha256 list). The real playlines are unchanged after
every run (357 of 357 match). The throwaway playline4's added saves were removed at the end.

## F. Follow-ups (what each needs)

1. **1.2**: the combat module's hit route for a player victim: synthesise `S_CombatHitData` into
   the victim's hit slot, so the game computes and plays the reaction.
2. **1.3**: the sync attack's row GUID offset. Run a fight with a wolf or a grapple and read the
   `WO151-SYNCDUMP` rows against `combat_action_sync_attack.xml`, as was done for the hit rows.
3. **3.3**: a scene initialised and never played (it waits for a quest step). This is WO-149 C's
   dialogue-as-triggerer work.
4. **3.5**: dialogue lines through the dialogue module's own line player (native), and barks
   through `StartMonolog` with a topic id from the dialogue database.
5. **Phase 2**: NPC riders on copies (no code mounts a rider's copy); riding together on both
   screens, with frames (two players).
6. **4.1**: the bed's kept picker, reproduced at a bed.
