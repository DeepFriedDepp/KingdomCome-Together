# WO-217 — The world around the bodies: loot, flags, corpses, props, carts — and the last of the activity gap

Version **0.64.4** (the maintainer's). Measured on the bench (two copies of the game on one PC, joined through our own relay with the
field's link shape: 246 KB/s, 26 ± 10 ms each way, 0.5 % loss, two stalls a minute). Every new mechanism has its own switch
(`mp_w217_status` lists them); each part's twin is the same build with that switch off (`KCDMP_TEST_W217_OFF=<names>` for both agents and
both games), the instruments kept on. Nothing built here reads input: `tools/Check-NoInputCapture.ps1` passes on every binary and script,
test tools included, first and last (below).

Roles only: "the host", "the joiner". "A copy" is the joiner's local body of one of the host's NPCs; "the writer" is the joiner's
per-frame position writer (WO-118); "the owner" is the joiner's animation owner of copies (WO-208).

**Part 0's two answers (the shipped 0.64.3, every default as shipped).**
- **0.1 — talks, trades and forced dialogues: they FAILED for the joiner.** Each conversation started within half a second, then hung
  before its first choice: 0.64.3 hides and freezes the game's own conversation actors (its dialogue twins) on the joiner as if they
  were bodies the host does not stream. The resume rule itself held (every talk gave its copy its brain back; none was refused).
  Fixed (`mp_dialog_twins`): with the fix the joiner's talk reaches its choices and the trade screen; with it off (the twin) it hangs
  again.
- **0.2 — seated guests in the tavern: they FAILED.** 107 single-frame pulls over 50 cm in 10 minutes on 10 seated or seating copies —
  the copy's own seat and the stream each writing the body in turn. Fixed in part (`mp_seat_writer`); the bar (no pull over 50 cm)
  is not met — the numbers and what is left are in 0.2 below.

## Part 0 — the shipped 0.64.3, checked

**How it was installed.** The bench cannot run Setup.exe itself (it would install into the real game and the redirected per-user
folders), so the installer's own payload — the folder `Build-Installer` packed into `KingdomComeTogether-Setup-0.64.3-9dfc03a-2611100119.exe`
— was checked file for file against the installer's install manifest (1,038 files, every sha256 equal) and run on the bench pair as it
is: the DLL, the agent and the relay of the payload, the mod's pak of the payload, every default as shipped.

**0.2 — seated guests, 10 minutes in the tavern (the joiner 2.5 m from the innkeeper, 17 host NPCs within 18 m): FAIL on the
shipped build.** 107 pull windows over 50 cm (a single frame moved a copy more than 50 cm), on 10 bodies; the worst two 45 and 37
windows. By what the copy and its host NPC were doing at the time: the host NPC in a conversation 34 (16 of them seated), both
seated 5, the host NPC seated and the copy standing 4, other or moving 10, and 54 with no probe row near (the copy not sampled then). 30 drift hand-backs
(the writer giving a body back to its own engine) in the same window. Who wrote each side: the stream's writer pulls the copy to the
host NPC's place; the copy's own engine puts it back on its seat (its own NPC-state seat, kept by `copy_neutral_always`); the
hand-back lets the engine win until the writer takes it again — two writers, each side every few seconds.

**0.2's fix (`mp_seat_writer`): one writer for a seated copy.** While the host's line says its NPC sits, the seat holds the copy and the
stream never places it — one write first when the copy is over 2.5 m from its host NPC's seat, and no more re-placing once two
placements did not stick (its own sit took it back to its own seat); a copy still sitting while its host NPC stands is released the
game's way (the owner's release), held still at most 4 s. The tavern again, 10 minutes, the same scene each time:

| | pull windows over 50 cm | what most of them were |
|---|---|---|
| the twin (every WO-217 switch off) | 150 | no probe row near 107; standing 22; seated both 5 |
| the first rule (seat holds only a copy already in a seat) | 455 | a copy standing beside its seated host NPC, dragged by the stream (253); a copy still sitting while its host NPC stood (105) |
| the rule above, before "stop re-placing" | 139 | one copy placed every 5 s and taken back by its own sit, 6 m off (107 of the seat pulls) |
| the rule above (the build that ships; the first all-on acceptance run, its tavern window with the joiner's queue clean) | **111** | no probe row near 83; moving or standing 9; other 8; a conversation 7; seated both 1 |

The WO's bar — no pull over 50 cm in 10 minutes — is not met by any of these: 150 in the twin, 111 with the rule that ships. (The
later acceptance runs' taverns ran under the bench's queue — the acceptance scenes, below — and measure the queue.)

**0.1 — a talk, a trade and a forced dialogue by each player: FAIL on the shipped build, for the joiner.** The resume rule itself
held: in each of the joiner's talks the copy got its brain back for the conversation (7 talks, 7 `WO216-EXEMPT why=resume`,
0 `WO216-NORESUME`), and the game started each conversation 0.3–0.4 s after the press. But the conversation then stalled before
its first choice: WO-216's spawn watch takes the game's own **dialogue twins** — the stand-ins the engine spawns for every
participant of a conversation, `DialogTwin_<npc>` and `DialogTwin_Dude` — for bodies the host does not stream, and hides and freezes
them in the frame they are made (`WO216-LOCALSPAWN npc=DialogTwin_ttkc_woman_2 ... hidden this frame`). The conversation's partner
is then a hidden actor ("Skipping playing animation on hidden actor DialogTwin_…"), no dialogue camera is valid, no choice is ever
offered, and the open conversation blocks the next one (the forced dialogue: "not played during interactive scene"). The host's
conversations reach their choices and the trade screen. **Fix:** `mp_dialog_twins` (on by default) — the engine's dialogue twins are
never hidden, frozen or counted by B; off is the shipped rule (the twin).

| 0.1 on the bench | the shipped 0.64.3 | the fix (`mp_dialog_twins` on) | the twin (the fix off) |
|---|---|---|---|
| the joiner's first conversation: started after the press | 0.3–0.4 s | 0.4 s (the innkeeper's trade) | 0.3 s (a merchant) |
| … it reached its choices | no: stalled, the twin hidden | yes | no: stalled, the twins hidden again (2 `WO216-LOCALSPAWN npc=DialogTwin_…`) |
| the joiner's trade screen | never | open (the shop option chosen) | never: all four merchants refused while the first talk hung |
| the joiner's forced dialogue (the bailiff): started | blocked by the stalled talk | 0 s, the bailiff's 14 choices shown | blocked |
| the host's trade / forced, for comparison | the screen opened; forced started | 0.07 s, the screen open; forced started (0 s) | the same |

A plain villager's talk could not be staged on either machine: the bench save's Henry is greyed out with the villagers in the host's
world (`IsDialogRestricted`, the same on the host's NPC and the joiner's copy — read on both right after a join), so the bench plays a
merchant's conversation to its goodbye instead; the acceptance run's scene 1 repeats all three with the fix on. The forced call the
bench makes is `DialogModule.ForceDialog` on the nearest plain villager: the game picks one of its own forced dialogues (a surrender or
a threat) and ends it itself, on both machines. The bench first asked it of the village's bailiff; on the host that played the bench
save's own waiting quest scene (a fast travel and a story conversation with a forced choice that never closed), so a quest giver's
forced dialogue is the field's, not the bench's.

## A — the activity gap (`placement`)

**A.0 — every attempt measured, one line each.** The owner now writes one line per activity the host's NPC holds (`WO217-PLACE`): the
posture family and fragment, how long the host held it, the outcome — placed (and how fast it showed), placed then lost (and to what),
refused (the engine's reason), deferred, paced (the queue full, and what held its head), held (a one-shot, its own attack, a row),
superseded, not tried — and the planner's own state (WO-141's path, the neutral rule, the distance to the activity's object).

The first table (the smith's house, the bench's village, one run; the first fix of A.1 already in — below), per window, of the
activities the host's NPCs held 2 s or more:

| window | held ≥ 2 s | shown within 1 s | the misses, by reason |
|---|---|---|---|
| day (4 min, the smith at his forge) | 28 | 9 (32 %) | the copy's queue full, its own never-started look (6) or movement (6) action at its head; not tried 4; refused here with no queue 2 |
| midday (2 min) | 9 | 7 (78 %) | queue full under its own look action 1; refused here 1 |
| night (2 min, the house asleep) | 99 | 84 (85 %) | queue full under its own look (7) or movement (3) action; not tried 5 |

**The top reason, the same in every window:** a copy's action queue held full (WO-209's cap) by its own look or movement action that
never started — the frozen copy's leftovers never run, so the queue never drains, the owner's install never starts, and after two tries
WO-215's watchdog refuses the activity for 30 s ("refused here").

**A.1 — the fixes, each under `placement`:**
1. *The NPC-state deferral (from WO-216's acceptance: 247 of 247 reseats were path 24).* With the neutral rule on, a copy's
   NPC-state placement never comes; the owner no longer leaves a posture copy to it — it installs the host's activity itself.
2. *The clogged queue (from A.0's table above).* When the owner must install and the queue is full, it first stops the copy's own
   never-started look and movement actions — the game's own stop (`IAction::Stop`), never one of ours, never a running one, at most
   once a second per copy (`WO217-UNCLOG`); the install follows on the next pass.

**After both fixes** (the same scene, a later build with both on):

| window | held ≥ 2 s | shown within 1 s (the WO's bar: 90 %) | before the second fix | the misses left |
|---|---|---|---|---|
| day | 40 | **34 (85 %)** | 32 % | never shown 4: the smith's forging loop (3) and a cooking pot (1), installed twice and refused — work bound to an object the bare install does not use; shown late (over 1 s) 2 |
| midday | 30 | **25 (83 %)** | 78 % | shown late 3; never shown 2 (held for a one-shot; queue full under a facial expression); and 5 shown in time, then taken over by the copy's own later action (a scripted serve-beer callback, a leaning variation ×3, an idle movement) |
| night | 78 | **77 (98.7 %)** | 85 % | shown late 1; 2 sleepers shown in time, then taken over by an idle movement of their own |

The night window meets the bar; day and midday do not. The second fix stopped the copies' own stale actions 9 times in the run
(`WO217-UNCLOG`, two each: a movement and a look). What is left, by reason: **object-bound work loops** (the forge, the pot — the
activity needs its smart object's use, which the owner's bare install does not make; the NPC-state placement that would make it is the
path the neutral rule keeps closed) and **the copy's own later actions taking a placed activity over** (a callback, a variation, an
idle movement). Neither has a fix in this build; both are named in the bug report's line.

**The twin** (the same scene, every WO-217 switch off — the shipped rules), A.0's table without any fix:

| window | held ≥ 2 s | shown within 1 s | the misses, by reason |
|---|---|---|---|
| day | 46 | 18 (39 %) | deferred to an NPC-state placement that never came 12, and the same with the queue full under the copy's own look (11) or movement (3) action; held 2 |
| midday | 60 | 46 (77 %) | deferred 5, deferred with the queue full under its own look (5) or movement (2); placed then lost 4 |
| night | 97 | 60 (62 %) | deferred 15, deferred with the queue full under its own look (11) or movement (10); placed then lost 3 |

| A.0's per-attempt measure: shown within 1 s | the twin | every switch on |
|---|---|---|
| day | 39 % | **85 %** |
| midday | 77 % | **83 %** |
| night | 62 % | **98.7 %** |

**The WO's bar, on the WO's own measure** (the probe that gave WO-216's twin numbers: of the activities the host's NPCs held 2 s or
more, the share the copy showed within 1 s; the seconds an activity was missing on the joiner), per window:

| | the bar | every switch on | the twin (this run) | WO-216's twin |
|---|---|---|---|---|
| shown within 1 s — day / midday / night | ≥ 90 % each | **100 % / 100 % / 100 %** (23, 19, 12 activities) | 37 / 66 / 88 % (30, 32, 24) | 73 / 52 / 100 % |
| activity missing — day / midday / night | ≤ 208 / 80 / 11 s | **4.2 / 1.2 / 31.5 s** | 333 / 196 / 0 s | 395 / 235 / 9 s |

The shown bar is met in all three windows; the missing-time bar in day and midday, not at night (31.5 s against 11 s; the night's A.0 lines
show two sleepers taken over by an idle movement of their own). A.0's per-attempt measure is stricter (every attempt, including the 30 s refusals of
the smith's forge) and is the table above.

**In the acceptance's order** (the players arrive at the smith's house mid-activity, after the road and the talks) the probe has no
copies to read: it counts a body as a copy only while the position writer has it bound, and a copy standing in an activity is the
owner's, never bound. There the measure pairs each joiner row with the host's own row of that NPC on the one PC clock (checked against
the probe on the A runs above: 100 / 100 / 50 % for 100 / 100 / 100 %, and 40 / 67 / 74 % for 37 / 66 / 88 % in the twin): **67 / 72 /
20 %** with every switch on, 57 / 46 / 22 % in the twin (whose queue was up to 106 s behind) — the acceptance scenes, below.

## B — the small carries

**B.1 (`ghost_far`).** WO-215's ghost fight ended when no opponent of this player was near the ghost; a real opponent fighting the joiner
from 42 m away (the host's bandit, held off on purpose) ended it while the fight was still on. Now the joiner's own list of the host
NPCs whose fight target is this player's figure (the host's opponent rows, `MP-W209`'s) counts at any distance; the ghost fight ends
only when none of them is alive, streamed or not. **On the bench** (WO-216 H's twin case, staged: a bandit of a camp next to the
players becomes the ghost, a second one fights the joiner's figure from 48 m — 53 m on the joiner's side — held there): the joiner's
fight was **kept for the whole 30 s** with `ghost_far` on. The twin's run was not a clean twin: a third bandit stood within 15 m, and
WO-215's own rule keeps a fight while anyone living is near; the scene now clears the players' surroundings first (the acceptance's
twin repeats it). The acceptance could not stage it either (in both runs the game's own combat ended the joiner's fight after about
3 s, before WO-215's ghost fight began); B.1 stands on the run above.

**B.2 (`kd_follow`).** A copy knocked down got up on its own engine's 4.5 s timer while its host NPC still lay. Now the copy's ragdoll
manager's own stand-up flag (`C_RagdollManager +0x80`, read off `CanAutoStandUp`'s code when the DLL arms) is held while the host NPC
is down; a copy its engine stood up anyway is laid down again the game's way (`RequestRagdoll`, at most 12 times, 0.8 s apart); the
get-up is the host's up edge only. On the bench (the A run's brawl, b7): two bandits knocked down for 5.2 s and 4.4 s; each copy fell
0.15 s and 0.10 s after its host NPC and got up **68 ms and 86 ms after the host's own get-up** — none before; the engine stood one copy up
early once and it was laid down again (`WO217-KD refall`). A second run (the final rules): two more knockdowns, get-ups **109 ms and
1,111 ms** after the host's own — the second over the WO's 300 ms (the get-up waits for the host's up edge on the stream; why that
one came late is not known); none before.
The first all-on acceptance run (its brawl with the joiner's queue clean): two more, **66 ms and 75 ms** after the host's own; none
before. **The twin** (`kd_follow` off): one copy got up **2.4 s before** its host NPC (its own engine), the other 72 ms after.
The suppress flag was found by its code shape in this build (one match).

**B.3 (`long_names`).** A name over 63 characters (the animal care's horses) could not be carried: the stream's name field is 64 bytes.
Now both ends use the same short alias for such a name (`lh` + two 16-bit sums of its bytes) on the wire, the DLL's name read and the
mod's lookups translate it back; the wire format is unchanged (no protocol bump). Unit-tested (the same alias on both ends, a
console command with the alias runs on the real name). **On the bench: not shown.** Both players stood 8 m from an animal-care horse
for 40 s with `long_names` on in both DLLs; no line on either machine named its alias, and the joiner's body of it could not be
compared — B.3 is built, not proven.

**B.4 (`layer_watch`).** Bodies a layer switches on were running until the 30 s full scan found them. Now the entity system's own
`EnableLayer` (found by its own log line, a vtable slot) is watched: a layer switched on starts a full census at once and each second
for 30 s (a first run showed a village's bodies starting 16 s after their layer came on); a body that appears during a layer's watch
and runs is caught as the layer's (`WO217-LAYER found ... at a layer's activation`), suspended. **On the bench** (35 min, 11 layers on):
nothing ran from a layer. The census found running brains 5 times in 2,142 checks, none of them a layer's: 8 far villagers of a
village a game script woke at once (already listed bodies, all beyond 150 m), a wandering beggar, two animals of the wildlife spawner, one
villager — each suspended again within the second. The WO's "0 found by the full scan" is not met; what is left is not layers.
**The twin** (every WO-217 switch off): running brains in 146 of 2,081 checks (with them on: 5).

## C / D — one loot, one stolen mark (`loot_live`, `item_flags`)

**C.1 one inventory, pushed live.** While a body's loot screen is open on either machine, the host's world is the body's only
inventory: the host sends the body's list (every item's host id, class, amount, condition, worn, owner, stolen) whenever it changes, and
the joiner's copy is brought to that list by moves only (an item made in a hidden escrow stash and moved in, an item gone moved out to
it) — nothing is deleted under an open screen (WO-166's crash). The game's own screen follows a move in place.

**C.2 a take is a request.** The joiner's take, take-all or equip-from-body moves the item into the hidden escrow and asks the host; the
item enters the joiner's pack only when the host grants it. The host grants the first asker (the item moves from the body into the
host's own escrow, keyed by the asker and the request) and refuses the second ("gone"): the refused joiner's escrow copy is removed,
the row disappears and the screen shows "Already taken"; nothing is equipped or created. A request with no answer in 5 s is cancelled:
the joiner removes its escrow copy and the host puts its escrowed item back into the body. The host's own takes go through the same
ledger (as "the host" asking itself).

**C.3 the ledger, both sides.** Every request, grant, refusal, cancel, removal from the body and application is a `WO217-LEDGER` line
with the host's item id; the bug report's tool balances granted = applied = removed per item and names any item that is not.

**C.4 the body's look.** A worn item taken off the body on the host is taken off its copy on the joiner (the list's worn flag; WO-216's
outfit rule dresses the rest).

**D item flags.** The list carries each item's owner and stolen mark; a granted item keeps them in the joiner's pack, a drop keeps
them, and a stolen item the joiner carries is judged by the host's world (WO-195B's joint crime).

**On the bench** (the save's dead villager, 11 items; both loot screens open — the joiner's 0.52 s after it asked; the host takes 3 and
the joiner 3, one by one in turns, and twice both take the same item within 200 ms):

| the ledger (both sides, by the host's item id) | |
|---|---|
| items taken off the body | 8 |
| granted to the joiner / applied in the joiner's pack | 3 / 3 |
| the host's own takes (through the same ledger) | 5 (its 3 and both contested items) |
| refused ("Already taken" on the joiner's screen) | the joiner's 2 contested asks |
| cancelled (no answer in 5 s) | 0 |
| **duplicated / lost** | **0 / 0** — every item balanced (granted = applied = removed) |

**C.2:** each contested item has exactly one owner (the host both times; the joiner's request was refused and its row removed).
**C.1:** the other's open screen lost each taken item 2.1–3.1 s after the take *as the bench reads it* — every read is a console round
trip of a second or more, so the in-game refresh (the WO's 300 ms) is neither shown nor refuted. **D:** the bench's staging of three
stolen items did not take (0 marked), so the stolen mark on the wire is unit-tested only; every taken item carried its owner (the
taker) after the take on both machines.

**The acceptance's scene 5** (the same body, the build that ships): 8 items taken off it; granted 3 = applied 3; the host's own 5;
refused 4 ("Already taken" on the joiner's screen, framed); cancelled 0; **duplicated 0, lost 0**.

**The twin** (`loot_live` and `item_flags` off — WO-134's loot as shipped): no ledger at all, and the other player's open screen kept
every taken item for the whole 1.5 s watched (6 of 6 takes); with the switches on it lost every one.

## E — what an encounter leaves (`encounter_world`)

**E.1 corpses.** The host lists the dead within 100 m of its player and each partner's figure every 2 s (a change goes out at once). The
joiner applies each death the existing way (the owner-death apply), keeps the body out of WO-216's hiding, shows it and lays it where
the host's lies; a body this world never had (an encounter's own dead) is made dead under the host's name (WO-131's stand-in, created
dead the WO-137 way).

**E.2 props.** The host lists the props its engine spawned at runtime near the players (the engine's own "spawned" flag; basic and
rigid entities, campfires, cart stashes, dead horses); the joiner makes the missing ones under the same name (class, archetype, model,
the host's pose), never saved, and removes its own when the host's list drops them.

**E.3 the searcher.** A copy searching a body plays the host's own action on the stream's body (WO-200's neutral copies); what it
searches is E.1's corpse or E.2's prop, now lying where the host's lies.

**On the bench: E is not proven.** The game would not start a road encounter on demand (the robbery by its place override, the
players 40 m and then 180 m from the place, 4 and 5 minutes: no line of it in the host's log). E.1 ran on the save's own dead villager:
the host listed him every 30 s, the joiner logged him shown dead in place (`WO217-CORPSE ... here=shown dead=true`), at once and after
12 s — but he is dead in both worlds from the save, and the twin (`encounter_world` off) shows him the same. The cases E exists for (a
body killed out of the joiner's range, an encounter's own dead and props, a robber searching them) are scene 7 of the field test.

## F — carts (`carts`)

Measured first (a live census of the bench save, where an armed caravan is already on the road in both worlds under the same names):
the joiner's carts drove their own route — 470 m from the host's after ten minutes — while their soldiers' copies followed the host's.
Now the host lists its carts within 150 m of the players four times a second (position, angles, its riders); the joiner's DLL plays
each one back 0.35 s behind the host's clock, interpolated, and writes it every frame with the call the NPC writer makes — the host's
height as it is, never the ground writer. A cart the host stops naming for 3 s is its own engine's again.

The cart that is moved is the cart's **top-level body**: a `WHCart` is a child of its wagon (`frontPart[...]` / `backPart[...]`), and the
position-rotation call sets a child's local transform — the first bench run wrote the child and put the joiner's carts 3.4 km away and
hundreds of metres underground. Now the host names the topmost body and the DLL never writes an entity that has a parent.

**On the bench** (the save's armed caravan, both players 20 m beside its path, the joiner's mod measuring each cart within 80 m twice a
second, 209 samples over the pass):

| the bar (the WO's) | measured |
|---|---|
| the cart within 10 cm of the ground | 209 of 209 (the highest 0.10 m; the host's own cart 0.12 m) |
| within 1 m of the host's cart | 209 of 209 (the farthest 0.95 m, p95 0.61 m — the 0.35 s playback delay at walking pace) |
| its riders seated on it the whole pass | 407 of 407 rider-samples |

**The acceptance's scene 7** (the build that ships, the same caravan): 252 samples — within 1 m of the host's cart 250 (the farthest
1.26 m, p95 0.60 m), within 10 cm of the ground 251 (the highest 0.12 m), riders seated 494 of 494; the pass framed at about 20 m (the
same cart and horse in both views).

**The twin** (`carts` off): the joiner's cart drove its own route, 57–61 m from the host's (0 of 5 samples near the players within 1 m;
in the acceptance's twin 128 m); its height was right (its own physics on the ground).

**The horse: not proven.** Nothing here hitches a horse; the frames disagree between runs: in the first carts run the joiner's cart
stood without its horse, in the acceptance's pass the joiner's cart has its driver and both horses in front as on the host (the same
arrangement in both views). Whether they are attached the engine's way (walking in the harness) was not measured; the WO's "horse
attached the engine's way" is open for the field (scene 8).

## The joiner's queue in a busy village (`route_skip`)

Found by the acceptance runs, not asked by the WO: from the joiner's first conversations in the village (10:00, the bench's busiest
scene) on, the joiner's agent ran **30–106 s behind** the host's stream in the twin (every WO-217 switch off: the shipped 0.64.3) and up
to 368 s in an all-on run on the same build (`MP-W207-Q lag_ms`; the game's own `WO157-CONN ... 31603 ms behind`). The queue was the
host's route lines (WO-216 A, message 0x85: 500–1,400 waiting) and the NPC animation state behind them (0x7B). The host sends a route
line for every changed movement request of its streamed NPCs, about 25 a second in the village, whenever WO-216's `route_replay` is on
(it is); the joiner hands each to its DLL — one pipe round trip of about 32 ms each, one at a time — and with `mp_route` off (as it
ships, WO-216) nothing in the DLL reads them: only the route replay and the horse replay do. So the pipe was full of lines no one used,
and every stream behind it was late: the joiner's copies of the village's people arrived after the joiner's talk was asked (the game
cancelled it), a dropped item showed late. The short A runs (the smithy right after the join) never filled it.

**Fix (`route_skip`, the agent's own switch, on):** the joiner drops a route line while its own `mp_route` is off; with `mp_route on` (or
`route_skip` off — the twin) every line goes in as in 0.64.3. No engine call, no DLL change.

**On the bench, the acceptance with the fix against its twin:** the joiner's queue at its worst in the talks 3.3 s (the twin 104 s),
at the smith's house 11.7 s (106 s), on the road and at the loot, the drops and the doors under 1.1 s (2.9 s); 10,322 route lines
dropped in the run. The packed tavern still fills it (below: the bench's two games on one PC), so the field's bug report carries
the queue line.

## The thread and lock audit

Every engine call or write this work adds, with its thread and its guard (the standing rules: an AI call on the AI update's thread through
the entry hook; no engine call that may take an engine lock under our fault guard):

| what | the call | thread | under our fault guard | verdict |
|---|---|---|---|---|
| 0.1 the dialogue twins | none: WO-216's spawn watch and census skip `DialogTwin_*` names | main | — | no new call |
| 0.2 the seat writer | reads of the copy's activity state (WO-141's guarded reads); a release asked through the owner's existing queue | main | reads only | ok |
| A.1 #1 the deferral | a branch: the owner's existing `install_fragment` follows | main (the owner) | as before | no new call |
| A.1 #2 the clogged queue | `IAction::Stop` on the copy's own pending look / movement actions — the stop the owner already makes on its own pending actions | main (the owner) | as the owner's own stop | ok, same call |
| B.2 the knockdown | one byte write to the ragdoll manager's stand-up flag; `RequestRagdoll` — the call WO-210's fall already makes | main | yes (a store; the existing call) | ok |
| B.3 long names | a hash in the existing guarded name reads and lookups | main | as before | no new call |
| B.4 the layer watch | `EnableLayer` (the vtable slot) runs first, outside any guard of ours; our own leaf mutex queues the layer's name | the activating thread (main in every run) | no | ok |
| C / D / E.1 / E.2 | Lua binds: inventory moves, item owner, `SpawnEntity`, `RemoveEntity` (our own, by token), positions | main (Lua) | no | ok |
| E.1 the deaths | the owner-death apply every streamed death already takes | as before | as before | ok, same call |
| F the carts | `FindEntityByName` once per cart, then `IEntity::SetPosRotScale` each frame — the call the NPC writer makes each frame — on a body with no parent only | main | as the writer's | ok, same call |
| `route_skip` | none: the joiner's agent drops a message before any pipe op | agent | — | no engine call |

## G — the things in between

**G.1 drops (the top rule: players dropping items for each other keeps working exactly as today).** Each player drops a weapon, food
and groschen for the other; the other picks each up (the game's own pick-up function); per item: the dropper's count down, the copy on
the other's ground, the taker's count up, nothing left behind.

| | weapon | food | groschen | duplicated | lost |
|---|---|---|---|---|---|
| before (the shipped 0.64.3), host → joiner | ok (seen in 2.5 s) | ok (2.3 s) | ok, the whole purse (2.4 s) | 0 | 0 |
| before, joiner → host | ok (2.1 s) | ok (2.3 s) | ok, the whole purse (2.4 s) | 0 | 0 |
| after (0.64.4, the acceptance's scene 8), host → joiner | ok (seen in 3.3 s) | ok (4.0 s) | ok, the whole purse (3.5 s) | 0 | 0 |
| after, joiner → host | ok (2.5 s) | ok (2.6 s) | ok, the whole purse (2.5 s) | 0 | 0 |
| the twin (every WO-217 switch off) | ok both ways (2.4–2.5 s) | ok | ok | 0 | 0 |

(The bench's first drop runs were wrong about the game, not the mod: the scene dropped a weapon the player held — the engine itself
expired it, "dropped & lost" — and read the pick-up while a conversation the bench had left open was still blocking the player. The
scene now drops a fresh weapon, waits up to 60 s for a background game, and counts only its own item on the ground.)

**G.2 loose items.** Measured on the shipped build first: the only loose items near the bench's spot were three weapons; one of them
lay only in the host's world, and the bench's pick-up call (the game's own pick-up function) took none of them on either machine, so
nothing was learned about a taken loose item on the other side. No fix; the field's bug report carries WO-134's loose-item lines.

**G.3 doors.** By the tavern (the first probe found no door within 60 m of the bench's own spot): each player uses five doors the
player's way (the door's own use function, standing at it), both sides compared 3 s later; then 2 minutes of the village's people,
26 doors compared every 15 s.

| | uses: the same on both after | doors that differed while the village used them |
|---|---|---|
| every switch on (the acceptance's scene 8) | 10 of 10 | 1 of 26, in every sample: closed and unlocked on the host, closed and **locked** on the joiner |
| the first all-on acceptance run | 9 of 10 (the joiner's use of a door locked on its side only) | 4 locked on the joiner only; a door a villager opened, open on the host and shut on the joiner |
| the twin | 9 of 10 (the same: a door locked on the joiner's side) | the same two kinds |

A player's own use is carried both ways (WO-151's door sync: the host's world owns every door, and each door that moves or locks
there is sent). What differs takes two different paths, so no fix here (the WO: "fix if one path"): a door **locked by the joiner's
own world** (closed and locked there, unlocked on the host — the host's door never changed, so nothing is sent to undo it), and a door
**a villager opened on the host** that stayed shut on the joiner (that move did not reach the joiner's copy in the watch, though the
door sync is meant to send it). Both are in the bug report's door lines (`WO151-DOOR`) for the field.

## The acceptance scenes

Every switch on, then every WO-217 switch off (the twin), the same plan and the same scene code: the caravan and the corpses first
(the save's caravan is on the road only on the first morning), then the talks of both roles, the smith's house (day, midday, night),
the tavern (10 minutes), the brawl and the far opponent, the loot, the drops and the doors. **Every switch on** is the build that
ships (with `route_skip`); **the twin** is the build before it with every WO-217 switch off — the shipped 0.64.3's rules, `route_skip`
included. Framed pairs where a scene has one subject both players can see (the host's view and the joiner's at one moment, the subject
3–8 m away, nothing between).

The bench's own limit came first: the joiner's agent queue (`MP-W207-Q`), per scene, as the runs went — a number measured under a
queue tens of seconds behind is a measure of the queue, not of the scene:

| the joiner's queue, at its worst | talks | smith's house | tavern | brawl | loot, drops, doors, caravan, corpses |
|---|---|---|---|---|---|
| every switch on | 3.3 s | 11.7 s (4 % of the time over 5 s) | **100 s** (88 %) | **64 s** (100 %) | ≤ 1.1 s |
| the twin | **104 s** | **106 s** (28 %) | **78 s** (91 %) | **43 s** (31 %) | ≤ 2.9 s |

With `route_skip` the talks and the smith's house run clean; the packed tavern still fills the joiner's pipe on the bench: two games
on one PC put the joiner at about 35 fps there, and the pipe serves about one request a frame — the NPC animation (0x7B, 20 a second)
and the activity streams (0x6F, 0x6B) alone are more than that. A joiner on a PC of its own runs about twice the frame rate; the
field's bug report carries the queue line (`MP-W207-Q lag_ms`), the first thing to read in it.

| scene (the acceptance's) | measured | every switch on | the twin | framed pair |
|---|---|---|---|---|
| 1 talks, both roles | started within 2 s, finished | **joiner: the talk started 0.4 s and finished**; the trade's conversation started 0.3 s and finished, its shop screen did not open; the forced call found no dialogue for the villager the game picked (no start). **Host: all three started within 0.08 s and finished**, the shop open | **joiner: the talk started 0.4 s and hung** — every merchant refused the trade while it hung, the forced dialogue never started; host: all three | none (a talk is one player's) |
| 2 the tavern (0.2) | pulls over 50 cm, 10 min | 146 (101 on 16 seated copies) — under the queue (mean 62 s behind) | 82 (34 on 12) — under the queue (mean 46 s) | unframeable: the guest picked sat out of sight of both spots (night) |
| 3 the smith's house (A) | shown within 1 s (the WO's 90 %), activity missing — day / midday / night | **67 / 72 / 20 %** (57, 25, 10 activities); missing 337 / 138 / 166 s | 57 / 46 / 22 % (42, 22, 9); missing 795 / 471 / 46 s — under the queue | **framed** (day): both 5 m from the smith — the host's smith hammers at the anvil, the joiner's copy stands at the forge, not working (the object-bound loop, A above) |
| 4 the brawl, the far opponent (B) | falls and get-ups; the fight kept | two knockdowns: the copies fell 31 s and 50 s late and got up 29 s and 49 s after the host — **the queue was 50–64 s behind**; none got up before its host. The far opponent: not staged (the game's own combat ended the joiner's fight after 3.0 s; WO-215's ghost fight never began) | one copy fell 3.7 s late and got up 1.2 s after its host, the other 75 ms / 92 ms; none before. Far opponent: not staged (2.8 s) | unframeable: the joiner's copies were nearly a minute behind (its view: the dark camp) |
| 5 looting one body (C, D) | the ledger; "Already taken" | **8 items: removed 8, granted 3 = applied 3, the host's own 5, refused 4, cancelled 0 — duplicated 0, lost 0**; the other's screen lost each taken item 2.3–2.8 s after (the bench's read) | no ledger; the other's screen kept every taken item (6 of 6) | **framed**: both loot screens on the same body, the same list; "Already taken" on the joiner's |
| 6 the corpses (E.1) | the host's dead, on the joiner | shown dead in place after 12 s (0.0 m off) | the same (the save's body is dead in both worlds) | **framed**: the body in the flowers, the same place in both views |
| 7 the caravan (F) | within 1 m of the host's cart, within 10 cm of the ground, riders seated | **the joiner's own measure, 252 samples within 80 m: within 1 m 250 (the farthest 1.26 m, p95 0.60 m); within 10 cm of the ground 251 (the highest 0.12 m); riders seated 494 of 494** | the joiner's cart on its own route, 128 m from the host's | **framed** at the pass (about 20 m — the cart moves; the 3–8 m spot after it found the cart gone on and a guard's copy in the joiner's view: unframeable) |
| 8 the drops (G.1), the doors (G.3) | each player's drop picked up by the other; doors used, compared | **drops 6 of 6** (seen by the other in 2.5–4.0 s), duplicated 0, lost 0; doors: 10 uses the same after on both; of 26 doors watched, 1 differed in every sample (locked on the joiner, unlocked on the host) | drops 6 of 6 (2.4–2.5 s); doors: 9 of 10 (a joiner's use of a door locked on its side), NPC-opened doors open on the host and shut on the joiner | none (items on the ground) |

**Retaken:** the whole acceptance, three times, for the bench's own faults (the reasons are the research notes'): the first all-on run
left the trade screen open on both machines (the bench's key table had no X); the first twin's forced dialogue with the village's
bailiff played the save's own quest scene and kept the host in a story conversation; a later all-on run sat the host on a bench (a use
key pressed outside a conversation) and froze the joiner in the pause menu (an Esc with nothing to close). The scene code now presses
neither. **Unframeable:** the tavern's guest, the brawl, the caravan after its pass (above).

**What the acceptance adds to the parts above:** 0.1's fix holds in the full plan (the joiner's talk finishes; the twin's hangs); C's
ledger balances again (8 items, nothing duplicated or lost) and the twin has none; F's playback holds over the pass (250 of 252 within
1 m) and the twin's cart drives its own route; the drops work exactly as before (6 of 6 both ways, in both). The smith's house in the
acceptance's order (the players arrive mid-activity) shows **67 / 72 / 20 %**, below the dedicated A run's 85 / 83 / 98.7 % and the
bar — refused here (21, 11, 8 of the misses), not tried (12, 7, 3) and the queue held full (9, 3, 1) lead; the WO's bar on its own
measure is met only in the dedicated run (A above). The tavern and the brawl measured the bench's queue, not the rules; their
numbers are the dedicated runs' (0.2, B.2 above).

## Gates and the installer

**The working tree's gates** (the code commit): 100 of 100 — the no-input-capture gate first and last (every binary built here and
every script, the bench's tools and the input helper included: "nothing checked can read what a person types or clicks"), the native
build and its 10,439 tests, the pak, the relay's 230 round-trip tests, the agent's 2,086, Setup's 77, the bug report's 95, every Lua
suite (WO-217's 33), the static gates, the relay message table and fault-mode gates, the digest, the bench's 42 and the save tools'.
A first run failed three older loot suites (WO-134 9 of 61, WO-166 13 of 70, WO-195's loot 6 of 17): they test WO-134's own loot, and
with WO-217's switches on by default they ran the live loot instead; they now run their path with `mp_loot_live` and `mp_item_flags`
off (the live loot is WO-217's suite, which keeps WO-166's rule: nothing created or deleted in the copy under an open screen).

**The installer** (from a clean clone of the commit, the start saves copied in): `KingdomComeTogether-Setup-0.64.4-8ff7e8d-2611101831.exe`,
107,918,364 bytes, build id `8ff7e8d-2611101831`, unsigned, kept local (never put on GitHub). Its gates — the safety ones are hard:

| gate | result |
|---|---|
| publishing (the pending commit against the denylist; names and handles from the field bundles) | PASS |
| the save round trip, live (WO-175 S5, on the bench's host copy) | PASS |
| the real saves unchanged (their hash list, before and after) | MATCH |
| no input tooling in the payload (WO-175), no screenshot (WO-192), no input capture (WO-203) | PASS, PASS, PASS |
| Setup's install / verify / uninstall tests | 41 of 41 |
| the published relay, agent and smoke client; the relay's fault modes | PASS |
| the frame-rate soak | waived (the WO gates only the safety ones and the join: every run's join passed) |

## Open — what the field session must show

- **The joiner's queue** (first): in a busy village and in a packed tavern, how far the joiner runs behind — the bug report's
  `MP-W207-Q lag_ms` lines. With `route_skip` the bench's talks and smith's house ran under 12 s; its packed tavern did not (two
  games on one PC). A joiner on its own PC is the measure.
- **Scene 4 (talks and trades as the joiner)**: every talk reaches its choices, the trade screen opens and a sale goes through (the
  bench's Henry has nothing to sell and is barred from the villagers' talk).
- **Scene 2 (the smith's house)**: the work tied to an object (the smith at his forge, a cook at her pot) — on the bench the copy
  stands at the forge without working.
- **Scene 3 (the tavern)**: how often a seated guest still jumps (`.` each time): 111 pulls in 10 minutes on the bench against the
  twin's 150; the bar is 0.
- **Scene 5 (a fight to the death, and a knockdown)**: a knocked-down copy gets up with its host (on the bench 66–109 ms, once
  1.1 s); the far opponent's fight kept (the bench staged it once).
- **Scene 6 (looting together)**: one owner for every contested item, "Already taken" for the other, nothing lost or doubled (the bug
  report's ledger line); the stolen mark (the bench could not stage a stolen item).
- **Scene 7 (a road encounter)**: the bench could not make the game start one; the corpses, props and the searchers on the joiner's
  screen are only tested there.
- **Scene 8 (a caravan)**: carts on the road and riders seated (measured on the bench); the horses with the cart (the bench saw both).
- **Doors**: a door locked on the joiner's side only, a door a villager opened shut for the joiner (`.` at the door).
- **Horses of the animal care** (B.3's long names): never seen as a copy on the bench.
