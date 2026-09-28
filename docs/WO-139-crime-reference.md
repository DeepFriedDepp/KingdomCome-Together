# WO-139 reference A — how KCD2 raises a crime (shipped-data research)

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios. The research behind `docs/WO-139-findings.md` §3.

Scope: Scripts.pak (XGenAI behaviour trees, Skald quest modules, Lua), Tables.pak, IPL_GameData concept
definitions. Read-only. No game launched. Every claim is tagged **(data-verified)** (read in the data) or
**(inferred)** (reasoned, or behaviour hidden in native code).

Path legend (all relative to the extracted game paks):
`SW/` = `Scripts/AI/npc/basic/switch/` · `CR/` = `Scripts/AI/crime/` · `PL/` = `Scripts/AI/player/switch/` ·
`QU/` = `Scripts/Quests/Final/Barbora/utils/crime/` · `LUA/` = `Scripts/Scripts/` · `TB/` = `Tables/Libs/Tables/` ·
`DEF` = `IPL_GameData/Libs/concept/definitions.xml` · `DOCS` = Warhorse script_bind.zip in the Modding Tools install
(outside the paks, cited only as support). `:NN` = line in the original XML; `:NNF` = the line sits inside
`<ForestContainer>` (see §0).

---

## Answers first

1. **How each crime becomes an information:** in the observer's own switch brain. The trigger is one of four things: native perception of the local player in a perceivable state (`pickpocketing`, `loot`, `lockpick`, `trespass`, `carriedBody`…), a short-lived crime volume (`theft`, `crime_hit`, `crime_playerMounted`, `crime_graveRobbing`…), a native message (`hitReaction`, `minigamePickpocket`, `hearingInfo`), or an injected `switch:stimulus:*`. `PerceivedWuid` is always the **object** of the crime, never the culprit: the victim, the corpse, the lock, the horse, the stash, theft volume or item, or the home or area. Every path is gated to `$__player` (or `$__playerDog`) and reacts with `ReactionNpc="$__player"`. The exceptions are hit, combat and animalAbuse, whose stimuli carry a culprit field. For hit and combat that field only turns into an information when it is the player. (data-verified)
2. **What an information carries:** `{label, perceivedWuid, position}`, held by one or more NPCs. Everything else is a tagged dynamic value: `victim`, `metadata{createdAt, createdBy}`, `items`, `value`, `theftMethod`, `kind`, `fine`, `eyeWitnesses`, and so on. No tag names a culprit. Labels are the 30 rows of `TB/rpg/crime.xml`, and the label *is* the crime type. (data-verified)
3. **How witnesses learn:**
   - Full recognition of the player's state, or of a volume, or hearing a sound.
   - The `crime_stimulus` mailbox is a prefix filter `type="switch:stimulus"` (`TB/ai/mailbox_filter.xml:37`). `CR/processStimulusMessages.xml` drains it, but only for 9 types.
   - Shipped senders: the `QU/*/pushstimulus_*` Skald modules, 3 direct quest nodes and 1 AI tree. No Lua sender ships.
   - Every shipped `pushstimulus_hit` call wires `attacker=player`. (data-verified)
4. **Spreading and consequences:**
   - Reacting NPCs emit the information natively: 10 m for crimes, 25 m for the alarm, 40 m in long mode. Reporters walk to an authority, security or mate and `TransferInformation`. Receivers keep it only if the sender is a friend.
   - Reputation hits use the row `auto_witness_crime_<label>` per witness and `auto_faction_crime_<label>` per settlement faction when an authority learns. Reputation is always NPC or faction → player.
   - "Wanted" is native: `IsWanted`, the `Cpp:Wanted` buff, and `crime_disableWantedStatus`, which has no tree use.
   - Crime levels run none < offence < trespass < property < violent < murder.
   (data-verified; the native internals are inferred)
5. **NPC culprits: no.** An engine crime has no culprit slot, and every crime information in an NPC's memory is treated as the local player's. `PlayersCrimeInformationKnown` counts any `isCrime` info; arrest, self-help, criminal recognition and punishment all target `$__player`.
   - The closest thing is **hostility without a crime**:
     - a hit with a non-player attacker leads to a skirmish decision against that attacker;
     - NPC-only combat leads to join, flee or watch;
     - enemy-on-sight;
     - `crime:attackInitiatedByConcept`;
     - `combat_forcedTarget`.
   - Animal abuse is the only path where a non-player culprit is both a reaction target and stored (inside the `stimulusAnimalAbuse` value). The information still counts against the player downstream. (data-verified, with the downstream effect inferred)
6. **Crime-legality contexts:**
   - **Victim-side:** `crime_ignoredNPCHitVolume`, `crime_ignoredCorpse`, `crime_ignoredUnconsciousBody`, `crime_ignoredPickpocket`, `crime_legalToLoot`, `combat_ignoreMurderedByPlayer`, `crime_animal_legalToKill`, `crime_ignoredAnimalHitVolume`.
   - **Horse-side:** `crime_ignoredHorseTheft_Horse`, `switch_horse_enableMountIsLegal`, `crime_ignoreHorseTheftInSettlement`.
   - **Observer-side:** everything else, including `crime_disableReport`, which only stops *that* NPC from reporting. Relation contexts run observer → victim.
   - `IsLegalToLoot` is a soul scriptbind that picks the Lua loot prompt. (data-verified)
7. **Where the Lua interaction layer lives:**
   - `BasicAIActions:GetActions`: pickpocket, knock-out, stealth kill, mercy kill.
   - `BasicActor:AddLootAction`: `IsLegalToLoot`.
   - `PickableItem:GetActions`: `CanSteal` / `OnSteal`.
   - `Stash:GetActions` / `UsesStealUiPrompt`: steal prompt and lockpick.
   - `AnimDoor:Lockpick`, `Lockpickable:OnUsedHold`.
   - `Horse:GetActions`: "mount and steal" when the horse is not the player's and `IsMountLegal()` is false.

   These functions only choose the prompt and call a native `Request*`, `OnSteal`, `Mount` or `Minigame` function. The crime itself is raised by native state and the AI trees. (data-verified)

**Corrections to the earlier note:**
- (a) `CR/createInformation.xml` is **obsolete**. Its `<Root>` is `<ErrorNode Message="'Function is obsolete. Use its code successor.'">` (`:18`). The quoted `<CreateInformation … ToWhom="$this.id">` sits in the detached forest. The live node is the native **`CreateInformationWrapper Label= PerceivedWuid= PositionType= PositionVec3= PositionWuid= Information=`**, with 87 executed uses.
- (b) Only `<Root>` executes. `<ForestContainer>` holds editor-detached nodes. The 35 obsolete trees (34 in `CR/` plus `SW/chooseReaction.xml`) keep their old logic only there (§0).
- (c) "An injected crime counts as the local player's" holds for theft, murder, kettlePoisoning, escalatedTrespass, disturbance and information. For **hit** and **combat** it holds only when the attacker or a participant is the player or the player's dog. Otherwise no information is created at all.
- (d) The other `switch:stimulus:*` types are routed into `crime_stimulus` by the prefix filter but never consumed: lockpick, pickpocket, horseTheft, corpseViolation, trespass, unconsciousBody, frisk, offence and the rest.

---

## 0. Reading notes (how the trees execute)

- **Root vs Forest (data-verified):** every tree file has `<Root>`, which executes, and an optional `<ForestContainer>`. The editor copy of the forest is literally called `<Forest>`, so these are nodes not wired to the root.
  - Example: `CR/createInformation.xml:18` Root = `ErrorNode "Function is obsolete. Use its code successor."`. The old `RetrieveInformation → CreateInformation → ExecuteReputationHitWitness → metadata` chain is at `:24F–:48F`.
  - The same pattern holds for 35 trees whose logic moved to native nodes: `createCombatInformation`, `createAssaultInformationsFromLastHits`, `playersCrimeInformationKnown`, `getMostImportantCrimeInformation`, `isInformationRelatedToNpc`, `executeReputationHit`, `affectFeelings`, `chooseReportDestination*`, `checkReactionLimit`, `getCrimeLevelByLabel/ByStimulusKind`, `isHorseRelevant`, `isPlayerOnStolenRelevantHorse`, `SW/chooseReaction.xml`, and others.
  - The forests are still the best available spec of what the native successors do. I cite them as "old logic" (data-verified for the old logic; that the native node behaves the same is inferred).
- **Per-NPC entry point** is `SW/switch.xml` (the "switch" subbrain). The inboxes it consumes (data-verified):
  - `perceptionInfo` → `handlePerception` → `handleAwareness` (`:159`).
  - `informationDiff` → `handleInformationDiff` (`:70`).
  - `hitReaction` → `handleHitReaction` (`:320`).
  - `minigamePickpocket` → `handlePickpocket` (`:390`).
  - `lockpickedDoor` → `handleLockpickedDoor` (`:414`).
  - `crime_attackInitiatedByConcept` → `callInterrupt_attack` (`:485`).
  - `Function_crime_processStimulusMessages` (`:567`) for `crime_stimulus`.

  The hitReaction, pickpocket and stimulus loops sit under `NonMonsterLODBehaviorBarrier RunLogic="Halt"` (`:309`), so NPCs in monster-LOD do not process them. `processStimulusMessages` starts with `ClearInbox inbox="'crime_stimulus'"` (`CR/processStimulusMessages.xml:41`). (data-verified; that messages sent during MLOD are lost is inferred)
- **Recognition gating** (`SW/handlePerception.xml`) (data-verified):

  | Perception | Result |
  |---|---|
  | `threshold > 0 & < 1` | `EntityContext switch_recognitionLevel_I` |
  | `threshold == 1`, outside the recognition border | `switch_recognitionLevel_II` |
  | `threshold == 1`, inside the border | full awareness |

  Almost every crime handler returns `Success` or plays only a stealth "recognizing/checking" reaction at level I or II. **Information is created only at full recognition.**
- **The player is an "NPC" to the trees:** `GetType … TreatPlayerAsNPC="true"` (`SW/handleAwareness.xml:38`). All player-specific crime states are then tested inside one block, `IfCondition condition="$awareness.perceptible == $__player"` (`SW/handleAwareness.xml:87`). (data-verified)

---

## 1. Per crime kind: trigger → handler → information

`ChooseReaction` is the native reaction picker (attack, arrest, report, flee, watch, selfhelp…). Its `ReactionNpc` parameter is the only place a "culprit" enters the crime pipeline.

| Crime | Trigger (what the NPC must get) | Handler chain | Information created | Player gate(s) |
|---|---|---|---|---|
| **Theft from container/chest** | Perceivable `'loot'` on `$__player` (`SW/handleAwareness.xml:98`) + native link `'loot'` from `$__player` to the stash | `SW/handleAwareness_loot.xml` → `CR/checkReactionToTheftFrom.xml` → `CR/createTheftInformation.xml` → `SW/handleStimulusTheft.xml` | `'theft'`, **PerceivedWuid = stash**, position = player (`PositionWuid="$__player"`, `createTheftInformation.xml:152`), `victim=$owner` (`:165`), `theftMethod=loot` (`handleAwareness_loot.xml:115`) | `GraphSearch Origin="$__player"` (loot link); `condition="$owner ~= $__null & $owner ~= $__player"` + observer `Flag="friend"` of owner (`checkReactionToTheftFrom.xml:12`); `ReactionNpc="$__player"` (`handleStimulusTheft.xml:114`) |
| Theft from container, discovered later | Owner's daily/stash checks: `PL/handleLoot.xml:32` sends `crime:dailyCheck:chestEntry` to "Mrkev" (crime manager linked from `$__land`); `CR/makeDailyChecks.xml` → `checkHomeStashes` | `SW/interrupt_checkStash.xml`, `SW/interrupt_checkHomeStashes.xml` | `'nonAttributedCrime'`, PerceivedWuid = stash, `victim` = stash owner (`interrupt_checkStash.xml:52`) | Unattributed. It becomes the player's via hot-entity or suspicion (§3) |
| **Theft of item picked up in world** | Lua `self.item:OnSteal(user.id)` (`LUA/Entities/Items/PickableItem.lua:121`, hold "@ui_hud_stealItem"). Native code leaves a `'theft'` volume (type UNK) with link `'stealData'` {owner, timestamp, count, instance/class}. The spawn is not in any tree (inferred) | `SW/handleAwareness.xml:209` → `SW/handleAwareness_theft.xml` → `createTheftInformation` / `handleStimulusTheft` | `'theft'`, **PerceivedWuid = the theft volume** (`pivot=$volume`) (quest pushes use the item). Immediate → position = player. Else `'nonAttributedCrime'` | Gate first: `checkReactionToTheftFrom considerIfLegalToLoot="true"` (observer is a friend of the owner, owner ≠ `$__player`, no `crime_legalToLoot` on the owner). Then **immediate** if `$now < $data.timestamp + crime_theft_autoAttributionTimer` (2500 ms, `handleAwareness_theft.xml:60`), or a `'theft'` info already exists on the volume, or the owner is me or a mate and `HasSeenPlayer … FullyAwareOnly="true"` (`:70`). Owner or mates otherwise get **attributed** (long-term trespass memory of the home, or `crime_autoAttributeTheftToPlayer` on the observer, `:87`) or **nonAttributed**. Other friends outside the 2.5 s window do not react |
| Stolen goods seen later | Perceivable `'VisibleStolenEquipment'` on `$__player` (`handleAwareness.xml:108`); haggling (`crime:theftDetectedDuringHaggling`) | `SW/handleAwareness_stolenEquipment.xml` (`GetVisibleStolenEquipment`, `:40`); `CR/createtheftsfromshop.xml` → `CR/findstolenitems.xml` | `'theft'`, `method=seenEquipped` / `pick` | `findstolenitems` searches only `Origin="$__player"` (`:27`) and the player's horse (`GetPlayerHorse`) |
| **Pickpocketing** | Witness: perceivable `'pickpocketing'` on `$__player` (`handleAwareness.xml:93`) + link `'pickpocketing'` from `$__player` to victim. Victim: `minigamePickpocket` message with event `failedPouch`/`failedSatchel` | Witness `SW/handleAwareness_pickpocket.xml`; victim `SW/handlePickpocket.xml` (`$stimulus.pivot = $this.id`, `:22`); both → `SW/handleStimulusPickpocket.xml` | `'pickpocket'`, **PerceivedWuid = victim** (`:39`), dyn `victim`, `bagType`. Later `SW/interrupt_checkPockets.xml` → `'theft'` (method pickpocket) | `GraphSearch Origin="$__player"` (`handleAwareness_pickpocket.xml:27`); witness must be `Flag="friend"` of victim (`:40`); `ReactionNpc="$__player"` (`handleStimulusPickpocket.xml:55`) |
| **Lockpicking (doors, chests)** | Perceivable `'lockpick'` on `$__player` (`handleAwareness.xml:103`) + native link `'lockpick'` from `$__player` to the lock; or hearing `sound.lockpick` (`SW/handleStimulusSound.xml:524`) | `SW/handleAwareness_lockpick.xml` → `SW/handleStimulusLockpick.xml` | `'lockpick'`, **PerceivedWuid = lock** (door/stash) (`:36`), `victim` = owner, `lockType` | `SubGraph="'lockpick'"` from `$__player` (`handleAwareness_lockpick.xml:59`); legal if link `crime_lockpickIsLegal` exists (`:85`); `ReactionNpc="$__player"` (`handleStimulusLockpick.xml:74`) |
| Lockpicked door found later | `lockpickedDoor` inbox (door data) | `SW/handleLockpickedDoor.xml` → `callInterrupt_checkProperty` (`:30`); Lua `AnimDoor:SpawnSuspiciousVolume` (`'crime_suspiciousDoor'`, 24 h) → `SW/handleAwareness_suspiciousDoor.xml` → `handleStimulusTrespass` | No crime info directly (investigation/trespass) | Investigation assumes the player (hot entity `$__player`) |
| **Trespass** | Perceivable `'trespass'` on `$__player` (`handleAwareness.xml:137`) | `SW/handleAwareness_trespass.xml` → native `DetermineTrespassReaction Observer="$this.id" Target="$__player"` (`:33`) → escalated: `SW/handleStimulusEscalatedTrespass.xml` (native `CreateTrespassInformation`); not escalated: `handleStimulusTrespass` (warn/watch, no info) | `'trespass'`, **PerceivedWuid = home / trespassArea / `$__player`** (`CR/createTrespassInformation.xml:65`), position = player position, `victim` = home owner or observer | `Target="$__player"`; `ReactionNpc="$__player"` (`handleStimulusEscalatedTrespass.xml:118`). Plus a *complementary* trespass info after most crimes via `CR/assessTrespass.xml:25` (player in personal+ area) |
| **Assault (victim)** | Native `hitReaction` {attacker, hitType, hitStrength} | `SW/handleHitReaction.xml` (`CR/translateHitReaction.xml` → attackKind) → `SW/handleStimulusHit.xml` | Native `CreateCombatInformation Victim="$this.id" AttackKind= DirectHit=` (`handleStimulusHit.xml:232,442`) → label `'assault'`/`'aggression'`/`'assaultByDog'`/`'resistingArrest'` (old logic `CR/createCombatInformation.xml:46F,49F`), **PerceivedWuid = victim** | Only in the branch `condition="$stimulus.attacker == $__player \| $stimulus.attacker == $__playerDog"` (`:61`). Any other attacker → `DecideSkirmishReactionCrime … RelationOverride="Hostile" TargetNpc="$stimulus.attacker"` (`:378`), no info. The victim only enters handleStimulusHit if the attacker is an `enemy` or `$__player`/`$__playerDog` (`handleHitReaction.xml:303,308`) |
| **Assault (witness)** | `'crime_hit'` volume (2 s) spawned by the victim (`SW/handleHitReaction_spawnVolume.xml:21`) carrying `crime:hitVolume` {attacker, target, kind…}. Also seeing any NPC in perceivable `'combat'` → `SW/handleAwareness_combat.xml` | `handleAwareness.xml:229` → `SW/handleAwareness_hitVolume.xml` → `SW/handleStimulusCombat.xml` | Native `CreateAssaultInformationsFromLastHits` (`handleStimulusCombat.xml:122`): old logic walks `GraphSearch Origin="$__player"` links `'crime_lastHitByPlayer'` (`CR/createAssaultInformationsFromLastHits.xml:39F`) → `'assault'`/`'corpseViolation'`/`'assaultAnimal'`, PerceivedWuid = victim | Volumes are spawned only for `$hitReaction.attacker == $__player \| $__playerDog` (melee, `handleHitReaction.xml:255`), `== $__player` (missile), or with the attacker **hard-coded** `$__player` (stealth `:197`, criminal horse collision `:169`). Combat info only if `condition="$playerIsInSkirmish \| $playerDogIsInSkirmish"` (`handleStimulusCombat.xml:106`); else the skirmish decision uses `information=""` (`:440–451`) |
| **Murder** | Witness: `'crime_hit'` volume whose target is dead + friend + `condition="$volumeData.attacker == $__player \| $__playerDog"` (`handleAwareness_hitVolume.xml:152`). Discovery: perceivable `'dead'` on an NPC (`handleAwareness.xml:176`) | → `SW/handleStimulusMurder.xml`; discovery `SW/handleAwareness_corpse.xml` (+ `CR/createCorpseInformation.xml`) | `'corpse'` (not a crime) + `'murder'`/`'murderByDog'`, **PerceivedWuid = corpse** (`handleStimulusMurder.xml:45`), dyn `victim=corpse`, `killedByHorse`, `bodyIsCarried` | `ReactionNpc="$__player"` (`handleStimulusMurder.xml:70`). Discovery attributes only if link `'crime_lastHitByPlayer'` from `$__player` is fresh (`crime_lastHit_expiration_time` 360 s, `handleAwareness_corpse.xml:60,126`), or assault/theft on that corpse is already known, and `HasSeenPlayer` + `isPlayerSuspicious`. Else `'nonAttributedCrime'` (`:306`) or just `'corpse'`. `combat_ignoreMurderedByPlayer` on the victim cancels it (`handleAwareness_hitVolume.xml:175`) |
| Carrying/holding a body | Perceivable `'carriedBody'`/`'heldBody'` on `$__player` (`handleAwareness.xml:147,152`). A perceived NPC that has a `bodyCarrier`/`bodyHolder` link is **re-targeted** to the player: `$awareness.perceptible = $__player` (`:71`) | `SW/handleAwareness_bodyCarrier.xml` / `bodyHolder.xml` → murder / corpseViolation / assault (`CreateCombatInformation Victim="$body"`) | As murder / assault | `carrier="$__player"`; freshness of `crime_lastHitByPlayer` (`handleAwareness_bodyCarrier.xml:101…`) |
| **Knockout / takedown** | Lua `BasicAIActions:OnKnockout` → `user.actor:RequestKnockOut(self.id)`. Victim gets a `MeleeStealth` hitReaction | `handleHitReaction.xml:193–205` stealth branch → volume + `AddLink From="$__player" … 'crime_lastHitByPlayer'`. Discovery → `SW/handleAwareness_unconsciousBody.xml`. Waking → `SW/state_unconscious.xml`. Failed takedown → `SW/handleStealthKillResult.xml` (`$stimulusHit.attacker = $stealthKillResult.attacker`, `:23`) | Discovery: `CreateCombatInformation Victim="$body"` (`:205`) with stimulusKind `takedown` (`:161`), or `'nonAttributedCrime'` (`:330`). Waking: `CreateCombatInformation Victim="$this.id"` if link `crime_preUnconsciousnessLastHit` from `$__player` exists (`state_unconscious.xml:65,75`) → `checkPockets attributeToPlayer="true"` (`:81`) | **Stealth branch hard-codes the culprit:** `$hitVolumeData.attacker = $__player` with no attacker check (`handleHitReaction.xml:197`). Discovery: `$stimulusCombat.participant1 = $__player` (`:209`) |
| **"Mercy kill"** | Lua `BasicAIActions` offers "@ui_hud_mercy_kill"/"…_unconscious" on dead/unconscious NPCs → `user.actor:RequestMercyKill(self.id)` (`LUA/Entities/AI/Shared/BasicAIActions.lua:91,251`) | No dedicated tree or label. The kill is a hitReaction/corpse like any other (inferred) | Falls under `'murder'` / `'corpseViolation'` (inferred) | As murder. "mercy" in the trees (`SW/interrupt_mercy.xml`, `CR/mercy/*`) means an NPC *yielding*, which is unrelated (data-verified) |
| **Corpse looting / looting an unconscious body** | `'loot'` state on `$__player` whose loot link points at an NPC (`handleAwareness_loot.xml`: `$stashType == 'NPC'`) | `SW/handleAwareness_lootCorpse.xml` (+ `handleAwareness_corpse` / `handleAwareness_unconsciousBody` on the same body) | `'theft'` with `method=lootCorpse` (dead) or `lootUnconsciousBody` (+ wake-up follow-up) (`:30`), owner = pivot = **body** | Legal if `crime_legalToLoot` on the corpse (`:13`); witness must be `Flag="friend"` of the body |
| Corpse violation | Hitting an old/resolved corpse (`crime_corpseViolationTimer` 10 s) via hit volume; carrying | `SW/handleStimulusCorpseViolation.xml` | `'corpseViolation'`, PerceivedWuid = corpse (`:41`) | Volume gate as murder; `ReactionNpc="$__player"` (`:53`) |
| **Horse theft** | The player brain spawns a `'crime_playerMounted'` volume (2 s) when `condition="$mount ~= $playerHorse"` (`PL/switch.xml:155,159`); or the observer sees `$__player` riding: link `'mount'` from `$__player` | Volume → `SW/handleAwareness_playerMountedVolume.xml` (immediate); sighting → `SW/handleAwareness_playerMount.xml` (`Origin="$__player"`, `:17`) → native `IsHorseRelevant` (`:39`) → `SW/handleStimulusHorseTheft.xml` | `'horseTheft'`, **PerceivedWuid = horse** (`:32`), `victim` = owner of the horse's home (`:41`), `immediate` | `ReactionNpc="$__player"` (`:53`). Old relevance logic: mate of the horse, or authority of the same settlement (`CR/isHorseRelevant.xml:34F`) |
| **Poaching** | Animal-side `'crime_animal_hit'` volume; carcass looting | `SW/handleAwareness_animal_hitVolume.xml` → `SW/handleStimulusAnimalAbuse.xml` (`isPoaching`) → gamekeeper → `CR/createOrUpdatePoachingInformation.xml` | `'poaching'`, **PerceivedWuid = animal** (`:38`), dyn `crimeType{assault,murder,theft}`, `victim=$this.id` (gamekeeper). Domestic animals → `'assaultAnimal'`/`'murderAnimal'` with `victim` = home owner | `condition="$volumeData.attacker == $__player \| $volumeData.attacker == $__playerDog"` (`handleAwareness_animal_hitVolume.xml:40`); only `crime_isGameKeeper` observers react to wild-animal kills (`handleStimulusAnimalAbuse.xml:75`); carcass looting sets `$stimulus.culprit = $__player` (`handleAwareness_animal_lootCorpse.xml:36`) |

Other labels follow the same object-not-culprit rule (data-verified):
- `friskRefusal`, `drawnWeapon` and `sneak` use `PerceivedWuid="$__player"`: `SW/handleStimulusHit.xml:190`, `SW/interrupt_frisk.xml:175`, `CR/createCrimesFromOffences.xml:32`, the last fed by `CR/getPlayersOffences.xml` (`npc="$__player"`, `:47`).
- `graveRobbing` uses the grave (volume from Lua `Hole.lua:142`).
- `kettlePoisoning` uses the kettle, `pilloryBreak` uses the volume, `disturbance` uses `$stimulus.perceivedWuid`.
- `alarm` and `motivation` use the NPC itself (`$this.id`).
- All of them react with `ReactionNpc="$__player"`: `SW/handleStimulusDisturbance.xml:50`, `…GraveRobbing.xml:40`, `…KettlePoisoning.xml:33`, `…PilloryBreak.xml:35`, `…Threat.xml:55`, `…Aim.xml:47`, `…Information.xml:325`.

Only three handlers pass a non-constant culprit to `ChooseReaction` (data-verified):
- `ReactionNpc="$stimulus.attacker"` (`SW/handleStimulusHit.xml:297,301`, player/dog branch only);
- `ReactionNpc="$reactionNpc"`, which is `$__player` or `$__playerDog` (`SW/handleStimulusCombat.xml:219`);
- `ReactionNpc="$stimulus.culprit"` (`SW/handleStimulusAnimalAbuse.xml:181`).

---

## 2. The information record

**Fields (data-verified).** The only struct fields are `perceivedWuid`, `label` and `position`, exposed as the message ports `Content_information_perceivedWuid`, `Content_information_label` and `Content_information_position` (`DEF` `InstantSendMessage_switch_stimulus_information` / `…_theft` / `…_murder`).
- `ToWhom` is a creation parameter naming the holder. In the old logic it is `ToWhom="$this.id"` (`CR/createInformation.xml:28F`). It is not a field.
- An information is identified by `(perceivedWuid, label)`: `RetrieveInformation PerceivedWuid= label=` takes only those two.
- It is shared between holders: `GetKnownInformations Holder=`, `TransferInformation`, `CheckInformationKnowledge`, `DestroyInformationFromHolder`, `DestroyInformationCompletely`. (data-verified node names; the shared-object model is inferred)

**Position** is set through `PositionType` = `perceivedWuid` | `positionVec3` | `positionWuid` (`crime_createInformationPositionType`). Theft uses `PositionWuid="$__player"` when immediate; trespass uses the player's position. (data-verified)

**Dynamic values** are tag → value, written under `LockDynamicInformationValues` (data-verified, executed roots):

| Tag | Set by / meaning |
|---|---|
| `metadata` (`crime:informationMetadata`) | Old logic: `GetTime … OutVar="$metadata.createdAt"`, `$metadata.createdBy = $this.id` for brand-new infos (`CR/createInformation.xml:47F–48F`); read by `CR/checkFreshlyAttributedInformation.xml` against `crime_veryFreshCriminalThreshold` (6000 ms) |
| `victim` | Owner/corpse/pivot/home owner/observer (`createTheftInformation.xml:165`, `handleStimulusMurder.xml`, `handleStimulusHorseTheft.xml:41` …). Read by `CR/determineCrimeVictim.xml:17,23` |
| `items`, `value`, `theftMethod`, `immediate`, `kettleType`, `guid`, `stolenPrice` | Theft (`CR/createTheftInformation.xml`, `SW/handleStimulusTheft.xml`) |
| `kind` (attack kind), `confiscation` | Combat infos (old `createCombatInformation` logic) |
| `bagType` | Pickpocket |
| `lockType` | Lockpick |
| `bodyIsCarried`, `killedByDog`, `killedByHorse`, `seenBy` | Murder/corpse |
| `stimulusKind` | `nonAttributedCrime`/`alarm` (what kind of crime it was) |
| `informationNotAttributedBy` | NPCs that failed to attribute it (`CR/insertnpccannotattributenonattributedcrime.xml`) |
| `eyeWitnesses` | `CR/addEyeWitness.xml` |
| `fine`, `deadOrSprayed` | `CR/calculatePunishment.xml:223–314` |
| `alreadyGotRepHit`, `factionsAlreadyGotRepHit` | Reputation de-dupe (old `executeReputationHit` logic) |
| `alreadyAlarmedNpcs`, `alreadyReactedToCrimeNpcs`, `urgent`, `crimeInformation` | Alarm/emit bookkeeping (`CR/emitInformation.xml:99,102`; `SW/handleStimulusInformation.xml`) |
| `noninvestigable`, `robbedWhileUnconscious`, `complementaryToOtherCrime`, `isKzikTrespass`, `relatedToEveryone`, `crimeType`, `animal*`, `attributedCrime`, `stimulusAnimalAbuse`, `stimulusUnknownShooter` | Misc |

**No dynamic value names a culprit** (no `culprit`, `attacker`, `criminal` or `perpetrator` tag is ever set). The one partial exception is `stimulusAnimalAbuse`, which stores the whole `switch:stimulus:animalAbuse` struct, and that struct has a `culprit` member (`SW/handleStimulusAnimalAbuse.xml:169`). Downstream it is read for barks and metaroles (`CR/attack|arrest|report/startBark.xml`, `CR/getCrimeMetaroleLabel.xml`) and by `interrupt_scan` for a position. (data-verified)

**The crime table** (`TB/rpg/crime.xml`). Columns: `label, importance, isCrime, isSpreadable, isViolent, expiration, fine, jail, confiscation, metaroleLabel, scalingWithSocialClass, ui_name`. Fines are in decigroschen: `LUA/Systems/Crime.lua` says "price is fine value in decigroschen", and `CR/calculatePunishment.xml` converts a horse's `NominalPrice` "into decigroschen". The units of `jail` and `expiration` are not stated (probably days, inferred). (data-verified)

| label | isCrime | importance | fine | jail | expiration | confiscation | isViolent | isSpreadable | scalesWithSocialClass | metarole |
|---|---|---|---|---|---|---|---|---|---|---|
| aggression | true | 110 | 750 | 1 | 2 | | true | true | true | AGRESE |
| alarm | false | 0 | | | | | | false | | |
| animal_alarm | false | 1 | | | | | | false | | |
| animal_howl | false | 1 | | | | | | false | | |
| assault | true | 130 | 1500 | 5 | 5 | | true | true | true | ASSAULT |
| assaultAnimal | true | 91 | 0 | 1 | 2 | | true | true | false | NASILI_NA_ZVIRETI__ASSAULT |
| assaultByDog | true | 124 | 1500 | 5 | 5 | | true | true | true | ASSAULT_PSEM |
| corpse | false | 0 | | | 7 | | true | true | | MRTVOLA |
| corpseViolation | true | 170 | 2000 | 5 | 4 | true | true | true | true | HANOBENI_MRTVOLY |
| disturbance | true | 10 | 100 | | 1 | | | true | | VYTRZNOST |
| drawnWeapon | true | 20 | 250 | | 1 | | | true | | VYTAZENA_ZBRAN |
| friskRefusal | true | 40 | 100 | 1 | 1 | true | | true | | |
| graveRobbing | true | 85 | 2000 | 3 | 4 | true | | true | false | VYKRADANI_HROBU |
| horseTheft | true | 95 | 2000 | 3 | 4 | false | | true | false | KRADEZ_KONE |
| kettlePoisoning | true | 120 | 1750 | 3 | 5 | | | true | false | OTRAVA_KOTLIKU |
| lockpick | true | 70 | 600 | 3 | 4 | true | | true | false | LOCKPICK |
| missingNpc | false | 3 | | | | | | true | | ZMIZELE_NPC |
| motivation | false | 1 | | | | | | false | | |
| murder | true | 190 | 20000 | 7 | 7 | true | true | true | true | VRAZDA |
| murderAnimal | true | 100 | 0 | 1 | 4 | true | true | true | false | NASILI_NA_ZVIRETI__MURDER |
| murderByDog | true | 180 | 20000 | 7 | 7 | | true | true | true | VRAZDA_PSEM |
| nonAttributedCrime | false | 2 | | | 0.2 | | | false | | |
| perkFineReduction | false | 0 | 0 | 0 | | false | | true | | |
| pickpocket | true | 80 | 550 | 2 | 4 | true | | true | true | KRADEZ |
| pilloryBreak | true | 160 | 1000 | 1 | 2 | | | true | false | OSVOBOZENI_Z_PRANYRE |
| poaching | true | 125 | 7500 | 1 | 5 | true | | true | false | PYTLACTVI |
| resistingArrest | true | 0 | 750 | 5 | 1 | | true | true | false | ODPOR_PRI_ZATYKANI |
| sneak | true | 15 | 50 | | 1 | | | true | | |
| theft | true | 90 | 500 | 3 | 4 | true | | true | false | KRADEZ |
| trespass | true | 50 | 250 | 1 | 2 | true | | true | false | TRESPASS |

**Label → crime type and level.** The label *is* the crime type. The trees read `$data:crime[label].isCrime` (62×), `.metaroleLabel`, `.isViolent`, `.importance`, `.fine` and `.scalingWithSocialClass`; `calculatePunishment` also reads `.jail` and `.confiscation`. (data-verified)

- **Crime level.** Old `CR/getCrimeLevelByLabel.xml` logic (now native `GetCrimeLevelByLabel`):

  | Level | Labels |
  |---|---|
  | murder | `murder`, `murderByDog` (`:19F`) |
  | violent | `assault`, `assaultByDog`, `aggression`, `corpseViolation`, `corpse`, `poaching`, `kettlePoisoning`, `resistingArrest` |
  | property | `graveRobbing`, `horseTheft`, `lockpick`, `pickpocket`, `pilloryBreak`, `theft`, `assaultAnimal`, `murderAnimal` |
  | trespass | `trespass` |
  | offence | `disturbance`, `friskRefusal`, `drawnWeapon`, `forbiddenEquipment`, `sneak`, `nonAttributedCrime` |

  (data-verified old logic)
- **Victim resolution** (`CR/determineCrimeVictim.xml`, live):
  - No victim ("public" crimes): `drawnWeapon`, `forbiddenEquipment`, `friskRefusal`, `graveRobbing`, `pilloryBreak`, `resistingArrest`, `sneak`.
  - Victim taken from the `victim` tag: all the rest.

  `forbiddenEquipment` appears here and in `CR/getStimulusKindFromString.xml:60` but has **no row** in `crime.xml`. (data-verified)

---

## 3. Witnesses, volumes, hearing, and the `crime_stimulus` mailbox

**What an NPC must perceive (data-verified).**

- **(a) The local player at full recognition** with a native perceivable state. Every such state is tested with `PerceivedWUID="$__player"` (`SW/handleAwareness.xml:93–152`):
  - `'pickpocketing'`, `'loot'`, `'lockpick'`, `'VisibleStolenEquipment'`, `'aim'`, `'threat'`, `'trespass'`, `'sleep'`, `'heldBody'`, `'carriedBody'`;
  - plus friskable, offences, player-mount and bandit near-trespass.

  **No tree tests these states on any other entity.**
- **(b) A perceptible volume** (type `UNK`), dispatched by its label:

  | Volume label | Spawner | Lifetime |
  |---|---|---|
  | `'theft'` + link `'stealData'` | native (inferred) | — |
  | `'crime_hit'` + link `'crime_hit'` data `crime:hitVolume` | `SW/handleHitReaction_spawnVolume.xml` | 2 s, visibility 1 |
  | `'crime_animal_hit'` | animal hit reaction | 3 s |
  | `'crime_playerMounted'` | `PL/switch.xml` | 2 s |
  | `'crime_corpseMissing'` | `state_dead` | 5 min |
  | `'crime_missingNpc'` | — | 20 min |
  | `'crime_pilloryBreak'` | — | 2 min |
  | `'crime_kettle*'` | — | — |
  | `'switch_arrowTouchdown'` | player arrows | 4 s |
  | `'crime_suspiciousDoor'` | Lua `AnimDoor:SpawnSuspiciousVolume` | 24 h |
  | `'crime_graveRobbing'` | Lua `Hole:OnUsed` | 10 s |
  | `'dead'` | monster-LOD corpses | — |

  Handled volumes are then `IgnorePerception`'d.
- **(c) Any NPC** whose perceivable state is `'combat'`, `'dead'` or `'unconscious'`, or `'crime_mourn'`/`'crime_arrest'`. These are the only states checked on non-player humans (`handleAwareness.xml:82,176,181`; `handleAwareness_combat.xml`). Animals (`NHNPC`) are also checked for `dead`, `unconscious`, `combat` and `crime_animal_startled`. The enemy relationship (`CheckRelationshipInterval_SoulToSoul … Flag="enemy"`) is checked for every perceived NPC (`:76`).
- **(d) Hearing:** the native `hearingInfo {soundId, position}` → `SW/handleHearing.xml` → `SW/handleStimulusSound.xml`. It covers lockpick, door, combat, gun/bow fired, arrow hit, whistle, dog attack command, decoy, lure and others. The sound carries no source entity, and investigations make `$__player` the hot entity (`Function_switch_addHotEntity entity="$__player"`, 6× in handleStimulusSound). Door sounds come from Lua `Crime.ProduceAiSoundOnDudePosition` → `XGenAIModule.ProduceSoundWUID(soundKind, player.this.id, …)` (`LUA/Systems/Crime.lua:135`).
- **(e) Unattributed crimes and hot entities.** `SW/handleStimulusNonAttributedCrime.xml:60` does `addHotEntity … entity="$__player" expiration="20"` and scans. When the NPC later perceives the player, `SW/handleAwareness_informations.xml` attributes the crime:
  - it takes the `crime_reactor` link from `$__player` (`:42`);
  - it runs `canAttributeNonattributedCrime`, then native `IsPlayerSuspicious` (`CR/isPlayerSuspicious.xml`), then transforms the info.
  - Separately, `CR/attributeCrimes.xml:29` re-creates a non-attributed theft as `'theft'` with `PositionWuid="$__player"`.

**Message definitions and payloads** (`DEF`, `InstantSendMessage_switch_stimulus_*`; the type names are also listed in `Scripts/AI/MessageTypes.xml:4–31` with `SendableByConceptNode="1"`) (data-verified):

| Type | Payload (`Content_*`) |
|---|---|
| `switch:stimulus:hit` (`DEF:14357`) | `attacker, kind (crime_attackKind), hitStrength (HitReactionStrength), victim, directHit, freshlyAttributedCrime, shouldSendNotification` |
| `switch:stimulus:theft` (`DEF:14528`) | `information_perceivedWuid, information_label, information_position, immediate, isNonAttributed, freshlyAttributedCrime, method (crime_theftMethod), count, owner, pivot, kettleType__, treatAsPersonalSource, shouldCheckHomeStashes` |
| `switch:stimulus:murder` (`DEF:14443`) | `information_*, corpse, isCarried, freshlyAttributedCrime, killedByDog, killedByHorse` |
| `switch:stimulus:combat` | `participant1, participant2, attackKind, isHorseCollision, freshlyAttributedCrime, hitStrength` |
| `switch:stimulus:animalAbuse` (`DEF:14209`) | `culprit, victim, animalAbuseKind, isPoaching, isRanged, attributedCrime, shouldIncreaseCount` |
| `switch:stimulus:information` | `information_perceivedWuid, information_label, information_position, updated, sender` |
| `switch:stimulus:disturbance` | `perceivedWuid, priceOverride, skipInitialReaction` |
| `switch:stimulus:escalatedTrespass` | `wuidType, trespassArea, home, trespassingRepeatedly, stimulusKind, createInformationOnly, isKzikTrespass` |
| `switch:stimulus:kettlePoisoning` | `kettle, victim, markKettleAsPoisoned, kettleType__` |
| `switch:stimulus:lockpick` / `pickpocket` / `trespass` / `nonAttributedCrime` / `criminal` / `shooter` … | `lock` / `pivot, bagType__` / `isCampTrespass, severeness, area` / `information_*, corpseState__` / `freshlyAttributedCrime, freshCrime, information_*` / `shooter, projectileType, hitStrength, position, target, gotDirectHit, …` |

**Routing (data-verified).**
- `TB/ai/mailbox.xml:165`: `mailbox_name="crime_stimulus" message_limit="-1" on_accept="2"` (consume).
- `TB/ai/mailbox_filter.xml:37`: the same mailbox id with `type="switch:stimulus"`, a prefix filter, so every `switch:stimulus:*` lands there.
- `TB/ai/mailbox_group.xml:17` binds it to `crime/processstimulusmessages.xml` / `crime_processStimulusMessages`.

That tree has `ProcessMessage … inbox="'crime_stimulus'"` loops for exactly **animalAbuse, combat, disturbance, escalatedTrespass, hit, information, kettlePoisoning, murder, theft** (`CR/processStimulusMessages.xml:44–84`). Each is handed to the matching `switch_handleStimulus*`. The other types are declared as variables but never read.

**Senders (data-verified).**
- **Skald utility modules:** `QU/hit/pushstimulus_hit.xml:29`, `theft/`, `murder/`, `combat/`, `disturbance/`, `escalatedtrespass/`, `kettlepoisoning/`, `animalabuse/`.
  - Shipped call sites in `Quests/Final`: hit 3, theft 5, murder 3, escalatedTrespass 10, disturbance 6, animalAbuse 4.
  - **All 3 `pushstimulus_hit` calls wire `attacker=ASSET:player`.**
  - `pushstimulus_animalabuse` never wires `culprit`, so it stays null.
- **Direct quest nodes** (under `Scripts/Quests/Final/Barbora/kutnohorsko/`): `sabotazLazni/hibernable/pruzkum_a_sabotaz_lazni/aplikace_blech.xml` (disturbance), `sMlynariNejsouZerty/fight_se_straznym/crime_consequences.xml` (information, `Content_information_label=assault`), `kubaParalu/infiltrace/dead_goclin_perception.xml` (murder).
- **AI tree:** `Scripts/AI/quests/kocovnickaCest/villageGuard_startReportingPlayer.xml` (`InstantSendMessageToNPC target="$this.id" variable="$stimulusDisturbance"`).
- **Lua:** none. No shipped `.lua` file contains the string "stimulus".

**Example call** (the quest pattern), with the module wiring from `QU/hit/pushstimulus_hit.xml:29–36`:
```xml
<InstantSendMessage … MessageType="switch:stimulus:hit">
  <Constant Name="Content_hitStrength" Value="Unpleasant" />
  <Edge From="receiver" To="Receiver" />   <Edge From="attacker" To="Content_attacker" />
  <Edge From="victim" To="Content_victim" /> <Edge From="attackkind" To="Content_kind" />
  <Edge From="directhit" To="Content_directHit" />
```
Instance: `Scripts/Quests/Final/Barbora/trosecko/korenarkaZachrana/dialogy_po_jeskyni_a_retezec_pomst/pomsty_usmireni.xml:219–226`.
```xml
<pushstimulus_hit … Namespace="utils.crime.hit">
  <Asset Name="receiver" Alias="jakes" /> <Asset Name="attacker" Alias="player" /> <Asset Name="victim" Alias="jakes" />
  <Constant Name="attackkind" Value="armed" /> <Constant Name="directhit" Value="true" />
```
Lua equivalent (inferred; the pattern is copied from `LUA/Entities/AI/Horse.lua` `table.MakeFromType('animal:startle', {origin = player.this.id})` + `XGenAIModule.SendMessageToEntityData(id, type, tbl)`; `table.MakeFromType` is `LUA/Utils/TableUtils.lua:363`; enum field encoding is unverified):
```lua
local m = table.MakeFromType('switch:stimulus:theft', { owner = npc.this.id, pivot = item_wuid, count = 1,
          method = enum_crime_theftMethod.pick })   -- enum global name unverified; the value MUST be 'pick'
XGenAIModule.SendMessageToEntityData(npc.this.id, 'switch:stimulus:theft', m)   -- blamed on $__player
```
`method` must be `pick`, as the Skald module sets it with `Constant Content_method=pick`. Any other value makes
`CR/createTheftInformation.xml` fall through to `ErrorNode 'ERROR: Unknown theft method!'`. `information.label` must stay
empty so that `handleStimulusTheft` builds the information itself. (data-verified tree logic; the Lua encoding is inferred)

---

## 4. Spreading, reporting, reputation, wanted, levels, timers

- **Emitting (data-verified).** `CR/emitInformation.xml` runs while an NPC reacts. It calls `InformationEmittingStart Information="$crimeInformation_local" … Radius=` with a script param (`:159`):
  - `crime_crimeInformationEmitDistance` = 10;
  - `crime_crimeInformationEmitDistance_long` = 40;
  - period 5 s for attack/arrest/selfhelp, 15 s for watch.

  Campers and group members also emit an `'alarm'` information: `PerceivedWuid="$this.id"`, position = the crime, dynamic `crimeInformation`, `urgent` and `stimulusKind`, over `crime_alarmInformationEmitDistance` = 25 (`:61`). Emitting is suppressed for public enemies and by `crime_disableCrimeInformationEmit` (`:143`). (Distances from `TB/ai/ScriptParams.xml:225–226`.)
- **Receiving (data-verified).** Native `informationDiff` messages → `SW/handleInformationDiff.xml`.
  - A `'Received'` info from a sender with `Flag="friend"` (`:54`) → `SW/handleStimulusInformation.xml` (reaction `ReactionNpc="$__player"`, `:325`).
  - From a non-friend → `DestroyInformationFromHolder` (`:76`).
  - `switch_disabledInformationReaction` on the receiver destroys non-friend infos (`SW/switch.xml`).
- **Spreading among friends:** `CR/spreadInformationWithinFriends.xml:14`, native `SpreadInformation WuidArray="$friends"`, used from `interrupt_attack`. There is also `CR/spreadInformations.xml`. (data-verified)
- **Reporting (data-verified).** `SW/callInterrupt_report` → `SW/interrupt_report.xml`. The NPC walks or runs to `ChooseReportDestination` (native), emits on the way, then does `TransferInformation … target="$reportData.destination"` (`:433`).
  - Old logic of `CR/chooseReportDestination.xml`: destination types security, authority and mate. `EntityContextCheck context="crime_disableReport" target=""` → none (`:21F`). `$isPlayerRelated = $data:crime[label].isCrime | label == 'nonAttributedCrime'` (`:47F`): being a crime is *equated* with being player-related.
- **How guards learn:** direct perception, emitted infos from friends, reports and spreads. When an authority (`crime_isAuthority`, Storm rule `contexts_authority` → `<isAuthorityFigure/>` in `IPL_GameData/Libs/Storm/contexts/contexts.xml`) gets an info with status Created or Received, it runs `ExecuteReputationHitFaction Npc="$this.id"` (`SW/handleInformationDiff.xml:44,66`). (data-verified)
- **Reputation.** Always NPC or faction towards the player.
  - **Witness hit:** old logic `ExecuteReputationHitWitness` on brand-new infos (`CR/createInformation.xml:44F`); old `CR/executeReputationHit.xml` builds token `'witness_crime_' + label` (`:35F`) → `AffectFeelings`. Old `CR/affectFeelings.xml:21F,27F` turns that into `'auto_' + token` (+`_bandit`) → `SetReputationNPC`. Rows exist in `TB/rpg/reputation_change.xml`, e.g. `auto_witness_crime_theft` −0.1 (`:95`). (data-verified)
  - **Faction hit:** token `'auto_faction_crime_' + label` → `SetReputationFaction` on the observer's `settlement` faction (`executeReputationHit.xml:59F`); e.g. `auto_faction_crime_theft` −0.1 (`:106`). (data-verified)
  - **Victim's own hit:** `AffectFeelings Npc="$this.id" Token="$feelingsToken"` (`SW/handleStimulusHit.xml:106`, token `'hit_'+kind`), rows `auto_hit_melee_armed` −0.45 etc. (`reputation_change.xml:74`), only in the player/dog branch that starts at `:61`. (data-verified)
  - **The target is the player:** reputation_change targets are "npc only / faction+superfaction / … / all" (`TB/rpg/reputation_change_target.xml`); the FactionTree has `Faction Name="player"` (`TB/rpg/FactionTree.xml:2097`) as the relation target; `CR/resetReputationAfterCrimeResolve.xml:19` does `Reconcile faction="player"`. `DOCS`: `soul:ModifyPlayerReputation(name)` modifies "NPCs relationship (reputation) to player". (data-verified)
  - `crime_suppressReputationPenalties` exists only as a native side effect. `CR/changeReputationAround.xml` has **no callers**. (data-verified)
- **Wanted.**
  - `DEF:14730` `IsWanted`: "Check whether the player has wanted icon on their screen … wanted in any currently present locations".
  - `TB/rpg/soul_crime_role.xml:6` "soldier … affects wanted icon". Social classes bailiff, watchman, guard, guardLeader, soldier_crimeAuthority, huntsman_crimeAuthority and catchpole carry role 2; class `player` has role 0 (`TB/rpg/social_class.xml`).
  - Buff implementation `Cpp:Wanted` (`TB/rpg/buff__perk_kcd1.xml:8`, perk `perk_ordinary_mug`).
  - `crime_disableWantedStatus` [Game] `SideEffect="disableWantedStatus"` (`TB/ai/ScriptContext.xml:500`) is never used in any tree.
  - `DOCS` also lists `Game.SetWantedLevel(int)` (unverified in this build).

  So "wanted" is computed natively from what authorities know (inferred). (data-verified for the rest)
- **Crime levels.** Enum `crime_crimeLevel: none, offence, trespass, property, violent, murder` (`DEF`); the label mapping is in §2. (data-verified)
  - Per-NPC *escalation* state is a set of contexts with native side effects: `crime_escalationLevel_{looking, reporting, investigating, recognizing, checking, confrontingTrespass, confrontingGeneral}` (SideEffect `crimeLevel*`, `TB/ai/ScriptContext.xml:560ff`). `SW/switch.xml` holds `crime_escalationLevel_recognizing` and `ShowTutorial Name="crimeIcon_general"` while recognizing. These drive the HUD crime icons (inferred).
- **Freshness timers** (`TB/ai/ScriptParams.xml`) (data-verified):

  | Parameter | Value | Meaning |
  |---|---|---|
  | `crime_theft_autoAttributionTimer` | 2500 ms (`:148`) | theft volume counts as witnessed |
  | `crime_theftVolumeDespawnTimerWhenSeen` | 2500 | |
  | `crime_theft_perceptionAttributionTimer` | 300000 | |
  | `crime_lastHit_expiration_time` | 360000 ms (`:64`) | corpse/body attribution via `crime_lastHitByPlayer` |
  | `crime_veryFreshCriminalThreshold` | 6000 ms | freshly attributed criminal |
  | `crime_somewhatFreshCriminalThreshold` | 15000 | |
  | `crime_freshViolentInformationTimer` | 20000 | |
  | `crime_corpseViolationTimer` | 10000 ms | |
  | `crime_longTermMemoryTrespassExpiration` | 7200 | |
  | `crime_resolveCooldown` | 900000 | |

  Hot entities expire in 20 s (300 s for `stolenCorpse`). Information expiry is `crime.xml` `expiration`, applied natively (inferred). Forgetting: `CR/forgetCrimes.xml` destroys every `isCrime`/`nonAttributedCrime` info held (`crime:forgetCrimesData`); `crime_forgetCrimesWhenUnconcious` does the same when knocked out (`SW/state_unconscious.xml`).

---

## 5. NPC culprits: verdict

**Verdict:** a non-player soul **cannot** be the perpetrator of an engine crime. The model has no culprit slot, and everything that consumes crime informations addresses `$__player`. The only things you can aim at an NPC are **fights** and **bark text**, never arrest, fine, wanted status or reputation.

Why (data-verified unless marked):
1. **There is no culprit in the data.** An information is `{label, perceivedWuid, position}` + tags, with no culprit tag (§2). `PerceivedWuid` is the object: victim, corpse, lock, horse, stash, volume, item or area.
2. **Holding a crime info means knowing a crime of the player.**
   - Old `CR/playersCrimeInformationKnown.xml:23F`: true if any known info has `$data:crime[label].isCrime`. That native node gates self-defence (`SW/handleStimulusHit.xml`), criminal recognition and robbing.
   - `SW/handleAwareness_informations.xml:128,138`: on perceiving **`$__player`**, `PlayersCrimeInformationKnown` → `GetMostImportantCrimeInformation` → `handleStimulusCriminal`, whose decision is `DecideSkirmishReactionCrime … TargetNpc="$__player"` (`SW/handleStimulusCriminal.xml:370`).
3. **Consequences address the player only.**
   - `SW/callInterrupt_arrest.xml` has **no target parameter**, and `SW/interrupt_arrest.xml` references `$__player` 27×.
   - Self-help, punishment (`CR/teleportPlayerToPunishment.xml target="$__player"`), fines (`CR/calculatePunishment.xml` sums *all* known crime infos) and the wanted icon (`IsWanted` = "the player …") all do the same.
   - Every resolve step unlinks from `$__player` (`RemoveLink From="$__player" To="$information.perceivedWuid"`, `CR/resolveCrimeInformation.xml:73,96`).
4. **Attribution means attributing to the player.**
   - `canattributenonattributedcrime.xml` only checks that *this NPC* has not already failed to attribute, plus the corpse state.
   - `insertnpccannotattributenonattributedcrime.xml` records failures in `informationNotAttributedBy`.
   - Attribution then happens on perceiving `$__player` (`handleAwareness_informations.xml:42`, `CR/isPlayerSuspicious.xml` → native `IsPlayerSuspicious`) or via `CR/attributeCrimes.xml:29` (`PositionWuid="$__player"`).
   - `determineCrimeVictim.xml` only finds a *victim*.
5. **NPC attackers never create infos.**
   - `handleStimulusHit`: non-player attacker → `DecideSkirmishReactionCrime RelationOverride="Hostile" TargetNpc="$stimulus.attacker"`, no information (`:378`).
   - `handleStimulusCombat` without the player or dog in the skirmish → join/withdraw/watch with `information=""` (`:437–451`), and `combat_doNotJoinSkirmishesWithoutPlayer` (`:210`) lets NPCs ignore it.
   - Hit volumes are spawned only for player or player-dog attackers (`handleHitReaction.xml:255, ~325`), or with the attacker hard-coded to `$__player` (stealth `:197`, criminal horse collision `:169`).
   - Other dogs and animals are ordinary NPC attackers. The player's dog is treated as the player (`assaultByDog`/`murderByDog`, `crime_dogAttackCommand`, `$reactionNpc = $__playerDog`).
   - NPC-killed corpses become `'corpse'`/`'nonAttributedCrime'` at most.
6. **Old chooseReaction logic** (`SW/chooseReaction.xml`, obsolete; native `ChooseReaction` now):
   - `condition="$reactionNpc ~= $__player | $arrestEscalated | …"` → `attack` (`:168F`).
   - Reports only if `($reactionNpc == $__player | $reactionNpc == $__playerDog)` (`:407F,417F`).

   So a non-player `ReactionNpc` gets attacked, never arrested or reported (data-verified old logic; that native still behaves this way is inferred).
7. **Animal abuse** is the one path with a non-player culprit and an information. `ChooseReaction ReactionNpc="$stimulus.culprit"` and `callInterrupt_attack target="$stimulus.culprit"` (`SW/handleStimulusAnimalAbuse.xml:79,181,185…`) will fight that NPC. But `callInterrupt_arrest` there still arrests the player, and the `'assaultAnimal'`/`'murderAnimal'`/`'poaching'` info in memory later counts as the player's (point 2). (inferred)

**What if you create an information with PerceivedWuid = some NPC's WUID?** That is the normal case for victim crimes: `assault` on X, `murder` of X, `pickpocket` of X. It means "someone did this *to* X", and every downstream tree blames `$__player`. A crime by X is impossible to express. (data-verified)

**Closest things to an NPC culprit (usable levers):**

| Lever | Effect | Evidence |
|---|---|---|
| `switch:stimulus:hit` with `attacker=<ghost>` sent to victim V | V makes a hostile skirmish decision against the ghost (attack/flee); no info, no reputation | `SW/handleStimulusHit.xml:61,378` (data-verified) |
| `switch:stimulus:combat` with participants not including the player | Receiver decides join/withdraw/watch vs `participant1` | `SW/handleStimulusCombat.xml:437` (data-verified) |
| Faction relation `enemy` towards the ghost | Enemy-on-sight: `handleAwareness_enemy` → `handleStimulusEnemy` → `DecideSkirmishReactionCrime … TargetNpc="$stimulus.enemy"` → attack. Suppressed by `crime_doNotReactToEnemiesOnSight` | `SW/handleAwareness.xml:76`, `SW/handleStimulusEnemy.xml:30`, `SW/handleAwareness_enemy.xml:14` (data-verified) |
| `crime:attackInitiatedByConcept {target, priorityTarget}` | `SW/switch.xml:485–493`: inbox → `callInterrupt_attack target="$attackInitiatedByConcept.target"` (`:490`), male NPCs only (`ErrorNode 'Women can not fight!'`, `:493`) | (data-verified) |
| `combat_forcedTarget` [Relation, SideEffect combatForcedTarget] | Retargets a fighting NPC (used by quests: `RelationContext … target="$__player"`, `$forcedTarget`, `$victim`) | `TB/ai/ScriptContext.xml:667` (data-verified) |

**Co-op hazards spotted:**
- (1) The stealth-hit branch writes `$hitVolumeData.attacker = $__player` and `AddLink From="$__player" … 'crime_lastHitByPlayer'` with no attacker check (`SW/handleHitReaction.xml:193–204`). A `MeleeStealth` hit on a local NPC delivered by the remote avatar would be blamed on the local player. (data-verified branch; the scenario is inferred)
- (2) The murder gate `condition="$volumeData.attacker == $__player | $__playerDog"` (`SW/handleAwareness_hitVolume.xml:152`) is probably always true while the player owns a dog (quirk, inferred). It is only reachable through player-spawned volumes.
- (3) Hitting the ghost *is* a crime of the local player whose victim is the ghost. The victim-side `crime_ignoredNPCHitVolume` blocks witnesses (`:50`). The victim's own reaction is governed by `switch_disabledHitReaction` / `switch_disabledHitBehavioralReaction` on the ghost itself.

---

## 6. Crime-legality script contexts (`TB/ai/ScriptContext.xml`) — who they are checked on

`target=""` or `$this.id` = the observer or the NPC itself. Check sites are the executed trees (data-verified), except rows with no tree use, whose side effect is native (inferred).

| Context [class] | Checked on | Where | What it disables / does |
|---|---|---|---|
| `crime_ignoredNPCHitVolume` [Entity] (`:433`) | **victim** `$volumeData.target` | `SW/handleAwareness_hitVolume.xml:50` | Witnesses ignore hit volumes of hits on this entity (no assault/murder/corpseViolation from witnessing) |
| `crime_ignoreNPCHitVolume` [Relation] | observer → victim | same | Same, per pair |
| `crime_ignoreNPCHitVolumes` [Entity] | observer | same | Observer ignores all hit volumes |
| `crime_ignoredCorpse` [Entity] | **corpse** `$corpse`/`$body` | `SW/handleAwareness_corpse.xml:48`, `…_bodyCarrier.xml:48`, `…_animal_corpse`, `handleMlodAwareness` | `IgnorePerception` on the corpse: no corpse/murder info |
| `crime_ignoreCorpse` [Relation] / `crime_ignoreCorpses` [Entity] | observer → corpse / observer | same | Same |
| `crime_ignoredUnconsciousBody` [Entity] | **body** `$body`, `$enemy` | `SW/handleAwareness_unconsciousBody.xml:94`, `…_bodyCarrier`, `…_bodyHolder`, `…_enemy` | No takedown/assault discovery; not attacked as an enemy while down |
| `crime_ignoreUnconsciousBody` [Relation] / `crime_ignoreUnconsciousBodies` [Entity] | observer → body / observer | same | Same |
| `crime_ignoredPickpocket` [Entity] | **victim** `$stimulus.pivot` | `SW/handleAwareness_pickpocket.xml:30` | Witnesses do not create a pickpocket info (bark override via link `crime_pickpocketBarkOverride`) |
| `crime_ignorePickpocket` [Relation] / `crime_ignorePickpocketing` [Entity] | observer → victim / observer | same | Same |
| `crime_ignoredPickpocketFail` [Entity] | the victim itself (`target=""` in its own switch) | `SW/handlePickpocket.xml` | A failed pickpocket on me is not a crime |
| `switch_disabledPickpocketReaction` [Entity] | victim itself | `SW/switch.xml` | Ignore `minigamePickpocket` events |
| `crime_legalToLoot` [Entity, SideEffect legalToLoot] (`:132`) | **owner** `$owner` / **corpse** `$corpse` | `CR/checkReactionToTheftFrom.xml` (only when `considerIfLegalToLoot`, i.e. the theft-volume path), `SW/handleAwareness_lootCorpse.xml:13`, `…_animal_lootCorpse` | Looting that body / that owner's goods is not theft. Presumably what `soul:IsLegalToLoot()` returns (inferred) |
| Link `crime_lootIsLegal` / `crime_lockpickIsLegal` (links, not contexts; `Scripts/AI/LinkTagDefinitions.xml:90–91`) | stash/lock → observer or → itself | `SW/handleAwareness_loot.xml:97`, `SW/handleAwareness_lockpick.xml:85`, `handleStimulusSound` | Legal container looting / lockpicking |
| `crime_ignoredHorseTheft_Horse` [Entity] | **horse** `$mount` | `SW/handleAwareness_playerMount.xml:40`, `…_playerMountedVolume` | No horse-theft reaction to this horse |
| `crime_ignoredHorseTheft_NPC` [Entity] | observer | same | Observer ignores horse theft |
| `crime_ignoreHorseTheft` / `crime_forceReactionToHorseTheft` [Relation] | observer → horse | same | Ignore / force the reaction |
| `crime_forceReactionHorseTheft` [Entity] | observer | same | Force the reaction |
| `crime_ignoreHorseTheftInSettlement` [Entity, SideEffect] (`:408`) | horse (old `CR/isHorseRelevant.xml:34F` `target="$horse"`) | native now | Settlement authorities stop caring about this horse |
| `switch_horse_enableMountIsLegal` [Entity] (`:815`) | **horse**, in its own brain | `Scripts/AI/animal/basic/switch/animal_horseBrain.xml` → `ExecuteLua "entity:SetMountIsLegalFromAI(true)"` | Makes Lua `Horse:IsMountLegal()` true → prompt "@ui_hud_mount" instead of "…_and_steal". **No tree reads it for detection:** the player brain still spawns `crime_playerMounted` whenever `$mount ~= $playerHorse`, so whether native `IsHorseRelevant` honours it is unknown (inferred) |
| `switch_horse_disableMountableByPlayer` [Entity] | horse | `animal_horseBrain.xml` → `SetMountableByPlayerDisabledFromAI(true)` | No mount prompt |
| `crime_disableReport` [Entity, SideEffect crimeDisableReport] (`:602`) | **observer** (set on itself in `SW/interrupt_flee.xml:68`; old `chooseReportDestination.xml:21F` `target=""`) | native | This NPC won't report. **Not** a victim-side context |
| `crime_disableWantedStatus` [Game] (`:500`) | global | no tree use | Native wanted-status off |
| `crime_suppressReputationPenalties` [Entity] (`:158`) | ? | no tree use | Native side effect |
| `combat_ignoreMurderedByPlayer` [Entity] | **victim** | `SW/handleAwareness_hitVolume.xml:175`, bodyCarrier/Holder | Witnessed kill is not murder |
| `crime_animal_legalToKill`, `crime_ignoredAnimalHitVolume` [Entity] | **animal** victim | `SW/handleAwareness_animal_hitVolume.xml:62`, animal corpse/loot | No poaching/animal-abuse info |
| `crime_ignoreThefts`, `crime_ignoreLockpicking`, `crime_ignoreGraveRobbing`, `crime_ignoreWornStolenEquipment`, `crime_ignorePlayerPerception`, `crime_bff` [Entity] | observer | `handleAwareness_theft/loot`, `_lockpick`/`handleLockpickedDoor`/`handleStimulusSound`, `_graveRobbing`, `_stolenEquipment`, 21 files, 39 files | Observer ignores that crime kind / the player entirely / only barks (`crime_bff` via presets `crime_bestFriendsForever`, `crime_prettyGoodFriendsForever`) |
| `crime_autoAttributeTheftToPlayer` [Entity] | observer | `SW/handleAwareness_theft.xml:87` (set in `interrupt_checkHomeStashes`) | Unwitnessed thefts are attributed to the player |
| `switch_disabledHitReaction` / `switch_disabledHitBehavioralReaction` / `switch_disabledPerceptionReaction` / `switch_disabledInformationReaction` [Entity] | the NPC itself | `SW/switch.xml`, `handleHitReaction`, `handleStimulusHit`, … | Suppress that NPC's own hit / behavioural / perception / information reactions |
| `crime_isAuthority` [Entity, SideEffect isAuthority] | observer | 48+ files; Storm rule `contexts_authority` | Arrest/report destination; faction reputation hit on learning |

---

## 7. Lua: where the player-facing crime interactions live

| File | Function | Prompt / decision | Calls |
|---|---|---|---|
| `LUA/Entities/AI/Shared/BasicAIActions.lua` | `GetActions` | Pickpocket "@ui_hud_basic_steal" if `user.soul:HaveSkill('thievery') and self.human:CanBeRobbed()` (`:83`); stealth kill, knock out, horse pull-down; on dead/unconscious "@ui_hud_mercy_kill"/"_unconscious" (`:91–93`), else `AddLootAction`; grab body blocked by `self.soul:HasScriptContext("crime_greyOutGrabBody")` | `OnPickpocketing` → `user.human:RequestPickpocketing(self.id)` (`:269`); `OnKnockout` → `user.actor:RequestKnockOut` (`:239`); `OnStealthKill` → `RequestStealthKill`; `OnMercyKill` → `RequestMercyKill` (`:251`); `OnLoot` → `self.actor:RequestItemExchange(user.id)` (`:257`); `OnGrabCorpse` → `RequestGrabCorpse` |
| `LUA/Entities/actor/BasicActor.lua` | `AddLootAction` | `if self.soul:IsLegalToLoot()` → "@ui_hud_loot" (release), else "@ui_hud_rob_body" (hold) (`:243,248`) | `BasicAIActions.OnLoot` |
| `LUA/Entities/actor/BasicAnimal.lua` | `AddAnimalLootAction`, `OnUsed` | loot/butcher, `animal_disableLootButcherActions` | `RequestItemExchange` |
| `LUA/Entities/Items/PickableItem.lua` | `GetActions`, `OnUsedHold`, `OnUsed` | `self.item:CanSteal(user.id)` → "@ui_hud_stealItem" (hold, `:43`); `BelongsToDeadBody()` → "@ui_hud_loot"; else "@ui_pickup_item" | `self.item:OnSteal(user.id)` (`:121`); `DOCS`: "If user is player, asks if he wants to steal the item and steals it" |
| `LUA/Entities/WH/Stash/AnimStash.lua` | `GetActions`, `UsesStealUiPrompt` (`:788`), `Open`, `OnUsedHold` | Owner = `EntityModule.GetInventoryOwner(...)`; steal prompt "@ui_open_stash_crime" (hold, `:345`) unless the owner is null, the owner is `player`, or `RPG.IsPublicEnemy(ownerWuid)` (`:805`); lockpick prompt via `Crime.BuildLockpickPromptStrName` | `Open` → `XGenAIModule.LootInventoryBegin`, `user.actor:OpenItemTransferStore`, `Crime.ProduceAiSoundOnDudePosition(enum_sound.door,1)`, `self.stash:ReportOpened`; `OnUsedHold` → `Minigame.StartLockPicking(self.id)` (`:472`) |
| `LUA/Entities/Doors/AnimDoor.lua` | `GetActions`, `Lockpick` (`:517`), `SpawnSuspiciousVolume`, `ProduceAiSound` | lockpick prompt on the key side | `Minigame.StartLockPicking`; `XGenAIModule.SpawnPerceptibleVolume(…,'crime_suspiciousDoor','24h',…)` + `AddLink`; `Crime.ProduceAiSoundOnDudePosition` |
| `LUA/Entities/WH/Others/Lockpickable.lua` | `GetActions`, `OnUsedHold` | lockpick prompt | `Minigame.StartLockPicking(self.id)` (`:265`) |
| `LUA/Entities/WH/Others/Hole.lua` | `GetActions`, `OnUsed` | "@ui_hud_start_digging_illegal" if `bLegalToDig == false` | `XGenAIModule.SpawnPerceptibleVolume(pos,1,1.5,1,1,'crime_graveRobbing','10s',true,true)` + `AddLink(vol, self, 'crime_graveRobbing')` (`:142–143`) |
| `LUA/Entities/AI/Horse.lua` | `GetActions`, `OnMount` (`:267`), `IsMountLegal` (`:343`), `SetMountIsLegal`, `SetMountIsLegalFromAI` (`:319`), `SetMountableByPlayer(DisabledFromAI)`, `OnLoot` | `isFreeToTake = isPlayerHorse or self:IsMountLegal()` → "@ui_hud_mount" vs "@ui_hud_mount_and_steal" (`:246`); dead → `AddAnimalLootAction` | `user.human:Mount(self.id)` |
| `LUA/Systems/Crime.lua` | `BuildLockpickPromptStrName` (`:140`), `ProduceAiSoundOnDudePosition`, dialog-result senders (`crime:resolveDialogFeedback`, `crime:selfhelpChatFeedback`, …) | UI and dialog glue | `XGenAIModule.SendMessageToEntityData`, `XGenAIModule.ProduceSoundWUID` |

Native backing is documented in `DOCS` (read-only, outside the paks):
- `soul:IsLegalToLoot()`: "looting the soul (dead or unconscious) will not be seen as a crime".
- `XGenAIModule.SpawnPerceptibleVolume(pos, radius, height, visibility, conspicuousness, label, timer, worldTime, clipPoints)`.
- `XGenAIModule.AddLink(from, to, tag)`, which takes **no link data**. So Lua can spawn label-only crime volumes such as graveRobbing and suspiciousDoor, but not `theft`/`crime_hit` volumes, which need `stealData`/`crime:hitVolume` link data. (inferred from the signature)
- `item:CanSteal`, `item:OnSteal`, `AI.CreateStimulusEvent`.

The trigger for each crime is native state that the AI trees read: `loot`, `lockpick` and `pickpocketing` links plus perceivable states on the player, the theft volume and hit reactions. The Lua above only decides the prompt text and hold/release.

---

### Method note
Trees were dumped to compact text (Root vs detached Forest, with editor comments merged). Line numbers were
re-checked against the original XML.
