# WO-139 reference B — how guards act on crimes (KCD2 shipped data)

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios. The research behind `docs/WO-139-findings.md` §3.

**Scope.** This is a read-only study of the extracted game paks, plus ASCII strings from the Modding Tools game's `Bin\Win64ReleaseSteamLTO_DLL\*.dll`. Paths are relative to the extracted paks unless they start with `DLL:`.

**How claims are marked.**
- **(data-verified):** read directly in the cited file.
- **(inferred):** my interpretation of names or structure. The data does not prove it.

**How the sources were read.**
- Quotes are XML-decoded (`&apos;` becomes `'`). Dialogue voice lines are paraphrased in English rather than quoted.
- BT logic was read from `<Root>` only. `<ForestContainer>` nodes are detached and never run. When I cite one, I mark it **FOREST**.
- Several BT functions now have `Root = ErrorNode 'Function is obsolete. Use its code successor.'`. Their forest is the pre-port reference for a native node with the same name (for example `ChooseReaction`, `IsHorseRelevant`, `AddArrestEscalation`, `CreateCombatInformation`). What they say about *current* behaviour is (inferred).

---

## Answers first

1. **The stop.**
   - The native `ChooseReaction` node (called with `ReactionNpc=$__player`) sends an *authority* to `interrupt_arrest`. It picks attack instead for violent crimes that are fresh or escalated.
   - The guard follows `$__player`, barks, and opens the chat `STRAZ_ZATYKANI_CHAT` (options: surrender or refuse).
   - `crime_resolveCrimeDialogue` then starts the FaderDialog `straze__zatykaci_dialog`, decision alias `arrestdialogue_strazeZatykaciDialog`, through `Function_speech_dialogInitiator … metarole='STRAZ_ZATYKANI' preset=fader recipient=$__player`. The partner is hard-coded as the player.
   - Choices: pay the fine; "not enough money"; accept punishment; one skill check out of persuade, impress, dread, scholarship (needs a perk) and drinking (needs a perk); fight.
   - `Crime.SendResolveDialogResult` sends `crime:resolveDialogFeedback` to the guard. Each result is applied as follows:
     - **fine:** `Confiscate ConfiscateFine=true`, and items go to the district `crime_stolenItemsStorageChest`.
     - **skillCheck:** no fine, and the theft items are `LegalizeItem`'d.
     - **punishment:** confiscation, a settlement-wide recheck, then `crime_forcePlayerPunishment`.
     - **fight:** a `resistingArrest` crime, arrest escalation, and `callInterrupt_attack`.
   - Every crime information the guard holds is `DestroyInformationCompletely`'d. That includes what it pooled from friends within 50 m just before the dialogue.

2. **Starting it on purpose.** Yes, but only from an AI tree running in the guard's own brain. The shipped example is quest tree `sermiri_arrest`, started by the Skald `EnableBehavior` node. It spreads information into the guard, then calls `Function_crime_resolveCrimeDialogue`. Quests can also call `crime_prepareSecondArrestForConcept`, or reuse the punishment module `playpunishment_cutscenebuffsmonolog` directly.
   - Requirements: the guard holds at least one crime information; only one resolver runs at a time (`ResolveCrimeDecorator`, global Semaphore `crime_resolveDialog`, count 1); the guard's role has metarole `STRAZ_ZATYKANI`; the partner is a PLAYER-metarole soul.
   - There is no ForceDialog or StartCrimeDialog node.

3. **Punishment.** The AI picks the type in `crime_getResolveDialogContext`: branding if murder, poaching or discounted fine > 20000; beating if violent; otherwise pillory. Branding becomes execution if the player is already branded. The quest `open_world/nextnextgenpunishment` then runs this sequence:
   - **Fader and teleport:** a fader plays, then the player is teleported to `punishment_teleportPoint`.
   - **Clock:** if the player is ≤200 m from the punishment cutscene and it is not an execution, a SkipTime cutscene plays: short between 08:00 and 17:00, long otherwise. If >200 m, a fast-travel cutscene plays, then `AdvanceWorldTime TimeOfDay=10h0m0s` (skipped for execution). For a second arrest from far away: fast travel, then `AdvanceWorldTime 9h`. Beating and branding in-game cutscenes are declared `Time="8h"`.
   - **Debuffs:** they scale with the fine.
     - Pillory: charisma −2/−4/−6, plus dirt, and hunger and exhaustion halved.
     - Beating: combat stats ×0.8/0.7/0.6, plus blood, and health −20/40/60 %.
     - Branding: stat `brn` buffs, health −6.9 %, and faction reputation `branding` −0.5.
   - **End:** the buffs end on their timers, or the penitent-pilgrimage quests remove them.
   - **No unequip:** the open-world path does **not** unequip the player.

4. **Execution.** It happens when the crime is branding-level and `IsPlayerFreshlyBranded` is true. In Hardcore the check is `brn > 0`, and the dialogue also counts the Hardcore Known Criminal perk.
   - The execution cutscene comes from the `$__land` link `punishment_executionCH`.
   - Its `AfterPlay` fires `GameOver Reason=44` (`game_over_crime_execution`, type 6 `GameFailedInstantBackground`).
   - Before that, the crimes were already destroyed and items confiscated. No time skip or debuff is applied. The player reloads (inferred).

5. **Pursuit.** Every pursuit node targets `$__player`.
   - `CrimeFollower Target=$__player` uses modes Default, DontBackOff or NoMoveOnlyTurn, and roles Main, Assist or Observer.
   - Only one guard can "urge" the player at a time. This is the `crime_playerUrging` link on the player.
   - `estimatePlayersPosition` locks out after losing sight, then uses `SetEstimatedFollow`.
   - `handlePlayersEscape`: 7 checks 2 s apart without seeing the player fire `PlayerLost`. That triggers `AddArrestEscalation` and a search (lookAround).
   - `sharePlayersLastSeenPosition` shares the last-seen position within 20 m.
   - An escalation less than 5 min old (`crime_arrestEscalationPeriod`=300000) turns later reactions into `attack`.
   - Resisting (hitting an arresting authority) creates the crime `resistingArrest`.

6. **Stolen goods.**
   - "Stolen" is a native item flag set by `item:OnSteal`. It carries an owner and a stolen-from inventory.
   - The BT finds stolen items with `ItemParamFilter Param=Stolen` (plus `StolenFromFilter` and `GetStolenInfo`). It checks the player, the player's horse, and dropped items within 3 m.
   - Owners and their mates spot worn loot through `GetVisibleStolenEquipment`, which creates a theft with method `seenEquipped`.
   - Frisk: only authorities on stationary duty, with a chance formula and a 90 min cooldown. If stolen items are found, all are confiscated to the chest. No crime and no fine are recorded. Refusing creates `friskRefusal` and an attack.

7. **Horses.** The mount prompt and the crime reaction are two separate systems.
   - **Prompt:** "Mount and steal" (`@ui_hud_mount_and_steal`) shows unless the horse is `player:GetHorseId()` or `Horse:IsMountLegal()` is true. That is either the entity property `bMountIsLegal` or `SetMountIsLegalFromAI`, driven by the context `switch_horse_enableMountIsLegal`.
   - **Crime:** the player brain spawns a 2 s `crime_playerMounted` volume on *any* horse that is not `GetPlayerHorse`. NPCs react unless the horse or NPC has `crime_ignoredHorseTheft_Horse`, `crime_ignoredHorseTheft_NPC`, or the relation `crime_ignoreHorseTheft`. Otherwise the native `IsHorseRelevant` decides: the NPC is a mate of the horse, or an authority of the same settlement.
   - Quests that lend a horse set both contexts.

8. **Non-witness guards, and where "wanted" lives.**
   - Yes, a guard who did not witness the crime can run the dialogue and the punishment.
   - Informations spread by several routes: emit, alarm, report `TransferInformation`, guards joining an arrest, a resolve-time friend pool within 50 m, a town-wide pool at punishment time, and a periodic broadcast within the faction subtree.
   - They expire after `expiration` × `InformationExpirationBase` days.
   - Any authority that holds a crime of the player's and sees the player arrests them (`PlayersCrimeInformationKnown`).
   - "Wanted" is not a buff or context on the player. It is derived natively from NPC memories per location (`IsWanted`). The player carries only helper links and cooldowns.

### Corrections to the earlier context

- **Unequip.** The open-world punishment does not "unequip all". `nextnextgenpunishment.xml` passes `shouldplaytextcutsceneinsteadofunequip Value="true"`, and `execute_cutscene` only runs `unequipallplayersitems` through `ifnot26` (NOT shouldplaytext). Unequip happens only in quest punishments that pass `false` (sMlynariNejsouZerty, listovniTajemstvi, mucirna). (data-verified)
- **When the clock moves.** `AdvanceWorldTime 10h` needs both conditions: farther than 200 m *and* not an execution. Within 200 m, a SkipTime cutscene is used instead, chosen by time of day. (data-verified, §3.4)
- **Who picks the punishment.** The dialogue's punishment ports (`arrestdialogue.punishmentpillory/…/fine/punishmenttype`) are **not wired** in `nextnextgenpunishment.xml`; only `arrestdialogue.normalarrestentered` is. The AI tree decides the punishment type and passes it through the concept signal `crime_forcePunishmentFromAI`. (data-verified)

---

## 1. The "stop"

### 1.1 Choosing the reaction

- **Stimulus handlers.** For example `Scripts/AI/npc/basic/switch/handleStimulusTheft.xml` and `handleStimulusHorseTheft.xml`. They call `ChooseReaction Information=$information ReactionNpc=$__player … Source=$source StimulusKind=…`, then dispatch its callbacks `Arrest`, `Attack`, `Report`, `Selfhelp`, `Flee`, `Watch`, `LookAround` to `Function_callInterrupt_*`. (data-verified)
- **The obsolete BT reference.** `Scripts/AI/npc/basic/switch/chooseReaction.xml` (Root is an ErrorNode; the logic is in the **FOREST**). (inferred as current behaviour)
  - An authority (`EntityContextCheck context=crime_isAuthority`) gets **attack** in any of these cases:
    - The source is personal and `crimeLevel >= violent`.
    - `$reactionNpc ~= $__player`.
    - `$arrestEscalated` is true.
    - The label is `'assault'`, `'aggression'` or `'murder'`.
    - The crime is horseTheft *and* the player is riding the relevant horse (`IsPlayerOnStolenRelevantHorse`).
    - The NPC has `crime_useAttackForArrest`.
  - Otherwise an authority gets **arrest**.
  - A recurring property-level crime while the guard is already in `crime_interruptArrest` calls `AddArrestEscalation`, unless the NPC has `crime_disableArrestEscalationForRecurrentCrime`.
  - Non-authorities get selfhelp, report, flee or watch.
- **Known criminal seen later.** `Scripts/AI/npc/basic/switch/handleStimulusCriminal.xml` handles an authority meeting a known criminal. (data-verified)
  - Violent-level crimes lead to attack if the information is fresh (`$now < createdAt + crime_freshViolentInformationTimer`; 20000 ms, ×3 for murder), if `crime_useAttackForArrest` is set, if `HasActiveThreats`, or if `GetArrestEscalation` is true.
  - Otherwise `Function_callInterrupt_arrest … crimeSeen=false`.
- **The arrest interrupt call.** `Scripts/AI/npc/basic/switch/callInterrupt_arrest.xml` runs `AddInterrupt_arrest … Behavior='interrupt_arrest' Priority=$priority (150) urgency=Fast`. During fast travel it runs `AddInterrupt_interrupt_intermediateEventBehaviour` instead. It is skipped under `crime_global_suppressBehavioralReaction` or `crime_suppressBehavioralReaction`. (data-verified)

### 1.2 Other confrontations that feed arrest

| Behaviour | What it does | Source |
|---|---|---|
| **warnPlayer** (drawn weapon, no torch) | Barks (`STRAZ_VIDI_HRACE_S_VYTAZENOU_ZBRANI_MELEE`, `NPC_REAGUJE_NA_HRACE_BEZ_POCHODNE`, …) and loops every 250 ms. After `Wait duration='30s'` it sets `$arrestPlayer = true`. If offences remain, it runs `createCrimesFromOffences` and `Function_callInterrupt_arrest … crimeSeen=true previousReaction=warnPlayer`. Otherwise it goes to mindPlayer. (data-verified) | `Scripts/AI/npc/basic/switch/interrupt_warnPlayer.xml` |
| **report** (civilian witness) | Runs to the chosen destination. On arrival, for every known info except `motivation`: `TransferInformation source= target=$reportData.destination`. The guard then follows up (returnWithHelp or lookAround). (data-verified) | `…/switch/interrupt_report.xml` |
| **trespass** | Warnings every 6–9 s. The home counter goes up by severity × step (+1 at night). When `$counter > crime_trespassEscalationThreshold` (12; semi-trespass capped at 3), it runs `Function_switch_handleStimulusEscalatedTrespass`, which reaches `ChooseReaction`. It also runs `InterruptSkipTime` on a time-skipping player within 4 m. (data-verified) | `…/switch/interrupt_watchTrespass.xml`, `Tables/Libs/Tables/ai/ScriptParams.xml` |
| **search** (crimesearch/lookAround) | A guard that knows of a crime but did not see the player searches. Finding the player produces the arrest intro `notResistingArrest_lookedFor` (`previousReaction == lookAround`). (data-verified) | `Scripts/AI/crime/getResolutionDialogIntroKind.xml` |

### 1.3 `interrupt_arrest`: approach, summons, chat, escalation

Source: `Scripts/AI/npc/basic/switch/interrupt_arrest.xml`. (data-verified unless noted)

- **Contexts on the guard:**
  - `crime_interruptArrest` and `crime_playerUnderArrestByAuthority target=$this.id`.
  - Global `GameContext context=crime_escalationLevel_script_global_confrontingGeneral` and `crime_music_high`.
  - `AddBuff … '1951e0bc-…'` (`interrupt_deafness`, `hearing=0`, from `Tables/Libs/Tables/rpg/buff.xml`).
  - `RemoveBuffs … 'ffc20522-…'` ("rm drunkeness").
- **Knock-out suppression.** Unless the NPC has `combat_leavePlayerUnconsciousAfterFight`, the preset `GameContextPreset preset=crime_suppressUnconsciousPlayerShenanigans` is active. Per `Tables/Libs/Tables/ai/ScriptContextPreset.xml` it sets `disableHangoverTeleport`, `crime_disableRobbingUnconsciousPlayer`, `crime_disabledThrowingOutUnconsciousPlayer` and `crime_suppressUnconsciousTimeskip`.
- **Movement.** `SubsequentLocationDecorator Location=$__player` wraps `Function_crime_drawWeaponDecorator subtreeFile='crime/arrestselfhelp/actionsubtree.xml'`. That subtree runs `CrimeFollower Target=$__player Mode=DontBackOff Role=Main` (the urger) or `Role=Assist`.
- **Spreading.** `Function_crime_emitInformation … reactionKind=arrest emitAlarmInformation=$const_true` (see §8).
- **Contact at 7 m.** `DistanceGate … ReferencePt=$__player Low=7.000000` leads to `ForceLook`. Then:
  - Bark `metarole='STRAZ_ZATYKANI_PRVNI_VYZVA'`.
  - Chat: `Function_speech_dialogInitiator … metarole='STRAZ_ZATYKANI_CHAT' preset=$enum:dialogPreset.chat recipient=$__player`.
  - Every `Wait duration='6s' variation='2s'` without chat feedback, `surrenderAttempts += 1`. At `>= 4` it sets `$goIntoAttack = true`; below that it barks `STRAZ_ZATYKANI_OPAKOVANA_VYZVA`.
- **The chat dialogue.** `Scripts/Quests/Final/Barbora/utils/crime/punishment/arrestdialogue/straze__zatykaci_chat.xml` has `Dialogue Type="chat" … Decision … Alias="zatykaciChat" TimeLimit="10"`. Its outcomes, as wired in `utils/crime/punishment/arrestdialogue.xml`:
  - Surrender prompt: port `vstoupit_do_zatykani` sends `crime:arrestChatFeedback` `accept`, which sets `$goIntoResolve = true`.
  - Refuse prompt: port `hrac_odporuje` sends `reject`.
  - No answer: port `evade`.
  - `reject`, `evade` and the default branch all set `$goIntoAttack = true`.
- **Auto-resolve.** `$goIntoResolve` is also set, without asking, when the player is lying down (`StanceBarrier stance=lying`), unconscious (`DeadUnconsciousGate State=Unconscious`), time-skipping (`IsTimeSkipping`), lockpicking (`LinkGate … tag='lockpick'`), looting (`tag='loot'`) or reading.
- **Attack triggers** (`$goIntoAttack`):
  - The player gets more than `crime_arrest_maxDistanceFromChatStart` (25 m) from where the chat started.
  - Aiming (`ExternalLock … 'observerModeAim_lock'`, KCD2-131594).
  - Mounting (`StanceBarrier … stance=horse`, KCD2-89853).
  - A drawn weapon (polled every `250ms`).
  - More than 1 min without closing to 7 m (`Wait duration='1m'`).
  - A `crime_cooperationNotification` of `arrestEscalation` or `selfhelpEscalation` from another reactor.
  - The attack itself: `AffectFeelings … Token='confrontationDialog_run' AffectReputation=true`, a broadcast of `arrestEscalation`, then `Function_callInterrupt_attack … priority=160 relationOverride=true … target=$__player`.
- **Escape.** `CallDecorator_crime_handlePlayersEscape` on `PlayerLost` runs `AddArrestEscalation Npc=$this.id Reset=false` and `Function_callInterrupt_lookAround`.
- **Losing the target crime.** On `crime_cooperationNotification` `crimeResolved` or `informationTransformed`, `CheckInformationKnowledge Information=$arrestData.information Holder=$this.id`. If the guard no longer knows it, it takes the next `GetMostImportantCrimeInformation` (authority: `OnlyRelated=false`). If there is none, `$crimesResolved = true` and the arrest ends.

### 1.4 The resolve dialogue: node, partner, preconditions

Source: `Scripts/AI/crime/resolveCrimeDialogue.xml`. (data-verified)

- **Wrapper.** The whole resolve runs under:
  - `EntityContext context=crime_inCrimeDialog` on the guard *and* on `$__player`, with the comment "so that we can access it kinda globally".
  - `GameContext context=NoDog`.
  - `Semaphore SemaphoreCount=1 … LockManagerType=Global SemaphoreName='crime_resolveDialog'`. Only one crime dialogue can run in the world at a time.
- **Pooling information first:**
  - `CircularSpatialQuery Radius=50.000000 Center=$this.id … Filter=LivingHumanNPCsPlayerExcluded`, then `Function_spreadInformationWithinFriends` (`SpreadInformation` among friends).
  - `GetKnownInformations Holder=$this.id`.
  - Assaults on victims who are now dead become murder, and so on (`crime_transformInformation`).
  - `getPlayersOffences` and `createCrimesFromOffences` (drawn weapon or no torch *now*).
  - `cleanupDuplicitThefts`, `singleResistingArrestFix` (adds a `disturbance` if `resistingArrest` is the only crime), `crime_calculatePunishment`.
- **Unconscious player:**
  - If the NPC has `combat_leavePlayerUnconsciousAfterFight`, the result is `leaveUnconscious` with no dialogue.
  - If the NPC's `crime_npcCooldowns.resolve_evade` is younger than `crime_resolveCooldown` (900000 ms), the dialogue is skipped. The result is `GameOver Reason=game_over_bohutaArrested` (Bohuta), or `GameOver Reason=DiedInCombat` under `crime_killUnconsciousPlayerOnRepeatedResolve`, or otherwise direct `punishment`.
- **Dialogue start, under `ResolveCrimeDecorator Information=$knownInformations` then `FaderBarrier Fader='crimeDialog'`. The editor comment says "all this needs to be under resolveCrimeDecorator".**
  1. `Function_crime_getResolveDialogContext …` builds the float map `dialogContext` (§3.1).
  2. It waits until neither the guard nor the player is in a dialogue, for at most `Wait duration='4s'`.
  3. The metarole is picked:
     - A quest override, via link `crime_arrestResolutionOverride` plus a predicate tree (`IncludeTree File=$arrestResolutionOverrideData.predicateFilename`).
     - Else `'STRAZ_ZATYKANI_ZIKMUND_TABOR'` in the kzik camp.
     - Else `'STRAZ_ZATYKANI'`.
  4. If the player is unconscious: `Wait 2s`, `AddBuff SoulWUID=$__player BuffGUID='bd22f98a-…'` (`remove_unconsciousness`), then `AddInterrupt … 'interrupt_player_waitAction'`.
  5. `Function_speech_dialogInitiator … context=$dialogContext … metarole=$arrest_metarole preset=$enum:dialogPreset.fader recipient=$__player`.
  6. `ProcessMessage … timeout='10s' … inbox='crime_resolveDialogFeedback'` reads `$resolution.kind = $resolveDialogFeedback.action`. The **fallback when no feedback arrives is `$resolution.kind = …skillCheck`**.
  7. `$resolveSuccessful = $resolution.kind ~= fight`.
  8. `SendAIConceptSignal_crime_arrestResolveResolution`.
- **How the dialogue is found.** `Scripts/AI/speech/dialoginitiator.xml` defaults to `recipient:_wuid In $__player`. `polyloginitiator_init.xml` puts the initiator first; the native `RequestDialog DecisionAlias=… SoulsToMetaroles=…` then `DoDialog` selects the dialogue by metarole (`dialoginitiator_inner.xml`).
  - `Tables/Libs/Tables/rpg/role.xml` maps role `STRAZNY_ZATYKANI` (also `ALBIK_ZATYKANI`, `OPAT_ZATYKANI`, `STRAZ_KLASTER`) to `metarole_name="STRAZ_ZATYKANI"`, and role `HRAC` to `metarole_name="PLAYER"`.
  - The role is granted by the Storm rule `crime_authority` (`<isAuthorityFigure />`, `addRole name="STRAZNY_ZATYKANI"`) in `IPL_GameData/Libs/Storm/roles/world/crime.xml`.
  - The partner is always the player: every crime caller passes `recipient=$__player`, and the dialogue's other role needs metarole PLAYER. (data-verified)
- **The dialogue itself.** `Scripts/Quests/Final/Barbora/utils/crime/punishment/arrestdialogue/straze__zatykaci_dialog.xml`:
  - `<FaderDialog Name="straze__zatykaci_dialog">`, `<Decision Name="dec1" … Alias="arrestdialogue_strazeZatykaciDialog" …>`.
  - `SelectedSoul Role="HRAC"` and `Role="STRAZNY_ZATYKANI"`.
  - Entry is `seq2836` (`var('isSecondArrest')==0`, trigger `normalarrestentered`) or `seq2837` (`isSecondArrest==1`).

### 1.5 Choices (decision `dec6`, `DesignName="hráč se rozhoduje - Henry"`, `EnableCrimeHud="true"`)

Choices are listed by sequence name, with the prompt paraphrased. (data-verified)

| Sequence(s) | Choice (paraphrase) | Condition | Result sent |
|---|---|---|---|
| `fineValue` → `dec14` / `dec1059` / `dec16` | "I'll pay the fine" | `var('fine') > 0 & var('fineDisabled') == 0 & !var('fineChosen') & var('players_money') >= var('fine')` | `Crime.SendResolveDialogResult(dc, enum_crime_resolutionKind.fine)` (seq44, seq2585, seq2586). If `confiscation > 0` there is a search first. For murder the pay line is "blood money" (seq2667). |
| `fakeFine` | "I don't have enough money" | `var('players_money') < var('fine') …` | The guard names the punishment (seq2574–2577) and goes back to `dec6`. |
| `seq2549` / `2550` / `2551` / `2552` | "I accept the punishment" | `punishment_pillory`, `_beating`, `_branding`, or branding with `playerBranded` / `known_criminal_poprava` | `PunishmentType='Pillory'`/`'Beating'`/`'Branding'`/`'Execution'`. Henry: dec1049 `jailChosen` → dec1059 (search lines if `confiscation > 0`) → dec16 → dec1026 `no_override` → seq249/247/245/246, which send `…resolutionKind.punishment`. Bohuta: seq2735 sends punishment. |
| `persuade_difficulty_1…5_1` | "We'll work something out…" | `var('skillcheckLevel')==N AND var('skillcheck')<1` | `SkillCheckType='persuade'`, `SkillCheck='Medium'…'AutoFail'` |
| `impress_difficulty_*` | "Do you know who I am?" | same | `SkillCheckType='impress'` |
| `threaten_difficulty_*` | "Let me go or else" | same | `SkillCheckType='dread'` |
| `seq2768…2774_1` | "The law is on my side" | `… AND Port('has_perk_zaklady_prava_ii')` (perk **Basic law II** `74a0160c-…`, `Tables/Libs/Tables/rpg/perk__kcd2.xml`) | `SkillCheckType='scholarship'` |
| `seq2777…2784_1` | "I only had a little to drink" | `… Port('has_perk_incapable_drunk') AND var('murder')==0 …` (perk `3dcced66-…` **Incapable drunk** AND `BuffTagCheck` 0 or 1) | `SkillCheckType='drinking'` |
| `seq31` → `fight_exit` | "I won't surrender! (fight)" | always | `Crime.SendResolveDialogResult(dc, enum_crime_resolutionKind.fight)` |

- **Skill-check difficulty.** `ExecuteLua … Crime.GetSkillcheckLevelFromPrice(data.discountedFine)`. In `Scripts/Scripts/Systems/Crime.lua` the thresholds in decigroschen are <500 → 1, <1000 → 2, <1500 → 3, <7500 → 4, <20000 → 5, else 6. Levels map to Medium, Hard, VeryHard, ExtremelyHard, RidiculouslyHard and AutoFail.
- **Skill-check outcome.** Only one attempt (`GameUtils.SetLocalVar("skillcheck", 1)`).
  - Success: `SkillcheckCondition='Success' Reputation='quest_decrease_3_small'`, reaching `skill_check_success_exit`, which sends `enum_crime_resolutionKind.skillCheck`.
  - Failure: `SkillcheckCondition='Fail' Reputation='quest_decrease_4_normal' … GoToDecision='dec6'`.
  - `Tables/Libs/Tables/rpg/reputation_change.xml`: `quest_decrease_3_small change="-0.18"`, `quest_decrease_4_normal change="-0.25"`, both target 15 "all". (data-verified; the reputation is applied by the dialogue system, inferred)
- **Not present.** There is no bribe option and no "it wasn't me" option for guards. Excuses exist only as persuade lines. Bribe-like "pay all money" exists only for civilian selfhelp (`crime_selfhelpResolutionKind.payAllMoney`, which uses `MoveItem … ItemGUID='5ef63059-…' … Amount=$playersMoney` in `Scripts/AI/crime/selfhelp_resolveCrimeDialogue.xml`) and for bandit surrender dialogues. Surrender exists as the arrest chat option, and in combat through `interrupt_attack` → `crime_playerIsSurrendering` / `crime_playerSurrenderChatFeedback` → `Function_crime_resolveCrimeDialogue` (`Scripts/AI/npc/basic/switch/interrupt_attack.xml`). (data-verified)
- **The feedback path.** `Crime.SendResolveDialogResult(dc, action)` checks the roles `{'STRAZNY_ZATYKANI','STRAZ_V_ZIKMUNDOVE_TABORE','STRAZ_KLASTER','ALBIK_ZATYKANI','OPAT_ZATYKANI'}` and calls `XGenAIModule.SendMessageToEntityData(entity, 'crime:resolveDialogFeedback', {action=action})`. The node equivalent is Skald module `utils/crime/sendarrestresolvefeedback.xml`, `InstantSendMessage MessageType="crime:resolveDialogFeedback"`, which `arrestdialogue.xml` uses to send `resolvekind Value="questPunishment"`. (data-verified)

### 1.6 Applying the result (`crime_resolveCrimeDialogue`, after the dialogue)

Source: `Scripts/AI/crime/resolveCrimeDialogue.xml`. (data-verified)

**Branch for fine, skillCheck, fight and leaveUnconscious** (`kind ~= punishment & ~= questPunishment & ~= secondArrest`):

- On success:
  - `Function_player_dismountHorse dismountOnlyIfRelevantHorse=true`.
  - `confiscateFine = kind == fine`. `confiscateItems = punishment.confiscation > 0 & kind ~= skillCheck ? all : relevant`.
  - `Function_crime_destroyTheftVolumesForConfiscatedItems`, then `Confiscate ConfiscateItems=$confiscateItems ConfiscateFine=$confiscateFine TargetStash=$stolenItemsChest`.
  - If a fine was taken: `SendAIConceptSignal_crime_moneyTaken amount=$punishment.fine`.
  - For each info with `isCrime`: `Function_crime_resolveCrimeInformation` (details below).
  - `broadcastCooperationNotification crimeResolved`, `Function_crime_addFriskCooldown`.
- Always: `Function_crime_addResolveCooldown` (`resolve_sucess` / `resolve_evade` timestamps on nearby NPCs).
- Then by kind:
  - **fight:** `AffectFeelings Token='confrontationDialog_run'`, `CreateCombatInformation Victim=$this.id AttackKind=unarmed DirectHit=true`. Because the guard carries `crime_playerUnderArrestByAuthority`, this creates `resistingArrest` (FOREST of `crime/createCombatInformation.xml`, inferred). Then `AddArrestEscalation Npc=$this.id Reset=false` and `Function_callInterrupt_attack … escalatedFromFailedSurrender=true … priority=160 … target=$__player`.
  - **fine, skillCheck:** `player:holsterWeapon` request. For skillCheck when the punishment would have been execution, the `closeOne` achievement.
  - **fine or skillCheck and the player is in trespass:** `callInterrupt_watchTrespass … onlyWaitingForDeparture=true` (or `throwOutUnconsciousPlayer`). Otherwise `callInterrupt_mindPlayer`.
  - **A stolen horse not at home within 10 m:** a link `crime_followUpBehavior_returnStartledAnimal` with reason `afterArrest` (the horse is returned).

**Branch for punishment, questPunishment and secondArrest:**

- Pool information: `getKnownInformationForQuestPunishment` (area `crime_questPunishment_reconcileArea`) or `getKnownInformationForSecondArrest shouldIncludeLastArrester=false` (town-wide, §3.2).
- `ResolveCrimeDecorator`, then `crime_recalculatePunishment`.
- If `kind ~= secondArrest & (newPunishmentType == punishmentType | skipDialogue | questPunishment)`:
  - Dismount.
  - `Confiscate … ConfiscateFine=false` if `confiscation > 0`.
  - `resolveCrimeInformation` on all crimes.
  - `crime_questPunishment_aiResolveFinished` (a quest does the punishment), **or** `Function_crime_forcePlayerPunishment fine=$punishment.fine punishmentType=$punishmentType` followed by `crime_aiResolveFinished`.
- Otherwise ("Round 2"): `Function_crime_prepareSecondArrestForConcept`, with `punishmentChanged = crimesResolved = true`.

**Cleanup** (FuseBox `OnFail` branch, which runs when the resolve subtree is torn down; exact FuseBox semantics are native, inferred):

- `IfElseCondition … $resolveSuccessful | $punishmentChanged` → `Function_crime_reconcileAfterResolve reconcileParticipants=$nearbyNpcs`.
  - `Scripts/AI/crime/reconcileAfterResolve.xml`: `Reconcile faction=player` plus `AddNervousness Change=auto_reconcile` on the guard and every participant, and `AddArrestEscalation … Reset=true` on participants.
- `resetReputationAfterCrimeResolve` (Reconcile everyone within 30 m) is used **only** by the civilian `selfhelp_resolveCrimeDialogue`. (data-verified)

**`crime_resolveCrimeInformation`** (`Scripts/AI/crime/resolveCrimeInformation.xml`) does per-label bookkeeping, then **`CheckInformationKnowledge Holder=$this.id` → `DestroyInformationCompletely Information=$information`**.
- Theft: `ResolveStolenItems Items=$stolenItems`. **If the result was a skill check:** `LegalizeItem` per instance, or `crime_legalizeDivisibleItem` for stacks. Also `ResetStashRobbedValue` / `ResetNPCRobbedValue`, and theft volumes are despawned.
- Lockpick: the stash or door is re-`Lock()`ed.
- Assault or murder: corpse links, `crime_lastHitByPlayer` removed.
- Home trespass: escalation paused.

**Informations removed, and from whom.** Every crime the resolving guard knows, including what it just pooled from friends within 50 m (and, on the punishment path, from every civilian of the punishment location), is destroyed with `DestroyInformationCompletely`.
- Contrast `crime_forgetCrimes` (`DestroyInformationFromHolder`, only that NPC). Quests trigger it through `utils/crime/stopcrime.xml`: `crime:stopCrime` → `so_interrupt_onUpdate`/`crime_stopCrime` → `stopReaction` sent to all `crime_reactor` links, then `crime:forgetCrimesData` sent to the listed NPCs.
- That "Completely" deletes the information for every holder is inferred from the node name versus `…FromHolder`, but it is strongly implied. Other reactors are told to re-check through `cooperationNotification crimeResolved`. (data-verified wiring)

### 1.7 What moves

| Value | Where / how | Tag |
|---|---|---|
| **Money** | `Confiscate … ConfiscateFine=true` (fine result only). Native `C_CrimeResolver::ConfiscateFine`. DLL strings: "Player too poor to pay fine, check if Confiscate is correctly set up.", "Trying to confiscate zero amount of money." The amount is the resolver's *reduced* fine: `GetCrimeData Fine=$discountedFine` overwrites `dialogContext['fine']`; perk reductions come from `C_CrimeResolver::ComputeReducedFines` and label `perkFineReduction` in `Tables/Libs/Tables/rpg/crime.xml`. The UI shows `fine/10` (`DivideFloat … B=10` → `Payment_fineValue`, so the table is in decigroschen). Where the coins end up is not visible in data. | data-verified flags and strings; that the reduced fine is the charged amount is inferred; destination unknown |
| **Items** | `Confiscate ConfiscateItems=relevant\|all\|relevantOwnAndMates TargetStash=…`. The target comes from `Scripts/AI/crime/getStolenItemsStorageChest.xml`: authority → nearest `crime_stolenItemsStorageChest` of `GetCrimeDistrict`; gamekeeper → `crime_gameMeatRefrigeratorChest`; else a Chest on the guard's `home`; else the guard's own inventory. DLL: "Failed to borrow item %s to confiscater …", "Failed to transfer non-quest item %s to target inventory …" (quest items are only borrowed, inferred). | data-verified |
| **Reputation** | Fight: `AffectFeelings Token='confrontationDialog_run'` (maps to `auto_confrontationDialog_run change="-0.15" reputation_cap="-0.4"`, inferred mapping). Skill checks: −0.18 on success, −0.25 on failure. Success: `Reconcile faction=player` (guard + 50 m pool). Branding: faction `branding` −0.5 (§3.5). Every punishment: `ReconcileWithPublicFriends`. | data-verified (token → table mapping inferred) |
| **Cooldowns** | Guard: `crime_npcCooldowns` (`resolve_sucess` / `resolve_evade`). Player: `crime_globalCooldowns.lastFriskTimestamp` (`addFriskCooldown`), and also `crime:friskCooldownRequest` after punishment. | data-verified |

---

## 2. Starting the dialogue on purpose

- **Quest-run AI tree (the proven route).** `Scripts/AI/quests/sermiri/sermiri.xml`, tree `sermiri_arrest`:
  - It re-creates the arrest contexts.
  - `SpreadInformation WuidArray=$wuidDataArray` (witnesses → guard), then `GetMostImportantCrimeInformation`, then `TeleportAction Position=$wuidData`.
  - `Function_crime_getResolutionDialogIntroKind crimeSeen=true …`, then **`Function_crime_resolveCrimeDialogue postresolveSubscribers= resolveData=$resolveData …`**.
  - It is started by Skald `EnableBehavior … <Constant Name="Behavior" Value="sermiri_arrest" />` on `NPC Alias="arrestGuard"` with a witness array, in `Scripts/Quests/Final/Barbora/kutnohorsko/sermiri/streaming_kumel_guards.xml`.
  - `sermiri_tournamentPunnishment` calls `Function_crime_prepareSecondArrestForConcept` directly, which forces the town-guard second-arrest flow. (data-verified)
- **Other entry points** (data-verified):
  - The quest can replace the dialogue metarole through `crime_arrestResolutionOverride` (`utils/crime/crimearrestoverride.xml`, a `LinkEffect Tag="crime_arrestResolutionOverride"` with metarole and predicate tree).
  - The dialogue var `questPunishmentOverride` (sequence `quest_override`) plus the result `questPunishment` hands the punishment to the quest (`crime_questPunishment_aiResolveFinished`; listeners include `kutnohorsko/erik/hibernables/arrest.xml` and `klaster/.../crime_v_klastere.xml`).
  - Quests may skip AI entirely and run `open_world.nextnextgenpunishment.utils.playpunishment_cutscenebuffsmonolog` (sMlynariNejsouZerty, listovniTajemstvi, mucirna).
  - The second arrest itself is a quest `AddInterruptConceptNode … Signature="interrupt_secondArrest"` with `Priority=255 Urgency=Instant`.
- **No force node.** There is no generic "ForceDialog/StartCrimeDialog" node. `speech_dialogInitiator` → `RequestDialog` is generic, and nothing in the AI trees starts the arrest decision except the three callers: `resolveCrimeDialogue`, `interrupt_secondArrest` and the quest tree above. (data-verified by grep)
- **What must be present:**
  1. The tree runs in the **guard's brain**. It uses `$this.id` everywhere, the host must be an information holder, and the DLL errors "Cannot retrieve information holder from host." and "Information must be held in an array." come from `ResolveCrimeDecorator`. (data-verified strings)
  2. **At least one crime information held by the guard**. `calculatePunishment` iterates `GetKnownInformations Holder=$this.id`, and `getResolveDialogContext` ends in `ErrorNode 'unhandled crime for arrest dialog'` when `punishment.primary` has no known label. (data-verified)
  3. **A single crime resolver**: `ResolveCrimeDecorator` ("Crime resolver already initialized."), the singleton `C_AISingletons::CrimeResolver()` (DLL test string), `GetCrimeData`/`Confiscate` ("Needs to run under resolveCrimeDecorator.", "Confiscate called with uninitialized crime resolver."), and the global Semaphore `crime_resolveDialog` count 1. (data-verified)
  4. A soul with a role whose metarole is `STRAZ_ZATYKANI` (authority figures only), and the PLAYER as partner. (data-verified)
  5. The AI must be waiting on `crime_resolveDialogFeedback`, or the result is lost. If no answer arrives in 10 s the tree assumes `skillCheck`. (data-verified; what happens when a quest starts the Skald dialogue by itself without the AI is inferred to be broken)

---

## 3. Punishment

### 3.1 Choosing the punishment (AI side)

- **`crime_calculatePunishment`** (`Scripts/AI/crime/calculatePunishment.xml`) sums the fine per crime using `Tables/Libs/Tables/rpg/crime.xml`:
  - Examples of `fine`: theft 500, murder 20000, assault 1500, horseTheft 2000. Social-class multiplier `GetStatusMultiplier` applies where `scalingWithSocialClass`.
  - Theft uses the item value × `crime_theft_fineMultiplier` plus the base fine.
  - A stolen horse that is dead, or "pay'n'spray" (`perceivedWuid == GetPlayerHorse`), adds `soul:GetDerivedStat('cnp')*10`.
  - Flags: `murder`, `violent`, `poaching`, `confiscation` (2 for theft or pickpocket, 1 for crimes with `confiscation="true"`), `crimeCount`, `victimIsGuard`, `victimIsWoman`, and `primary` (highest `importance`).
  - `punishment.jail` is summed but never used to jail anyone. (data-verified)
- **`crime_getResolveDialogContext`** (`Scripts/AI/crime/getresolvedialogcontext.xml`):
  - `IfElseCondition … $punishment.murder == 1 | $punishment.poaching == 1 | $discountedFine > $data:script_param['crime_punishmentFineThresholdForBranding'].value` sets `punishment_branding = 1`. The threshold is `Value="20000"` in `ScriptParams.xml`.
  - Otherwise `$punishment.violent == 1` gives `punishment_beating`; otherwise `punishment_pillory`.
  - `playerBranded`: `GameModeCondition GameMode=Hardcore` → `LuaGate … player.soul:GetDerivedStat('brn') > 0`; otherwise `IsPlayerFreshlyBranded` (a native node in DLL `XBehaviorModule.dll`, `C_IsPlayerFreshlyBranded`).
  - `punishmentType` = pillory, beating, branding (if not branded) or execution (branding and branded). (data-verified)
  - The dialogue additionally shows execution for `Port('known_criminal_poprava')`. That is `hasplayereverbeenbranded` (`brn >= 0.5`, `utils/rpg/hasplayereverbeenbranded.xml`) AND the script perk `68fb5e7f-…` `Hardcore_KnownCriminal` (`perk__hardcore.xml`). The AI's Hardcore rule `brn > 0` covers the same case. (data-verified; the equivalence is inferred)
- **Intro kind.** `crime_getResolutionDialogIntroKind` picks one of `unconscious`, `resistingArrest_beaten`, `notResistingArrest_lookedFor` (the guard came from lookAround), `notResistingArrest_crimeSeenByGuard`, `notResistingArrest_foundAccidentally`, `resistingArrest_violent` (a `resistingArrest` info exists), `resistingArrest_running` or `resistingArrest_repeated`. These only choose the guard's opening lines. (data-verified)

### 3.2 The town-wide recheck ("second arrest")

- **Pooling the town.** `Scripts/AI/crime/getKnownInformationForSecondArrest.xml`:
  - `getPunishmentArea` → `getPunishmentLocationGUID` → **`GetCiviliansForLocation location=$locationGUID`**.
  - It adds `$this.id` (and the last arrester).
  - `EntityContext context=crime_receiveIrrelevantInformationSpreads` wraps **`SpreadInformation WuidArray=$validCivilians`**, then `GetKnownInformations Holder=$this.id`.
  - The guard therefore learns every crime known to any civilian of the punishment town. (data-verified)
- **Recalculation.** `crime_recalculatePunishment` runs `calculatePunishment` again plus `getResolveDialogContext` with intro `notResistingArrest_crimeSeenByGuard`. (data-verified)
- **If the type changed:** `Scripts/AI/crime/prepareSecondArrestForConcept.xml` runs.
  - It links `crime_secondArrest_lastArrester`.
  - It takes the punishment area's `punishment_defaultGuard`, waking it if it is unconscious and has `punishment_wakeUpForSecondArrest`. That sets `guardAvailable`.
  - It links `punishment_guardSpawnPoint`, then sends `SendAIConceptSignal_crime_punishmentRecalculateDialog guardAvailable=… punismentLocation=…`.
- **The quest side.** `open_world/nextnextgenpunishment/second_arrest_before_new_punishment.xml` → `fasttravel_if_needed` → `secondarrest_logic`:
  - If no guard is available, a guard random event spawns (`SoulPool="extraGuards_dummy"`).
  - `AddInterruptConceptNode … Behavior=interrupt_secondArrest … Priority=255 Urgency=Instant`.
- **The second arrest itself.** `Scripts/AI/npc/basic/switch/interrupt_secondArrest.xml`:
  - `FaderBarrier Fader='crime_secondArrestFader_2'`.
  - `TeleportAction Position=$guardPoint`, and the player is teleported to the `playerPoint` link through `switch:teleportRequest`.
  - The same dialogue opens with `dialogContext['isSecondArrest'] = 1` and `ProcessMessage … timeout='-1'`.
  - Results: fine → confiscation + fine. Punishment → `Function_crime_forcePlayerPunishment fine=$dialogContext['fine'] punishmentType=$newPunishmentType`. Fight → attack. (data-verified)

### 3.3 From AI to quest

- **`crime_forcePlayerPunishment`** (`Scripts/AI/crime/forcePlayerPunishment.xml`) runs three steps: `Function_crime_preparePunishmentForConcept`, then `SendAIConceptSignal_crime_forcePunishmentFromAI punishmentType fine`, then `SendAIConceptSignal_crime_playerPunishedNotification`. (data-verified)
- **`crime_preparePunishmentForConcept`** (`Scripts/AI/crime/preparePunishmentForConcept.xml`):
  - It gets the area from `getPunishmentArea`: the `crime_punishmentArea` the player is in (`punishment_redirectArea` can redirect), else the `$__land` link `punishment_fallbackArea`, then the link data `punishment_location`.
  - It picks the cutscene holder:
    - Execution: `GraphSearch Origin=$__land … LinkTagFilter tag='punishment_executionCH'` ("Execution is static per map").
    - Pillory: `punishment_pilloryCH_firstRun`, or `_var` once `GameContextCheck context=crime_punishmentPilloryFirstRunExecuted`.
    - Beating: `punishment_beatingCH`. Branding: `punishment_brandingCH`.
  - It re-links the open-world QSO assets `punishment_cutscene` and `punishment_teleportPoint`, links `fastTravel` from the fast-travel holder to the teleport point, and clears the `teleport` link on the `punishment_timeAdvance` holder. (data-verified)
- **Unused pieces.** `crime_chooseJail` and `crime_teleportPlayerToPunishment` have **no callers** (grep over `Scripts/AI`). `crime_isPlayerUnderArrest`, `crime_addArrestEscalation`, `crime_getArrestEscalation`, `crime_isHorseRelevant`, `crime_isPlayerOnStolenRelevantHorse`, `crime_createCombatInformation` and `switch_chooseReaction` are "obsolete, use its code successor". `crime_findStolenItemsFromInventory` has an empty Root commented `OBSOLETE:`. `utils/crime/punishment/punishment_executecutscenes.xml` (with `AdvanceWorldTime 8h` "only if 19:00-8:00") is referenced only by its own library. `punishment_forcepunishmentforquest.xml` is "(OBSOLETE,EMPTY)". Jail infrastructure exists natively (`C_Jail` in `XGenAIModule.dll`/`PlayerModule.dll`, `C_JailRecoveryBuff` in `RPGModule.dll`), but no shipped tree uses a `<Jail>` node. (data-verified)
- **Quest root.** `Scripts/Quests/Final/Barbora/open_world/nextnextgenpunishment.xml`, with `punishment_from_ai.xml` (`AIConceptSignalTrigger … NotificationName="crime_forcePunishmentFromAI"`, `Switch … SwitchValues="pillory beating branding execution"`). `triggersequence15` runs:
  - A: store the fine.
  - B: store the type.
  - C: `disabledEvents=true`, which sets `SetGameContext crime_playerInPunishment` and `crime_disabledFrisk`, plus `DisableRandomEvent RandomEventTag=All`.
  - D: `AddBuff 46683e3b-…` (`remove_injuries`), then `If CheckGameContext player_henry`, then `playpunishment_cutscenebuffsmonolog` with `isopenworldpunishment=true`, `shouldcheckskiptime=true`, `shouldadddebuff=true`, `shouldplaymonolog=true`, `shouldplaytextcutsceneinsteadofunequip=true`, `shouldchangeweather=true`, `shouldplayfasttravel=true`.
  - On `punishmentdone`: codex perk `codex_gen_crime`, `PilloryFirstRunExecuted`, and the events are cleared. (data-verified)

### 3.4 Exactly where the clock moves

All paths below are under `Scripts/Quests/Final/Barbora/open_world/nextnextgenpunishment/utils/playpunishment_cutscenebuffsmonolog/` (abbreviated `…/pp/`) unless stated. (data-verified unless noted)

**Order.** `FaderCutscene` (`AutoFinish=false`) → `AfterPlay` → `triggersequence14`:
- A: teleport the player (open world: `NPCs_TeleportIngame … destinations Alias="punishment_teleportPoint"`, an *interrupt* `teleport` at Priority 199, so it is asynchronous).
- B: unequip (only if not `shouldplaytext`).
- C: `skiptime_fasttravel_or_nothing`.
- D: finish the fader.
- E: enqueue the punishment cutscene.
- F: text cutscene "Later…" (only if `shouldplaytext` and not Execution).

`shouldcheckskiptime` reaching `execute_cutscene` is `and15` = `shouldcheckskiptime` AND `compare14 (punishmenttype NotEquals Execution)`. Note16 says "SkipTime while waiting for an execution is just annoying".

| # | Node and attributes | Condition | File |
|---|---|---|---|
| 1 | `DistanceCheck origin=player_any target=punishment_cutscene operator=LessEquals distance=200` | Measured when C fires. The teleport in A is an interrupt, so this effectively measures from the arrest spot (inferred). | `…/pp/execute_cutscene/skiptime_fasttravel_or_nothing.xml` |
| 2 | ≤200 m and `shouldcheckskiptime` → `skip_time_if_too_close`: `GetTimeOfDay`, `compare4 GreaterEquals 8h` AND `compare4_1 LessEquals 17h`. True enqueues `skiptimecutscene_short` (asset `punishment_skipTime_short`); false enqueues `skiptimecutscene_long` (`punishment_skipTime_long`). If the player is unconscious: `AddBuff bd22f98a-…` (remove unconsciousness). | Not execution; within 200 m | `…/skip_time_if_too_close.xml` |
| 3 | The only crime skip-time cutscene defined is `SkipTimeCutscene Name="crime_skipTime"` (`Tables/Libs/Tables/ui/cutscene.xml`). Which cutscene each holder plays, and the skip length, are level-entity data and not in the paks (holder-to-cutscene binding inferred). Note17 in `playpunishment_cutscenebuffsmonolog.xml`: "Short is 2hours when it's between 8am-10am – Long is til next 10am" (the code uses 8h–17h). | — | note: data-verified text; meaning inferred |
| 4 | >200 m and `shouldplayfasttravel` → `fasttravel_if_far_away` enqueues holder `punishment_fastTravel`. Its destination is the `fastTravel` link to the teleport point set by `preparePunishmentForConcept`; the cutscene is presumably `FastTravelCutscene Name="crime_punishment_fastTravel"` (inferred binding). On `OnFinished` and `shouldcheckskiptime`: **`wh::rpgmodule::AdvanceWorldTime TimeOfDay="10h0m0s"`**. The Skald definition reads "Advances world time to the specified time of day (up to 24 hours)" (`IPL_GameData/Libs/concept/definitions.xml`). | Farther than 200 m. For execution, fast travel still plays but there is no AdvanceWorldTime. Whether the fast travel itself adds travel time is inferred. | `…/skiptime_fasttravel_or_nothing.xml`, `…/fasttravel_if_far_away.xml` |
| 5 | Beating and branding cutscenes: `IngameCutscene Name="crime_beating_trosecko" Time="8h"` (the same for kutnahora, suchdol, malesov, miskovice and zikmundtabor, and for `crime_branding_*`). Pillory and execution are `RenderedCutscene` videos with no `Time`. | Whenever beating or branding plays. Whether `Time` jumps the world clock or only the cutscene lighting is inferred. | `Tables/Libs/Tables/ui/cutscene.xml` |
| 6 | Second arrest: `fasttravel_if_needed`. `or6` = `DistanceCheck ≤200` OR `currentlevel Equals Klaster`. True: no fast travel. Otherwise `punishment_fastTravel`, then **`AdvanceWorldTime TimeOfDay="9h"`**. | A second arrest far from the punishment spot, not in the monastery | `open_world/nextnextgenpunishment/second_arrest_before_new_punishment/fasttravel_if_needed.xml` |
| 7 | Knock-out time skip suppressed: `crime_suppressUnconsciousPlayerShenanigans` → `crime_suppressUnconsciousTimeskip` (Game context, `SideEffect="crimeSuppressUnconsciousTimeskip"` in `Tables/Libs/Tables/ai/ScriptContext.xml`; consumed natively in `WHGame.dll`). | Active during `interrupt_arrest`, `interrupt_attack` and `interrupt_selfhelp`, unless the NPC has `combat_leavePlayerUnconsciousAfterFight`. The skip itself is native (inferred). | `Scripts/AI/npc/basic/switch/interrupt_arrest.xml` |
| 8 | Guards react to the player's own time skip: `IsTimeSkipping` makes arrest and frisk skip straight to the dialogue. `interrupt_watchTrespass` runs `InterruptSkipTime` within 4 m. | During arrest, frisk or trespass watch | `interrupt_arrest.xml`, `interrupt_frisk.xml`, `interrupt_watchTrespass.xml` |
| — | Not executed: the FOREST of `resolveCrimeDialogue.xml` contains `Wait duration='20m'` ("KCD2-213332 – let player be briefly unconscious"). The legacy `punishment_executecutscenes.xml` uses `AdvanceWorldTime TimeOfDay="8h"`. | Detached or unused | — |

**Other things done during the punishment:**
- `DespawnRandomEvents Tag=All` on `BeforePlay`.
- `ChangeWeather Profile="semicloudy_clear" BlendTime="0"`.
- `TextCutscene Name="crime_punishmentTimeAdvance" … Duration="3s"` (the "Later…" card).

### 3.5 What each punishment does (`…/pp/punishment_debuff.xml` → `utils/crime/punishment/addpunishmentbuff.xml`)

**Severity** (`…/getpunishmentseverity.xml`): fine ≤ `crime_punishmentSeverityThreshold_medium` (500) is Weak; ≤ `_high` (1000) is Medium; otherwise Strong. The fine is in decigroschen. (data-verified)

| Type | Effects (data-verified) | Buff (`Tables/Libs/Tables/rpg/buff.xml`) |
|---|---|---|
| Pillory | `AddBuff` by severity; `pillory_specific_debuffs`: `AddDirt Value=1` and `SetState exhaust` / `hunger` = 0.5 × current | `crime_punishment_pillory_weak` / `_medium` / `_strong`: `charisma-2,con+0.2,plr+1` / `charisma-4,con+0.4,plr+2` / `charisma-6,con+0.6,plr+3`; `duration="1800"` / `2400` / `3600` |
| Beating | `AddBuff` by severity; `AddBlood` torso and both arms = 1; `SetState health` = health − health × 0.2 / 0.4 / 0.6 (health loss even when no new buff is added) | `crime_punishment_beating_*`: `strength*0.8,agility*0.8,vitality*0.8,courage*0.8,srg*0.8,hlt*0.8,bea+1` (0.7 and `bea+2`, 0.6 and `bea+3`); 1800 / 2400 / 3600 |
| Branding | `AddBuff 2140972b-…` then `AddBuff bc7ec5a9-…`; `SetState health` = health − health × 0.069; tutorial `OB_T11_Brand`; **`AddReputationChange ReputationChange="branding"`** on `GetFaction` `kutnohorsko_settlements` (Kutnohorsko) or `trosecko_settlements` (default) | `branding`: `brn+0.5;brn+0.5`, `duration="10800"`, `Cpp:Branding`. `crime_punishment_brand`: `grm*0.5,sdn+1,brn+0.5`, 10800, `Cpp:ModifiableTimed`. `reputation_change.xml`: `branding change="-0.5" instant="true"`. |
| All | `AddBuff e928b585-…` (`player_remove_drunkness`), `crime:friskCooldownRequest`, `ReconcileWithPublicFriends`, post-punishment monologue barks | — |

- **Tier rules.** A new buff is added only if the new tier (1/2/3) is ≥ the current `plr` or `bea` stat. Lower tiers are removed first (`removebuff8`, `removebuff8_1`). Hardcore perks swap in harsher buffs (`perk_buff_override.xml`, for example `crime_punishment_pillory_strong_hardcore charisma-20`). (data-verified)
- **What ends them.** Buff timers (the table does not state the unit of `duration`), or the penitent-pilgrimage quests (`kutnohorsko/kajicna_pout__kutnohorsko.xml`, `trosecko/kajicna_pout__trosecko/…`), which use `utils/crime/punishmentremovedebuffs.xml` (`RemoveBuff` for all seven pillory, beating and brand buffs). "Freshly branded" presumably tracks the 10800 brand buffs, and `brn >= 0.5` remains afterwards as "ever branded" (inferred).
- **Other ties.** The stat `brn` also gates the criminal perks `perk_criminal_stat_bonuses` (`Cpp:IsBranded`) and `perk_criminal_price_bonus` in `buff__perk.xml`. (data-verified)

---

## 4. Execution

- **Trigger.** Branding-level crime and `playerBranded` (§3.1), giving punishment type `execution`. The dialogue choice `seq2552 … PunishmentType='Execution'` runs dec1026, then `seq249`, then `Crime.SendResolveDialogResult(dc, …punishment)`. Paying the fine is still offered unless `fineDisabled` is set. The only cheap outs are paying, a skill check (achievement `closeOne`) or fighting. (data-verified)
- **Flow.** `preparePunishmentForConcept` → `punishment_executionCH` from `$__land` → quest cutscene `crime_execution_trosecko` or `_kutnahora` (`RenderedCutscene … cin_s9912t_crime__beheading_troskovice` / `cin_s9924k…`). In `playpunishment_cutscenebuffsmonolog.xml`: `execute_cutscene.cutsceneonplayed` (`PunishmentCutscene.AfterPlay`) → `ifcompare13 … ValueB="Execution"` → **`Function … MethodName="wh::playermodule::GameOver"` `Reason Value="44"`**. (data-verified)
- **GameOver 44.** `Tables/Libs/Tables/rpg/game_over.xml`: `game_over_id="44" game_over_name="game_over_crime_execution" game_over_type_id="6" game_over_ui_message="game_over_crime_execution_text"`. `game_over_type.xml`: type 6 = `GameFailedInstantBackground`. (data-verified)
- **State left behind.**
  - No SkipTime or AdvanceWorldTime (the Execution check in and15). No text card (compare32). No debuff or reputation, because `punishmentdoneexec` never fires in the open-world variant: `ifnot34` needs `shouldplaytext` false.
  - Before the game over, the AI has already destroyed the crime informations (`resolveCrimeInformation`) and confiscated items if `confiscation > 0`. The quest has set `crime_playerInPunishment`, `crime_disabledFrisk` and `DisableRandomEvent`, and applied `remove_injuries`.
  - Game over forces a reload, so nothing persists except what an autosave captured. (data-verified wiring; the reload consequence is inferred)
- **Other crime game-overs.**
  - `GameOver Reason=game_over_bohutaArrested` (68): unconscious Bohuta with the evade cooldown (resolveCrimeDialogue), or the dialogue port `punishmentbohuta` (arrestdialogue.xml).
  - `GameOver Reason=DiedInCombat` under `crime_killUnconsciousPlayerOnRepeatedResolve`.
  - Several quest-specific crime game-overs (59, 60, 76, 80, 88, 91, 94). (data-verified)

---

## 5. Pursuit

| Piece | Behaviour | Source (data-verified unless noted) |
|---|---|---|
| **CrimeFollower** | Native node. Attributes: `Target`, `Mode ∈ {Default, DontBackOff, NoMoveOnlyTurn}`, `Role ∈ {Main, Assist, Observer}`, `RelativeSpeedLimit`, `DisableGhosting`. Tuned by the CVars `WH_AI_CrimeFollower_DistanceMain` / `…DistanceAssist` / `…DistanceObserver` / `…ApproachDistance` / `…AreaRadius` / `…DistanceVariance` / `…RepulsionOverride` (DLL strings). Of 85 logic uses in AI XML (170 tags counting EditorData mirrors), 65 are `Target="$__player"`. Arrest and frisk use `Mode=DontBackOff Role=Main` (the urger) or `Role=Assist`, `RelativeSpeedLimit=Dash`. | `Scripts/AI/crime/arrestselfhelp/actionsubtree.xml`, `DLL:XGenAIModule.dll` |
| **One urger per player** | `crime_managePlayerUrging` keeps a single link `crime_playerUrging` from `$__player` to the NPC allowed to talk. Higher `crime_playerUrgingPriority` steals it; selfhelp yields to arrest. | `Scripts/AI/crime/managePlayerUrging.xml` |
| **continuouslyFollowPlayer** | `CrimeFollower Target=$__player Mode=NoMoveOnlyTurn Role=Main` plus `Move destinationSpecification=$__player`; wider stop distances while the player has a `mount` link | `Scripts/AI/crime/continuouslyfollowplayer.xml` |
| **estimatePlayersPosition** | States lockedOn, lockingOut, lockedOut, lockingOn. Losing sight waits `maxLockingOutTimeout=6000` ms × min(dist²/900, 1), then `SetEstimatedFollow EstimateTargetPosition=true` (the follower predicts the player; inferred "cheat"). Seeing again takes 400 ms to lock on. | `Scripts/AI/crime/estimatePlayersPosition.xml` |
| **handlePlayersEscape** | Every `Wait '2s'`, if not `HasSeenPlayer … FullyAwareOnly=true` and the perception focus is not the player, the counter drops by 1 from `npcPersistency` (7). At 0: `InstantCallback_empty EventName='PlayerLost'`, so about 14 s unseen. | `Scripts/AI/crime/handlePlayersEscape.xml` |
| **sharePlayersLastSeenPosition** | Writes `AddLink From=$__player To=$this.id Tag='crime_playerLastSeen'`. Reads the newer entries of friends within `crime_sharePlayerPos_posShareDistance` (20 m). A friend in an interrupt who sees the player within 5 m fires `PlayerFound`. Barks `CUMIL_PROZRAZUJE_HRACE` (bystanders point the player out). | `Scripts/AI/crime/sharePlayersLastSeenPosition.xml`, `ScriptParams.xml` |
| **Arrest escalation** | Native `AddArrestEscalation Npc Reset` / `GetArrestEscalation IsEscalated`. The old BT (FOREST) was a link `crime_arrestEscalation` from `$__player` to the NPC with a timestamp; "escalated" meant `timestamp > now − crime_arrestEscalationPeriod` (`Value="300000"`, 5 min) and the NPC within `crime_crimeSceneSpatialSize` (20 m) of the player (inferred as current). It is set on: fight in the dialogue, `PlayerLost`, selfhelp or arrest escalation, and recurring crimes. It is reset by reconcile. Effect: `ChooseReaction` / `handleStimulusCriminal` choose attack. | `Scripts/AI/crime/addArrestEscalation.xml`, `getArrestEscalation.xml` |
| **Resisting → combat** | The attack triggers in §1.3. `CreateCombatInformation` on an authority victim carrying `crime_playerUnderArrestByAuthority` produces label `'resistingArrest'` (FOREST; inferred as current). `crime.xml`: `resistingArrest fine="750" jail="5" isViolent="true" importance="0"`. A lone `resistingArrest` gets an extra disturbance at resolve. If the most important known crime is `resistingArrest` (importance 0, so it is effectively alone), `handleAwareness_informations` drops it with `DestroyInformationFromHolder` instead of reacting. | `crime/createCombatInformation.xml`, `crime/singleresistingarrestfix.xml` |
| **Joining an arrest** | `handleAwareness_arrest`: an NPC that perceives a *friend* who has a `crime_reactor` link and has seen the player gets `TransferInformation source=$target target=$this.id` for all of that friend's informations, then `handleAwareness`. | `Scripts/AI/npc/basic/switch/handleAwareness_arrest.xml` |

**Who is targeted.** Always the local player: `$__player` in `CrimeFollower`, `callInterrupt_attack target=$__player`, `dialogInitiator recipient=$__player`, `HasSeenPlayer`, and the player-side links. The only other `ReactionNpc` value ever used is the player's dog (`$__playerDog`). Informations carry no "criminal" field: `perceivedWuid` is the victim or object (for example `CreateInformationWrapper Label='theft' PerceivedWuid=$pivot`), so the criminal is implicitly the player. (data-verified; the "implicit" part is inferred)

---

## 6. Stolen goods

- **Becoming stolen.** `Scripts/Scripts/Entities/Items/PickableItem.lua`: `if (self.item:CanSteal(self.user.id))` shows `@ui_hud_stealItem` (hold), which calls `self.item:OnSteal(user.id)`. Stashes pick the steal prompt from ownership: `Stash:UsesStealUiPrompt()` → `EntityModule.GetInventoryOwner(...)` (`Scripts/Scripts/Entities/WH/Stash/AnimStash.lua`). The native side stores the `Stolen` item param, the owner (`GetOwner Object=`) and the stolen-from inventory (`GetStolenInfo … InventoryOut=`, `StolenFromFilter Inventory=`). (data-verified names; that the flag is native is inferred)
- **Theft information.** `Scripts/AI/crime/createTheftInformation.xml` sets the dynamic values `theftMethod` (pick, loot, pickpocket, lootCorpse, lootUnconsciousBody, seenEquipped, kettleEating), `items` (itemPrescriptor: instance, or class+count), `value` (item price or `GetNPCRobbedValue` / `GetStashRobbedValue`), `victim` (owner) and `immediate`. Theft volumes are perceptible volumes linked `stealData`, with the owner holding `stealDataReverse`.
  - `crime_updateStolenItemsOnInformation` merges items later found on the player into the information. `crime_legalizeDivisibleItem` runs `LegalizeItem` on stacks of the class whose `GetOwner == owner` until the count is covered.
  - `crime_destroyTheftVolumesForConfiscatedItems` despawns the theft volumes of confiscated items (`all` or `relevantOwnAndMates`). (data-verified)
- **Search: `crime_findStolenItems`** (`Scripts/AI/crime/findstolenitems.xml`). `GraphSearch Origin=$__player … AllowedEdges='inventory' … ItemParamFilter Param=Stolen`, optionally with `StolenFromFilter`. It also searches `GetPlayerHorse` inventories (depth 2), and `CircularSpatialQuery Radius=crime_findStolenItems_groundRadius (3)`, which runs `GetStolenInfo` on dropped items. (data-verified)
- **Frisk trigger.** `handleAwareness_friskable`: skipped under `crime_disabledFrisk` (set during punishment), `crime_disableFrisk`, BFF, if the NPC is already in a crime interrupt, if the player carries a body, or in `suppressFrisk` areas. Otherwise it needs `crime_canFriskPlayer` + `crime_canFriskPlayerBehavior` and `RandomGate opensWithChance=$friskChance`, followed by `handleStimulusFriskable` (only if `GetCrimeSceneData … CrimeLevel == none`), then `callInterrupt_frisk`. (data-verified)
- **Frisk chance** (`Scripts/AI/crime/getFriskChance.xml`):
  - It is 0 when the player is on a horse or in a minigame, or when any `crime_reactor` (arrest, attack, selfhelp or report) is within 20 m.
  - Otherwise it needs `crime_frisk_cooldown` (5400000 ms) since the last frisk and `crime_frisk_chanceCooldown` (180000 ms) since the last roll.
  - Chance: +0.5 or +0.25 for high or mid Material angriness; **+0.25 if `crime_isAuthorityOnStationaryDuty`, else −2**; +0.25 for renown `havent_heard`; −0.5 for relation `atLeast_4_high`, +0.25 for `atMost_1_horrible`. Clamped to 0–1, then `× RPG.FriskProbabilityMod`. Guard roles come from the Storm rule `crime_frisk_roles` (`<isGuard/>`). (data-verified)
- **`interrupt_frisk`.** The guard follows (`CrimeFollower Target=$__player`), barks `STRAZ_VYZYVA_K_FRISKU_PRVNI_VYZVA` and opens chat `STRAZ_VYZYVA_K_FRISKU`. The same escalation rules as arrest apply: 4 ignored summons, 25 m away, horse or weapon, or 1 min.
  - Chat `evade` → `CreateInformationWrapper Label='friskRefusal' PerceivedWuid=$__player` + attack (`friskRefusal fine="100" confiscation="true"`).
  - Otherwise, under `ResolveCrimeDecorator`: `crime_findStolenItems`, then the hidden-pockets perk (`perkChance = player.soul:GetDerivedStat('fac')`, `RandomGate`) can hide the find. Then dialogue `STRAZ_FRISK` (`open_world/frisk/friskovaci_dialog.xml`).
  - Frisk dialogue choices: be searched; persuade, impress or dread (Medium, one try); refuse.
  - Results sent through `Crime.SendFriskDialogResult`:
    - `release`: nothing happens.
    - **`frisk` (stolen items found): `Confiscate ConfiscateItems=All ConfiscateFine=false TargetStash=$stolenItemsChest`**, then `callInterrupt_mindPlayer`. No crime information, no fine, no arrest.
    - `fight`: `friskRefusal` + attack.
  - Afterwards `crime_nextFrisk` is set to +45 min. (data-verified)
- **Worn loot spotted.** `handleAwareness_stolenEquipment`: `GetVisibleStolenEquipment Equipment=$stolenItems` (native), ammo excluded, only items owned by the NPC or a mate (`GetAreMates`). It creates a theft with method `seenEquipped` (pivot = home) and runs `handleStimulusTheft` under `crime_disableArrestEscalationForRecurrentCrime`. Disabled by `crime_ignoreWornStolenEquipment`. (data-verified)
- **Victims checking pockets.** `crime_checkPockets` is the victim side, not a guard search: `GetNPCRobbedValue > 0` → `callInterrupt_checkPockets`. (data-verified)
- **Found during arrest.** Arrest confiscation is `Confiscate` `relevant` (the crimes' own items) or `all` (every stolen-flag item, when `punishment.confiscation > 0` and the result is not a skill check). Both go to the storage chest. The dialogue branches on `GetCrimeData FoundAnyUnknownStolenItem` / `FoundAnyKnownStolenItem`. DLL: "Theft fine %d lower than the value %d of stolen items found on the player." (data-verified; the relevant-vs-all meaning is inferred)
- **Legalizing.** A successful skill check legalizes the listed theft items (§1.6). (data-verified)

---

## 7. Horses

- **The prompt** (`Scripts/Scripts/Entities/AI/Horse.lua`, `Horse:GetActions`):
  - `local isPlayerHorse = user.player:GetHorseId() == self.id` and `local isFreeToTake = isPlayerHorse or self:IsMountLegal()`.
  - `Pick(isFreeToTake, "@ui_hud_mount", "@ui_hud_mount_and_steal")`; `Pick(isFreeToTake, AHT_RELEASE, AHT_HOLD)`.
  - `Horse:IsMountLegal()` returns `self.mountIsLegal or self.mountIsLegalFromAI`. `mountIsLegal` comes from the entity property `bMountIsLegal` (default `false`), is saved in `OnSave`/`OnLoad`, and can be set with `Horse:SetMountIsLegal`. `mountIsLegalFromAI` is "controlled from AI switch_animal_horseBrain by a context, doesn't save". (data-verified)
- **Horse brain** (`Scripts/AI/animal/basic/switch/animal_horseBrain.xml`):
  - `ExecuteLua code=entity:SetMountIsLegalFromAI(false)` runs until `EntityContextBarrier context=switch_horse_enableMountIsLegal target=$this.id`, then `(true)` (KCD2-107030).
  - When mounted: `LuaGate code=return entity:IsMountLegal()`. If not legal, not suppressed (`switch_horse_suppressMoraleHitWhenMounting`) and not the player's horse, it gets the "OnMount" buff, a startled sound and the "Mounted" buff. (data-verified)
- **The crime side is independent of that Lua flag** (`Scripts/AI/player/switch/switch.xml`, player brain). When a `mount` link appears: `GetPlayerHorse`; if `$mount ~= $playerHorse`, then `Function_crime_updatePlayerMountedHorseData` (link `crime_horseMounted` on the horse with `initialPosition` and timestamp, cooldown `crime_horseTheft_updatePlayerMountedPositionCooldown` 10000) and **`SpawnExpiringPerceptibleVolume Expiration='2s' … Label='crime_playerMounted'`** with `crime_playerMounted` data `.mount`. (data-verified)
- **NPC reaction** (`handleAwareness_playerMountedVolume.xml` for the volume, "react regardless of cone"; `handleAwareness_playerMount.xml` for sight of a mounted player):
  - Forced by `crime_forceReactionHorseTheft` (entity) or `crime_forceReactionToHorseTheft` (relation).
  - Ignored by `crime_ignoredHorseTheft_Horse` (on the horse), `crime_ignoredHorseTheft_NPC` (on the NPC) or relation `crime_ignoreHorseTheft`.
  - Otherwise the native `IsHorseRelevant`, then `handleStimulusHorseTheft` (victim = `GetOwner` of the horse's `home`), then `ChooseReaction`. (data-verified)
- **`IsHorseRelevant`** (FOREST reference, `Scripts/AI/crime/isHorseRelevant.xml`, note "Horse is relevant for mates and guards in the same settlement"). A horse is relevant if it is not `GetPlayerHorse`, and either the NPC is a mate of the horse (`GetAreMates`), or the NPC is an authority and the horse lacks `crime_ignoreHorseTheftInSettlement` (context with `SideEffect="ignoreHorseTheftInSettlement"`, set for example by quest `kutnohorsko/ukradenyKun`) and `GetFactionWithLabel … Label=settlement` of the horse's highest-status mate equals the NPC's settlement faction. (inferred as current)
- **Legal horses in practice.** Quests that lend a horse set **both** `switch_horse_enableMountIsLegal` (the prompt) and `crime_ignoredHorseTheft_Horse` (the crime), for example `trosecko/kocovnickaCest/hracuv_pujceny_zavodni_kun.xml` (`SetEntityContext … playersBorrowedHorse`). **Ownership** = `GetPlayerHorse`, the horse registered as the player's (the same as `player:GetHorseId()`). (data-verified)
- **Horse theft consequences:**
  - Riding a stolen relevant horse makes the reaction `attack` (chooseReaction FOREST).
  - The fine is 2000 × social class, plus the horse value if it is dead or "pay'n'spray".
  - At resolve, `player_dismountHorse dismountOnlyIfRelevantHorse=true` (punishment dismounts regardless), and the horse is returned home through `crime_followUpBehavior_returnStartledAnimal`.
  - The frisk chance is 0 while mounted. (data-verified)

---

## 8. Non-witness guards, and where "wanted" lives

**Information travels between NPCs.** (data-verified unless noted)
- **Emit:** `Scripts/AI/crime/emitInformation.xml`. `InformationEmittingStart … Periodicity=$emitPeriod` is `'5s'` during arrest, attack or selfhelp (`'15s'` watch). The radius is `crime_crimeInformationEmitDistance` (10 m), or `_long` (40 m). The `'alarm'` info carries `crimeInformation` at `crime_alarmInformationEmitDistance` (25 m).
- **Report:** `TransferInformation` of all known infos to the guard (§1.2).
- **Joining an arrest:** `handleAwareness_arrest`, a `TransferInformation` from an arresting friend.
- **Resolve pool:** friends within 50 m (`spreadInformationWithinFriends`).
- **Punishment pool:** all civilians of the punishment location (`GetCiviliansForLocation`, then `SpreadInformation`).
- **Scripted spreads:** quest trees such as `sermiri_arrest` (`SpreadInformation WuidArray=…`).
- **Periodic faction broadcast.** RPG params in `DLL:RPGModule.dll` are listed together with these descriptions:
  - `InformationBroadcastPeriod`: "Information is broadcast in a faction subtree periodically after this amount of time." It is set to `rpg_param_value="1"` in `Tables/Libs/Tables/rpg/rpg_param.xml`.
  - `InformationBroadcastIgnoreRadius`: "NPCs around the player will not receive information from periodic broadcasts."
  - `InformationBroadcastDelay`: "Fresh information will broadcast only if it is at least this old."
  - `InformationExpirationBase`: "Multiply by a modifier from crime table to get amount of time after which an information expires. [day]".
  - `InformationVersionRefresh`.
  - The crime `expiration` in `crime.xml` is, for example, murder `7`, theft `4`, drawnWeapon `1`. The description strings are data-verified; pairing each string with its parameter name is inferred.
- **Spreadable crimes.** `isSpreadable="true"` is set on every `isCrime="true"` label in `crime.xml`. Among non-crimes, `alarm`, `animal_*`, `motivation` and `nonAttributedCrime` are not spreadable. `C_InformationPropagationManager::SpreadInformationsBetween` in `DLL:XGenAIModule.dll` is the native implementation.

**A non-witness guard acts.** `handleAwareness_informations.xml`: on seeing the player, `PlayersCrimeInformationKnown Npc=$this.id OnlyRelated=false` → `GetMostImportantCrimeInformation` → `handleStimulusCriminal`, which reaches arrest for authorities with `crimeSeen=false` (intro `notResistingArrest_foundAccidentally`, "arrested later as a wanted man"). Everything downstream (dialogue, fine, punishment, recheck) uses only the guard's *held* informations, so a guard that heard about the crime can run the whole flow. `resolveCrimeDialogue` even pools friends first, and the punishment recheck pools the town. (data-verified)

**"Wanted" is not stored on the player as a buff or context.** (data-verified symbols; the aggregation model is inferred)
- `IsWanted` is defined as "Check whether the player has wanted icon on their screen. Specifically if they are wanted in any currently present locations." (`IPL_GameData/Libs/concept/definitions.xml`, `wh::rpgmodule::IsWanted`).
- The native counters `C_NPCFactionNode::IncrementKnownCrimes` / `DecrementKnownCrimes` log "NPC %s has no location and therefore cannot contribute to known crimes." (`DLL:RPGModule.dll`), and `C_InformationPropagationManager::IncrementKnownCrimes` exists (`DLL:XGenAIModule.dll`).
- The map uses `WantedLevel` and `SetWantedState` per location (`DLL:GUIModule.dll`).
- `crime_disableWantedStatus` (Game context, `SideEffect="disableWantedStatus"`) turns it off.
- The only buff tied to it is `Cpp:Wanted` on `perk_ordinary_mug` (`con-1`, active while wanted).
- Conclusion: "wanted" = NPC-held crime informations, counted per location or faction node.

**Crime state that does live on the player** (data-verified):

| Kind | Items |
|---|---|
| Links from `$__player` | `crime_reactor` (to each reacting NPC, with `reactionKind` and `information`), `crime_playerUrging`, `crime_playerLastSeen`, `crime_globalCooldowns` (player→player: `lastFriskTimestamp`, `lastFriskChanceTimestamp`, `lastNoTorchGuardTimestamp`, `lastDrawnWeaponGuardTimestamp`, `robWhileUnconsciousTimestamp`), `crime_nextFrisk`, `crime_lastHitByPlayer`, `crime_preUnconsciousnessLastHit`; the old `crime_arrestEscalation` |
| Item state | the `Stolen` item flag |
| Contexts | `crime_inCrimeDialog` (during the dialogue); `crime_playerInPunishment` and `crime_disabledFrisk` (Game contexts during the punishment) |
| Stats and buffs | brand stat `brn` and the punishment debuffs |

---

## Notes for co-op (all inferred)

- **Player-bound everything.** Every crime node addresses `$__player`: dialogue partner, followers, attack target, links, and a single urger link. A second, ghost player cannot be stopped, talked to or punished by vanilla guards. Crimes are implicitly the host player's.
- **One resolve at a time.** The resolver is a global singleton, and Semaphore `crime_resolveDialog` holds one resolve dialogue world-wide.
- **Clock moves.** Punishment changes world time: SkipTime cutscenes, `AdvanceWorldTime 10h/9h`, possibly the `Time="8h"` cutscenes, and native fast travel. It also teleports the local player and plays fader or video cutscenes. Any shared clock must expect these jumps.
- **Settlement-wide state.** Resolving `DestroyInformationCompletely`s crimes that other NPCs may hold. The punishment recheck pulls in every civilian of the town, so crime state is effectively settlement-wide rather than per guard.
