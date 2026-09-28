# WO-141A — activity and idle census

**Read-only census, 2026-09-28.** Every kind of "a body doing something at or
with something, or in a held pose" in KCD2, for NPCs and the player: the
checklist for WO-141. Sources are the Modding Tools install's paks only
(`Scripts.pak`, `Tables.pak`, `Animations.pak`, `IPL_GameData.pak`, and the
three levels' `level.pak`). Nothing was launched, built, or committed.

Evidence marks: **(data-verified)**: read directly from the shipped data,
counted by script. **(inferred)**: a reading of the data that no run has
confirmed. Game text is cited by string ID only.

**Counting conventions.**
* **Placed** = entities in `objects_mission0.xml` summed over the three
  level paks (`trosecko`, `kutnohorsko`, and the small `klaster`), written
  `N`. Streamed layer files (`layers/*.xml`) add more, written `+L`.
  (data-verified)
* **Links** = NPC schedule edges (`tables/ai/scheduler.xml`,
  `S_ActivityLink`) whose behaviour targets that smart object. It stands in
  for "how many NPC schedules use it" (one NPC hub has one link per use).
  Resolution: the scheduler's 64-bit `TargetGuid`, read as a little-endian
  GUID struct (`Data1,Data2,Data3`), equals the first 16 hex digits of the
  level's `EntityGuid`. 121,404 of 171,717 links resolve. The 50,313 that
  don't are almost all behaviour-less home-area links. (data-verified)
* **Visual weight** = how wrong it looks on the other screen when it isn't
  reproduced. **High**: body position or whole-body pose wrong (a standing
  sleeper, a standing sitter, a corpse standing up, a person floating
  beside a bed). **Medium**: full-body loop missing but position plausible
  (a smith standing idle at the anvil), or a prop visibly wrong. **Low**:
  upper-body or idle variation.

---

## 1. The answer

### 1.1 How many mechanisms

**Five mechanisms cover everything NPCs do at objects. Two of them, stance
and unstance, cover about 92 % of it.** All five are elements or actions of
one system: Warhorse's **NPC state** planner (`Libs/Tables/ai/NPCState*.xml`,
driven by behaviour-tree nodes). The player's sitting and sleeping run
through the **same** system. The player's crafting minigames are separate.

| # | mechanism | BT node(s) | data | covers |
|---|---|---|---|---|
| **M1** | **Stance**: a held body state bound to a smart object | `StanceElement stance= smartObject=` (1,010 uses), `CartStanceElement`, `HorseUsageElement` | `ChangeStanceAction` in `NPCStateActionDatabase.xml` (sit down / stand up / lie down / sit up, 4 urgencies each); stance groups in `action/ActorStanceGroups.xml` | sitting (bench, chair, ground, table), lying (bed, ground, tent, shelter), crouch, horse, cart |
| **M2** | **Unstance**: an In → Loop → Out activity animation, optionally aligned to a location object and optionally requiring a stance | `UnstanceAction` (2,122), `UnstanceElement` (320), `JoinedUnstanceAction` (132, 2-party synced) | `NPCStateUnstanceDatabase.xml`: **654** unstances; 397 have an In fragment, 388 an Out; 126 require a stance (79 sitting, 27 lying, 18 standing, 2 crouch) | nearly every job, chore, idle-on-object, lean, pray, wait, wounded pose, dead-body pose |
| **M3** | **Hand content**: a real inventory item held in the left or right hand for the duration | `HandContentElement hand= item= decisionLabel=` (1,128), `PlaceAction`, `ItemSetAsideElement`, `InstantPutItemInHand` | `DecisionLabelDatabase.xml`; `PickUp*` / `Place*` / `PutItemInHand*` actions in `NPCStateActionDatabase.xml` | the hoe, broom, bucket, stein, bowl, sack, fishing rod, torch, halberd |
| **M4** | **One-shot fragment**: a single Mannequin fragment, aligned or not | `AnimationAction` (1,208; 297 distinct fragments), `JoinedAnimationAction` (280; 82, paired), `PlayAdditiveAnimation` (74; 13, upper-body) | Mannequin ADBs; baked table `animation/anim_fragment.xml` (9,722 rows, 2,155 fragment ids, 899 of them aligned) | serve beer, drink from a trough, bark, cheer, beggar takes alms, stoke fire |
| **M5** | **Activity locomotion**: moving *is* the activity | `Move` / `ExactMove` under an `EntityContext actorCondition_*` (e.g. `actorCondition_forcedHoeing`) or with `changeNPCState` | `ScriptContext.xml` (9 `actorCondition_*` contexts: hoeing, drunkenness, fatness, injured, armored, crime scanning/watching) | **field hoeing**, drunk / injured gait, city walks, bailiff inspection walks |

Two support mechanisms ride on top of these:

* **M6 — slave objects**: an animated object driven in sync by the user's
  fragment. It is the `slaveObject=` argument on M2/M4 (book, chest, forge
  bellows, tongs, doors, rope, hangman's halter), with `kcd_slave*.adb`
  databases and `Animations/assets/*` skeletons (grindstone wheel, well,
  churn, alchemy bellows, crushing-mill wheel). (data-verified)
* **M7 — animation-event props**: temporary props spawned into a hand by
  events in the CAF (`spawnitem`/`despawnitem` 245 each, `pickup`/`place`
  262/255, `phaseitem` 103, `Attach`/`Detach` 559/519). §6. (data-verified)

**The player** adds one more:

* **M8 — native minigame state** (`actor_state` tag `minigame`,
  `Minigame.*` scriptbind): grindstone, smithing, alchemy, dice, lockpicking,
  pickpocketing, herb gathering, grave digging, stone throwing, reading,
  transcription, washing. Each is a native module that plays its own
  fragments (`SharpeningMinigame`, `Blacksmithing*`, `Alchemy*`,
  `DiceGameThrow`…). No behaviour tree drives the player's body there.
  (data-verified: Lua entry points; the per-frame animation driver is native,
  inferred)

**The smallest set for about 90 %: M1 + M2, then M3.** Of the 110,447
schedule links that resolve to a smart-object template (data-verified):

| bucket | links | share |
|---|---|---|
| stance only (bed, sit place) | 55,987 | 50.7 % |
| unstance (with or without stance / hand) | 42,062 | 38.1 % |
| unstance chosen by entity Lua or property (dead bodies, waiting spots, party, tied, lying-harmed, special sitting, cheering, spectators, halberdiers) | 3,796 | 3.4 % |
| pure locomotion (city walk, additive crowd) | 6,683 | 6.1 % |
| one-shot or hand-only | 950 | 0.9 % |
| quest controllers and misc. | ≈ 970 | 0.9 % |

So **stance + unstance ≈ 92 %** of scheduled object use. **Hand content (M3)
is the next-cheapest step up in fidelity.** Without it the unstance plays
with empty hands. The field hoe, the bucket and the broom are all M3. M4
and M5 are the remaining few percent, but M5 holds the one case already
seen in the field (§1.3).

### 1.2 Top kinds by visual weight × commonness

| rank | kind | mechanism | placed | links | weight |
|---|---|---|---|---|---|
| 1 | **sleeping in bed** (NPC and player) | M1 `lying` | 2,864 beds (2,076 +788) | 47,347 | high: a standing sleeper |
| 2 | **sitting** on benches, chairs, ground, at tables | M1 `sitting` | 7,093 sit places (5,428 +1,665) | 8,146 | high: a standing sitter |
| 3 | **guard post** idle | M2 `guard` (+M3 crossbow) | 1,543 (1,100 +443) | 3,388 | medium |
| 4 | **tavern** (serve, eat mash/chicken, drink, talk) | M1 + M2 + M3 + M4 | 275 (227 +48) + 1,017 item slots | 3,061 | high (seated eaters) |
| 5 | **leaning** back / left / right on walls | M2 aligned | 1,533 (1,278 +255) | 2,605 | high: leaning on air |
| 6 | **hearth**: cooking, eating from the pot, stoking | M1 + M2 + M3 | 418 (349 +69) | 2,300 | medium to high |
| 7 | **water tube**: wash face, drink, fetch water | M2 + M4 + M3 bucket | 405 (367 +38) | 2,050 | medium |
| 8 | **praying** (standing, kneeling, at a kneeler) | M2 | 181 +28 kneelers | 1,991 | high (kneeling) |
| 9 | **posed dead bodies** | M2 via entity Lua | 473 SO (136 +337) + 350 `DeadBody_Human` | 1,838 | high: a corpse standing |
| 10 | **bench activities** (embroidery, spindle, basket weaving, snooze, sunbathe, cry) | M1 + M2 + M3 | 295 | 1,670 | high (seated) |
| 11 | **chest check** | M2 + M6 chest | 1,615 | 1,615 | medium |
| 12 | **waiting spot** idles (arms crossed, nervous…) | M2 via entity property | 1,448 | 1,447 | low to medium |
| 13 | **animal idles** (dog, hare, pig, deer, horse, cow, sheep) | M1 `lying`/`sitting` + M2 | ~1,180 | ~4,600 | medium (a lying cow standing) |
| 14 | **field hoeing** | **M3 + M5** | 252 (202 +50) | 252 | **high: the hoe held out sideways** |
| 15 | **sweeping** | M2 + M3 broom | 466 | 466 | medium |
| 16 | **seller at a stall** | M2 `seller`/`seller2` | 144 | 480 | medium |
| 17 | **dice table** (players, kibitzers) | M1 + M2 + M4 | 109 | 328 | high (seated) |
| 18 | **hen care, milking, pig feeding** | M2 + M3 | ~190 | ~530 | medium |
| 19 | **chopping, sawing, fence repair, shoveling, mining** | M2 aligned + M3 | ~620 | ~620 | medium to high |
| 20 | **cart riding** (driver, passengers) | M1 `cart` slot | 71 carts (5 +66) | 288 | high: sitting on air |

### 1.3 The field case, explained

The field hoe is **M3 + M5, not an unstance.** `profession/farmer/so_field.xml`
(tree `hoeing`) wraps a `HandContentElement hand="Left"
decisionLabel="farmer_hoe"` around an `ExactMove` to a first point, then an
`EntityContext context="actorCondition_forcedHoeing"` around a `Move` to a
second point. The hoe is a real item in the hand, and the hoeing motion is
a **locomotion style** switched on by the actor-condition context. A copy
that gets the hand item but not the context, and stands still, shows
exactly the reported pose: a farmer holding a hoe out sideways. Vineyard
hoeing is different: it is an aligned unstance (`hoeingOnPlace`, +
`vineyard_hoe`). (data-verified)

### 1.4 Facts WO-141 should not re-derive

* **The `IsAligned` default is "aligned"** (inferred, strong). This settles
  WO-116's open question. Unstances that omit `IsAligned` (333) have
  In-fragments that the baked `anim_fragment.xml` marks `aligned="true"`
  180 times out of 192 (94 %). Explicit `IsAligned="false"` unstances have
  42 out of 196 (21 %). Treat an omitted `IsAligned` as aligned to the
  location object. The same holds for `UseLocationObject`.
* **Aligned means the activity owns the body's position.** Stances and
  aligned unstances snap the body to the smart object's helper. WO-116 §1
  measured the pull. A position stream that fights it loses.
* **The player's sitting and sleeping are NPC-state stances.**
  `Bed.lua`/`chair.lua` send `player:request … mode('use')`. The bed and
  sit-place templates carry `playerAction_stanceObject`
  (`player/scheduler/playerAction_stanceObject.xml`: `StanceElement`
  lying / sitting). One sync path can serve both. (data-verified)
* **Unstances also come from entity Lua and properties, not only from
  trees.** 394 of the 654 unstances are named literally in behaviour trees.
  Another 183 are named in entity scripts or smart-entity tables:
  `DeadBody_Human.lua` (≈ 40 corpse poses), `SO_Party_*.lua`,
  `SO_CheeringSpot_*.lua`, `SO_LyingHarmed*.lua`,
  `SO_SpecialSittingActivity.lua`, and the waiting-spot variant property.
  63 appear nowhere and are probably dead data. A reader that keys on the
  tree node alone misses about 30 %. (data-verified)
* **Children**: there is a `Human_Child` skeleton (`SkeletonList.xml`) but
  no child Mannequin database, and no child unstance or fragment. "Children
  playing" is not a systemic activity in this data. (data-verified absence)
* **No player fishing.** Fishing exists only as an NPC unstance (`fishing`,
  one placement) and as `Fish` boids (241). No player fishing entity, Lua,
  or UI element exists. (data-verified absence)
* **No NPC uses a grindstone.** In `Grindstone.lua` the NPC branch
  (`Sharpening.Start`) is commented out. Grindstones are player-only.
  (data-verified)

---

## 2. How each mechanism looks on the wire (for WO-141)

| mechanism | minimal state to send | notes |
|---|---|---|
| M1 stance | stance enum (standing, sitting, lying, crouch, horse, cart) + smart-object WUID (+ cart slot) | the object's helper set (`soclass_SmartObjectHelpers`, e.g. `Sit_1Place_Bench_Low_Table_Left`) picks the exact pose and enter/exit fragments; the receiver needs only the object |
| M2 unstance | unstance name (hash) + location-object WUID + phase (in / loop / out) | the name alone gives In/Loop/Out fragments, tags, alignment and required stance; `UnstanceTransitionDatabase` gives loop→loop transitions (grooming left→right, laundry→stone, seller↔seller2) |
| M3 hand | left and right item class (or `none`) | items come from the NPC's home slot or a linked `ItemSlot` (`hoe`, `axeSlot`, `broom`, `bowlStorage` links) |
| M4 one-shot | fragment id + tags + align object, fire-and-forget | 297 + 82 + 13 distinct; `JoinedAnimationAction` needs the partner too |
| M5 locomotion style | the active `actorCondition_*` context (9 values) | changes the gait, not the pose; rides on the existing movement stream |
| M6 slave object | usually implied by the unstance (`FragTags="slaveChest"`, `[forgeBag]`) | the object's own animation must play on both machines |
| M7 event props | implied by the fragment (spawned by CAF events) | reproduce the fragment and the prop follows, if the receiver's item GUIDs resolve |
| M8 minigame | minigame type + object WUID + coarse phase | the player's body is native-driven; replay the fragment family, not the minigame |

(inferred design notes; the data columns are data-verified)

---

## 3. The full table

Columns: **placed** = `N (+L)`, **links** = schedule links. Tree paths are
relative to `Scripts.pak: Scripts/AI/`. Smart-entity templates are
`Tables.pak: Libs/Tables/ai/smartEntity/SmartEntity__<name>.xml`.
**pos** = does the activity own the body's position (align / snap).

### 3.1 M1 — stances (held body states)

| kind | who | mechanism | where defined | placed / links | weight | pos | notes |
|---|---|---|---|---|---|---|---|
| sleep in bed | both | M1 `lying` on `so_bed` | `world/so_bed.xml::use`; player `playerAction_stanceObject`, `playerAction_wakeUpOnBed`; entity `StanceSmartObject` (`Bed.esSleepQuality`), `BedTrigger` for the player | 2,864 beds (2,076 +788); 47,347 links; player triggers 1,914 (+469) | **high** | yes | helper variants: `Bed_1Place_High` 605, `_Low` 525, `_Tent` 463, `_Bench` 397, `_Low_Var2` 311, `_Low_Var1` 293, `Bed_2Place_High_Left/Right` 134 each; bed types `GroundBed` 1,021, `NormalBed` 700, `Bench` 357 (obj only); crime anims draw weapons straight from bed (`*_bed_to_guard_*`) |
| sit on bench | both | M1 `sitting` on `so_sitPlace` | `world/so_sitPlace.xml::use`; `ActorStanceGroups.xml` (`sitting` by table, `sittingNoTable`) | 3,200 low + 296 high bench places | **high** | yes | 1.6 % random chance a man falls asleep on a bench (`RandomGate 0.016` → unstance `camper_snooze`) |
| sit at table | both | M1 `sitting`, table side from helper | same | `_Table_Left/Right` 1,165 each, `_Both` 218, `_BothBack` 96 | **high** | yes | eating and dice build on this |
| sit on chair | both | M1 `sitting` + **chair moves** | `Chair` entity (229 +108), `chair.lua` | `Sit_1Place_Chair_Low` 197, `_High` 141 | **high** | yes | the chair is attached to the sitter during step-in/out (`attachobject`/`detachobject` events in 30 `sitting_chair_*` CAFs), so the chair's own position changes |
| sit on ground | both | M1 `sitting` (`isGroundPlace:true`) | `so_sitPlace` holders, `Script.Misc isGroundPlace:true` (499) | `Sit_1Place_Ground_Var1–4` 395/80/56/55, `_Inured` 22 | **high** | yes | no bench snooze on ground places |
| lie on ground / tent / shelter | both | M1 `lying` (stance groups `lyingGround`, `lyingTent`, `lyingShelter`) | as beds | included in beds | **high** | yes | |
| crouch | both | M1 `crouch` | `ChangeCrouchAction`; player `playerAction_crouch.xml` | 64 tree uses | medium | no | stealth and battle kneel |
| on horse (NPC, incl. stopped) | both | M1 `horse` + `HorseUsageElement` | `RiderActionIn/Out` (`GetOnHorse`, `GetOffHorse`); `playerAction_horse.xml` | 117 tree uses; horses 523 (148 +375) | **high** when wrong (a rider without a horse) | yes | already in the MP horse path (WO-40/58) |
| cart driver / passenger | both | M1 `cart` via `CartStanceElement cartslot=` | slots `driver`, `leftFront`, `rightFront`, `leftBack`, `playerFront`, `playerBack`, `any`; `CartActionIn/Out` | carts 71 (5 +66); mount points 101; 288 links (`cart_driver`, `cart_passenger`, `cart_horse`, `cart_accompany` 71 each) | **high** | yes (to the moving cart) | the passenger must follow the cart's transform |
| animals lying / sitting | NPC | M1 via `NPCStateStanceAnimDatabase.xml` | cattle, sheep, pig, deer doe, dog, hare, wolf, boar, wild dog → loop fragment `Lying`/`Relaxing`/`Sitting` | see §3.8 | medium | no | |

### 3.2 M2 — workstations and trades (aligned unstances)

| kind | who | mechanism | where defined | placed / links | weight | pos | notes |
|---|---|---|---|---|---|---|---|
| forge: heat, forge | NPC | M2 `blacksmith_heating`, `blacksmith_forging` + M3 sword (L) and hammer (R) + M6 forge bag | `profession/blacksmith/so_blacksmith.xml` | 38 (33 +5); 38 links | **high** | yes | loop→loop transitions heat↔forge; `blacksmith_coop_slave` = 2-smith coop |
| armour smith | NPC | M2 `armorsmith` (sitting) | `profession/blacksmith` | 4 | medium | yes | |
| foundry / casting | NPC | M2 `foundry_castHeating(Tongs)` + M3 tongs + M6 casting tongs | `profession/blacksmith/so_foundry.xml` | rare (helper `Foundry` 2) | medium | yes | |
| grindstone | player | M8 | `Grindstone.lua` → `actor:OpenItemSelectionSharpening`; fragments `SharpeningMinigame(In/Out)` | 40 (30 +10) | **high** (a player standing at a wheel) | yes | wheel = M6 asset `grindstone_spinning_<weapon>` (one per weapon family) |
| smithing | player | M8 | `Smithery.lua`, `ForgeBuilderTrigger.lua`; fragments `Blacksmithing*` (`kcd_male_blacksmithing.adb`); anim events `AttachHammer`/`AttachWorkpiece`/`StrokeHit` | 28 (23 +5) | **high** | yes | workpiece and hammer attach by anim event |
| alchemy | both | NPC: M2 `apothecaryAlchemy`, `apothecaryMortar`, `herbalistAlchemy`; player: M8 | `profession/apothecary`, `profession/herbalist`; `AlchemyTable.lua`; `male_alchemy.adb` | tables 20 (15 +5); mortar spots 11 | **high** (player), medium (NPC) | yes | NPC `so_alchemyTable::use` is only a `Wait`; the apothecary trees do the animating. Bellows/kettle are M6 assets |
| herb crushing | NPC | M2 `apothecaryMortar(Loop)` | `so_crushingHerbsSpot` | 11 | medium | yes | |
| tanning | NPC | M2 `scrapeLeather`, `stirLeather` | `profession/tanner` | 11 scraping logs + 4 tubs | medium | yes | lime-water pour uses M3 bucket |
| butchering / smokehouse | NPC | M2 `butcherSmokeHouse{Check,Empty,Fill,Stoke}` (+ doe table, hooks) | `profession/butcher` | 128 smoke/dry racks (108 +20), 28 butcher links | medium | no | CAF `attach` events for sausages, doe pieces |
| baking / milling | NPC | M2 `bakerSifting`; miller one-shots; `miller_carrySack` (M3) | `profession/miller` | miller watch 150; carry sack 164 (136 +28) | medium | yes | pour-grain, sift-flour templates exist, rare |
| weaving / sewing / embroidery | NPC | M2 `tailor_sewing`, `tailor_embroidery` (sitting), `tailor_measure`, `tailor_pattern` | `profession/tailor`; bench activity | 6 tables + bench activity | medium | yes | |
| shoemaking | NPC | M2 `shoeMaking` | `profession/shoemaking` | 17 (14 +3) | medium | yes | CAF spawns the needle |
| carpentry / debarking | NPC | M2 `debarkingWood` | `profession/carpenter` | 49 (46 +3) | medium | yes | |
| sawing | NPC | M2 `sawingWood` | `profession/lumberjack` | 98 (90 +8) | medium | yes | |
| wood chopping | NPC | M2 `lumberjack_woodChopping` + M3 axe + M4 `WoodChopping_*ToBasket/Stack` | `profession/lumberjack/so_choppingWood.xml` | 156 (136 +20) | medium | yes | logs spawned per stroke by CAF events (`log_cut`, `log_cut_half`) |
| tree felling / branches | NPC | M2 `cuttingTree`, `cuttingBranches` + M3 axe | `profession/lumberjack` | 4 | medium | yes | |
| stone mining / chiselling | NPC | M2 `stoneMining(_soft)`, `stoneChiselling` + M3 pickaxe | `profession/stonemason` | 73 + 32 | medium | yes | |
| mine: sort / roast / crush / shovel coal | NPC | M2 `stoneSorting`, `stoneRoastering`, `stoneCrushing`, `coalThrowing(_soft)` + M3 shovel | `profession/miner`, `profession/coalman` | 11 / 4 / 2 / 118 | medium | yes | sorting spawns stones by CAF event |
| charcoal kiln | NPC | M2 `kilnRepair` + M3 shovel | `profession/coalman` | 1 | low | yes | |
| mint | NPC | M2 `mintage` + M3 hammer / die | `world/so_mintage.xml` | 4 | medium | yes | |
| scribe / transcribing | both | NPC: M2 `Transcribing`, `scribeTableListening` (sitting); player: M8 `StartBookTranscription` | `profession/transcribing`, `TranscriptionTable.lua` | 50 (46 +4); 100 links | high (seated) | yes | |
| bailiff counting money | NPC | M2 `BailiffCounting` (sitting) | `profession/bailiff` | 21 | high (seated) | yes | |
| painter | NPC | M2 `painter_cleanBrush`, `painter_skull` | `profession/painter` | 4 | low | yes | |
| butter churning | NPC | M2 `housekeeper_churnMakeButter` + M6 churn | `profession/housekeeper` | 7 | medium | yes | |
| bath-house service | NPC | M2 `bath`, `spaWash(_customer)`, `bathrobesCleanTube`, `bathrobesSitting` + M3 herbs/stein/bucket | `profession/spa` | 46 (24 +22); 281 links | high (a bather in no tub) | yes | `bath` is flagged `UseForDialogueTwin` |

### 3.3 M2 / M3 / M5 — fieldwork and chores

| kind | who | mechanism | where defined | placed / links | weight | pos | notes |
|---|---|---|---|---|---|---|---|
| **field hoeing** | NPC | **M3 `farmer_hoe` + M5 `actorCondition_forcedHoeing`** | `profession/farmer/so_field.xml::hoeing` | 252 (202 +50); 252 links | **high** | no (walks between two points) | §1.3 |
| vineyard hoeing | NPC | M2 `hoeingOnPlace` + M3 `vineyard_hoe` | `profession/vineyard` | 52 (40 +12) | medium | yes | |
| weeding | NPC | M2 `weeding` / `weeding_aligned` | `so_weeding`, `so_weedout_point` | 131 + 46 | medium (kneeling) | partly | |
| grave digging | NPC | M2 `digging` + M3 `shovel_digGrave` | `so_infiniteDiggingGrave` | 49 | medium | yes | player digging = M8 `Minigame.StartHoleDigging` (holes 70) |
| sweeping | NPC | M2 `sweeping` + M3 `tavern_broom` | `world/so_sweeping.xml` | 466 (364 +102); 466 links | medium | no | broom from the `broom` item slot (301) |
| laundry | NPC | M2 `housekeeper_laundry` ↔ `housekeeper_laundry_stone` + M3 basket | `world/so_waterResource.xml` | 39 (33 +6); 224 links | medium (kneeling) | yes | |
| fetching / carrying water | NPC | M2 `housekeeper_bucketFillWaterResource` + M4 `BucketPour`, `FarmerGetWater` + M3 bucket | `world/so_waterTube.xml`, `so_waterWell` | tubes 405, wells 27 | medium | yes | well = M4 `Well` + M6 well asset |
| washing face / drinking at trough | both | NPC: M2 `housekeeper_faceWash`, M4 `HousekeeperDrinkWaterTub`, `CampDrinkWatertube`; player: `WaterTubeActionTrigger` → washing minigame | `world/so_waterTube.xml`; `WashingMinigame` BT node | tubes 405; player triggers 442 (400 +42) | medium | yes | |
| chopping block / firewood | NPC | M4 `WoodChopping_*`, housekeeper firewood fragments; `phaseitem` on logs | `profession/lumberjack`, `profession/housekeeper` | chopping helpers 163 | medium | yes | |
| carrying a sack / basket | NPC | M3 `miller_carrySack`, `miner_carryBasket` + M5 carry gait (inferred) | `profession/miller/so_carrysack.xml`, `profession/miner/so_carrybasket.xml` | 164 + 7 | medium | no | `kcd_carryItem_tags.xml` tags |
| fence repair | NPC | M2 `repairFenceHammer` | `profession/housekeeper`, `profession/miller` | 175 (151 +24) | medium | yes | |
| feeding hens / picking eggs / plucking | NPC | M2 `housekeeper_feedingHen`, `_pickingEggs`, `_pluckingHen` (sitting) + M3 grain sack / egg basket / chicken | `world/so_animalCare_hen.xml` | 108 (95 +13); 326 links | medium | yes | |
| milking | NPC | M2 `housekeeper_milking` (+ the cow's `housekeeper_milking_cow`) + M3 bucket | `world/so_animalCare_cow.xml` | 59; 178 links | medium | yes | the cow takes a paired unstance |
| feeding pigs / dog | NPC | M2 `housekeeper_feedingPigs` + M3 bucket; M4 dog care fragments | `world/so_animalCare_pig.xml`, `_dog.xml` | 22 + 21; pig feeder 24 | low | yes | |
| horse grooming / watering | NPC | M2 `camper_horseGrooming{Left,Right}` (+ `_horse` paired), `camper_horseWater` | `world/so_animalCare_horse.xml`, quest trees | rare (16 links) | medium | yes | loop transitions left↔right |
| cooking at hearth | NPC | M2 `housekeeper_cooking_home`, `camper_cooking` + M3 herbs/ingredients/wood + M4 `Cooking*`, `CookingScoopToBowl` | `world/so_fireplace.xml` | 418 (349 +69); 2,300 links | medium | yes | player cooking = `FoodProcessingTrigger` (below) |
| stoking / tinkering a camp fire | NPC | M4 `CampFireplaceStoking`, `CampFireplaceTinkering01` | same | same | low | yes | |

### 3.4 M1 + M2 + M4 — tavern, eating, drinking, dice

| kind | who | mechanism | where defined | placed / links | weight | pos | notes |
|---|---|---|---|---|---|---|---|
| guest eats mash / chicken at table | NPC | M1 sitting + M2 `guest_eatingMashSitting_{left,right}`, `eatingChicken{Left,Right}Sitting` + M3 bowl/chicken | `profession/tavern/bartender.xml` | tavern SO 275; bowl slots 335 L + 335 R, chicken 170+78 | **high** | yes | bowl contents change by `phaseitem` |
| guest drinks beer | NPC | M1 + M4 `Guest_DrinkBeer`, `Guest_Call` + M3 `tavern_stein` | same | stein slots 51 | high (seated) | yes | |
| bartender serves / cleans / clears | NPC | M4 `Bartender_ServeBeer`, `Bartender_CleaningTable`, `GetMash` + M3 bowls/tray | same | 3,061 tavern links | medium | yes | |
| innkeeper talks with guests | NPC | M2 `bartender_talk` | `profession/tavern/innkeeper.xml` | 169+ links | low | yes | |
| eating at home (from the pot) | NPC | M1 sitting + M2 `housekeeper_eat`, `_eat_waiting` + M3 bowl | `world/so_fireplace.xml::eating` | 418 links | high (seated) | yes | |
| camp eating / drinking | NPC | M2 `eating` (sitting), `camper_wineDrinking`; M4 `DrinkWineskin` | `profession/camper`, `so_campBuffable`, `so_benchCampFun` | 224 buffable (63 +161), 100 bench-fun | medium | yes | |
| drink wine at a table | NPC | M1 + M4 `Guest_DrinkWine` | `so_drinkWineAtTable` | 46 (9 +37) | high (seated) | yes | |
| dice: challenger / opponent | both | M1 sitting + M4 `DiceGame*` + M7 dice/cup pickups; player: M8 (`DiceInteractor`, `DiceMinigameCup`) | `world/so_diceTable_new.xml` | 109 (50 +59); 328 links | **high** | yes | `ExplicitFragmentAnimAction TakeAwayDice*`, `TakeAwayTankard` |
| dice kibitzer | NPC | M2 `diceKibitzer` | same | 110 links | low | yes | |
| player eats / drinks from inventory | player | item-use fragment (native) | `UseItem`; `eating_*`/`drinking_*` CAFs | everywhere | medium | no | |
| player eats from a pot / drinks from a barrel | player | `KettleActionTrigger` → `BehaviorEatingPot`, `BehaviorEatingBoiler`, `BehaviorDrinkBarrelSmallSpigot` | entity properties | 642 (412 +230) | medium | yes | the Hold action poisons (`BehaviorPoisoning*`) |
| player cooks / dries / smokes food | player | `FoodProcessingTrigger` (cooking 349, drying 71, smoking 37) | entity properties | 546 (457 +89) | medium | yes | |

### 3.5 M2 — idles on objects

| kind | who | mechanism | where defined | placed / links | weight | pos | notes |
|---|---|---|---|---|---|---|---|
| lean back / left / right on a wall | NPC | M2 `LeaningBack`, `LeaningLeft`, `LeaningRight` (aligned) | `world/so_leaning_*.xml`; `DetailMovementSmartObject` | 1,065 / 254 / 214 (+27 by a smithy); 2,605 links | **high** (leaning on air) | yes | `use_withBark` variant barks |
| lean on a fence / rail | NPC | M2 `leaningRail1–3`, `leaningRailQuest` | `world/so_leaningRail.xml`; `SO_LeaningRail` | 77 (70 +7) | high | yes | |
| lean front / exhausted lean / lean on sword | NPC | M2 `leaningFront`, `exhaustedLean`, `leanOnSword` | special trees | rare | medium | yes | |
| sit by a fire / bench activities | NPC | M1 + M2 `tailor_embroidery`, `housekeeper_spindle`, `_basketWeaving`, `_cream`, `_sunbathing`, `cryingOnBench`, `camper_snooze` | `world/so_benchActivity.xml` | 295 (269 +26); 1,670 links | **high** | yes | basket / cream pot = M3 |
| camp bench fun | NPC | M1 + M2 `camper_knifeSharpening`, `_repairGear`, `_throwingKnife` | `profession/camper/so_benchCampFun.xml` | 100 (78 +22); 400 links | high | yes | CAF spawns knife, arrow |
| praying standing / kneeling | both | M2 `PrayStanding`, `PrayKneelingGround(_female)`, `PrayStandingJew`, `PrayKneelingDesk`; player `prayKneelingGround_player` | `profession/parson`, `so_praying`, `so_kneeler`, `so_player_prayingSpot` | 181 + 28 kneelers + 63 player spots; 1,991 links | **high** (kneeling) | yes | the player praying spot is an `InteractionTrigger` with a perk (53) |
| confession | NPC | M1 + M2 `ConfessionParson`, `ConfessionSinner` | `profession/parson` | 10; 20 links | high | yes | |
| reading (lectern, sitting, standing, library shelf) | NPC | M2 `readingBookStand`, `readingSittingNoTable`, `readingSittingWithTable`, `readingStanding`, `readingLibrary{Up,Mid,Down}` + M6 book | `profession/parson`, `profession/scribe` | lecterns 56 (38 +18), reading spots 33; 153 links | medium to high | yes | book is a slave object (`kcd_book_database.adb`) |
| playing an instrument | NPC | M4 `PlayFluteSong` (sitting), `PlayingFlute`; party `…playingFlute/Lute_holding…` | `so_flutist`, `SO_Party_Standing.lua` | 15 (13 +2); 30 links | medium | yes | CAF spawns `flute`; lute rare |
| storyteller | NPC | M1 sitting + dialogue | `so_storyteller` | 20 | medium | yes | |
| relieving oneself | NPC | M4 `Piss`, `PissDrunk` | `profession/camper/so_camppissplace.xml` | rare (quest and camp holders) | low | no | vomiting: `Vomit`, `VomitDrunk`, party throwing-up unstances |
| inspecting (bailiff walks) | NPC | M2 `LookingAtDetail`, `LookingStraight(_right)`, `LookingUpward`, `LookingWideAround`, `lookingAtDetailLoop_disagree`, `scribeReadingWalkaround` | `profession/bailiff`, `so_inspection`, `so_bailiff_inspection*` | ~190; 184+ links | low | yes | |
| chest check | NPC | M2 `chestCheck` + M6 chest | `so_chest` (`GenU_chestCheck`) | 1,615 (1,259 +356) | medium | yes | |
| waiting (arms crossed, nervous, holding arm, akimbo, alarmed) | NPC | M2 via property `WaitingSpot.esWaitingSpot_Variant` → `waiting_*` | `special/waitingSpot.xml`; `SO_WaitingSpot` | 1,448; variants: `male_armsCrossed` 299, `male_nervous_armOnChin` 90, `female_holdingArm` 62, `male_nervous_armsAkimbo` 61, … | low to medium | yes (location object) | |
| city-walk point (stop and look) | NPC | M5 `Move`/`ExactMove` + `Wait` (no animation of its own) | `world/so_citywalk_point.xml` | 2,321 (2,141 +180); 5,251 links | low | no | |
| additive crowd (enter a house to sleep) | NPC | M5 movement + door use | `world/so_additiveNPCs.xml` | 716 (Kutná Hora only); 1,432 links | low | no | the body vanishes through a door |
| doors | both | M4 door fragments + M6 door (`kcd_slaveDoor*`) | `world/so_door.xml`; `AnimDoor` | 1,517 (1,335 +182) | medium | yes | lockpick / rattle / lock variants |
| ladders | both | ladder state (`actor_state` `ladder`) | `world/so_ladder.xml`; `Libs/Tables/animation/ladder.xml` | 366 (299 +67) | **high** (climbing air) | yes | siege ladders are M6 (`battleLadder*`) |
| bell ringing / watchman horn | NPC | M4 `BellRinging`, `AlarmBell`; `WatchmanBlowhorn` | `so_bell`, `so_watchmanSpot` | 3 / 8 | low | yes | |
| meditation | NPC | M2 `meditate_ground` | `special/so_meditationspot.xml` | 1 | low | yes | |
| stretching | NPC | M2 `Stretching` | `so_stretch` | 4 | low | yes | |

### 3.6 Guards, crowds and posts

| kind | who | mechanism | where defined | placed / links | weight | pos | notes |
|---|---|---|---|---|---|---|---|
| guard post | NPC | M2 `guard` (+ M3 `guard_crossbow` where set) | `profession/guard/so_guardspot_point.xml` (`guard`, `guardNonImportant(Continual)`, `guardOffDuty`, `kick`) | 1,543 (1,100 +443); 3,388 links | medium | yes (location) | `Script.Misc shouldUseCrossbow:true` (27) |
| halberdier at attention / asleep on halberd | NPC | M2 `halberdierGuard_atAttention`, `_sleeping` (`PolearmFallToSleep`) + M3 `guard_halberd` | `special/halberdierGuard` | 19 | medium | no | |
| guard of honour / stand like a guard | NPC | M2 `Quest_GuardHalberd`, `Quest_Watcher(Side)` | `guardHalberdHonor`, `standLikeGuard` | 12 + 70 | medium | yes | |
| patrol | NPC | M5 movement | profession / quest trees | everywhere | low | no | |
| tournament and spectator crowds | NPC | M2 `tournamentCrowd_standing_1–5`, `_leaning_1–3`, `cheering*`; M4 `TournamentCrowdHappy/Sad` | `so_cheeringSpot`, `cheeringSpot_simple`, `spectatorSpot`; `SO_CheeringSpot_*.lua` | ~120; 150+ links | low | yes | |
| party (standing, duo, leaning, barrel, lying, dancing) | NPC | M2 via `SO_Party_*.lua` (≈ 40 `party_*` unstances) + M3 `party_holdingItem` cup | `special/party/*.xml` | 190 (28 +162); 240 links | medium | yes | duo = two NPCs locked together (`JoinedUnstanceAction`) |
| small-talking watchers | NPC | M2 `smallTalkingWatchers_{front,side}_{leader,minion}` | `so_smallTalkingWatchers` | 2 | low | yes | |
| dialogue pose (standing / sitting) | both | M2 `ingameDialogPose`, `_sitting`, `_OnSpot`; `DialogMood_*` | `speech/*`, `so_dialogPose` | 21 | low | yes | talking itself is out of scope (WO-90) |

### 3.7 Merchants, beggars, sick, wounded, tied, dead

| kind | who | mechanism | where defined | placed / links | weight | pos | notes |
|---|---|---|---|---|---|---|---|
| merchant at a stall | NPC | M2 `seller` ↔ `seller2`; M4 `SellerInvite`, `SellerShopping2Pay` | `profession/seller/so_seller.xml` | 144 (130 +14); 480 links | medium | yes | `housekeeper_shopping` for the customer |
| beggar kneeling / lying | NPC | M2 `BeggarKneeling`, `BeggarLaying`; paired `ParsonGiveAlms*` + `JoinedAnimationAction BeggarTake(Priest)` | `profession/beggar`, `so_beggar` | 59; 177 links | **high** (lying) | yes | |
| sick in bed / lying sick | NPC | M1 lying + M2 `beSick`, `lyingSick_back`, `lyingInjured_bedHigh/Low(_typhus*)` | `special/so_injured.xml`, `SO_LyingHarmed_Healing.lua` | 58 healing SOs + 87 injured SOs; 227 links | **high** | yes | |
| wounded on the ground | NPC | M2 `woundedLying_1–5(_bedLow)`, `lyingWounded_01–05(_highBed)`, `injured` | `SO_LyingHarmed*.lua`, `so_injured` | 35 (10 +25) + above | **high** | yes | `lyingWounded_*` are `UseForDialogueTwin` |
| healer treating a patient | NPC | M2 `heal_healer`/`heal_injured`, `healingLeft/Right`, `healingBedLeft`, `healing_typhus*_master/slave` (paired) | `special/so_injured.xml` | as above | high | yes | |
| sitting injured | NPC | M2 `sittingInjured`, `Sitting_shitty`, `Quest_SynchroSittingWounded` | `special/so_sitinjured.xml` | rare | high | yes | |
| tied up (standing, sitting, lying, wounded, to a pole) | NPC | M2 `beTied_*` (5 variants) + M3 `tiedSpot_rope_cuffs`; player untying = `ActionTrigger` `JoinedAnimation` (`TiedUpOut_Master`) | `special/tiedSpot/beTied.xml` | 77 (73 +4) | **high** | yes | helper `tiedSpot_sittingWithoutPole` 57 |
| pillory | both | M2 `Pillory` + M6 pillory | `special/pillory` | 10 (4 +6) | **high** | yes | |
| hanged | NPC | M2 `hangman_alive` + M6 halter; corpses `deadBody_hanged_*` | `special/hangman` | 1 + 34 hanged corpses | high | yes | |
| posed dead body | NPC | M2 chosen in `DeadBody_Human.lua` / `DeadBody_Horse.lua` / `SO_DeadBody_Human_Hanged.lua`: ≈ 50 `deadBody_*` poses (on back, stomach, side, over a rail / wall / palisade / horse, in a coffin, impaled, sitting against a wall) | `special/deadBody/*.xml` | 473 SO (136 +337; includes the `SO_DeadBody_*` classes and 34 hanged) + 350 plain `DeadBody_Human`; 1,838 links | **high** (a corpse standing) | yes | looted variant `deadBody_male_looting_*` is 2-party |
| looting a body (NPC) | NPC | M2 `Loot`, `LootAligned` | `special/genericLooting` | 26 | medium | yes | |

### 3.8 Animals

| kind | who | mechanism | where defined | placed / links | weight | pos | notes |
|---|---|---|---|---|---|---|---|
| dog: sleep, sit, dig, sniff, eat, bark, howl | NPC | M1 lying / sitting + M2 `animal_dog_sleep1–3`, `_dig`, `_sniff`, `dog_eatingStanding/Laying`; M4 `Bark`, `Howl` | `animal/so_animalBehaviors_dog.xml` | 194 (176 +18); 1,492 links | medium | no | marking spots exist (`DogMarkingSpot`) |
| hare: sleep, eat, stand | NPC | M1 lying + M2 `animal_eat` | `…_hare.xml` | 257; 1,132 links | low | no | |
| deer: lie, eat, roar | NPC | M1 lying + M2 `animal_eat`; M4 `Roaring` | `…_deer.xml` | 272; 494 links | low | no | |
| horse: grazing, idle, drinking at a trough | NPC | M2 `horse_grazing`, `animal_horseDrink` (aligned) | `…_horse.xml`, `animal/so_horseParkingSpot.xml` | 248 + 208; 702 links | medium | yes (trough) | |
| pig: sleep, wallow, sniff, eat at the trough | NPC | M1 lying + M2 `animal_pig_rochneni`, `_sniffing`, `_troughEating` | `…_pig.xml` | 130; 702 links | medium | yes (trough) | |
| cow: sleep lying, eat, low | NPC | M1 lying + M2 `animal_sleepLying`, `animal_eat`; M4 `Mooing` | `…_cow.xml` | 47; 205 links | medium | no | milking pairs with the housekeeper |
| sheep: sleep, eat | NPC | M1 lying + M2 `animal_sheep_sleep`, `animal_eat` | `…_sheep.xml` | 35; 108 links | low | no | |
| boids: chickens, fish, rats, field mice, birds taking off, cats | — | native boid / flock system, no NPC state | `Entities/Boids/*.lua`, `BirdsTakeoff`, `CatHolder` | chickens 176, fish 241, rats 125, mice 330, bird take-offs 426, cats 215 | low | — | local ambience; not worth syncing (inferred) |

### 3.9 Player activities (what the other screen should show)

| kind | mechanism | where defined | placed | weight | pos | notes |
|---|---|---|---|---|---|---|
| sit (bench, chair, ground, table) | M1 via `player:request mode('use')` → `playerAction_stanceObject` | `chair.lua`, `ActionTrigger @ui_use_sit` (4,823) | same seats as NPCs | **high** | yes | chair moves (step-in attach) |
| sleep / lie down | M1 lying via `player_use_sleep` | `Bed.lua`, `BedTrigger` (`@ui_hud_sleep_and_save` / `@ui_hud_sleep`) | 2,383 triggers | **high** | yes | sleep also skips time: host-only in MP (WO-122/133) |
| wait / skip time | UI `SkipTime`; `playerWait.xml` | `SkipTimeCutsceneData` 164 | — | low | no | usually while seated or lying |
| grindstone | M8 | `Grindstone.lua` | 40 | **high** | yes | per-weapon wheel animation |
| smithing (forge builder, smithery) | M8 | `Smithery.lua`, `ForgeBuilderTrigger.lua`; `Blacksmith*` tables in `Libs/Tables/minigame` | 28 | **high** | yes | |
| alchemy | M8 | `AlchemyTable.lua`, `AlchemyItem.lua` (bellows, kettle, mortar are items at the table: 422) | 20 tables | **high** | yes | |
| reading a book | M8 `Minigame.CanStartReadingMinigame` | `Book.lua`; `esReadingQuality` on seats and beds (`bench_notable` 3,252, `bench_table` 1,931, `bed_*` …) | anywhere seated | medium | inherits the seat | reading quality depends on the seat, so the player reads *while* seated |
| transcription | M8 `human:StartBookTranscription` | `TranscriptionTable.lua` | rare | high (seated) | yes | |
| washing at a trough / pier | washing minigame | `WaterTubeActionTrigger` (`CannotWashReason`, soap on piers); `WashingMinigame` node | 442 | medium | yes | pier variant washes clothes (`Hold`) |
| eat / drink from inventory | item use (native) | `UseItem` | — | medium | no | |
| eat from a pot, drink from a barrel | `KettleActionTrigger` sAction | as §3.4 | 642 | medium | yes | |
| cook, dry, smoke food | `FoodProcessingTrigger` | as §3.4 | 546 | medium | yes | |
| dice | M8 `DiceInteractor` + seated M1 | `so_diceTable_new` `interrupt_playerSit` | 109 | **high** | yes | |
| praying | M2 `prayKneelingGround_player` | `so_player_prayingSpot`, `InteractionTrigger` (perk) | 63 | **high** | yes | |
| archery range / contest | shooting state (`actor_state` `shooting`) at `ShootingTarget` | `ShootingTarget.lua`, `dlc2_archery` | 344 targets (20 +324) | low | no | the bow draw is combat (WO-121) |
| stone throwing | M8 `actor_state` `stoneThrowing` | `StoneThrowingNode/Pile.lua` | 32 + 19 | medium | yes | |
| grave digging | M8 `Minigame.StartHoleDigging` | `Hole.lua` | 70 | medium | yes | |
| herb gathering | M8 `Minigame.StartHerbGathering`; `animation/picking.xml`, `item/pickable_area_desc.xml` | vegetation | everywhere | low | no | |
| lockpicking | M8 `Minigame.StartLockPicking`; UI `LockPicking` | doors 1,517, `Stash` 3,853, `Lockpickable` 11 | medium | yes | NPC lockpicking fragments exist too |
| pickpocketing | UI `Pickpocketing` | — | — | low | yes (behind the victim) | |
| looting / chests | `ItemTransfer` UI + chest fragments | `Stash` 3,853 | low | yes | |
| picking up items | `Picking` fragments (`animation/picking.xml`) | `ItemSlot` everywhere | low | no | |
| carrying a body | `actor_state` `carryCorpse` | `player/switch/handleBodyCarrier.xml` | — | **high** (arms full of nothing) | no | |
| riding / horse | M1 horse | `playerAction_horse.xml` | — | high | yes | already synced |
| cart passenger | M1 cart slots `playerFront`, `playerBack` | `CartStanceElement` | 71 carts | high | yes | |
| climbing ladders | ladder state | `animation/ladder.xml` | 366 | high | yes | |
| crouch / stealth | M1 crouch | `playerAction_crouch.xml` | — | medium | no | |
| torch draw | M3 torch + draw action | `playerAction_drawTorch.xml` | — | medium | no | |
| pillory (punishment) | M2 `Pillory` | `special/pillory` | 10 | high | yes | WO-139 crime flow |
| bath-house services | faded time skip + spa unstances | `profession/spa`, `so_bathhouse` | 2 | low | yes | |
| dancing (party quest) | paired M2 `Party_DancingPlayerWithFemale_*` | `special/party/party_dancingPlayerWithFemale_*.xml` | rare | low | yes | |
| fishing | **does not exist for the player** | — | — | — | — | §1.4 |

---

## 4. Other NPC systems that hold a pose

| kind | mechanism | where | count | weight |
|---|---|---|---|---|
| battle groups (kneel with shield, longsword idle, gate attack, battlements, stone throwing, ladders) | M2 `Battle*`, `GateAttack*`, M1 crouch, `MinigameElement BattleArchery/StoneThrowing` | `battles/battlegroupcontroller.xml`, `battle/battleLadderController.xml` | 213 controllers; 2,357 + 224 links; battlements 314 | high inside sieges |
| random events (surrender, cower, loot, look around, cheer) | M2 `CrimeSurrender`, `Cower(Ground)`, `Loot(Aligned)`, `LookingAround`, `FleeLookingAround`, `cheering` | `events/randomEventsBehaviors.xml` | 248; 4,437 links | medium |
| crime reactions (mourn on bench, search corpse, point, go away) | M1 + M2 `cryingOnBench`, `Quest_SittingSad`; M4 `Pointing`, `GuardSearchCorpse`; additive `CrimeGoAway*` | `crime/*` | everywhere, reactive | medium |
| stealth-sleep and hangover teleports | teleport + stance | `player/switch/sleepWalkingTeleport.xml`, `switch.xml::hangoverTeleport` | — | low |

---

## 5. The long tail (unusual, one-off, cutscene-like)

All data-verified by name. Each is an unstance or fragment used by **one**
quest's trees or one placed holder. Syncing them needs nothing beyond M1–M4,
but they are rare enough to accept a fallback.

* **Hostage situations**: male or female kidnapper holds a hostage (calm /
  tense, tied or loose), with the partner's paired unstances and one-shot
  outcomes (`hostageSituation_*`, `Quest_HostageSituation_*`,
  `Quest_FemmeFatal_*`); 3 placed, several quests.
* **Violence set pieces**: drowning in a tub, throwing a victim off a wall,
  pulling a woman to the ground, halberd kill, dagger kill on the ground
  (`QuestAttackDrowning*`, `throwVictimOffWall_*`,
  `QuestWallThrowDown{Master,Slave}`, `killWithHalberd_attacker`,
  `utokNaNebakov_groundDaggerKill`). 2-party, aligned, `JoinedAnimationAction`.
* **Lying under a horse** (`Quest_UnderHorseIdle`, `Quest_LieDownHorseLoop`,
  release pairs); **petting a horse** (`Quest_NPCPettingHorse` + the horse's
  side).
* **Wounded hunter set** (`QuestHunterInjuryIdle*`), **a poisoned noble in
  bed / on the ground** (`predaniVChramu_poisonedAlbik_*`), **a dying
  hermit** (`poustevnik_lyingDying`), **a sleeping wounded soldier**
  (`m49_*`, `WoundedAulitzSleep`).
* **Soldiers hiding behind barricades / crossbow aims / crouch with ranged
  weapon** (`Quest_SoldierHidingBarricade*`, `Quest_Crossbow*IdleAim`,
  `Quest_CrouchWithRangedWeapon`). **Holding a door shut**
  (`m48c_soldierHoldingDoor`). **Towing a cart** (`m42_CartTow`,
  `m48a_CartTow`); **pushing a wagon** (`wagonFrontPush`, `wagonBackPush`,
  explicitly `IsAligned="true"`).
* **Hiding a merchant** in a cart or under stairs (`Quest_BrabantHiding*`).
  **A man sitting in prison** (`Quest_ManSittingPrison`). **A drunk passed
  out** (`passedOutDrunk_*`, `Quest_PassedOutDrunk`, `Quest_DrunkFall`).
* **Quest laundry in a river / on a pier**, **a slide-and-bandit ambush**,
  **a capon breathing / looking around / sliding** (`prepadeni_*`).
* **Wine dealing, tapping wine, spicing food, poisoning a horse, untying a
  body, looking at a stone, picking up a key from a latrine** (single
  holders: `Quest_WineDealing`, `socky_innkeeper_tappingWine`,
  `zaby_spiceUpFood`, `kocovnickaCest_*`, `KeyPickupFromToilet*`).
* **Dancing** (`Quest_Dance01`, party dancing), **a noob's sword play**
  (`noob_sword_training`, `NoobSwordPlay`), **a hero pose**, **an assassin's
  threat**, **showing off a sword** (`sermiri_showOff*`).
* **Commander speech, pointing, synchro-walks** (`CommanderSpeech`,
  `SynchrowalkLoop`, `Zachrana_DarkWoods_SynchroWalk_*`: the player walks in
  lock-step with an NPC).
* **A raven's idle** (`raven_idle`, forge DLC), **the forge DLC's
  chastity-belt idle** (`waiting_idleNoVariations`).
* **Lying under a tree, for the player** (`lyingUnderTree_player`, one level
  reference).
* **Cutscene characters**: 6,068 `AnimChar` entities in layers only, plus
  3,062 `MusicCutscene` / 391 `IngameCutsceneData`. These are
  track-view-driven, not NPC state. A cutscene is its own sync question
  (out of scope here).

---

## 6. Held props

### 6.1 How props get into hands (four routes, data-verified)

| route | what | persistence | count |
|---|---|---|---|
| **M3 hand content** (`HandContentElement`) | a **real inventory item** moved from the NPC's home slot, or from a linked `ItemSlot`, into the left or right hand; `PlaceAction` / `ItemSetAsideElement` put it back or set it aside | for the whole behaviour; survives save/load (`NPCStateSaveLoad` autotests) | 564 element uses; ≈ 70 decision labels outside tests |
| **M7 CAF anim events** | `spawnitem`/`despawnitem` (a temporary item by GUID, into `Right`/`Left`/`thigh`); `pickup`/`place` (from a named slot link: `mortarSlot`, `pestleSlot`, `scoop`); `phaseitem` (changes an item's phase, e.g. bowl contents 1.0 → 0.25); `Attach`/`Detach`, `attachLeft/Right`, `attachobject`/`detachobject` | for the length of one fragment | 245 / 262 / 103 / ≈ 1,100 events |
| **M6 slave objects** | the object itself animates in sync (book pages, chest lid, forge bag, casting tongs, bellows, pliers, barber scissors, ceiling shackles, doors, hangman's halter, well, churn, grindstone wheel) | for the unstance or fragment | 16 `kcd_slave*.adb` + `Animations/assets/*` |
| **Mannequin `AttachProp`** | a CGF bolted onto an attachment point | per fragment | **1** use in all ADBs (an arrow to the quiver). Negligible |

**Consequence for WO-141** (inferred): route 1 is **state** that must be
synced (the item class per hand). Routes 2–3 follow from the fragment, so
replaying the fragment reproduces them, as long as the receiver resolves
the same item GUIDs. The field-case bug is route 1 applied without the
behaviour that gives it meaning.

### 6.2 Props held during activities (M3 decision labels, non-test)

| prop (label) | activity | tree |
|---|---|---|
| hoe (`farmer_hoe`, **Left**) | field hoeing | `profession/farmer/so_field.xml` |
| hoe (`vineyard_hoe`) | vineyard hoeing | `profession/vineyard/hoeing.xml` |
| sword + hammer (`blacksmith_sword` L, `blacksmith_hammer` R) | forging | `profession/blacksmith/so_blacksmith.xml` |
| tongs (`foundry_tongs`) | casting | `profession/blacksmith/so_foundry.xml` |
| axe (`camper_woodChopping`, `axe_cutTree`, `axe_cutBranches`) | chopping, felling | `profession/lumberjack/*` |
| shovel (`miner_shovel`, `shovel_coalThrow`, `shovel_kilnRepair`, `shovel_digGrave`) | mining, coal, kiln, graves | `profession/miner`, `coalman`, quests |
| pickaxe (`miner_pickaxe`) | stone mining | `profession/stonemason/so_mining.xml` |
| sack (`miller_carrySack`), basket (`miner_carryBasket`) | carrying | `profession/miller`, `profession/miner` |
| fishing rod (`fishingRod`, **Right**) | fishing | `profession/fisher/so_fishing.xml` |
| broom (`tavern_broom`) | sweeping | `world/so_sweeping.xml` |
| bucket (`housekeeper_refillWatertube_bucket`, `_cow_bucket`, `_pig_bucket`, `_water_bucket`) | water, milking, pigs, lime water | `world/so_waterTube.xml`, `so_animalCare_*`, `profession/tanner` |
| laundry basket, weaving basket, cream pot | laundry, bench work | `world/so_waterResource.xml`, `so_benchActivity.xml` |
| grain sack, egg basket, a chicken | hen care | `world/so_animalCare_hen.xml` |
| herbs, ingredients, firewood, eating bowl | hearth | `world/so_fireplace.xml` |
| stein, bowls, chicken (`tavern_*`) | tavern | `profession/tavern/bartender.xml` |
| spa herbs, spa stein | bath-house | `profession/spa/so_bath.xml` |
| book (`parson_book`, `scribe_libraryBook`) | reading | `profession/parson`, `profession/bailiff` |
| cup (`party_holdingItem`) | party | `special/party/*` |
| halberd (`guard_halberd`), crossbow (`guard_crossbow`) | guard posts | `special/halberdierGuard`, `profession/guard` |
| torch / light source (`lightSource`, `battle_torch`) | night walks, battles | many |
| rope cuffs (`tiedSpot_rope_cuffs`) | tied / hostage | `special/tiedSpot`, `special/hostageSituation` |
| wine jug, goblet, food (quest-specific labels) | single quests | `quests/*` |

### 6.3 Event-spawned props (M7), by item

From `male.animevents` / `female.animevents` `spawnitem` GUIDs resolved
against the item tables (data-verified): wooden spoon for mash (20 CAFs),
cut logs and half logs (35), small stones (19, mine sorting), wineskin
(11), chicken thigh (10), needle (8, shoemaking and embroidery), potion
(8 + 5), knife (6) and butcher's knife (4), egg tool (6), cleaning rag (5),
rope cuffs (5), arrow for repair (4), curry comb (4), hunting horn (4),
branch (3), tankard (3), flute (3). Two GUIDs (14 and 12 uses) did not
resolve against the item tables.

---

## 7. Method and reproducibility

* Extracted with Python `zipfile` at below-normal priority into the session
  scratch folder. Some level paks store `\`-separated names in the local
  header, so the reader patches `orig_filename`.
* **Behaviour trees**: every `Scripts/AI/**/*.xml` parsed (4,512 trees). Node
  census, then per-tree sets of unstance / stance / fragment / hand / cart /
  minigame / slave, with `Function_*` calls followed transitively (crime,
  switch, speech, stealth and interrupt helpers excluded).
* **Placement**: `objects_mission0.xml` of all three levels (142,423
  entities) plus the streamed `layers/*.xml` (79,261 more after removing 729
  duplicates). Smart-object template = `Properties.guidSmartObjectType` →
  `SmartEntityTemplate DatabaseId` (365 templates placed).
* **Schedules**: `tables/ai/scheduler.xml` of each level (90,256 hubs,
  171,717 links), resolved as described in the header.
* **Unstance references**: literal tree references (394), then a token scan
  of all Scripts/Tables XML and Lua plus all level files for the other 260
  (183 found, 63 found nowhere, 11 test-only, 3 level-only).
* **Not done**: no runtime check. Placement counts say what *can* happen;
  how often each happens on screen depends on schedules, time of day and
  streaming. The `IsAligned` default is inferred from baked fragment data,
  not from code.

**Key files for WO-141** (all in the Modding Tools paks):
`Tables.pak: Libs/Tables/ai/NPCStateUnstanceDatabase.xml`,
`NPCStateActionDatabase.xml`, `NPCStateUnstanceTransitionDatabase.xml`,
`NPCStateStanceAnimDatabase.xml`, `DecisionLabelDatabase.xml`,
`smartEntity/SmartEntity__*.xml`, `ScriptContext.xml`;
`Libs/Tables/action/ActorStanceGroups.xml`, `actor_state.xml`;
`Libs/Tables/animation/anim_fragment.xml`;
`Scripts.pak: Scripts/AI/world/*`, `profession/*`, `special/*`,
`animal/*`, `player/scheduler/*`;
`Scripts/Entities/WH/{Bed,Chair,Minigames}/*.lua`;
`Animations.pak: Animations/Mannequin/ADB/*.adb`,
`Animations/humans/{male,female}/*.animevents`;
`level.pak: objects_mission0.xml`, `layers/*.xml`,
`tables/ai/scheduler.xml`.
