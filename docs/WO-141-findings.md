# WO-141 — Activities and animal attacks, animated on the other screen: findings

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios.

Evidence marks: **(observed)** seen live in the game, on consecutive frames of
the screen that matters; **(synthetic)** the real game against a synthetic peer,
or a console stand-in for a player's action; **(code-verified)** read in the
code or the game's binaries, or pinned by a unit test, but not run live;
**(inconclusive)** tried, no clear answer.

All live runs were solo on one machine (the Modding Tools game, 1.5.5), on a
throwaway copy of a save: the real game as the **host** with a scripted partner
(avatarpeer, run h1), and the real game as a **joiner** of a synthetic host
(synthpeer, runs j1–j3; j1 replayed NPC rows the real host had sent in h1). No
input was sent: a player's use of a bench or a trough is the trigger's own Lua
call typed in the console. "Both screens" means the real game's screen in each
role, one role per run.

## The answer

**Sync what the activity is, not the animation.** Every body the game drives —
an NPC, the player, our avatars — has an NPC-state context, and its current
state says, in the game's own terms, "this body is in this stance on this
object" (a bed, a seat, a kneeler, a cart slot) and "this body is in this
unstance at this location object" (a lean, a guard post, a workbench, a wash at
a trough). The DLL reads that; the other machine's copy — or the player's
avatar — is given the same activity on the same object, and **the game's own
NPC-state machine** places it there. Objects travel as level entity GUIDs, the
same on every machine that loaded the level.

| the requirement | how it works | evidence |
|---|---|---|
| NPC activities on the joiner: beds, benches, leans, guard posts, a bench snooze | the host reads its tracked NPCs' states (250 ms), sends each change and every held activity again every 10 s; the joiner's copy takes it through the game's post-load placement | **(observed)** j1: a guard's copy lies in his bed (frame 2); `LeaningBack`, `LeaningRight`, `guard`, `sitting`+`camper_snooze` copies in step; 13 rows applied, 9 NPCs; h1 sent 1,891 rows |
| the field bug (a sleeper standing in his bed) is gone | A/B on the joiner's copy after a state reset: `mp_activities off` → the guard stands in his bed; `on` → he lies again at once | **(observed)** j1, frame 1 |
| the copy stays bound (no free brain, WO-131); an activity owns the body while it lasts | the position writer yields to an activity that holds the body on its object (`MP-NPCWRITE … activity-hold`) and takes the body back on "none" | **(observed)** j1/j3 (the hold line, the body on the object while the stream point is elsewhere); **(code-verified)** the release |
| the reconcile keeps the copy in step | out of step → applied at once, re-checked every 1.5 s; the game resetting a copy's state is re-applied within one tick (250 ms) | **(observed)** j1 |
| the players' activities: the host on a bench, seen by the joiner; the joiner on a bench, seen by the host | each player's own body is read the same way (a sit is `PlayerStateHandler.ChangeStance(bench, "sitting")`, a stance in his state) and sent as a player row; the avatar takes the stance | **(observed)** h1: the host's sit captured and sent; the partner's avatar sits on an outdoor bench on the host's screen and stands up again (frames 3–4) |
| the joiner's trough, seen by the host | the trough's wash is a one-shot the player's state never holds (`ActionTrigger:ReportUse → PlayerStateHandler.PlayAnimationAction(trough, "WashFace")`, 5.2 s). The mod wraps the trough class's `ReportUse`; the agent names the NPCs' own `housekeeper_faceWash` at the same trough; the DLL reports it as the player's activity for 6 s | **(observed)** j3, the joiner's side end to end: `WO141-ANIM WashFace at SmartObjectHolder6[…]` → `shown as housekeeper_faceWash for 6.0 s -> ok` → the host received `unstance=195@<trough> owns`, and `none` 6.0 s later. The other half — an avatar washing at the trough — **(observed)** j1 on the joiner's screen (frame 5: walks to the tub, bends over it, straightens). Both halves are the same apply; a real host watching a real joiner wash needs two machines |
| the joiner's grindstone, seen by the host | the player's state holds `Minigame 'Sharpening'` and the row carries it (minigame 1); **nothing in the game's NPC activities shows a grindstone** (NPCs never use one), and the player's `SharpeningMinigame` fragment does not play on an avatar through the combat action path | **(observed)** captured on the wire; **not shown**: the avatar stands at the grindstone (the stream) |
| the nameplate follows a sitting avatar | the label is drawn over the avatar's body (head height 0.75 m lying, 1.35 m sitting, 1.25 m kneeling) while an activity holds it; the maintainer saw it stay at the streamed standing point | **(observed)** j3, frame 6 (before / after); the first attempt keyed the table by number while the ghost tables use the string id — fixed, pinned by the Lua suite |
| a copy mid-activity leaves it first for a fight, a talk, a knockout or a death | the joiner's agent leaves the activity (stands the copy up, the stream takes it) while the host's NPC is engaged with a player, talking, knocked out or dead (WO-136's down bits), and gives it back after | **(code-verified)** unit tests (`A_copy_leaves_its_activity_for_…`); not seen live (needs a fight at an NPC in an activity) |
| animal attacks on the joiner | the bite was never shown because the swing path resolved the attacker through a human-only binder; it now falls back to the actor system's own actor for a body that is not a human, and the bite plays on the joiner's copy | **(observed)** j1: a wolf's lunge and bite on the joiner's screen (frame 7); `SWING: entity=… is not a human -- the actor system's own actor` ×3, `animal_attacks=3` |
| settings | `mp_activities on\|off` and `mp_animal_attacks on\|off`, both **default on** (proven above); `mp_activity_status` | **(observed)** `mp_activities` off/on live (the A/B); **(synthetic)** both switches in the Lua suite; **(code-verified)** the agent's bite gate |

**Not covered**, left out or not built (§4 has every census row): hand content
(M3: the hoe, a broom, a woodworker's drawknife), NPC one-shots (M4: serving
beer, drinking from a trough), activity gaits (M5: the field hoe walk, a drunk
gait), the player's grindstone, smithing, alchemy, reading and dice (only the
trough's wash has an NPC activity to show it), cart passengers following the
cart, and animals' own idles (read and sent like any NPC's, not seen live).

## 1. The NPC-state context (read)

| what | where (XGenAIModule, this build) | how it was found |
|---|---|---|
| a body's context | `mgr = *(XGenAI+0x2E52F08)`, `obj = mgr->vtbl[0x20](&entityId)`, context `obj+0x9C0`, checked by its vftable `.?AVC_NPCContext@NPCState@xgenaimodule@wh@@` | XGenAI's own `ResetCurrentStateElementCommand` asks the manager this way (string anchor, then its references) |
| current / loaded state | `ctx+0x90` `C_NPCCurrentState`, `ctx+0x180` `C_NPCRequiredState`; a pending request id at `ctx+0x5E8` (-1 none) | disassembly; live reads |
| the element slots | the state's vector at `+0x10/+0x18` (16-byte shared_ptr entries): [0] Stance, [1] LeftHand, [2] RightHand, [3] Unstance, [4] ChangeEquipment, [5] Minigame, then extras | live reads; the game's own state text (`FUN_181898330`) as a cross-check |
| the element layouts | Stance: `+0x28` stance id, `+0x30` object WUID, `+0x38` cart slot; Unstance: `+0x28` unstance id (the index into `NPCStateUnstanceDatabase.xml`, 654 rows), `+0x30` location WUID; HandContent: `+0x28` item WUID; Minigame: `+0x28` type, `+0x30` object | live reads against the game's text |
| the stance enum | 0 undefined, 1 standing, 2 lying, 3 sitting, 4 kneel, 5 horse, 6 crouch, 7 cart | the game's stance names |
| a WUID → an entity, and back | the WUID service `*(XGenAI+0x2E55FC8)` (`vtbl[0x10]` → object `vtbl[0x10]` → entity), the reverse through the entity's GUID; a WUID's high byte is its type (0x08 smart object, 0x05 soul, 0x0A linkable object, 0x02 item), the rest a runtime index — so objects travel as **level GUIDs** | `C_ScriptBindXGenAIModule::GetEntityByWUID`; live |

The whole village read correctly on both machines: sleepers in their beds,
sitters on their benches, leaners, the guard at his post, the woodworker at his
bench **(observed)**, h1/j1.

## 2. The apply: the game's own placement after a load

A copy (or an avatar) is given an activity the way the game restores one after
a load: its **loaded** state is cleared by its own `Clear` (vtbl slot 0x38),
given a `StanceElementRequired` and/or an `UnstanceElement` built by the
reflection layer (`rttr::type::create` with the registered names
`wh::xgenaimodule::NPCState::StanceElementRequired` / `…::UnstanceElement` —
the C_ prefix is not in the registry; the variant holds the element's
shared_ptr inline), and `C_NPCContext::ExecuteStateChangeIntoLoadedState`
(found by its `__FUNCTION__` string) runs it. The body is put on the object in
the stance, or into the unstance at its location, **in one frame, by the
NPC-state machine itself — on a paused (WO-131) copy too, and on the player**
(Henry got the game's own "Get up [W]"). The In/Out fragments are not played:
it is the load's fast-forward. **(observed)** beds, benches, leans, the guard
post, the snooze, the trough's wash on an avatar.

The animated route (`C_NPCContext::RequestStateChange` with a built request) was
mapped but not needed.

**The writer yields.** While an applied activity owns the body (a stance on an
object, an unstance at a location), `npc_drive` stops writing the body's
position (`activity-hold`); "none" releases it and the stream takes the body
back **(observed)**.

**A refusal is retried, slowly.** The woodworker's `debarkingWood` at his bench
was refused on the copy every time (`exec 0`, the state stays empty; his host
body holds his drawknife, the copy holds nothing): 4 tries 1.5 s apart, then
one every 15 s — 154 tries (and 154 log lines) in a 45-minute run; the copy
stands at the bench **(observed)**. Why the state machine refuses it is
**(inconclusive)**: its unstance row is no different from `LeaningBack`'s; the
missing tool is the likeliest reason.

## 3. The flow

| | host | joiner |
|---|---|---|
| reads (DLL, every 250 ms) | its tracked NPCs (WO-138's set) and its player | its player |
| sends | NPC rows and its player's row: 0x6A ActivityHost to each joiner | its player's row: 0x6C ActivityPeer to the host |
| applies | each joiner's row on that joiner's avatar; forwards it to the other joiners | the host's NPC rows on its copies, every player's row on that player's avatar |

Wire (the WO-123 join channel, no protocol bump; the relay routes by
`Protocol.JoinWire`): `[kind 1 NPC|2 player][count]{[peer][nameLen][name][30]}`,
the 30 bytes being `[stance][cart][stanceObj:8][unstance:2][unstanceObj:8]
[minigame][minigameObj:8][flags]` (flags bit 0 = the activity owns the body).
Pipe: `0x27` → `0xA6`, rows `0xA7` (`native/KCDMP/wo141.h`). A change is sent
at once, a held activity again every 10 s; "none" once. A row that arrives
during a load is held and applied when the load ends; a joiner who is not in
the host's world (WO-140) applies none. An agent-side kinds mask (1 NPC
stances, 2 NPC unstances, 4 players) exists for a field A/B; all are on, and
there is no console command for it.

**Avatars:** a crouch rides WO-136's state block, never the activity; horse is
WO-40/136's.

## 4. Coverage against the census (`docs/WO-141A-activity-census.md`)

**Updated by WO-143** (0.42.0) for the rows it covers — tools in hand, the
field hoe and the other gaits, one-shots, the players' minigames, looks and
carts: `docs/WO-143-findings.md` §4. The rows below are WO-141's.

"Shown" = the other screen shows it; "stands" = the body stands at the spot on
the other screen (as before WO-141); "walks" = the stream walks it without the
activity's pose or tool.

### 4.1 M1 — stances

| census row | WO-141 | evidence |
|---|---|---|
| sleep in bed (NPC, player) | shown: the stance on the bed | **(observed)** NPC; **(code-verified)** the player's bed (same apply as the bench) |
| sit on bench / at table / on ground | shown | **(observed)** bench (NPC, the partner's avatar); **(code-verified)** table sides and ground places (the object's helper picks the pose) |
| sit on chair | shown; the chair does not move (the step-in that moves it is not played) | **(code-verified)** |
| lie on ground / tent / shelter | shown | **(code-verified)** |
| crouch | not an activity: WO-136's state block (avatars); NPC crouch carried as a stance | **(code-verified)** |
| on horse | WO-40/136 (excluded here) | — |
| cart driver / passenger | the cart slot travels; **following the moving cart is untested** | **(inconclusive)** |
| animals lying / sitting | read and sent like any tracked NPC's | **(code-verified)** |

### 4.2 M2 — workstations and trades

| census row | WO-141 | evidence |
|---|---|---|
| forge, armour smith, foundry, tanning, butchering, baking, weaving, shoemaking, carpentry/debarking, sawing, chopping, felling, mining, kiln, mint, bailiff counting, painter, butter churning, bath-house | the unstance travels; where the game takes it on the copy it is shown; **a trade that needs its tool in the hand is refused and the copy stands** | **(observed)** debarking refused (§2); **(code-verified)** the rest |
| grindstone (player) | captured (minigame 1), **not shown**: stands | **(observed)** |
| smithing (player) | not shown: stands | **(code-verified)** no matching NPC activity |
| alchemy | NPC: the unstance travels; player: not shown | **(code-verified)** |
| herb crushing, scribe/transcribing (NPC) | the unstance travels | **(code-verified)** |

### 4.3 Fieldwork and chores

| census row | WO-141 | evidence |
|---|---|---|
| **field hoeing** (M3 + M5) | **not covered**: walks, without the hoe | not built |
| vineyard hoeing, weeding, grave digging, laundry, fence repair, hen care, milking, pig feeding, horse grooming, cooking at the hearth | the unstance travels; the tool (M3) does not | **(code-verified)** |
| sweeping | the unstance travels (`sweeping` is unaligned, no location): shown on the spot if the game takes it without the broom | **(code-verified)** |
| fetching / carrying water | the unstance travels; the bucket does not | **(code-verified)** |
| washing face / drinking at a trough | NPC wash: shown (`housekeeper_faceWash`); NPC drink (M4): not covered; **player wash: shown** as the NPC wash | **(observed)** (§ the answer) |
| chopping block (M4), carrying a sack (M3 + M5) | not covered | not built |
| stoking a fire (M4) | not covered | not built |

### 4.4 Tavern, eating, drinking, dice

| census row | WO-141 | evidence |
|---|---|---|
| guests eating / drinking seated, drink wine at a table | the seat is shown (M1) and the eating unstance travels; the bowl, stein and M4 sips do not | **(code-verified)** |
| bartender serving / cleaning (M4) | not covered: walks | not built |
| innkeeper talking, eating at home, camp eating | the unstance travels | **(code-verified)** |
| dice challenger / opponent / kibitzer | the seat and `diceKibitzer` travel; the dice (M4, M7) do not | **(code-verified)** |
| player eats / drinks from inventory, from a pot or barrel, cooks | not covered | not built |

### 4.5 Idles, leaning, praying, inspecting

| census row | WO-141 | evidence |
|---|---|---|
| lean back / left / right | shown | **(observed)** `LeaningBack`, `LeaningRight` |
| lean on a fence, front, exhausted, on a sword | the unstance travels | **(code-verified)** |
| bench activities, camp bench fun | the seat and the unstance travel (`camper_snooze` shown) | **(observed)** snooze; **(code-verified)** the rest |
| praying (NPC; the player's `prayKneelingGround_player`) | the unstance travels (the player's is in his state, sent as his row) | **(code-verified)** |
| confession, reading, storyteller, inspecting, chest check, waiting spots, meditation, stretching | the unstance travels | **(code-verified)** |
| playing an instrument, relieving oneself, bells (M4) | not covered | not built |
| city-walk points, additive crowd, patrols (M5 movement) | already the stream | — |
| doors (M4 + M6), ladders | not covered | not built |

### 4.6 Guards, crowds, social

| census row | WO-141 | evidence |
|---|---|---|
| guard post | shown | **(observed)** `guard` |
| halberdier at attention / asleep, guard of honour | the unstance travels; the halberd (M3) does not | **(code-verified)** |
| tournament and party crowds, small-talking watchers, dialogue poses | the unstance travels | **(code-verified)** |

### 4.7 Merchants, beggars, the sick and wounded, the dead

| census row | WO-141 | evidence |
|---|---|---|
| merchant at a stall, beggar kneeling / lying | the unstance travels (`BeggarKneeling` read live) | **(code-verified)** apply |
| sick in bed, wounded on the ground, sitting injured, healer and patient | the stance and the unstance travel; paired healing needs both bodies | **(code-verified)** |
| tied up, pillory, hanged | the unstance travels; ropes / the pillory (M3, M6) do not | **(code-verified)** |
| posed dead bodies | level content, posed the same on both machines | — |
| looting a body (NPC) | the unstance travels | **(code-verified)** |

### 4.8 Animals

| census row | WO-141 | evidence |
|---|---|---|
| dog, hare, deer, horse, pig, cow, sheep idles | read and sent like any tracked NPC's | **(code-verified)** |
| **animal attacks** (wolf, dog, boar) | the bite plays on the joiner's copy | **(observed)** wolf; **(code-verified)** dog, boar (the same path) |
| boids (chickens, fish, rats, birds, cats) | no NPC state; per machine | — |

### 4.9 The player (M8 and the rest)

| census row | WO-141 | evidence |
|---|---|---|
| sit, sleep / lie down | shown on the avatar | **(observed)** sit; **(code-verified)** lie |
| wait / skip time | WO-140 | — |
| grindstone, smithing, alchemy, reading, transcription, dice | not shown: the avatar stands at the spot | §4.2 |
| washing at a trough | **shown** as the NPC wash at that trough | **(observed)** |
| washing at a pier (soap) | the same wrap; not tried | **(code-verified)** |
| praying | the unstance travels | **(code-verified)** |
| archery, stone throwing, grave digging, herb gathering, lockpicking, pickpocketing, looting, picking up | not covered | not built |
| carrying a body, takedowns | out of scope | — |
| riding | WO-40/136 | — |
| cart passenger, ladders, torch | not covered (the torch is WO-136's) | — |
| crouch | WO-136 | — |
| pillory (punishment) | the unstance travels | **(code-verified)** |
| bath-house, dancing | not covered | — |

### 4.10 Special groups

Battle groups, random events, crime reactions (unstances such as
`CrimeSurrender`, `Cower`, `cryingOnBench`) travel like any unstance
**(code-verified)**; their one-shots (M4) do not. Stealth-sleep teleports are
not activities.

## 5. Tried, and not used

| what | result |
|---|---|
| the player's minigame fragments on an avatar (`SharpeningMinigame`, `WashFace`) through the combat action queue (WO-44's path) | queued, nothing visible on the avatar **(observed)**; `QueueAnimationState` is not bound in Lua; `human:PlayAnim` does nothing on an NPC |
| an NPC activity for the grindstone | none in the 654-row database (NPCs sharpen knives sitting, `camper_knifeSharpening`, never at a wheel) |
| a Minigame element on a copy | not built (the player's minigame is his UI, not a body state) |

## 6. Defaults, and why

`mp_activities` **on**: stances and unstances are proven on the joiner's
copies and on avatars both ways; the rule fails closed (a row the game refuses
leaves the body standing, as before). `mp_animal_attacks` **on**: the bite is
proven. Nothing half-built ships on: hand content, NPC one-shots and gaits are
not built at all; the grindstone's minigame byte travels but nothing applies it.

## 7. Tests

| suite | what it pins | result |
|---|---|---|
| native `wo141_rules_tests.cpp` | synced stances, who owns the body, normalised rows, the 30-byte layout, in step, the reconcile's pace, the host's refresh, the one-shot standing in only for "none" | 237 native checks pass (all suites) |
| agent `Wo141Tests.cs` | the wire rows (from host / from joiner, sizes), the byte layout (the same bytes as native), codecs and refusals, the DLL frame and op bodies (the 75-character trough name that broke the first live try), when it runs, the capture mask, **leave-first for a fight, a talk, a knockout or a death**, the avatar's crouch, the kinds mask, 12-row batches, the one-shot map | 609 agent tests pass |
| relay round trip | 0x6A host → joiner, 0x6C joiner → host, the biggest body whole, a joiner never speaks for the host's NPCs, the host never sends a joiner's message | 56 relay tests pass |
| `Test-WO141Synthetic` (the real kdcmp.lua) | the switches and their lines, the agent's sync, the trough wrap (the game's call always runs; only the player's "Animation" at a linked object is sent; off = nothing; re-wrapped after a reload), the nameplate key, the status | 33/33 |
| every other gate | all `Test-*Synthetic.ps1`, the static checks, the payload smoke | green |

## 8. Open

* Hand content (M3) — the tool in the hand, and with it the trades the game
  refuses without it; then the field hoe (M3 + M5 `actorCondition_forcedHoeing`).
* NPC one-shots (M4) — serving, drinking, stoking.
* A refused activity is logged at every retry (every 15 s): quieter after the
  first few.
* Two machines: the leave-first rule in a real fight; a real host watching a
  real joiner at the trough and at the grindstone.
