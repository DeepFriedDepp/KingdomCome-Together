# WO-143 — Activities, part 2: findings

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios.

**Version: 0.42.0** (the maintainer's). Every gate is green, so the local
installer `release\KingdomComeTogether-Setup-0.42.0.exe` is built (details in
`docs/WO-143-progress.md`). No GitHub release. 0.41.7 stays the fallback,
bookmarked as the tag `v0.41.7` on `777b8c2`.

Evidence marks: **(observed)** seen live in the game, on consecutive frames of
the screen that matters; **(synthetic)** the real game against a synthetic
peer, or a console stand-in for a player's action; **(code-verified)** read in
the code or the game's binaries, or pinned by a unit test, but not run live;
**(inconclusive)** tried, no clear answer.

All live runs were solo on one machine (the Modding Tools game, 1.5.5), on a
throwaway copy of a save: the real game as the **host** with a scripted partner
(avatarpeer, runs H1–H2), and as a **joiner** of a synthetic host (synthpeer,
runs J1–J5), which streamed chosen townspeople at the places, with the
activities, tools, gaits and one-shots the real host had recorded in H1. No
input was sent. The maintainer watched J1 and reported the hoe's jitter; it is
fixed (§1.2).

## The answer

**The game places it; we only say what it is — and a paused copy's own
NPC-state machine has to run while it does.** The host reads, in the game's
own terms, what each tracked body holds and does: the tool in each hand, the
gait context, the one-shot it plays, who it looks at, and each player's
minigame. The joiner's copy gets the same item class, the same context, the
same fragment on the same body, and the game's own NPC-state machine shows it.
The copies are paused (WO-131), so their NPC-state machine does not run by
itself: the DLL runs its three steps while a one-shot plays, for two seconds
after a tool is taken, and all the time a copy hoes.

| piece (setting, default on) | what the other screen shows | evidence |
|---|---|---|
| **tools in hand** (`mp_hand_items`) | a standing or walking copy holds the host's tool: the field hoer her hoe (a temporary one from the mod when her copy owns none) | **(observed)** J1/J3 frames 3–5; H1 sent 1,669 hand messages (saws, hoes, a quill, axes and logs, tankards) |
| **activity gaits** (`mp_activity_gaits`) | the hoer hoes along her row at the host's own creep, bent over the hoe, and stands with it steady at the row end | **(observed)** J3 frames 3 and 5; the flicker the maintainer saw, before the fix, frame 4 |
| **one-shots** (`mp_oneshots`) | a seated copy plays the host NPC's one-shot and keeps its seat: a guest's drink, a dice player's reaction | **(observed)** J1 frame 6 (`Guest_DrinkBeer`, 8.8 s), J2 frame 7 (`DiceGameReaction`, 4.8 s); H1 captured 165 one-shots |
| **the players' minigames** (`mp_player_minigames`) | the partner's avatar plays the game's own minigame loop: grindstone (on the seat, where the game seats the player), reading, alchemy, herbs, lockpicking, digging, smithing; the loop stops when the minigame ends | **(observed)** the host's screen, H1/H2 frames 1–2 |
| **idles** (`mp_idles`) | standing copies idle and turn their heads to a nearby player **by themselves**; a forced look is never put on a paused copy (it swings the head and flashes the sheathed weapon) | **(observed)** J3/J4 frame 9 |
| **quieter logs** | a refused apply is logged on its first three tries, then once a minute with the count | **(observed)** `WO141-APPLY … (4 more tries since the last line)` |

**Not covered**, and why (§4 has every census row):

* **Tool trades at an object** (debarking, sawing, transcribing): the game's
  NPC-state search finds no path into them on the joiner's copy — with the tool
  in hand, with the object use's own tool fields filled, even from an empty
  state. Their tools live in the objects' holders, and on the joiner's copy of
  the world the holder still has its own. After three real refusals the copy is
  shown exactly as WO-141 shows it (it stands, the stream writes it again).
* **Seated copies' tools**: the game's path to a seated tool goes through
  standing — a guest given his tankard stood up and sat down again (frame 8) —
  so seated copies are never given tools. The tavern's tankards are the drink's
  own pickup from the table anyway: the drink plays, the tankard is not in the
  hand.
* **One-shots of standing copies the host's world paused mid-errand** (the
  innkeeper wiping a table): the paused walk holds the copy's NPC state, the
  request never starts, and after 15 s it is given up; the copy is unchanged.
* **Forced looks** on the joiner's copies (above).
* **Cart passengers following the cart**: the cart itself (`WHCart`) is not
  streamed; streamed cart horses walk off without it (frame 10, already so in
  0.41.7 whenever the host streams a caravan's horses). A cart stance now stays
  held on the joiner, so harnessed horses stay at their cart.
* **The grindstone's blade**: the avatar grinds on the seat, the partner's blade
  is not in its hands (the minigame's own item is not a hand item).

## 1. What each piece is, and what the live runs changed

### 1.1 Tools in hand (M3)

**Read.** The NPC-state context's current state (XGenAI object + 0x9C0,
C_NPCContext; current state + 0x90) keeps the hands in fixed slots: [1] left,
[2] right. A `C_HandContentElement` holds the item's WUID at + 0x28 and the
hand at + 0x48 (1 left, 2 right) — its GameLoad reads exactly those. The item's
class id is the game's own (`C_Item` vtbl slot 0x18: a Windows GUID in memory,
the text Lua's `ItemManager.GetItem(id).class` prints). The host sends a change
at once and a held tool again every 10 s.

**Apply.** WO-141's placement carries it: a `HandContentElementRequired` in the
loaded state beside the stance or the unstance, holding an item of that class
from the copy's own inventory. A copy that owns none asks the mod for a
temporary one (`inventory:CreateItem` in its own inventory; taken back once the
host's hands no longer want it). The object use names its tools too
(`C_UnstanceElement` + 0x60 right, + 0x68 left — its own text, "Hand items:
R: .. L: .."); they are filled from the hands.

**What the live runs changed.**

| found | change | evidence |
|---|---|---|
| a tool trade stays refused with its tool: "NPC state search failed: can't find a path from actions" (the carpenter's debarking, the sawyer, the scribe) | three real refusals (a tool still on its way does not count) and the tools are dropped: the activity alone, as WO-141 | **(observed)** J2/J3 logs; the scribe is refused even with no quill at all |
| a seated guest given his tankard: "Execution of 3 actions from load couldn't reach the loaded state in 9 updates", then "ForceIdleState clears 'sitting'" — he stood up, and the reconcile sat him down again | no tools on a body that sits, lies or kneels on an object | **(observed)** J2 frame 8; J3: "sits … its hands stay as they are", in step |
| a one-shot's request with an empty search state dropped the tool in the avatar's hand (H2) | before every one-shot (and its stop) the search state holds what WO-141 wants on the body, else what the body has now — the game's own requesters fill it the same way (the player's state handler, right before `RequestStateChange`) | **(observed)** H2; J1–J3: `keeping stance=sitting obj=…` |

### 1.2 Activity gaits (M5) and the hoe

**Read and apply.** The nine `actorCondition_*` entity contexts
(`forcedHoeing`, `forcedDrunkenness`, `forcedInjured`, `forcedArmored`,
`forcedFatness`, `fatness`, `forcedCrimeScanning`,
`forcedCrimeWatching_violent`, `_nonViolent`) through WO-68's context manager;
only contexts this DLL set are ever cleared (the store is refcounted).

**The host's hoer** (H1's recording): the hoeing context is on while she creeps
along a row — 4.5 m in about 74 s, 0.08–0.10 m/s in the 2 s rows a distant NPC
gets — off at the row end, a brisk walk back without it, on again.

**What the live runs changed.**

| found | change | evidence |
|---|---|---|
| at 0.08 m/s the gait writer reads standing (its walking floor is 0.1 m/s): the copy stood with its hoe, the context on | while the host shows a copy hoeing and it moves at all (> 5 mm/s), it walks, and the tags see 0.4 m/s along its motion (the pace J1 showed the hoeing walk at); the stream still places the body | **(observed)** J2 (standing) → J3 (hoeing, 12/12 frames, frame 3) |
| **the maintainer's report**: "Item jitter like crazy when she stopped moving with the Hoe". After any hoeing walk the hoe flipped between grips every frame while she stood; the context off at the stop did not help; putting the hoe away and taking it again cleared it | the paused copy's three NPC-state steps (the same ones a one-shot gets) run while a copy hoes and 3 s after; after a hand apply, 2 s | **(observed)** reproduced J1/J2 (frame 4); six seconds of those steps settled it on J2; J3: steady at the row end, twice (frame 5) |

The other eight contexts take the same path **(code-verified)**; none was seen
live (no drunk or injured NPC was tracked).

### 1.3 One-shots (M4)

**Capture.** A behaviour-tree one-shot is a `C_AnimAction` extra action on the
NPC-state change request (`S_NPCStateChangeRequest` + 0x48; the action's
fragment + 0x1A8, tags + 0x1B0, align object + 0x1B8), caught at
`C_NPCContext::RequestStateChange` (XGenAI + 0x1881090, an entry gate that
always runs the original). Tracked NPCs only, at most one per NPC per 1.5 s and
8 a second; this DLL's own requests (requester "KCDMP one-shot") are never
echoed.

**Play.** The request `PlayerStateHandler.PlayAnimationAction` builds for the
player: an anim action from the action database's template (+ 0x18CABA0), an
anim-event context (+ 0xDA6B20), the default required block (+ 0x506710), a
non-empty callback (an empty one makes the engine throw), interned text (a
reused buffer made a queued action play the wrong fragment). A paused copy's
NPC state is run by the DLL until the request's callback (its result: 0
failed, 2 done, 3 interrupted) or 15 s.

**What the live runs changed.**

| found | change | evidence |
|---|---|---|
| seated copies play it and keep their seat | — | **(observed)** J1 drink (ticked 220×), J2 dice (362×) |
| held for an aligned one-shot, the innkeeper walked off ~10 m under the tick (her own paused walk) and was snapped back | a copy is never held and plays its fragment where the stream has it (the host's game aligned the host's NPC there already) | **(observed)** J3 (the walk-off), J4/J5 (she stays; the request gives up after 15 s, quiet) |

### 1.4 The players' minigames on the avatar

No new message: WO-141's player rows already carry the minigame type and its
object. The avatar plays the game's own fragments, in order (entry, loop, out)
through the same request. The real player's minigame element holds no object
(an invalid WUID; `Grindstone5` has no WUID at all), so the loop plays where the
partner's stream has the avatar — where the partner's game seated the partner.

| minigame | on the avatar | evidence |
|---|---|---|
| 1 grindstone | `SharpeningMinigame` (tag `sword`): on the seat, foot on the pedal, hands on the wheel — when the avatar stands where the game seats the player (the entry's own alignment, computed from the game's table) | **(observed)** H2 frame 2 (the maintainer's note in H1 — the avatar on the far side of the seat — came from a guessed test position) |
| 2 reading | `ReadingBook` (seated: `sittingNoTable+book`, WO-141's seat kept) | **(observed)** H1 frame 1 |
| 3 alchemy, 4 herbs, 5 lockpicking, 6 digging, 12 smithing | `AlchemyIdle`, `PickingHerbs`, `LockpickingIdle`, `Digging`, `BlacksmithingAnvilIdle` | **(observed)** H1 frame 1 |
| 7 dice | `DiceGameIdle` (seated) | **(code-verified)** |
| 9 stone throwing | refused by the game on an avatar: it stands | **(observed)** H1 |
| the end | the out fragment, then the loop stops; a stuck one is released after 6 s | **(observed)** H1 |

### 1.5 Idles and looks

**Read.** The NPC object's look target (`C_NPC` vtbl[0x210] → + 0x1C0,
`C_NPCLookTarget`: + 0x30 the kind — 0 none, 1 an entity, 2 a point — + 0x38
its WUID), near players only (20 m), twice a second, at most 6 changes a second.
Sent as nobody / the host's player / a player's avatar / an NPC by name.

**Apply — not on the joiner's copies.** `actor:SetForcedLookObjectId` on a
paused copy swings its head between frames and flashes its sheathed weapon —
for the local player, an NPC and the host's avatar as the target, with the NPC
state run, and re-applied every frame. Cleared, the copy is steady. A copy the
host drives (a puppet) is therefore never forced to look; the look is cleared
if one was set. The copies' own game still turns their heads to a nearby player
(J4, frame 9). The host's rows cost little and stay (a copy that is not paused
would be forced).

### 1.6 Carts

The caravan H1 saw: a driver in slot 1 and two horses in slots 7 and 8 of one
cart (`WHCart[civilianCaravanFarmersB_cart1…]`, with its wheels, parts and a
mount point). Streamed horses are written; the cart is not streamed: the horses
walk off, the shafts stretch across the road, the cart and its driver stay
(frame 10). That is how 0.41.7 already shows a streamed caravan. WO-143's part:
a cart stance stays held on the joiner even when the game refuses it, so a
harnessed horse the host sends as a cart horse stays at its cart instead of
being written away. Following a moving cart needs the cart's own transform on
the wire — not in this work order.

## 2. The writer's hold, revisited

WO-141 holds the position writer off a body whose activity owns its place, from
the first try. For an activity the game keeps refusing, that left the copy
wherever the stream had left it — and free, so a forced look turned it bodily.
Now: held while in step or while it is tried; after the fourth miss (when the
retries back off to 15 s) it is written again, and held once more for each
retry. Cart stances stay held (§1.6). **(observed)** J3: the refused carpenter
keeps the host's heading.

## 3. The flow

| step | host | joiner |
|---|---|---|
| read | every 250 ms (WO-141's tick): hands, gaits; looks near players; one-shots as they are requested | — |
| wire | `ActivityExtra` 0x6E up / 0x6F down on the WO-123 join channel, host to joiner only (the relay's JoinWire row); kinds 1 hands, 2 gaits, 3 one-shot, 4 looks | — |
| apply | — | hands: WO-141's reconcile (with its stance / unstance, or alone); gaits: the context manager; one-shots: the request, run under the DLL's tick; looks: skipped on puppets |
| settle | — | the three NPC-state steps: during a one-shot, 2 s after a take, while hoeing + 3 s |
| avatars (both roles) | each player row with a minigame → the avatar's entry / loop / out | same |

## 4. Coverage against the census (`docs/WO-141A-activity-census.md`)

The rows WO-143 changes; every other row is as WO-141 §4 has it. "Shown" = the
other screen shows it; "as WO-141" = unchanged from 0.41.7.

| census row | WO-143 | evidence |
|---|---|---|
| **field hoeing** (M3 + M5) | **shown**: the hoe in hand, the hoeing walk at the host's creep, steady at the row end | **(observed)** |
| vineyard hoeing, weeding, grave digging, sweeping, carrying water or a sack, fence repair | the tool rides a standing body (the class the host names); the activity as WO-141 | **(code-verified)** (the hoer's path) |
| carpentry / debarking, sawing, chopping, felling, mining, forge, butchering and the other trades with a tool at an object (M2 + M3) | covered, **refused by the game**: after three tries shown as WO-141 (the copy stands); the tool is the holder's on the joiner | **(observed)** carpenter, sawyer (J2/J3) |
| scribe / transcribing | covered, refused (even without the quill) | **(observed)** |
| guests drinking / eating seated, dice (M1 + M4) | the one-shots **shown** seated (drink, dice reaction); the tankard, the bowl not in the hand (the game's own table pickup) | **(observed)** drink, dice |
| bartender serving / cleaning (M4) | covered; a standing copy paused mid-errand does not start it (quiet) | **(observed)** innkeeper (J4/J5) |
| chopping block, stoking a fire, a trough drink, a dog's howl, bells (M4) | covered, the same path | **(code-verified)** |
| drunk, injured, armoured, fat walks; crime scanning / watching (M5) | covered, the same path as the hoe | **(code-verified)** |
| halberdier's halberd, a merchant's goods, ropes (M3) | covered for standing bodies | **(code-verified)** |
| head turns, looking at someone (idles) | **not forced on the joiner's copies** (the game's forced look swings a paused copy's head and flashes its weapon); the copies turn their heads by themselves | **(observed)** |
| cart driver / passenger | the slot travels (WO-141); a refused cart stance stays held; **following the moving cart: not covered** (the cart is not streamed) | **(observed)** the stretch (J3); **(code-verified)** the hold |
| the player's grindstone, reading, alchemy, herbs, lockpicking, digging, smithing | **shown on the avatar** | **(observed)** |
| the player's dice | the loop, seated | **(code-verified)** |
| the player's stone throwing, archery, pickpocketing, distraction, forge building | not shown: the avatar stands (refused, or no body loop) | **(observed)** stone throwing |

## 5. Tried, and not used

* **The object use with its tools filled, from an empty state** (§1.1): still
  no path.
* **The tool first, then the object use** (two steps): the tool is taken, the
  object use still refused.
* **A forced look re-applied every frame**, and with the NPC state run: the head
  still swings (§1.5).
* **The walking class alone for a creeping hoer**: the tags read no velocity and
  the body stood (§1.2).
* **Holding a copy for an aligned one-shot** (§1.3).
* **The partner's blade in the avatar's hand for the grindstone**: a hand apply
  on a body in the minigame loop ends the loop; before the loop, the request's
  own search dropped it (H2) — the minigame's item is not a hand item.

## 6. Defaults, and why

All five settings default **on** (the maintainer's decision). Each piece fails
quietly when the game refuses it — the body looks as it did before (standing,
or the plain idle) — and every refusal the runs found is handled that way:
three tries then WO-141's activity alone; no tools on seated bodies; a request
given up after 15 s; no forced looks on paused copies; cart stances held.
Switchable live from the console: `mp_hand_items`, `mp_activity_gaits`,
`mp_oneshots`, `mp_player_minigames`, `mp_idles` (`on|off`), and
`mp_activity2_status` prints the state. Off puts tools away, clears the
contexts this DLL set, stops the avatars' loops and clears forced looks.

## 7. Tests

Native unit tests 289/289 (`wo143_rules_tests`: hands, the gait plan, the
quieter log, the one-shot throttle, the look budget and target kinds, the
refresh, the hoer's tags, the writer's hold, tools and seats); agent unit tests
622/622 (`Wo143Tests`: the wire, the frames, the minigame mapping and its steps,
temporary tools, looks, quiet copies, every setting); relay round trip 57/57
(hands, gaits, one-shots and looks cross host to joiner only);
`Test-WO143Synthetic` 63/63 (the Lua: temporary tools, the five switches, the
looks — including the puppet skip — and the status); every other synthetic
suite and both static checks; the payload smoke.

## 8. Open

* Following a moving cart: the cart's own transform on the wire (a cart stream
  like the NPC stream), then the riders ride it.
* Tool trades at an object: the holder's own item (a world item) would have to
  be the copy's, as it is the host NPC's.
* Forced looks on paused copies: the look target component itself
  (`C_NPCLookTarget`, the host's own values) instead of the scriptbind's forced
  look — not tried.
* A standing copy paused mid-errand could have its own walk cancelled before a
  one-shot.
* Two machines: `docs/TEST-0.42.0.md`.

## Frames (`docs/wo143-shots/`)

1. `1-host-screen-avatar-minigames.jpg` — the partner's avatar reading, at
   alchemy, picking herbs, lockpicking, digging, (stone throwing: stands),
   smithing (H1, three frames each).
2. `2-host-screen-avatar-grindstone-at-the-seat.jpg` — standing where the game
   seats the player, then on the seat grinding (H2).
3. `3-joiner-hoer-hoes-at-the-hosts-creep.jpg` — J3, 0.08 m/s with the hoeing
   context.
4. `4-joiner-hoe-flips-after-the-walk-before-the-fix.jpg` — J1, the jitter the
   maintainer saw: four consecutive frames, four grips.
5. `5-joiner-hoe-steady-at-the-row-end.jpg` — J3, the same stop after the fix.
6. `6-joiner-seated-guest-drinks.jpg` — J1, `Guest_DrinkBeer` on a seated copy.
7. `7-joiner-dice-reaction.jpg` — J2, `DiceGameReaction` at the dice board.
8. `8-joiner-guest-stood-up-by-a-tankard-why-no-seated-tools.jpg` — J2, the
   seated guest given his tankard as a tool: up, then down again.
9. `9-joiner-forced-look-flashes-the-sword-then-skipped.jpg` — J3 a forced look
   (the sword flashes in and out), J4 skipped (steady).
10. `10-joiner-cart-horses-walk-off.jpg` — J3, streamed cart horses leave their
    cart.
