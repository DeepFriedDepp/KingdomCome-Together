# WO-148A — the carry census: every way a player carries something

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios or PLAION. This page describes the game's systems in our own words;
file paths and identifiers point into the game's data, which belongs to
Warhorse Studios and PLAION and is not reproduced here.

Read-only census, 2026-10-01, of the Modding Tools build (game 1.5.5): the
game's Lua and AI data (`Scripts.pak`), its configuration and UI data
(`IPL_GameData.pak`), its tables (`Tables.pak`), the Mannequin databases in
`Animations.pak` (names and fragment databases only), Warhorse's script-bind
reference shipped with the Modding Tools, and the ASCII and RTTI strings of the
engine DLLs. Nothing was launched. Abbreviations: `SP:` Scripts.pak, `GD:`
IPL_GameData.pak, `TB:` Tables.pak (`Libs/Tables/`), `AN:` Animations.pak
(`Animations/Mannequin/`). Evidence marks: **(data)** read in a game file,
**(doc)** in the script-bind reference, **(reg)** a string registered in a DLL,
**(dis)** a disassembly, **(inf)** inferred, **(live?)** only the running game
can settle it.

## The answer

There are three carry systems, and every quest carry is one of them:

| # | kind | native system (EntityModule) | the carrier's state | the input while carrying | the WO-148 build |
|---|---|---|---|---|---|
| A | a **body** on the shoulder: dead, unconscious, or (a few quests) a living NPC with a script context | `C_ActorCarryCorpse`, `C_PlayerCarryCorpse`, `C_ActorActionCarryCorpse`; the body runs `C_ActorActionCarried` | actor state `carryCorpse` (the body: `carried`) | action map `carry_corpse_carrying` | shown: the partner's avatar carries this machine's copy with the game's own pick-up and set-down |
| B | **holding** a body after a stealth takedown (the clinch) | CombatModule stealth sync actions | combat guard `stealthGrab` | action map `combat_stealth` | the takedown itself goes through the host (WO-135); "pick up body" from the hold becomes kind A and is shown as A; "drop body" leaves it knocked out (WO-135's knockout sync) |
| C | an **item**: sacks, jugs, "bag and take" piles (carcasses, debris, dung) | `C_ActorActionCarryItem` with `C_CarryItemPile` / `C_CarryableItem` | actor state `carryItem` | action map `carry_item` | shown: the avatar holds the game's sack model on its right hand; a dropped one lies where it landed (a prop on the other screen) |
| D | quest carries | A or C inside quest graphs; two scripted joined-animation carries | — | — | A and C are shown; what the quest counts is the next phase |
| E | burial | A, then a quest's own "bury" hold prompt (the hole-digging minigame is separate) | `carryCorpse`, then the quest | — | the carry to the grave is shown; the burial is the quest's |

**No throw exists** for a body or an item. The only throws in the game are the
decoy and the stone-throwing minigame. **(data)**

## Kind A — a body

**Start.** The game's interaction on an NPC body offers "grab body" when
`user.actor:CanGrabCorpse(self.id)` holds, the body has no `disableGrabBody`
property, and the user is not female (`SP:Scripts/Entities/AI/Shared/BasicAIActions.lua:35-42`);
the prompt is greyed out by the script context `crime_greyOutGrabBody`. Its
callback `BasicAIActions:OnGrabCorpse(user)` calls
`user.actor:RequestGrabCorpse(self.id)` (`:263-266`) — the one Lua function
that runs, with the body as `self`. The input is `grab_body` in the default
`player` action map (`GD:Libs/Config/defaultProfile.xml:425-433`), interaction
`inr_pickupCorpse` (`GD:Libs/Config/interaction_filter.xml:37`). **(data)**

**End.** `put_corpse` in `carry_corpse_carrying` is a hold (`onHold`, 0.25 s)
(`defaultProfile.xml:525-534`); it can be refused for lack of room. No Lua runs
on the set-down. Dialogues that fade to black force an instant drop through
the player brain's `placeBody` inbox, which runs `InstantPlaceBody`
(`SP:AI/player/switch/switch.xml:127-129`). A heavy hit while carrying
ragdolls the carrier (below). **(data)**

**Who can be carried.** Dead or unconscious NPCs (the crime trees branch on the
carried body's dead/unconscious checks: `SP:AI/npc/basic/switch/handleAwareness_bodyCarrier.xml`).
A **living** NPC only with the entity script context `CarryLivingActor`
(`TB:ai/ScriptContext.xml:24`), used by five quest NPCs; `CarryUnpickableActor`
(`:25`) forbids a body; placed dead-body smart objects declare
`bPickableByPlayer` (`SP:Scripts/Entities/WH/Special/DeadBody/SO_DeadBody_Human*.lua`). **(data)**

**States.** Actor state `carryCorpse` (actor_state id 11, `TB:action/actor_state.xml:15`);
the stance `carryCorpse` (`E_ACTORSTANCE_CARRYCORPSE`) is defined for every
actor in `SP:Scripts/Entities/actor/BasicActor.lua:119-127` — and for dogs and
wolves. The body is in actor state `carried` (quests test it with
`wh::entitymodule::IsInActorState`). The native classes are named by their own
RTTI in EntityModule. **(data, reg)**

**Animations.** The carrier's fragments in `AN:ADB/kcd_male_database.adb`:
`CorpseGrab` and `CorpsePut` (tags `relatedMale` or `relatedFemale` plus
`carryCorpse`; stealth variants), `MotionIdle` / `MotionMovement` (walk and
run, one blend space) / `MotionTurn` with `carryCorpse`. The body plays the
matching `_s` clips through the carrier's `SlaveChar` scope
(`AN:ADB/kcd_male_controllerdefs.xml`; slave databases
`kcd_relatedMale_male_database.adb` and the female sets). **The fragments are
not player-only**: only the torch, turn and camera refinements carry the
`player` tag, the female carrier has a full set, and NPCs carry bodies in
quests and tests through the same native class (§NPCs). There is no sprint,
jump or crouch fragment while carrying. **(data)**

**Attachment.** Two things run together: the body's character is slaved to the
carrier's Mannequin (`SlaveChar`), and the body entity is attached at the pick-up
animation's `Attach` event and released at the set-down's `Detach` event
(`AN:../humans/male/male.animevents`; the native constructor hashes the two
event names (dis)). The serialized state (`CarryCorpseModel`) holds the
victim's GUID and two flags, enslaved and attached (dis). The editor previews
attach the slave at the carrier's `entity_right` character attachment (data);
that the game does the same is **(inf)**. The body stays a full, animated
character — no proxy, no hiding.

**What the game records.**
* links `carriedBody` (carrier → body) and `bodyCarrier` (body → carrier),
  defined in code (reg XGenAIModule), read by the crime trees and the dead
  body's own switch;
* the perceivable state `carriedBody` on the player and `carriedBody_npc` on
  the body; the body switches its own perceptibility off while carried;
* crime: an observer who sees the carried body of a friend books murder or
  corpse violation (`isCarried`), barks, sends the AI signal
  `carriedBodySeenNotification` and links `crime_reactedToBodyCarrierInstance`;
  the crime reactions are keyed to the local player only (WO-139);
* moving a corpse NPCs have already found spawns a five-minute
  `crime_corpseMissing` volume at its first place (`SP:AI/npc/basic/switch/state_dead.xml:92-130`);
* the quest node `ActorCarryCorpseTrigger` (ports `Soul`, `IsActive`;
  outputs `OnCarry`, `OnDrop`, `Corpse`) — the carry quests' trigger;
* weight: the buff `carrying_load` and the RPG parameters for the carried
  body's weight and stamina (a perk halves them). **(data, reg)**

**The Lua surface.**

| purpose | call | status |
|---|---|---|
| is this actor carrying a body | `actor:IsCarryingCorpse()` | (doc, reg; used in the game's `TriggerBase.lua:419`) |
| can it put it down | `actor:CanPutCorpse()` | (doc, reg) |
| which body | `XGenAIModule.FindLinks(player.this.id, 'carriedBody')[1]` → `XGenAIModule.GetEntityByWUID` | (doc) for FindLinks; the code-defined tag is (live?) |
| make an actor pick one up / put it down | `actor:CanGrabCorpse(id)`, `actor:RequestGrabCorpse(id)`, `actor:RequestPutCorpse()` | (doc, reg); WO-119 observed it on an avatar |
| force the player to drop at once | `XGenAIModule.SendMessageToEntity(player.this.id, 'placeBody', '')` | (live?) |

**Limits.** While carrying a body the interaction filter `CarryCorpse` denies
doors, ladders, mounting, beds and chairs, minigames, takedowns, item carrying
and pick-ups (`GD:Libs/Config/interaction_filter.xml:79-98`); script triggers
refuse unless they allow it (`TriggerBase.lua:418-421`); no reading. Quests can
disable grabbing (`no_grab_body`) or the set-down (`disablePutCorpseAction`).
The carry is saved and restored on load (`CarryCorpseModel`, its PostSerialize
re-finds the victim) **(dis; live?)**.

## Kind B — holding a body after a stealth takedown

A stealth knockout or kill on an unaware NPC (`stealth_kill` / `knock_out`,
`BasicAIActions.lua:45-60`, `RequestStealthKill` / `RequestKnockOut`) ends in
the clinch: combat guard `stealthGrab` (`TB:combat/combat_guard_type.xml:16`),
idle fragment `CombatStealthClinchMaster`. From the hold the action map
`combat_stealth` offers "pick up body" (`combat_stealth_pick_up_body`:
`CombatStealthGrabMaster`, which attaches the victim with the procedural
`CombatAttachToEntity` and continues as kind A) or "drop body"
(`combat_stealth_drop_body`: `CombatStealthPutDownMaster`, the victim ends
knocked out in a ragdoll). Links `heldBody` / `bodyHolder`; perceivable
`heldBody` on the player. **This route does not run `OnGrabCorpse`**: a carry
that starts here is found by `IsCarryingCorpse` and the `carriedBody` link.
**(data)**

## Kind C — items: sacks, jugs, piles

**Start.** From a pile (entity `CarryItemPile`,
`SP:Scripts/Entities/WH/Others/CarryItemPile.lua:29-52`): "bag and take"
(`CIPileBind.CanPackAndPick`) or "pick up" (`CanPickUp`), action `use`,
interaction `inr_carryItem`, callback `CarryItemPile:OnPickUp` →
`CIPileBind.PickUp(user.id, pile.id)`. Off the ground (entity class
`CarryableItem`, `SP:Scripts/Entities/Items/CarryableItem.lua:8-25`):
`CarryableItem:OnPickUp` → `CarryableItemBind.PickUp`. Piles answer only while a
quest activates them as source or target. **(data, reg)**

**End.** Into a pile: `deposit_item` in `carry_item` → `CarryItemPile:OnDeposit`
→ `CIPileBind.Deposit`. On the ground: `put_item` ("drop"), native, no Lua;
quests can forbid it (`no_carryitem_put`). **(data)**

**The item.** It stays its pile's: the actor only borrows it (the native test
asserts say so) and holds it in the hand slot from the pick-up animation's
`Attach` event. Sack classes are `MiscItem` with manipulation type 11 (the
`sack_wearable` model); jugs are type 22. A dropped carryable lies in the world
inventory but is never saved there (reg EntityModule). **(data, reg)**

**Animations.** `CarryItemPickup` / `CarryItemPlace` (variants for wagons,
jugs, bag-and-take, strewing), then locomotion by the hand item's tag `r_sack`:
walk only (`TB:item/ItemManipulationType.xml:43-48`). The non-`player` variants
are what the NPC millers use. **(data)**

**What the game records.** Quest nodes `ActorCarryItemTrigger`
(`OnPickedUp`, `OnDeposited`, `OnDropped`), `CarryItemSource`,
`CarryItemTarget`; the shared quest module
`SP:Quests/Final/Barbora/utils/minigames/sackcarrying.xml`; dirt on the player
(an RPG parameter). **(data, reg)**

**The Lua surface.** The three callbacks above can be wrapped (they name the
pile or the item); there is no drop callback; whether `human:GetItemInHand`
returns the borrowed item is **(live?)**. For another actor: `human:PickUpItem`,
`human:AttachEntityToHand`, `human:DetachFromHand` **(doc, reg)**; the NPC
millers' sack is the NPC tool `sack_miller` (`TB:item/item.xml:1794`), which
walks with the same `r_sack` animations. **(data)**

## Kind D — quest carries

Body carries (kind A, mostly through `ActorCarryCorpseTrigger`): sixteen quests
under `SP:Quests/Final/Barbora/` — among them `trosecko/zranenyLovci` (a wounded
huntsman carried along a route, a living NPC), `kutnohorsko/sesivaniTonici`
(a living NPC; putting him down fails the quest), `kutnohorsko/papezskyLegat`
(a living NPC carried through water), `kutnohorsko/stealthMiseZaJindru`,
`kutnohorsko/damaVNesnazich`, `kutnohorsko/budovaniLazni` (a living NPC),
`trosecko/zachrana` (three corpses to a grave), `trosecko/kocovnickaCest` and
`sedmStatecnych2` (a body to a burial). Two carries are scripted joined
animations instead (`predaniVChramu`, and one prompt string not traced to a
node). Item carries (kind C): fourteen quests, among them `mlynaruvUcen` (sacks
of flour to a wagon — its objective type `CarryingBags`; the state the WO-147
field log called `carryingBags` is this quest's, not the fight club's),
`kejkliri`, `socky`, `poustevnik`, `naTroskach`, `nebakovObrana`,
`karelNesePytel`, `praceNaVinici`, `uchazec` (rubble), `rasuvUcen` (carcasses),
`stareKosti` (bones) and two jug carries (`setkaniVRatbori1`/`2`). The fist-fight
library pauses a knocked-out opponent's wake-up while it is carried
(`fist_fights_common_library/wakeupafterknockout.xml`). **(data)**

## Kind E — burial

In `trosecko/zachrana` (and the burials of `kocovnickaCest` and
`sedmStatecnych2`): a shovel, a hole (`HoleTrigger`, grave states from "can dig"
to "buried"), the body carried in (kind A; the grave area checks
`IsInActorState carryCorpse`), then a quest prompt "bury" (an
`InteractorOverride` hold) that kills the soul with `HideBody` and plays a
cutscene. The hole-digging minigame itself (`Hole.lua`, `Minigame.StartHoleDigging`)
is not a carry. **(data)**

## NPCs as carriers (could the avatar do it?)

* Bodies: the brain nodes `PickUpBody` / `PlaceBody` / `InstantPickUpBody` /
  `InstantPlaceBody` (reg XGenAIModule; behaviour trees only), an engine test of
  an NPC carrying a corpse, and the door tree's handling of an NPC with a carried
  corpse — the same native class, the same fragments. **An NPC can play kind A.**
  WO-119 made a ghost do it from Lua (`RequestGrabCorpse` on the avatar).
* Sacks and baskets: NPCs use **hand content** (the NPC-state planner's
  `HandContentElement`, `miller_carrySack`, `miner_carryBasket`), not the
  carry-item action. **(data)**

## What WO-148 shows, kind by kind

| kind | this player's game (detection) | the other screen (shown) | the host's world |
|---|---|---|---|
| A body | the `OnGrabCorpse` wrap names the body; `IsCarryingCorpse` polled at 5 Hz; the stealth route found by the `carriedBody` link, else the nearest dead or unconscious body; the set-down read after a 1.6 s settle | the carrier's avatar runs `RequestGrabCorpse` on this machine's copy, carries it while the stream moves the avatar, `RequestPutCorpse` at the set-down; the body is then moved to where the carrier's game left it (or back to its pick-up spot if that would be in the air or under the ground) | decides who carries; a joiner's carry moves the host's real body |
| B hold | the takedown is the host's (WO-135); a "pick up body" from the hold is kind A | as A | as A |
| C item | the pile / ground pick-up and deposit wraps, the `put_item` key | the avatar holds the game's sack model on its right hand (`Human.AttachEntityToHand`) with the game's pick-up / place one-shots; a dropped sack is a prop where it landed | no pile changes (the quest side) |
| D quest | as A and C | as A and C | what a quest counts is the next phase |
| E burial | the carry is A | the carry is A | the burial is the quest's |

## Open questions for the live game

1. Does `FindLinks(player, 'carriedBody')` return the carried body for a code-defined link tag?
2. Does `RequestGrabCorpse` work on every avatar (WO-119 saw it once), and what does `CanGrabCorpse` require (distance, facing)?
3. Does `RequestPutCorpse` leave the body where the carrier's game did, or does the avatar's own position decide?
4. Does `human:GetItemInHand` return a borrowed sack, and does hand content `sack_miller` on an avatar walk with the sack?
5. Do the carry walk fragments play on an avatar the native writer moves (gait by pseudo-speed)?
6. Does a host quest react to its real body arriving somewhere because a joiner carried it there?
7. Save and load mid-carry; a carried knocked-out body that wakes; a heavy hit on a carrying avatar.

**Answered live (WO-148 Stage B, the Modding Tools game; `docs/WO-148-findings.md`
section 4.4):** (1) yes for the player — the link names the carried body; none
for an avatar; (2) yes on both sides, when `CanGrabCorpse` allows it (false while
another actor holds or puts down the body); the carry is reported once the pick-up
animation ends, 3.5–4.4 s after the request; (3) at the avatar's feet, 0.5–1.6 m
from where the carrier's game left it; (4) `GetItemInHand` returns a null handle
for a borrowed sack; hand content on an avatar is refused on a host, while the
game's sack model attached by `Human.AttachEntityToHand` walks with the hand; (5)
yes, the carry pose and walk play on an avatar the stream moves; (7, in part) a
knocked-out body that wakes ends the carry itself and stands up. Open: (6) and
the rest of (7).
