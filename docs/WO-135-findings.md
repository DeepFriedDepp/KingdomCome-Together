# WO-135 — the avatar is a puppet, and four more fixes: findings

Unattended, solo, one machine: the Modding Tools game, the throwaway save
`playline1/quicksave036` (and a reseeded copy of it as the synthetic host's
world), a local relay on its own port. The real game ran as the **host** with
`tools/wo121/avatarpeer` as the joiner (runs H1, H2) and as the **joiner** of
`tools/wo118/synthpeer --join-host125` (runs J1, J2: real joins from the main
menu). Evidence marks: (observed), (code-verified), (synthetic),
(inconclusive). Poses are judged only by consecutive frames on the screen that
matters. `VERSION` is **0.40.0** (the maintainer's string). Screens:
`docs/wo135-shots/`. Side effects and gates: `docs/WO-135-progress.md`.

## 0. Answer first

| phase | result |
|---|---|
| **1 the avatar is a puppet** | **fixed** (observed, solo). Two levers, both the engine's own: (a) a **native dialogue gate** at the one DialogModule function every dialogue, bark and monologue starts in — any dialogue with an avatar among its *speakers* is refused (the function's own "not started"); listeners untouched; (b) **27 per-soul script contexts** in three groups (witness, react, defence) that the brain checks on itself. With all four groups: 0 dialogues with the avatar as speaker in every run; the gate refused the avatar's hit screams and crime barks (observed). WO-131's settings are unchanged (never AI-ignorant, the player's faction, the 5 WO-121 contexts, native automation off) — §1.3 |
| found: a ragdolled avatar stayed in the ground | **fixed** (observed). The maintainer's report during H2: the avatar lay in the ground while the native writer refused it (`not-living`) every 10 s. WO-131's stand-up covered NPC copies only; avatars now get the same `actor:StandUp()` — on its feet 0.7 s later (sheet 1) |
| self-block / fair damage | **not produced solo** (inconclusive). No input-free attacker exists: spawned souls, a spawned wolf included, target the avatar and then "Requested combat termination: Instant termination" (as WO-132). Code-verified root cause: the brain's `interrupt_attack` wraps every fight in Melee{Defense,Guard,Offense}AutomationDecorators whose `active` is *not* the `combat_disable*Automation` contexts — it re-arms, every fight, the automation WO-119's native switch turns off. The defence group sets exactly those contexts (read back set). Also found: the field host's native log shows the joiner's *own* streamed blocks on the avatar (`block=1/0` edges, 308 fresh state samples) — part of the field's "blocked by itself" was the joiner's real blocks |
| T-pose | **not reproduced** (inconclusive): no long fight is possible solo (above). Two-player checklist item |
| **2 knockouts both ways** | **built, observed on both sides.** Host: a knocked-out NPC streams flag 2 (it already did); its body is lootable (BodyState "down" with its items); the joiner's finish reaches the host and **the joiner's avatar performs the game's own mercy kill** (frames, sheet 6), then the normal host-only death. Joiner: the copy goes down where the host's NPC lies (guard swapped for the imm-only knockout guard + the game's `unconscious` buffs, sheet 8), the host's looting of the knocked-out body reaches it (6 → 1 items), it gets up when the host's does (sheet 10), and its "kill" action becomes a request (the local kill never runs; host ok → the copy dies at the host's position, 0.62 m) |
| found: a woken copy stayed down | **fixed** (observed). The get-up is the brain's and a copy's brain is paused (WO-131): after the wake it lay ragdolled and `StandUp` did nothing (sheet 9). `actor:Revive(false)` 1.5 s after the wake puts it on its feet (sheet 10, J2) |
| **3 crouch** | **the chain works; the field's missing crouch is not explained** (observed / inconclusive). The player's own crouch setter (the function the crouch key reaches, WO-119) → captured → sent → the peer received `crouch=1` then `0` → the other screen's avatar crouches (4 consecutive frames on each side, sheets 4 and 11). The capture now also reads the engine's rendered stance (the Mannequin `stealth` tag), logs every edge with its source (`WO135-CROUCH local crouch=1 (source: byte+tag)`) and counts it (`cap_crouch`). A real crouch key press was never made (no input): the next two-player session's log names the source or proves the key path differs |
| **4 outfits both ways** | **fixed** (observed). Root cause (field log + code): the agent seeded each avatar's "already worn" set with the spawn preset, but that avatar never wore it (ten `UnequipItem failed. Item was not found` at the join) — so the joiner's padded gambeson, a preset class, was never equipped, and his plate over it was refused seven times ("Can't equip armor 'ArmPlate01_m01_C2'. It requires 'body_cloth_padded' slot to be filled") and suppressed for 10 minutes. Now the diff is against the avatar's **real** equipped set; one apply per avatar at a time (newest outfit wins); retries until it converges; a class the game truly refuses is logged once and skipped only until the outfit changes. Live: plate sent before its padded layer → refused once, worn one second later; both new pieces worn while the avatar held combat mode (sheet 3) |
| **5 same-build saves** | **built, observed.** The host announces its world's build (a new LootHost kind); the joiner filters Henry sources by it **before any request**. Live J1: the host announced a build no save here has → both local builds skipped (one line each), launcher state `wrong-build` with the plain message, 0 join requests reached the host; then the right build → both choices offered → Bring picked the newest same-build Henry → joined. Research: **no** to bringing a newer-build Henry (§5.3) |
| found: joins held the loading screen ~49 s | **fixed** (observed). J1 showed the engine's red "Loading screen timeouted while still in post load reconstruction!" (the maintainer saw it on screen). WO-131's copy guard paused NPCs right after `EntityModuleOnPostLoadGame`, before `Gameplay started`; paused NPCs never finish the AI's post-load reconstruction. The guard now skips every load: J2 post-load → gameplay ~2 s, no error |

## 1. Phase 1 — the avatar is a puppet

### 1.1 What the avatar did (the field host log)

Dialogues with the avatar as the speaker (`Ex: kcd2mp_0`), by kind:
received-hit screams (`boj_a_zraneni.inkasovany_zasah_*`, `COMBAT_VICTIM_SCREAM_RECEIVED_HIT`)
~35; recognition (`crime_reaction_barks.rozpoznavani`, `NPC_REAGUJE_NA_STAV_I`) 25;
torch (`pochoden`) 7; assault witness (`PRATELSKE_NPC_DOSTALO_ZASAH`) 5+;
"sees the player crouching" (`NPC_VIDI_HRACE_V_CROUCHI`) 3; practice-fight
spectator 3; greetings 6.

### 1.2 The levers (why each)

**Speech — the dialogue gate** (`native/KCDMP/wo135.cpp`). Every dialogue on
this build starts in DialogModule `+0x98290` (the only function referencing
"New dialogue '%s' is starting. Params: souls = '%s' …"). Its request lists the
speakers at `request+0x58..0x60` and listeners at `+0x70..0x78`, 8-byte soul ids
(read from that log line's own builder, `+0xA1650`: "Ex" loop, then "Nx"). An
entry hook returns 0 — the function's own failure value — when an avatar's soul
id (soul+0x40) is a speaker. Anchored by the string's single reference, the
unwind entry and the exact 20 prologue bytes; any miss = not armed, logged.
Why not a context: `speech_mute` (SideEffect `muteDialogue`), `RestrictDialog`
(already on, WO-65) and the combat-chat contexts left every avatar monologue
starting **and voiced**: with them all set, three hit screams each still
loaded their voice file (observed, H1).

**Witness** (14 contexts): `crime_ignorePlayerPerception` (handleAwareness: no
awareness of the player — no recognition, no crime seen), `crime_ignoreNPCHitVolumes`
(never witnesses an NPC being hit), `crime_ignoreAnimalHitVolumes`,
`crime_ignoreCombatSounds`, `crime_ignorePlayersSounds`, `crime_ignoreCorpses`,
`crime_ignoreUnconsciousBodies`, `crime_ignoreThefts`, `crime_ignorePickpocketing`,
`crime_ignoreLockpicking`, `crime_disableCrimeInformationEmit` (never spreads a
crime), `crime_disableReport` (never reports to a guard),
`crime_dontCreateInformationsWhenHit` (a hit on it never turns a guard on the
host), `switch_disabledInformationReaction`.

**React** (9): `switch_disabled{Perception,Hearing,Hit,HitBehavioral,NearMiss}Reaction`,
`crime_ignoreCrouchingPlayer`, `crime_ignorePlayerWithoutTorch`,
`crime_doNotReactToEnemiesOnSight`, `combat_neverSurrenderOrFlee`.

**Defence** (4): `combat_disable{MeleeDefense,Guard,Offense}Automation`,
`combat_disableCombatMovement` — the brain's own off switch for the automation
it re-arms each fight (§0).

Each context is checked by the brain on the NPC itself (`target=""` /
`$this.id` in `Scripts.pak :: AI/npc/basic/switch/*.xml`), so none changes how
anyone else perceives the avatar.

### 1.3 What changed from WO-131

| setting | WO-131 | WO-135 |
|---|---|---|
| AI ignorance (host, shared world) | never ignorant | unchanged |
| the player's faction | joined | unchanged (needed: enemies treat him as the host's side) |
| WO-121 contexts (5) | set | unchanged |
| native combat automation off | set | unchanged (the brain re-arms it; the defence group is the brain's switch) |
| dialogue gate | — | **new**: speech |
| witness / react / defence contexts | — | **new**: 27 |

`mp_avatar_quiet 0..15` (1 speech, 2 witness, 4 react, 8 defence; default 15)
picks the groups at run time (MotionConfig byte 5). Nothing makes NPCs ignore
the avatar: the wolf still targeted it with all groups on (`TargetChanged on
wo132_wolf (target kcd2mp_1)`, observed H1).

### 1.4 Checks (host, avatarpeer)

| check | result | mark |
|---|---|---|
| the host crouches 3 m in front of the avatar, groups **off** | 2 avatar monologues `NPC_VIDI_HRACE_V_CROUCHI` (the field's bark reproduced) | observed |
| … speech group only (the old context route) | still 2 | observed |
| … witness only / react only / witness+react | 0 each; a test soldier beside it barked every time (the control) | observed |
| scripted hits on the avatar, all groups | 0 avatar dialogues; `WO135-DIALOG refused #1` | observed |
| … speech gate off (mask 14) | the avatar barked (`NPC_VIDI_HRACOVA_PSA_UTOCIT`) | observed |
| contexts read back (`soul:HasScriptContext`) | 8/8 sampled set; the player has none | observed |
| the host's attack on an NPC beside the avatar | not produced: `DealDamage` with the player as attacker makes no hit volume (the victim itself did not react) | inconclusive |
| a guard only reacts to the joiner in his fight; a guard's hits hurt him | not testable solo (no fights in settlements, no input-free attacker) | — (checklist) |

## 2. Phase 2 — knockouts both ways

* **The state.** The engine's own knockout is the `unconscious` buff family
  (`Cpp:Unconscious`; Tables `rpg/buff.xml`); the wake is `remove_unconsciousness`.
  The host already streamed `IsUnconscious` as flag bit 1 (WO-38); nothing on the
  joiner acted on it but a one-shot clip, and the copy guard's `upr=1` made a
  real knockout impossible.
* **Joiner.** `KCD2MP_W135KoTick` (the WO-131 guard tick, 1 s) compares the host's
  flag with the copy: → `w135_ko` → wo131 op 2 **mode 2**: the imm-only
  `kcdmp_knockout_guard` on first (never a moment without imm), the full guard
  off, `infinite_unconsciousness_nonpersistent` + `unconscious_nonpersistend`
  (it wakes when the host's does, not on the game's timer). Wake: **mode 3** —
  those off, `remove_unconsciousness`, the full guard back — then
  `actor:Revive(false)` 1.5 s later (the get-up). The cosmetic WO-40 takedown clip
  is skipped under the guard. `mp_npc_ko_sync on|off`.
* **Finishing and takedowns.** `BasicAIActions.OnMercyKill / OnKnockout /
  OnStealthKill` (what the game's interactions call) are wrapped: on a
  host-owned copy they send LootAsk **Takedown** instead of running. The host's
  mod checks it (a human NPC, within 4 m of that joiner's avatar, down for
  mercy / up for the others) and runs the same `Request*` call with **the
  avatar as the actor**; if it has not taken after 3 s it applies the engine's
  result directly (death / `unconscious`); the answer goes back as LootHost
  TakedownResult. Pickpocketing a conscious NPC stays blocked (WO-131).
* **Loot.** WO-134's body rule already counted a knocked-out body; what was
  missing was the joiner's copy being down (its BodyState was stashed "alive
  here"). Now it is.
* **The joiner's knock-out blows through the hit gate** already knock the host's
  NPC out in the host's world (the field: the joiner's 12.3 hit knocked the
  bailiff out on the host) (observed in the field log).

| check | result | mark |
|---|---|---|
| host: test NPC knocked out | collapses; host streams flags 0 → 2 (sheet 5) | observed |
| host: woken | on its feet 1.2 s later (sheet 7) | observed |
| host: the joiner's loot ask on it | BodyState `open`, flags 1 (down), its items in 3 parts | observed |
| host: the joiner's finish | the avatar's `RequestMercyKill`: kneels over the body (sheet 6), dead bit out, `ok` back | observed |
| joiner: the host's NPC knocked out | `WO135-KO … knocked out ok=true local_now=down`; the copy collapses where it stands (sheet 8) | observed |
| joiner: the host loots it | BodyState update → the copy 6 → 1 items | observed |
| joiner: woken (J1, before the fix) | unconscious false, but still ragdolled; StandUp no effect (sheet 9) | observed |
| joiner: woken (J2, fixed) | down → on its feet ~1.5 s after the wake (sheet 10) | observed |
| joiner: the game's mercy kill on the copy | local kill never ran; request → host ok → the copy died at the host's position (0.62 m), guard lifted first | observed (synthetic host) |
| Lua rules (asks, retries, dead bodies, host refusals, the direct fallback) | Test-WO135Synthetic (b)–(d) | synthetic |

## 3. Phase 3 — crouch

* The capture read (`C_ActorStateExpansion` +0x18, which `SetCrouch` writes and
  `GetCrouch` returns — disassembled) works for the player: the player's own
  setter → `WO135-CROUCH local crouch=1` → the peer printed `got host state
  bits=Crouched crouch=1`, then `0` (observed, H2). Joiner side: the same, and
  both sources agreed (`source: byte+tag`, J2).
* The field log: the host received the joiner's state block all session
  (`block=1/0` edges, 308 fresh samples) but never a crouch bit; the host
  crouched (the avatar barked at it) but the joiner's copy never crouched.
  Without key input the key's own path cannot be exercised here, so the capture
  now reads the rendered stance too and logs every edge with its source.
* Apply: the avatar crouches on the other screen in 4 consecutive frames and
  stands again (host side sheet 4, joiner side sheet 11) (observed).
* The avatar does not react to crouching nearby (§1.4).

## 4. Phase 4 — outfits both ways

`GameBridge.cs` `ApplyAppearanceAsync` / `ConvergeAppearanceAsync` /
`VerifyAndRetryAsync`, rules in `Wo135Rules`.

| check | result | mark |
|---|---|---|
| `appearance mirror` (the host's 7 classes) | `+7 -10 (against its real equipped set of 10)`; worn at the first verify (sheet 2) | observed |
| two pieces, plate sent before its padded layer, the avatar in combat mode | plate refused once ("requires 'body_cloth_padded'"), worn one second later; read back both (sheet 3) | observed |
| a refused class | logged once, skipped until the outfit changes; never the whole outfit; not marked on a failed read or in combat | synthetic |

"local equipped-set read failed" (the sender's 800 ms REST read timing out once)
was harmless: the next poll recovered.

## 5. Phase 5 — same-build saves

### 5.1 The rule and the pick

A character and a world are combined from the same build only (WO-115's splice
check stays). The host reads its world's `BuildInfo` from the identifying save
and sends it to every joiner (LootHost **Build**, on change and every 30 s).
The joiner, before any request: Bring = the newest own Henry save of that
build; Start fresh = the first Henry save of a new game of that build; an
explicit `mp_join_henry playlineN/file` of another build is refused with the
plain message. Skips are logged once per file, one line per build. No
announcement within 15 s: no filter (logged). The launcher shows `choose`,
`choose-bring` / `choose-fresh` (only that button) or `wrong-build`:

> Your saves are from game version X, but your host's game is version Y. Start
> or load a game in the Modding Tools build and save once, then join again.

### 5.2 Checks

| check | result | mark |
|---|---|---|
| host announces its build | avatarpeer got `LootHost build … 1.5.5-release_1_5` | observed |
| the host's build matches no save here | 143 saves of 1.1.1 and 111 of 1.5.5 skipped (two lines); `wrong-build` + the message; **0** JoinRequests at the host | observed |
| … in game | the same message logged as a toast; the main menu draws no HUD, so there the launcher is what the player sees | observed |
| the right build | `choose`; Bring → `FIRST JOIN, Bring: the Henry comes from` the newest 1.5.5 save; spliced, loaded, joined | observed |
| mixed builds, pick order, one log per file, the message, the buttons | Wo135Tests | synthetic |

### 5.3 Research — a Henry from a newer build? **No.**

This machine has saves of two builds only (1.1.1-11377 and 1.5.5); none of
1.5.6, so the field's exact pair cannot be compared, and not the same character
in two builds. Comparing Henry's record across the two builds (`--save-tool
extract` + `blockdiff`): besides content, the **schema** differs — the 1.1.1
record carries fields `0928` and `12fc` that 1.5.5 lacks, 1.5.5 carries `1303`
that 1.1.1 lacks, and a 1.1.1 save has no playthrough seed at all (WO-125's
world identity is missing). The splice copies raw records; a field one build
writes and the other does not know is exactly what can load wrong or silently
drop. Recommendation: keep refusing; the message tells the player the one safe
route (a save made in the Modding Tools build). Revisit only with a 1.5.6/1.5.5
pair of the same character and a load test.

## 6. Runs

| run | the real game | what |
|---|---|---|
| H1 | host (avatarpeer) | contexts set/read back; crouch A/B by group; wolf targets the avatar; spawned souls never fight; hit screams survive every context; outfit/crouch not yet |
| H2 | host (avatarpeer) | the dialogue gate; hit-scream A/B; the avatar in the ground → StandUp; outfits (mirror, layered pair in combat); avatar crouch frames; crouch capture end to end; host knockout, loot ask, finish by the avatar, wake |
| J1 | joiner (synthetic host) | wrong-build refusal, right build, Bring join; the post-load timeout found; copy knockout, host loot of it, wake (found: stays down), the joiner's finish |
| J2 | joiner (synthetic host), restore join | no post-load timeout; wake with Revive; the host avatar crouching; the joiner's crouch capture (byte + tag) |

## 7. Two-player checklist (the next session)

`docs/TEST-0.40.0.md` section 4 — what each screen should show.

## 8. Carried forward

1. Self-block and fair damage against a real enemy; guards reacting only when
   the joiner is in the fight; the T-pose — all need a real fight (two players).
2. The crouch key's own path (the edge log names its source).
3. The host's crime reaction to the avatar's *takedowns* (a joiner's finish is
   performed by his avatar in the host's world): shared crime is the next WO.
4. A joiner who never saved in the Modding Tools build can only use Start fresh
   once they have a new game's first save there; the message says so.
