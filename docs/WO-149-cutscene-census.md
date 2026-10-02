# WO-149 — Part B census: every cutscene, and what two players need from it

**Read-only research, 2026-10-01.** Shared quests step 2 (WO-126 Part B): all cutscenes and set pieces game-wide,
joined for both players. Like WO-141A did for activities, this measures the problem before anything is built.
Nothing was launched, built, injected or committed except these three docs. The game's paks and binaries were opened
read-only from the Modding Tools install (`<MT>`); the retail install was not touched; no save was opened. Method,
files, tools and what was touched: `docs/WO-149-progress.md`. One row per cutscene or set piece:
`docs/WO-149-cutscene-census.csv`.

Evidence marks: **(data-verified)** read in the game's data · **(code-verified)** read in our code or a binary's
strings/disassembly · **(log)** seen in a field log · **(inferred)** · **(inconclusive)**. Game text is cited by ID only.
The two machines are called the host and the joiner. `<MT>` = the Modding Tools install, `<repo>` = this repo. RVAs are
the Modding Tools 1.5.5 build's, module-relative.

---

## 0. Answer first

### 0.1 What the game has

Twelve scene kinds cover everything, plus one engine mechanism that is not a content kind. Counts are over the whole
game: 219 quest roots (main 32 · side · activity · event · DLC) plus the shared content (crime, random events, libraries).

| kind | defined in the cutscene table | placed (holders, N+L, all three levels) | in quests: rows / handlers (main) | starts from | ends by (what the quest hears) | skippable |
|---|---:|---:|---|---|---|---|
| rendered_video | 93 entries (83 videos, 65 min of story footage) | 34 holders | 11 handlers (10) | handler `EnqueueCutscene` | `AfterPlay` at video end, `OnFinished` at release | **19 of 93** have a skip point |
| ingame_sequence | 228 | 222 holders, 391 data entities | 188 handlers (138) | handler `EnqueueCutscene` | `AfterPlay`, `OnFinished` | yes by the engine path (`SkipActiveCutscene`); UI exposure inconclusive |
| trackview_background | 223 | 198 holders, 739 sequence objects | 160 `PlayTrackView` nodes (141) | quest node | none (background) | no |
| fader_cutscene | 447 | 579 holder rows | 348 handlers (124) | handler | `AfterPlay`, `OnFinished` | no |
| skiptime_cutscene | 72 | 142 holder rows | 63 handlers + 39 skill-teacher lessons (24) | handler | `AfterPlay`, `OnFinished` | no |
| text_cutscene | 82 | 96 | 79 handlers (30) | handler | `OnFinished` | no |
| fasttravel_cutscene | 67 | 72 | 52 handlers + 161 random-event data rows (11) | handler | `OnFinished` | no |
| credits | 3 | 2 | 2 handlers (2) | handler | `OnFinished` | no |
| fader_dialogue | — | 679 dialogue holders | 2,570 modules (603) | quest/trigger, dialogue outcome | dialogue out-ports | per line only |
| forced_dialogue | — | (same holders) | 808 `ForcedDialog` + 289 NPC-started `Dialog` rows (443) | quest node `EnqueueDialogue` | dialogue out-ports | per line only |
| camera_takeover | — | 10,942 camera sources | 149 focus-camera nodes (39) | quest node | node's own end | n/a |
| set pieces | — | — | fights 354 · escort/follow 97 · teleports 260 · bed 17 · player switch 21 · surrender 233 · crime scenes 44 | quest nodes | per node | n/a |

* **"Interactive scene" is not a content kind.** No data defines one: no entity, no table row, no quest node (data-verified).
  It is the engine's **scene queue** (`C_InteractiveSceneManager`): one first-in-first-out queue that every cutscene,
  every player-facing dialogue and every `SceneFinishedWaiter` node enters. It is the mechanism under every row above
  (code-verified, §3).
* **Dialogue is the story.** Of the main story's estimated 23 hours of scripted time, about 85 % is dialogue
  (fader 40 %, forced 44 %), 11 % Ingame sequences, 1.5 % rendered video (inferred, §2.4). A plan that only handles
  cutscenes handles about 15 % of the scripted story.
* Quests are the starting point for 747 `CutsceneHandler` nodes; all 337 in the main quests now resolve to a cutscene
  (WO-126A left 29 unresolved; §1.13). Whole-game and per-class tables: §1; per-quest: §2.

### 0.2 What breaks today (field, 0.42.2) — the finding that decides the order

The shared world mirrors quest *State* steps (WO-137) and each machine's own quest graph then plays the scene
(WO-126A §4, WO-137 §9.7). When that works, the joiner's copy starts 0.1–1.5 s after the host's (log). The failure is
at the **end**: the joiner's copy of a scene does not finish.

* **8 joiner black screens, 20–204 s**, all on the joiner, none on the host (log, §4.3). The host's same scenes took 2–20 s.
  * **Positioning wait** (5 of 8): after the content ends, the engine waits for the scene's NPCs to reach their end
    positions (`[ScenePositioningManager]: NPC positioning took longer than expected`, 41–68 s; two never finished).
    The joiner's NPCs there are the host's stream-driven, paused copies, which cannot answer (inferred).
  * **Twin wait** (4 of 8, one case has both): a dialogue the game itself started on a paused copy sits in
    `WAITING_FOR_TWINS` and leaves after exactly 20.0 s; **zero** `DialogTwin` contexts were created in all four.
  * **What released them was a host action**: the host saving (the joiner mod's snapshot `QuickSave` makes the engine
    log `Cancelling FF for all NPCs`, then positioning finishes), or the host's reload and the joiner's rejoin load.
    Nothing was ended by `wh_ui_FaderSuspend` (it never appears in the pack).
* The scene queue is serial, so one stuck joiner scene holds every later scene behind it (queue waits of 49–315 s).
* The mod's cutscene "end" edge fires at `OnCutsceneEnd`, 41–60 s before the engine's release, so the host reads a black
  joiner as free (log, §4.5).
* About a third of what the host sends (31–36 %) is a quest-State change the joiner cannot apply because the mod
  recorded no port for it; the "dropped inactive" burst (614–640 records per load) is the load restoring every State, not
  lost steps (log, §5.3). Correction to the premise: the DLL's "no port" is a missing attribution, not a missing
  port (§5.2).
* Not in the attached pack and therefore **(inconclusive)** here: the lake massacre, "Henry falls" queued again, the
  opening siege played separately, and `WAITING_FOR_TWINS` in the herbalist wake-up (the wake-up is there; the twins are
  not). §4.4 places each in the game data instead.

### 0.3 Decision table (the policy is the maintainer's; these are recommendations)

"Both watch" = each machine plays the scene from its own copy of the graph, made safe by a scene guard.
"Triggerer only" = the player in the scene plays it; the other keeps playing; the outcome is mirrored as State steps.
"Hybrid" = both, with a defined fallback or a host-only part. Costs and the engine pieces are in §6; the full build plan
is §7.

| # | kind (rows in the game) | recommendation | why, in one line | main risk |
|---|---|---|---|---|
| 1 | **rendered_video** (11 handlers; 19 skippable) | **Both watch**, own copy, no start gate | already happens (`m03` +0.1 s); the video freezes only its own machine; the VideoMode pause is declined so the other world runs | 4 rendered holders reposition the player at the end; a skip is local, so durations differ |
| 2 | **ingame_sequence** (188; 138 main; the story's set pieces) | **Hybrid: both watch** behind a scene guard; fall back to triggerer-only when the partner is far, loading, or in another scene | each player must be in the story; the guard fixes the measured stalls (positioning) and bounds them | the joiner's NPC copies are paused: needs scoped resume and a bounded end; AnimChar doubles copy the *local* Henry |
| 3 | **fader_cutscene** (348; mostly plumbing and teleports) | **Both watch**, own copy, behind the same guard | short; each copy teleports its own player; many carry end positioning | a fader scene started on one machine alone (field case 5, 6) leaves the other running free |
| 4 | **skiptime_cutscene** (63 + lessons) | **Hybrid**: host clock only; both play the screen, the joiner's skipped to about 1 s | one world clock (WO-133/139/140); the native time gate and the punishment override already do this | skipping must not run twice; sleep vote (WO-140) covers player-chosen sleep only |
| 5 | **text_cutscene** (79) | **Both watch**, own copy | a 2–7 s black card; nothing to sync | none beyond the guard |
| 6 | **fasttravel_cutscene** (52 handlers; 152 random-event rows) | **Hybrid**: triggerer travels, partner is brought along (leash pull) | one world; the joiner's own fast travel is refused (WO-114) | the pull cannot run while the partner is in a scene |
| 7 | **trackview_background** (160; 141 main) | **Host only**; verify the joiner plays no copy | ambient/battle theatre on NPCs the host owns; nothing for the joiner to watch | the 6 foreground dark-woods visions in M02 are Ingame-class: handle them under row 2 |
| 8 | **credits** (2) | **Both watch** | end of game; trivial | none |
| 9 | **fader_dialogue** (2,570; 603 main) | **Triggerer only**, outcome mirrored | engine: dialogue targets the local player, twins and camera are per machine; 4 of 4 stuck dialogues were started by the game on a paused copy | the joiner's own copy must not start a host-triggered forced dialogue; joiner-triggered ones need the WO-137 talk route |
| 10 | **forced_dialogue** (1,097; 443 main) | **Triggerer only**, outcome mirrored | same | same; 291 of 335 main forced dialogues feed the graph through their out-ports |
| 11 | **camera_takeover** (149) | **Per machine**, nothing to sync | the camera is local | none |
| 12 | **scripted fights, surrender** (354, 233) | **Host-owned** (existing combat sync), joiner fights what the host's world shows | WO-121/131 | surrender pose on a copy is not built |
| 13 | **escort / follow** (97) | **Triggerer-led**: the NPC follows the player who triggered; the partner is not required | "an escort follows its leader" is not built (WO-144) | leader change mid-escort |
| 14 | **player teleports and level switches** (260; 2 level switches) | **Hybrid**: triggerer teleports, partner is placed with them | same end spot for both (leash "come along" exists) | a teleport mid-scene by the partner's own copy |
| 15 | **bed / wake-up** (17) | **Both** under the sleep vote (WO-140) and the wake scene's own parent | wake scenes are Ingame + dialogue pairs (herbalist) | see row 2 |
| 16 | **player switch / non-Henry** (21 switches, 8 stretches, about 1.4 h scripted) | **Host-only stretch**; the joiner is held and told, then caught up | Godwin is the host's `$__player`; joins are refused in non-Henry worlds (WO-125) | what the joiner does for up to an hour; the maintainer's call |

### 0.4 Proposed build order (Part B), by story covered and how badly it breaks today

Eight work orders; the first two do not depend on any policy decision.

1. **Scene guard (unstick the joiner).** Bounded positioning wait, scoped resume of the scene's NPC copies, a clean
   release; fixes the 8 field cases. Also: the mod's end edge, the misspelt scene-queue echo, a reset on load.
2. **Scene bus.** Native scene state (Queued, BeforePlay, AfterPlay, Finished, Interrupted) instead of log tailing
   (retail loses the log lines); one wire message with type, name, holder, module path.
3. **Dialogue = triggerer only** (rows 9–11): about 85 % of the scripted story. Also the port attribution fix
   (record the port at the port pulse, so "no port" changes become appliable).
4. **Cutscenes both watch** (rows 1–3, 5, 8): Ingame, Rendered, Fader, Text, with end placement for both players.
5. **Time and travel scenes** (rows 4, 6, 14, 15): skip-time, fast travel, teleports, sleep and wake.
6. **Set pieces** (rows 12, 13): surrender pose, escorts, quest items for the joiner.
7. **Catch-up after a join or rejoin**: replay quest State without replaying scenes.
8. **Non-Henry stretches** (row 16), last because it is policy-bound.

### 0.5 Known unknowns (only a live run settles these; full list §8)

Whether a joiner's scene finishes its end positioning once its participants are resumed; whether the engine's
`Cancelling FF` (save lock requested during fast-forward) can be used as the release on purpose; whether
`SkipActiveCutscene` on an Ingame scene reliably ends in `OnCutsceneEnd`; who calls `C_InteractiveSceneManager::Interrupt`;
whether retail keeps the same scene classes and vtable order; what the other screen shows when a player is far away,
loading or in another scene.

---

## 1. Phase 1 — every cutscene kind, counted

Sources: the cutscene table `Libs/Tables/ui/cutscene.xml` (1,215 live entries), the three levels' `objects_mission0.xml`
and 4,310 streamed layer files, `Cinematics.pak`, `Animations.pak`, `Music.pak`, 21,648 quest XML files, `GUIModule.dll`
strings. Counting convention as WO-141A: **N** = entities in `objects_mission0.xml` summed over `trosecko`,
`kutnohorsko`, `klaster`; **+L** = streamed layers. 19 holders are identical copies in two levels' files, so 1,353 holder
rows are 1,334 unique (data-verified).

### 1.1 What is defined, what is placed, what quests reference

| kind (table element) | defined | entries with ≥1 holder | holder rows | unique holders | quest-confirmed | registry-bound only | no reference |
|---|---:|---:|---:|---:|---:|---:|---:|
| rendered_video | 93 | 31 | 34 | 34 | 13 | 7 | 14 (crime pillory/execution) |
| ingame_sequence | 228 | 218 | 222 | 222 | 187 | 23 | 12 |
| trackview_background | 223 | 188 | 198 | 198 | 136 | 61 | 1 |
| fader_cutscene | 447 | 374 | 579 | 575 | 309 | 264 | 2 |
| fasttravel_cutscene | 67 | 58 | 72 | 71 | 58 | 13 | 0 |
| skiptime_cutscene | 72 | 67 | 142 | 136 | 74 | 62 | 0 |
| text_cutscene | 82 | 81 | 96 | 88 | 80 | 8 | 0 |
| credits | 3 | 2 | 2 | 2 | 2 | 0 | 0 |
| not in the table / no name | | | 8 | 8 | 4 | 4 | 0 |
| **total** | **1,215** | **1,019** | **1,353** | **1,334** | **863** | **442** | **29** |

(data-verified; "quest-confirmed" = a quest uses an alias that the quest's level asset registry resolves to that holder,
WO-126A §4.1; "registry-bound only" is real data but not proven used, inconclusive.) Of the 29 unreferenced holders, 21 are
the crime-punishment family driven by the crime module by location (inferred) and 3 are true orphans
(`argumentCutsceneHolder`, `hladAZmar_extras_archers`, `burntSeminStreamFader`).

Quest handlers: **747** `CutsceneHandler` nodes (731 outside the `utils` library definitions); 691 carry a holder alias, 631
resolve to a holder by the plain method and all 337 in the main quests resolve with the four extra mechanisms of §1.13
(data-verified). Whole game, primary rows by quest class (a row is a node, or a module root for dialogues):

| kind | main | side | activity | event | dlc | other | all |
|---|---|---|---|---|---|---|---|
| rendered_video | 10 | 0 | 0 | 0 | 0 | 1 | 11 |
| ingame_sequence | 138 | 32 | 0 | 0 | 18 | 0 | 188 |
| trackview_background | 141 | 17 | 0 | 0 | 0 | 0 | 158 |
| fader_cutscene | 124 | 138 | 9 | 4 | 45 | 18 | 338 |
| skiptime_cutscene | 24 | 30 | 2 | 0 | 6 | 39 | 101 |
| text_cutscene | 30 | 12 | 2 | 0 | 28 | 4 | 76 |
| fasttravel_cutscene | 11 | 31 | 2 | 152 | 15 | 2 | 213 |
| credits | 2 | 0 | 0 | 0 | 0 | 0 | 2 |
| fader_dialogue | 603 | 1083 | 87 | 111 | 376 | 310 | 2570 |
| forced_dialogue | 443 | 308 | 40 | 42 | 221 | 43 | 1097 |
| dialogue_twin | 3 | 0 | 0 | 0 | 1 | 0 | 4 |
| camera_takeover | 39 | 44 | 6 | 1 | 45 | 14 | 149 |
| setpiece_fight | 132 | 134 | 25 | 8 | 47 | 8 | 354 |
| setpiece_escort_follow | 57 | 37 | 0 | 0 | 3 | 0 | 97 |
| setpiece_teleport | 72 | 112 | 4 | 0 | 55 | 17 | 260 |
| setpiece_bedscene | 10 | 6 | 0 | 0 | 0 | 1 | 17 |
| setpiece_playerswitch | 21 | 0 | 0 | 0 | 0 | 0 | 21 |
| setpiece_other | 0 | 0 | 0 | 44 | 0 | 0 | 44 |
| surrender | 86 | 102 | 1 | 13 | 28 | 3 | 233 |
| **total** | **1946** | **2086** | **178** | **375** | **888** | **460** | 5933 |

(data-verified; rows are per definition: 23 of the 731 handlers sit in modules instanced more than once, which would make
835 weighted by instance.)

### 1.2 rendered_video (`RenderedCutscene`)

* **Defined** 93 entries over 83 distinct `.bk2` videos (Bink 2; 14.46 GB; 73 videos at 3840×1620, 10 main-menu loops at
  7680×2160; all 30 fps). Durations are exact from the Bink header: 4,536 s in all, 3,917 s excluding menu and startup
  (65 min); median story video 44 s; longest `finale_jostArmy` 319 s, `vezniNaTroskach_stormDream` 295 s,
  `intro_new_game` 199.5 s (data-verified).
* **Placed**: only 31 entries have a holder (34 holders). 11 are menu/startup videos played by cvar or last-save
  location. 43 more (`*_renderPartN`, `*_clip0N`) have neither a holder nor a literal use in quest XML: how the player reaches
  them is **(inconclusive)**. Quests play 11 handlers: `m03_trosky_journey` (M03), `utokNaNebakov_cutscene_march_render` (M09),
  `nebakovObrana_nightmare_render` (M11), `prijezdNaSuchdol_pistaFlashback` (M31),
  `oblehaniSuchdole_zacinaOblehaniCasosber` (M48a), the two `zoufalaObranaZaBohutu_battle*GameEnd` (M50),
  `finale_jostArmy`, `finale_henryReturnsToTheresa` (M51), one in the shared content (data-verified).
* **Not in any quest node** (the first two hours): the loading-screen videos `intro_new_game` (199.5 s) and
  `story_switch_to_trosecko` (169.7 s) play from the loading screen; five more movies (316 s) play inside the M02 dream
  sequences through a console track key (`wh_ui_PlayMovie`), so WO-126A's "Rendered 9" misses them (data-verified, D3).
* **Skippable**: 19 of 93 entries have a `SkipPoint` event (18 distinct videos, 86 skip points): the intro, the story
  switch, `m03_trosky_journey`, `finale_jostArmy`, the M09/M11 videos, the four siege videos, 8 crime entries. Story
  clips over 60 s without a skip point include `zachrana_fall_dream` 150 s, `utokNaMalesov_pistaDreamEvil_render` 129 s,
  `prijezdNaSuchdol_pistaFlashback` 94 s (data-verified). The engine skip itself (`SkipActiveCutscene`) returns true for
  Rendered; that it ignores videos without a skip point is inferred from the data.
* **Does**: full-screen video, input off; **freezes every Lua timer chain** and game time (observed WO-78/80/90/95:
  60–173 s); the WO-138 VideoMode decline lets the world run while a console-played video plays (observed); a quest-driven
  one under the decline is unproven.
* **Ends**: `AfterPlay` when the content ends, `OnFinished` at release; if the holder has fast-forward or
  player-reposition links, the end waits for the NPCs' end positioning.

### 1.3 ingame_sequence (`IngameCutscene`)

* **Defined** 228 entries; 93 set a time of day, 103 a start weather, 87 define checkpoints (341 in all), 13 dispose of
  corpses. **Placed**: 222 holders with 391 `IngameCutsceneData` (all in `objects_mission0.xml`), each carrying
  `esSequenceName` and (218 of 391) a layer profile `esGameProfile`. The stage of a story sequence is a streamed
  `cin_<code>__<name>` layer (204 layers; 5,992 `AnimChar` actors, 3,062 `MusicCutscene`) with baked per-actor clips
  (266 clip folders, 2,372 `.caf` in `Cinematics.pak`); only background tracks are authored as level XML
  (data-verified; the 201 story sequences have no `<Sequence>` in `moviedata.xml`: where their camera track lives is
  (inconclusive)). WO-141A's counts verified: `AnimChar` 6,068, `IngameCutsceneData` 391 exact; `MusicCutscene` is 3,063
  (3,062 in layers + 1 in a level file).
* **Quests**: 188 handlers (138 main, 32 side, 18 DLC); 30 of 32 main quests have one or more. Sequence lengths derivable
  for 177 of 423 linked holders: total 6,018 s, median 20.6 s, max 458 s (the rest inconclusive).
* **Does** (code-verified/data-verified): exclusive `cutscene` input map; the camera is taken; actors are `AnimChar`
  doubles (`CopySoulVisual`: the player double copies the **local** Henry, inferred); `Time` → `C_IngameCutscene::SetTime`;
  `StartWeather`/`EndWeather`; layer profile load; `DisposeOfCorpses`; holder `teleport` links place participants and
  `fastForward` links move them at the end, with `PlayerLinkRerouter` repositioning the player (92 holders; Ingame 34).
  Buff `death_protection_cutscene` and `mute_cutscene` ride on it. Does **not** freeze the mod's timer chains (observed WO-95).
* **Ends**: sequence end → `OnCutsceneEnd` → end positioning → `OnPositioningFinished` → finalize → `EndScene` →
  `ReleaseScene` (= `OnFinished`). Skip: `SkipActiveCutscene` stops the sequence and returns true.

### 1.4 trackview_background (`TrackViewCutscene`, `PlayTrackView`)

* **Defined** 223; placed on 198 holders, all linking an `IngameCutsceneData`; 739 `SequenceObject` entities (513 N + 226 L),
  199 `SequenceArea`, 13 `SequenceTrigger`. **Quests**: 160 `PlayTrackView` nodes (141 main: M48a 25, M48b 32, M48c 12, M50 35,
  M11 8, M44b 7, M09 6) and **0** `CutsceneHandler` plays one (data-verified). They are battle/idle/dead-body "theatre"
  tracks (2,429 entity nodes in the level sequence files) — no fader, no input lock, no positioning.
* Not skippable by `SkipActiveCutscene` (returns false); `Movie.StopAllCutScenes` / `mov_skipSequence` act on them.
* The 6 dark-woods visions of M02 are foreground (they lock movement and camera) and belong under row 2 of the decision table.

### 1.5 fader_cutscene (`FaderCutscene`)

447 entries, no data of their own; 579 holder rows (the most-placed kind; `randomEvents_fader` is on 82 holders,
`pocestny_newborn_playerIsPoisoned` on 52, `trainingGrounds_teleport` on 27). 348 handlers (124 main, 138 side, 45 DLC).
The cutscene object does nothing: `Start` goes straight to `OnCutsceneInitialized(true)`, `Skip` returns false; the black
screen is the scene manager's own fade, and the quest does its teleports, time changes and `SwitchPlayer` between
`BeforePlay` and `AfterPlay` (code-verified; quest side data-verified). 27 Fader holders reposition the player.
Duration is whatever the quest does behind the fade (host 0.1–7 s in the field, joiner up to 204 s when stuck).

### 1.6 skiptime_cutscene, text_cutscene, fasttravel_cutscene, credits

* **SkipTime** 72 entries; the type is the holder's `SkipTimeCutsceneData.esSkipTimeType` (a row of `skiptime.xml`, 90 rows),
  `Duration` (111 data entities: 2 h ×60, 1 h ×28, 3 h ×11) or `TargetTime` (36), never both. 63 handlers + 39
  skill-teacher lesson managers. In a session the clock is the host's; WO-139 sets the punishment entities to 1 s (code-verified).
* **Text** 82 entries, a black card of 2–7 s (3 s or 5 s for 64 of 82); `text_cutscene` input map; 79 handlers. 6 holders also
  link a skip-time entity (inferred: a text card that advances the clock).
* **FastTravel** 67 entries; the holder's single fast-travel point; 52 handlers and 161 random-event data rows
  (`EventInitiatedFastTravelData` 82, `PlayerInitiatedFastTravelData` 79; plumbing for the random events). The engine
  logs `FastTravel: started/ended` (not seen in any pack launch).
* **Credits** 3 entries (`credits`, `creditsFirst`, `creditsSecond`), 2 handlers, both in M51.

### 1.7 Dialogue that becomes a scene (fader dialogue, forced dialogue, dialogue twins, camera takeover)

* **fader_dialogue** 2,570 `FaderDialog` modules (603 main): a conversation wrapped by a named fader; only these may
  hide or show actors. **forced_dialogue** 808 `ForcedDialog` modules plus 289 NPC-started `Dialog` roots that involve the
  player (1,097 rows, 443 of them in the main quests): the forced ones are started by the quest (`EnqueueDialogue`, 405 edges),
  bypassing the NPC brain. 812 of 812 forced and 2,564 of
  2,570 fader modules name a player role. Dialogue out-ports feed the quest graph: 291 of 335 main forced and 373 of 603
  main fader conversations (WO-126A, data-verified).
* **Dialogue twins** are runtime entities: `C_DialogueTwinController::MakeTwin` spawns `DialogTwin_<soul>` for every
  participant including the local player (`DialogTwin_Dude`); the original is hidden/frozen; each twin carries the camera
  attachment (code-verified, log). **No `DialogTwin_*` entity exists in any level** (data-verified); the quest data names
  one in only 4 modules that reposition it (M08, M12, M44b, S306).
* **camera_takeover**: 149 `FocusCamera*` nodes (39 main) plus the dialogue camera (`C_DialogCameraManager`,
  `CameraSource` 10,942 entities, 6,451 named `*customCam*`) and `CustomCamera` decisions on 1,559 forced and 132 fader
  decisions.

### 1.8 Quest set pieces

| set piece | rows (main) | what it is | note |
|---|---:|---|---|
| scripted fights | 354 (132) | `skirmish` 140, `duelbehavior*` 96, `fightstart/fight` 79, `fist_fight_line` 27, `duel` 11 | 165 fight-bearing modules in the 32 main quests (WO-126A §5.3) |
| surrender | 233 (86) | `fightconfiguration_surrendering` (the opponent yields) plus 77 AI trees and `CrimeSurrender*`, 3 `surrenderHolder` entities | "surrender not built" (WO-137); no scene of its own |
| escorts and follows | 97 (57) | `tour_advanced` 41, `tour_simple` 31, `moveinformation_simple` 21, `startfollow` 2, formation routes | no node named Escort or Lead exists |
| teleports of the player | 260 (72) | NPC teleports 166; player teleports 73 (`PlayerAction_TeleportWithItems` 31, `DynamicCutsceneTeleport` 21, `…OnHorse` 21); `SwitchLevel` 15 | M30→trosecko and M12→kutnohorsko are the story's level switches |
| bed and wake-up | 17 (10) | `PlayerAction_WakeUpOnBed` 16 | after faders; paired with Ingame + dialogue (herbalist) |
| player switch | 21 (21) | `switchplayer` / `SwitchPlayer` | eight Godwin stretches (§2.5) |
| crime scenes | 44 (0) | `crimeScene` random events | punishment is a separate module family |
| input filters, outfit overrides, saves | 86 / 61 / 859 `SaveGame` | wired onto scenes | player gear: confiscation of the stash in 24 of 29 outfit overrides (WO-126A) |

The opening siege is: M30 `posledniPomazani` instancing the M50 `bitevniCast` battle module (one `SwitchPlayer` to
Godwin from the opening fader's `BeforePlay`, 32 background trackviews, 5 fights, then the finale video during which the
game switches back to Henry) (data-verified, §2.3).

### 1.9 Other things found

* **Loading-screen videos** are a seventh way a video starts (cvar/last-save menu loops; the intro and story-switch
  videos at level load).
* **Per-cutscene buffs**: `death_protection_cutscene` (imm=1, upr=1), `unconsciousness_protection_cutscene`,
  `mute_cutscene`, `cutscene_stopTime` (data-verified). AI knows `playCutscene`, `onCutsceneBegin/End` and a script-context
  node `cutsceneIsRunning` (data-verified).
* **83 cutscene-only souls** (`soul__cutscene.xml`) populate crowd scenes.
* The `.xsd` is stale (no Text or Credits element); one rendered entry has a subtitle attribute and no `.srt` in the paks;
  `CommandExecutor/cutscenes.xml` is an inert leftover from the first game (data-verified).
* **Commented-out**: `prepadeni_lakeMassacre` (the rendered path was cut; the Ingame sequence
  `cin_m0140t_prepadeni__lake_massacre` remains) (data-verified).

### 1.10 Where each kind is defined (one line each)

Table: `Tables.pak:Libs/Tables/ui/cutscene.xml`. Holders and data entities: `<level>/level.pak` `objects_mission0.xml` and
`layers/*.xml`. Sequences: level `moviedata.xml` (background only) and `cin_*` layers + `Cinematics.pak`/`Animations.pak` clips.
Videos: `Videos-part0..5.pak`, `IPL_Videos-part0..2.pak`. Quests: `Scripts.pak:Quests/Final/Barbora/**`. Skip-time types:
`ui/skiptime.xml`. Players: `Libs/Tables/player.xml`. Registry binding: level `SmartObjectHolder` entities with
`asset['alias']` links (25,099 links, 22,831 with a null `TargetGuid` that resolve only by id) (data-verified).

### 1.11 How scenes start

Counted over the 337 main-quest handlers by what feeds `EnqueueCutscene`: condition/function node 57, dialogue outcome 72
(DialogWrapper 38, FaderDialog 15, Scene 10, ForcedDialog 9), chained after another cutscene 21, quest State 17,
area/distance trigger 10, module out-port 9. Over all 731: condition 107, dialogue outcome (DialogWrapper 80, FaderDialog 58,
ForcedDialog 32), quest State 40, interaction trigger 34, module out-port 31, Haste 20. `AutoPlay=false` on 202 of 747 (the quest also
wires `PlayCutscene`, usually from `streamprofileshandling.onloaded`); `AutoFinish=false` on 107 (the quest wires
`FinishCutscene`) (data-verified).

### 1.12 What is wired onto a scene

Out-port consumers over the 731 handlers: State 256 (main 121), time 104 (77), stream profile 67 (39), weather 56 (41), save
31 (19), NPC teleport 17 (9), wake 16 (11), player teleport 14 (7), player switch 8 (8), kill 7 (3), unequip 5 (2), fight 4 (4),
game over 1 (1). Support nodes counted around scenes: `AdvanceWorldTime` 178, `ChangeWeather` 144, `GameOver` 96,
`SceneFinishedWaiter` 147, `PauseWorldTime` 46, `SaveLock` 30; `AddQuestItem` 433 and `AddReward` 1,172 in all quests
(data-verified; labels read from node names).

### 1.13 The 29 unresolved handlers of WO-126A

All 337 main handlers resolve now (Ingame 138, Fader 124, Text 30, SkipTime 24, FastTravel 9, Rendered 10, Credits 2).
Four mechanisms explain what the plain method missed: the holder arrives through a `Select` node (12), the parent
instance passes it over a module port found through the `Namespace` attribute (6), the alias is registered in a sub-system
registry (20), a registry entity of another class (3 DLC). Outside the main quests only a few handlers stay open
(`epilogFader_cutscene`, one duel, library rows). The totals agree (337 handlers, 141 background sequences)
(data-verified; the mapping of the old 29 to causes is inferred).

---

## 2. Phase 2 — where they are, quest by quest

### 2.1 The main quests, in code order

Counts from the quest walk (R rendered · I Ingame · Fa Fader · ST skip-time · FT fast-travel · Tx text; the rest are the other
kinds); M30 shows only its own nodes because its battle content is M50's module tree instanced inside it. The exact ordered
scene list per quest is in the CSV (rows keyed by the M-code); order there is the editor graph order, not an execution
trace (it reproduces WO-126A's order for M01; sandbox quests such as M05 interleave).

| code | quest | obj | R | I | Fa | ST | FT | Tx | trackview bg | forced dlg | fader dlg | fights | escort/follow | teleport | bed | player switch |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| M01 | prepadeni | 28 | 0 | 5 | 3 | 0 | 0 | 0 | 0 | 14 | 7 | 2 | 5 | 0 | 0 | 0 |
| M02 | zachrana | 52 | 0 | 4 | 3 | 0 | 0 | 1 | 3 | 21 | 10 | 2 | 0 | 0 | 0 | 0 |
| M03 | socky | 8 | 1 | 6 | 1 | 0 | 0 | 0 | 0 | 9 | 12 | 1 | 0 | 0 | 0 | 0 |
| M05 | svatba | 37 | 0 | 4 | 21 | 0 | 0 | 1 | 0 | 26 | 99 | 4 | 0 | 0 | 1 | 2 |
| M06 | naTroskach | 16 | 0 | 2 | 2 | 1 | 0 | 0 | 0 | 9 | 19 | 0 | 1 | 2 | 0 | 0 |
| M07 | nebakovPruzkum | 27 | 0 | 0 | 11 | 3 | 2 | 1 | 0 | 27 | 31 | 1 | 4 | 4 | 0 | 0 |
| M08 | mucirna | 17 | 0 | 3 | 4 | 0 | 1 | 1 | 0 | 13 | 13 | 3 | 8 | 5 | 0 | 0 |
| M09 | utokNaNebakov | 20 | 1 | 3 | 4 | 1 | 0 | 0 | 6 | 22 | 14 | 14 | 1 | 10 | 1 | 0 |
| M10 | bohutovaVlozka | 12 | 0 | 6 | 2 | 0 | 0 | 0 | 0 | 11 | 11 | 3 | 1 | 4 | 0 | 3 |
| M11 | nebakovObrana | 26 | 1 | 6 | 7 | 1 | 0 | 3 | 8 | 24 | 17 | 1 | 0 | 0 | 0 | 0 |
| M12 | vezniNaTroskach | 15 | 0 | 9 | 6 | 0 | 0 | 0 | 0 | 5 | 5 | 1 | 0 | 4 | 0 | 0 |
| M30 | posledniPomazani | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 1 |
| M31 | prijezdNaSuchdol | 8 | 1 | 3 | 5 | 2 | 0 | 1 | 0 | 6 | 9 | 1 | 0 | 2 | 0 | 0 |
| M32 | sedmStatecnych | 11 | 0 | 2 | 5 | 0 | 1 | 0 | 1 | 12 | 7 | 5 | 2 | 5 | 0 | 0 |
| M33 | hledaniLichtenstejna | 16 | 0 | 5 | 4 | 3 | 0 | 0 | 0 | 7 | 32 | 4 | 0 | 1 | 1 | 0 |
| M34 | kralovskeStribro | 24 | 0 | 1 | 2 | 0 | 0 | 0 | 0 | 7 | 36 | 10 | 0 | 0 | 0 | 0 |
| M35 | zachranaPtacka | 15 | 0 | 4 | 1 | 1 | 0 | 4 | 0 | 15 | 13 | 1 | 2 | 6 | 0 | 0 |
| M37a | setkaniVRatbori1 | 20 | 0 | 7 | 2 | 1 | 0 | 3 | 2 | 28 | 21 | 1 | 0 | 7 | 0 | 2 |
| M37b | setkaniVRatbori2 | 11 | 0 | 3 | 2 | 2 | 0 | 1 | 0 | 8 | 7 | 4 | 1 | 4 | 0 | 2 |
| M38 | sedmStatecnych2 | 34 | 0 | 3 | 7 | 0 | 2 | 2 | 1 | 13 | 39 | 7 | 1 | 0 | 1 | 0 |
| M42 | pogrom | 11 | 0 | 2 | 1 | 0 | 2 | 0 | 4 | 7 | 3 | 12 | 11 | 1 | 0 | 0 |
| M44a | zikmunduvTabor | 33 | 0 | 3 | 6 | 2 | 1 | 0 | 0 | 17 | 48 | 6 | 1 | 3 | 0 | 0 |
| M44b | utokNaMalesov | 18 | 1 | 6 | 5 | 1 | 0 | 2 | 7 | 24 | 6 | 4 | 3 | 0 | 1 | 0 |
| M45 | papezskyLegat | 23 | 0 | 4 | 3 | 3 | 2 | 3 | 0 | 18 | 34 | 13 | 3 | 7 | 1 | 0 |
| M46 | prepadeniVlasskehoDvora | 25 | 0 | 16 | 5 | 0 | 0 | 0 | 3 | 35 | 16 | 13 | 4 | 2 | 1 | 6 |
| M47 | erik | 10 | 0 | 7 | 1 | 0 | 0 | 1 | 0 | 5 | 15 | 1 | 0 | 0 | 0 | 0 |
| M48a | oblehaniSuchdole | 31 | 1 | 1 | 3 | 0 | 0 | 2 | 25 | 15 | 15 | 8 | 3 | 1 | 1 | 0 |
| M48b | rutinaAVypad | 23 | 0 | 2 | 1 | 0 | 0 | 0 | 32 | 6 | 25 | 2 | 3 | 1 | 0 | 0 |
| M48c |  | 0 | 0 | 7 | 2 | 2 | 0 | 0 | 12 | 18 | 26 | 7 | 0 | 2 | 1 | 2 |
| M49 | stealthMiseZaJindru | 5 | 0 | 7 | 1 | 0 | 0 | 0 | 0 | 8 | 4 | 1 | 0 | 0 | 0 | 0 |
| M50 | zoufalaObranaZaBohutu | 8 | 2 | 1 | 0 | 1 | 0 | 4 | 35 | 4 | 0 | 0 | 0 | 0 | 1 | 3 |
| M51 | finale | 14 | 2 | 6 | 4 | 0 | 0 | 0 | 2 | 9 | 9 | 0 | 3 | 0 | 0 | 0 |
| **all 32** | | 626 (WO-126A) | **10** | **138** | **124** | **24** | **11** | **30** | **141** | **443** | **603** | **132** | **57** | **72** | **10** | **21** |

(data-verified.) Quests with the most handlers: M05 svatba 26, M46 prepadeniVlasskehoDvora 21, M11 nebakovObrana 18,
M07 nebakovPruzkum 17. Side quests with the most cutscene handlers: S01 14, S39 13, S54 11, S30 10, S25 10. How many quests
hold each kind (main of 32 / side): Ingame 30 / 17, Rendered 8 / 0, Fader 30 / 50, SkipTime 14 / 20, forced dialogue 31 / 58,
fader dialogue 30 / 84, scripted fights 28 / 54, escorts 18 / 13, bed scenes 10 / 6.

### 2.2 Side quests, activities, events, DLC

Compact per-quest lists (all 174 non-main quests, with the ordered cutscene names) are in the scratchpad report and are
reproduced row by row in the CSV (`class=side|activity|event|dlc|other` in the notes). Totals by class are the table in §1.1.
The side quests hold 2,086 scene rows (0 rendered, 32 Ingame, 138 Fader, 1,083 fader dialogues, 308 forced dialogues,
134 fights); the DLC 890; events 375 (152 of the 213 fast-travel rows are random-event data); crime and punishment (open_world) is
the shared family §1.9.

### 2.3 The first two hours: the opening siege through Find Mutt

Scope: M30 (siege, as Godwin, with the M50 battle module) → M01 → M02 → M03 → S14 `hledaniPsa` (Find Mutt). **142 rows**
(111 if the 32 background siege trackviews count as one block): trackview 39, fader dialogue 21, forced dialogue 18, Ingame 17,
rendered 9, Fader 9, fights 9, escort 6, other set pieces 5, camera takeover 4, player switch 2, text 1, skip-time 1,
bed 1. About **47 minutes of non-interactive film** (11 min rendered, 36 min Ingame sequences; five dream movies of 316 s
are inside M02 sequences). Both/host rule (WO-126A §5.1, reported): 106 both-present, 33 host-only, 3 mixed.

```
New Game -> [video intro_new_game 199 s, loading screen]
 M30 (kutnohorsko, Godwin): fader -> crossbow lock -> battle (32 background trackviews, 5 fights) -> [video siege_finale_start 123 s; switch back to Henry]
 -> SwitchLevel story_switch_to_trosecko [video 170 s, loading screen]
 M01: faders -> ride -> meetingWithSheriff -> trialogue -> roadToCamp -> camp (training, duel, fire talk, dice) -> armorLake (176 s) -> reeds
      -> lakeMassacre (302 s) -> chase (rocks) -> henryFalls (28 s) -> M02
 M02: fall dream (205 s + 150 s movie) -> carried along the bank (5 visions) -> hut -> probuzeni (159 s + 105 s movie) -> day 2 (tasks, herbs, raid, potion)
      -> sleep -> prespani (117 s, 3 movies) -> day 3 (breakfast, farewell) -> M03
 M03: [video m03_trosky_journey 173 s] -> gate (283 s) -> tavern (65 s) -> Katerina (61 s) -> departure (67 s) -> sacks, dice, brawl -> stocks (74 s) -> release (132 s)
      -> odemceni_openworldu -> S14: fader -> talks -> bait vigil (30 min skip) -> second pack -> meeting Mutt (123 s) -> chat with Mutt
```

(data-verified; order is graph order.) The 142 numbered rows, each with kind, node/holder ID, trigger, end, player and world
effects, participants and skippability, are in the CSV marked "curated (first two hours)". Highlights that matter for two
players:

* Henry's inventory is wiped twice (`prepadeni_armorLake` AfterPlay, the M02 fall dream) and weapons are deleted at `roadToCamp`
  and in the chase; the world clock is quest-controlled in M01–M03 (`PauseWorldTime` plus `AdvanceWorldTime` sets) — host-only
  by the both/host rule (data-verified).
* M30's finale is enqueued again after a load (a post-load waiter feeds the same `EnqueueCutscene`), and the Godwin→Henry
  switch happens during that rendered video (data-verified). It is the one place in the first two hours where the data
  itself re-enqueues a scene after a load; "Henry falls" is a different case (§4.4).
* The M02 herbalist: day 1 `zachrana_probuzeni` then a forced conversation that hands out the tasks; day 3 `zachrana_prespani`
  then forced `pavlena__dialog_po_probuzeni` (data-verified).
* S14's meeting cutscene is in an area that plays when a player enters it, on that player's machine (WO-137 §8, re-verified §3.5).
* Tester path: the pack's 30 September launches hold `zachrana_prespani`, `m03_trosky_journey`, `socky_2_gate` to `socky_7_bergov`
  and the Find Mutt faders (§4).

### 2.4 How much of the main story each kind covers

Over the 32 main quests (1,946 scene rows, 626 objectives). Estimated scripted time is **23.1 hours** — a share of scripted
time, not of play time. The estimate uses 4 s per spoken line (dialogue ×0.5 for branches), a 20 s floor per Ingame sequence,
the table's exact video lengths, and counts nothing for fights, escorts, teleports and free play; every constant is inferred.

| kind | rows | share of rows | objectives touched | est. scripted time | share |
|---|---:|---:|---:|---:|---:|
| fader_dialogue | 603 | 31.0 % | 161 | 33,428 s | 40.2 % |
| forced_dialogue | 443 | 22.8 % | 165 | 36,628 s | 44.0 % |
| ingame_sequence | 138 | 7.1 % | 23 | 8,978 s | 10.8 % |
| trackview_background | 141 | 7.2 % | 16 | 0 | 0 % |
| setpiece_fight | 132 | 6.8 % | 57 | 0 | 0 % |
| fader_cutscene | 124 | 6.4 % | 68 | 834 s | 1.0 % |
| surrender | 86 | 4.4 % | 52 | 0 | 0 % |
| setpiece_teleport | 72 | 3.7 % | 19 | 0 | 0 % |
| escort/follow | 57 | 2.9 % | 25 | 0 | 0 % |
| camera_takeover | 39 | 2.0 % | 16 | 0 | 0 % |
| text_cutscene | 30 | 1.5 % | 14 | 180 s | 0.2 % |
| skiptime_cutscene | 24 | 1.2 % | 13 | 96 s | 0.1 % |
| player switch | 21 | 1.1 % | 5 | 0 | 0 % |
| fasttravel_cutscene | 11 | 0.6 % | 6 | 88 s | 0.1 % |
| rendered_video | 10 | 0.5 % | 1 | 1,223 s | 1.5 % |
| bed scenes | 10 | 0.5 % | 5 | 0 | 0 % |
| credits | 2 | 0.1 % | 0 | 300 s | 0.4 % |
| dialogue_twin | 3 | 0.2 % | 2 | 1,474 s | 1.8 % |

(data-verified counts, inferred time.) Read: two kinds, fader and forced dialogue, are about 54 % of main-quest scene rows and
85 % of scripted time; Ingame sequences are 11 %; Rendered video 1.5 %.

### 2.5 The player switches and the non-Henry stretches

The only other playable character is Godwin (`PlayerId 1`; no fast travel, no clothes change, no quest givers). 21 switches,
all in the main quests: M05, M10, M30, M37a, M37b, M46, M48c, M50. Scripted time inside the stretches (inferred): M50 (siege)
463 s, M05 intermezzo 207 s, M10 946 s, M37a 1,782 s, M37b 1,096 s, M46 501 s, M48c 126 s: about 5,100 s (1.4 h) of
scripted time plus play. Every switch is wired from a cutscene handler's `BeforePlay`/`AfterPlay` or a Haste entry; the
`switchplayer` library module also heals and cleans (data-verified). WO-125: joins are refused in non-Henry worlds; mid-session
the joiner is only told; WO-137: a host Godwin stretch holds the mirror (code-verified, from those docs).

---

## 3. Phase 3 — how the engine runs them

Binaries read: the Modding Tools build (`Bin/Win64ReleaseSteamLTO_DLL`, 1.5.5), strings, RTTI, exports, disassembly. The
retail install was not opened; retail statements come from WO-145's prose. Every RVA is module-relative on the Modding Tools build.

### 3.1 One scene queue, three scene players (code-verified)

`wh::guimodule::C_InteractiveSceneManager` (GUIModule, vftable `0x459B00`) runs **one scene at a time** from a FIFO queue with
no priority field. It is reached through the GUI module interface (`GameIface+0xF0`): the GUI module's one-line accessors are
slot `[0x38]` the fader controller (the WO-113 `GetFader`), `[0x48]` the cutscene player, `[0x50]` the scene manager. Three
classes implement the same scene-player interface and enqueue themselves into it: `C_CutscenePlayer` (GUIModule, vftable
`0x434140`), `C_DialogManager` (DialogModule, `0x21AA48`), and `C_CallbackScenePlayer` (ConceptModule, `0x3EC2E0`, the owner of
the quest node `SceneFinishedWaiter`). Manager vtable: `[0x18]` EnqueueScene `0x2818F0`, `[0x20]` EndScene `0x281B10`, `[0x28]`
IsSceneRunning, `[0x30]` finished-not-cleaned, `[0x38]` HasQueued, `[0x40]` Interrupt `0x281E50`; fields: current scene `+0x70`,
just-finished `+0x78`, waiting players `+0x20`, queue `+0x28..0x40`, fader controller `+0x88`, "waiting for readiness observer"
byte `+0x108`.

| situation | what the manager does |
|---|---|
| idle, a player enqueues | `current = player`, asks the fader controller to fade out with a continuation; when the fade is done and the scene was not interrupted, the player's `PlayScene` runs |
| busy | the player waits in the waiting-players container (log: "Scene '' was addded into waiting players …", the engine's own spelling) |
| between `EndScene` and `CleanupScene` | warning "Scene '%s' is being enqueued after the previous scene is already finished. Please use OnQueued signal."; late container |
| `EndScene` of the current scene | pops the next queued player into `current` with no idle gap, else `CleanupScene` |
| `Interrupt()` | interrupts current, finished and every queued player, clears the containers and the readiness state |

Blocking rules per source: **cutscenes** are always queued; a **normal player dialogue** is queued but only if the local player is
a participant; an **AI-forced dialogue** that arrives while a scene runs is **refused**, not queued ("Trying to enqueue dialogue
forced from AI while other scene is running"); `SceneFinishedWaiter` is refused when no scene is running ("can run only under a
fader"); ambient dialogues (`ingame`, `ingame monolog`, `chat`) bypass the manager (22 sample logs: 270 `EnqueueScene` lines against
15,805 ambient monologue starts). The scene starts only after the generic readiness service (`C_ReadinessService`, categories such
as `AfterGameLoad`, `LevelChanger`, `AfterSkipTime`) is ready (existence code-verified; the exact gate inferred).

### 3.2 The cutscene player, step by step (code-verified, order confirmed in the field logs)

```
InteractiveSceneManager::EnqueueScene called                              -> handler out-port OnQueued   (state 0)
CutscenePlayer::OnCutsceneInitialized called for <Type> cutscene '<name>' with holder '<h>' from module '<quest path>'
                                                                          -> BeforePlay                  (state 1)
CutscenePlayer::PlayCutscene            (same time when AutoPlay, else waits for the PlayCutscene port)
   ... the content plays ...
CutscenePlayer::OnCutsceneEnd
CutscenePlayer::OnRequestFastForwardedBehavior                            -> AfterPlay                   (state 2)
CutscenePlayer::OnPositioningFinished                                     (end positioning of the NPCs)
CutscenePlayer::FinishCutscene          (only when the quest sends it: AutoFinish=false)
CutscenePlayer::FinalizeCutscene
InteractiveSceneManager::EndScene / CleanupScene
CutscenePlayer::ReleaseScene                                              -> OnFinished                  (state 3; state 4 = interrupted, no port)
```

Correction to WO-126A §4.1: positioning is the **end-of-scene** NPC fast-forward, after `OnCutsceneEnd` — not a pre-roll.
`AfterPlay` waits for the NPCs when the holder has fast-forward links. Key RVAs: `EnqueueCutscene` `0x1456E0` (exported),
`OnCutsceneInitialized` `0x148EF0`, `PlayCutscene` `0x1457F0`, `OnCutsceneEnd` `0x145C90`, `OnPositioningFinished` `0x1468D0`,
`FinalizeCutscene` `0x145B80`, `Interrupt` `0x145FD0`, `ReleaseScene` `0x146D2A`, `SkipActiveCutscene` `0x146240` (exported),
`IsCutsceneInProgress` `0x146190` (exported). The state signal is a `C_Signal<C_CutsceneConfiguration const&, E_CutsceneState>`
(type string at GUIModule `0x43E790`) with five values (0 queued, 1 initialized, 2 ended, 3 finalized, 4 interrupted); the
handler's listener switches on it and fires the out port. **`Interrupt` does not raise `OnFinished`** (state 4 fires no port):
the quest chain behind it never runs. This answers WO-145 engine question 1.

The 8 cutscene classes and their type ids (`E_CutsceneType`): Fader 0, FastTravel 1, Ingame 2, Rendered 3, SkipTime 4,
TrackView 5, Text 6, Credits 7 (RTTI `C_FaderCutscene`, `C_IngameCutscene`, …; base `I_Cutscene`, `0x4340B0`). Skip:
`SkipActiveCutscene` returns **true for Ingame** (stops the sequence) and **Rendered** (asks the video system); **false** for
Fader, TrackView, Text, SkipTime, FastTravel and Credits.

### 3.3 The quest-side node `C_CutsceneHandler`

RTTI `.?AVC_CutsceneHandler@guimodule@wh@@`; state at `+0x338`: 0 idle, 1 queued, 2 ready and waiting for `PlayCutscene`,
3 playing, 4 ended and waiting for `FinishCutscene`, 5 finishing. In ports: `EnqueueCutscene` (accepted in state 0 with a
valid holder: **it accepts again as soon as the handler is back at 0**), `PlayCutscene` (state 2 and `AutoPlay=false`),
`FinishCutscene` (state 4 and `AutoFinish=false`). Out ports: `OnQueued`, `BeforePlay`, `AfterPlay`, `OnFinished`. Data edges
over the 747 handlers: 882 into `EnqueueCutscene` (module In port 299, `IfFunction` 60, Haste 54, `If` 49, another handler's
`OnQueued` 40, `InteractionTriggerNode` 38, `State.OnTrue/OnActive` 22), 214 into `PlayCutscene`, 123 into `FinishCutscene`; 805
`AfterPlay` consumers (Function 117, `State.SetActive` 43, `PlayerAction_WakeUpOnBed` 15), 355 `OnFinished` consumers
(`State.SetDone` 31, `AddReward` 9, `SaveGame.EnqueueSave` 8) (data-verified).

### 3.4 Dialogue as a scene, `WAITING_FOR_TWINS`, dialogue twins

A scene dialogue (`normal` type) goes `RequestDialog` → `StartDialogInt` → `manager->EnqueueScene` → `PlayScene` → the
`C_DialogInstance`. Its **24 states** (decoded from the to-string switch, `0x5A370`): 0 CREATED, 1 WAITING_FOR_POSITIONING,
**2 WAITING_FOR_TWINS**, 3 WAITING_FOR_NPC_FREEZE_EDDA, 4 WAITING_FOR_PLAYER_CLEANUP, 5 GENERATING_DECISIONS, 6 DECISIONS_GENERATED,
7 GREETING, 8 PROCESS_DECISIONS, 9 WAITING_FOR_INTERACTION, 10 PROCESSING_SELECTION, 11 WAITING_FOR_PLAYER_CONTROLLER, 12 ANIMATING,
13 INCLUDED_DECISIONS, 14 SKILL_CHECK, 15 SCRIPT_EXECUTION, 16 WAITING_FOR_AI, 17 SEQUENCE_COMPLETED, 18 WAITING_FOR_USER_INPUT,
19 CLEANUP, 20 INTERMISSION, 21 WAITING_FOR_INTERMISSION_END, 22 RESTART, 23 ENDED (code-verified). This corrects WO-145 §6:
`WAITING_FOR_TWINS` comes **before** the NPC-freeze state. What it waits for: `C_DialogueTwinController::MakeTwin`
(`0xEE630`) spawning `DialogTwin_%s` stand-ins for every participant including the local player (`DialogTwin_Dude`), plus the
clothing ray-casts (`wh_dlg_WaitForCloth`), horse repositioning and the player/twin overlap query. The original is
hidden/frozen; each twin carries a camera attachment (the engine logs `MasterSlaveManager is setting context '5' for entities
'DialogTwin_<soul>' -> '…CharacterCameraAttachment'` on both machines). **"Edda" is not defined anywhere in the binaries**
(inconclusive). Timeouts: `PlayerDialogController::NPCPauseRequests timed out after 20 s` (log); a request that is never accepted
logs `Canceling dialog request id N … Request timed out` (`wh_dlg_RequestTimeout` 20 s).

**Dialogue twins** are therefore not an entity kind in the data (no `DialogTwin_*` in any level) but a runtime pair per
participant. A **forced** dialogue bypasses the NPC brain (`ForceDialogNode` origin); a **fader** dialogue holds a named fader and
is the only kind allowed to hide or show actors. Interrupt line: `[ID: N] Dialog interrupted. [Ex0: <soul> Ex1: <soul> state:
<STATE> flags: N]`; end line `[ID: N] Dialog ending [… state: CLEANUP …]` (log).

### 3.5 How a quest learns a scene ended; cutscene areas

* **Cutscene**: the handler's out ports (§3.2/3.3): `AfterPlay` at content end (after fast-forward), `OnFinished` at
  `ReleaseScene`. An interrupted scene tells the quest nothing.
* **Dialogue**: a module out-port per outcome, wired by the parent to `State.Set<Value>`, `Timer`, `AddReward` and so on (8 % of
  all State edges come from dialogue outcomes: 2,313 of 29,078).
* **`SceneFinishedWaiter`** (149 nodes): `Enqueue` in, `OnEnqueue`/`OnFinished` out.
* **Cutscene areas** (WO-137 §8, re-verified): the second-pack module of `hledaniPsa` has three handlers (holders
  `vorechCS_mace`, `…_sword`, `…_bow`), each fed by a weapon-class `If`; the module In port `stream` drives `EnqueueCutscene`,
  `onloaded` drives `PlayCutscene`, both raised by the parent when its stream profile reports loaded. "Armed, not fired" means
  the State step that WO-137 mirrors arms the parent's flag; the scene itself plays when a player's streaming raises the ports,
  on that player's machine (data-verified structure, inferred semantics). `AutoPlay=false` handlers (202 of 747) wait for the level
  data (`esGameProfile`), which a joiner copy must also have streamed.
* **Engine listeners**: the quest graph's own hooks (`C_StateVariable` slots 33/42) see every State change; the cutscene
  player's state signal (subscribe through player vtbl `[0x70]`, ABI inconclusive) is the only place **Interrupted** is
  visible; `IsSceneRunning` and `IsCutsceneInProgress` can be polled. The Warhorse scriptbind docs list **no** scene start/end
  callback; `Movie.*`, `Human:IsInDialog/InterruptDialogs/ReadyForDialogWithTwins`, `DialogModule.ForceDialog/IsSoulInDialog` exist
  (data-verified, 5,017 doc pages).

### 3.6 Levers (console and native) and what each does to a scene

| lever | does | can start / end / skip / suppress |
|---|---|---|
| `wh_ui_PlayCutscene <name>` / `wh_ui_StopCutscene` | plays/stops **Rendered** videos only; no quest involvement | start/end (Rendered only) |
| `wh_ui_FaderSuspend` (cvar) / `wh_ui_KillFader <name>` | suspends all faders / deletes one (the 60 s stuck-fader warning names it) | releases a black screen, not the scene |
| `wh_concept_HasteTrigger <path>` | fires the real edge of any quest trigger; replays prerequisites and console commands; Modding Tools only | starts any quest scene with its side effects; unsafe in a shared world (WO-92) |
| `mov_NoCutscenes`, `mov_skipSequence`, `Movie.StopAllCutScenes` | skip TrackView-based scenes | skip (TrackView only) |
| `Human:InterruptDialogs()` | ends running dialogues (observed WO-112) | end dialogues |
| exported `SkipActiveCutscene` | skip, Ingame and Rendered only | skip |
| `wh_dlg_*` (`Enable`, `NoCameras`, `RequestTimeout`, `WaitForCloth`, `SkipPhysPosAdjustment`) | tune dialogue camera/twin/timing | suppress/tune dialogues |
| `wh_pl_ForcedSkipTime`, `wh_pl_StopSkipTimeDebug`, `wh_pl_FastTravelTo`, `wh_pl_SwitchPlayer`, `wh_sys_SwitchLevel` | start/stop skip, travel, switches | start |

Not found anywhere (code-verified over the strings of 15 modules): a command that **ends an arbitrary running scene cleanly**, or
that **starts an Ingame/Fader/Text/SkipTime/FastTravel cutscene by name**. Clean-end levers: the handler's `FinishCutscene` port
(needs `AutoFinish=false`, state 4), the AutoFinish path, `SkipActiveCutscene` for the content. `EndScene` is a plain manager slot
reachable by the same accessor, but the cutscene player's own flags (`+0x80..+0x83`) are not updated by it (inferred stale state).

### 3.7 What the mod does with scenes today

| mechanism | where | behaviour | shared-world gated? |
|---|---|---|---|
| old `MP-CUTSCENE` edge layer | `LogTailGameTransport.cs:390–394, 570–612`, `GameBridge.cs:839–861`, `kdcmp.lua:319–352` | parses `PlayCutscene`/`OnCutsceneEnd called for <Type> cutscene '<name>'` for Rendered, Ingame, Fader, Text, SkipTime; Rendered/Ingame set `cutsceneActive`, send StoryBeat kind 6 | **no** (WO-133 left it running) |
| Rendered-only pause flag | `LogTailGameTransport.cs:495–503` | `_cutsceneActive` from Rendered `PlayCutscene`/`OnCutsceneEnd` only; feeds PauseUp reason 0x08 | no |
| consumers of `cutsceneActive` | `kdcmp.lua:3203–3265` (NPC detach skipped), `4817–4871` (join deferral), `GameBridge.Wo114` (leash cutscene bit) | skip/defer/hold | leash and join: yes |
| stuck-scene detection | `GameBridge.Wo144.cs` | logs `' was added into waiting players'`, `Faders are faded out`, `Dialog interrupted.` as `MP-W144 engine:`; **no ender** | no |
| VideoMode decline | `wo138.cpp` (PauseGame gate), agent levers | declines `CCryAction::PauseGame` source 4 (VideoMode) and 7 (ESC menu) while a partner is present | partner-gated |
| punishment skip-time override | `kdcmp.lua:17870–17905` | sets `crime_punishment_skipTime_*` data to 1 s | yes |
| native joiner quest time gate | `wo137.cpp:455–476` | refuses `AdvanceWorldTime`/`PassLongTime` nodes on a joiner | yes |
| sleep vote | `wo140.cpp` | holds the `C_SkipTime` picker until the peers vote | yes |
| dialogue gate for avatars | `wo135.cpp:41–95` | refuses a dialogue with an avatar soul | session |
| quest layer scene prompt | `kdcmp.lua:20672–20730` | parks the old catch-up prompt during a cutscene | whole old layer off in a shared world |

Nothing in the shipped code handles `InteractiveScene`, `WAITING_FOR_TWINS`, `FaderSuspend`, `StopAllCutScenes`, `Movie.*`, or
calls `wh_ui_PlayCutscene`. The old layer's comment about "~4.8 s of wall-clock skew" is stale (the skew is per session, 0.1 s
or 92 s in this pack, and the 0.80 s entry offset of WO-95 is within WO-109's doubt). Three code facts worth knowing for the build:
the agent never resets `_localCutsceneActive` on a load or disconnect; the scene-queue echo pattern (`' was added into waiting
players'`, two d's) never matches the engine's three-d spelling; the mod's "end" edge fires at `OnCutsceneEnd`, not at the
release (§4.5). Retail: 22 of the 34 engine log fragments the agent tails are gone, including `CutscenePlayer::` (WO-145 §4.1),
so any scene feature that reads the log goes blind there: the build should read the engine's state natively.

### 3.8 Engine pieces a build needs: port-aware anchors, and WO-145 coverage

"Port-aware" = survives a rebuild or another build: RTTI name, a data/log string, a structural fingerprint, an export.

| piece | how to find it | WO-145 lists it? |
|---|---|---|
| GUI module accessors (`GameIface+0xF0`, slots 0x38/0x48/0x50) | RTTI `C_GUIModule`; one-line "return member" bodies; check the returned vptr against the RTTI of the target class | fader slot only (D-267, D-268, D-271, D-272) |
| `C_InteractiveSceneManager` (`IsSceneRunning`, `HasQueued`, `EndScene`, `Interrupt`) | RTTI; `__FUNCTION__` strings `…::EnqueueScene/EndScene/CleanupScene/Interrupt`; the log strings of §3.2; field layout | **no row** (prose only, §6 Q1) |
| `C_CutscenePlayer` (state signal, `SkipActiveCutscene`, `IsCutsceneInProgress`) | RTTI; exports on the Modding Tools build; vtable order | **no** (only the lost log fragment) |
| `C_CutsceneHandler` ports (`EnqueueCutscene`, `FinishCutscene`) | `FindNode` + `GetPort` + slot-15 pulse; port-name strings; state at `+0x338` | generic concept rows apply (D-141…152, D-242…249, D-439…481) |
| scene state enum 0..4 consumers | the emit sites (`mov r8d, N` before the emit call); the handler's switch ladder | no |
| `C_CallbackScenePlayer` / `SceneFinishedWaiter` | export `GetCallbackScenePlayer`; RTTI; strings "can run only under a fader", "node cannot be enqueued twice" | no |
| `C_DialogManager` / `C_DialogInstance` (state field `+0x308`, `StartDialogInt`, `EnqueuePlayerDialogue`) | the 24 state strings and the to-string switch; the "New dialogue … is starting" string; RTTI | start gate yes (D-428…430, hook `dialog-start-gate`); states and queue no |
| `C_DialogueTwinController::MakeTwin` | strings `DialogTwin_%s`, "Could not spawn twin entity", `wh_dlg_ShowOrigActor`, `wh_dlg_WaitForCloth` | no row |
| `[ScenePositioningManager]` (the "NPC positioning took longer than expected" warning) | that string in the log; locate by it (new from this census) | no |
| fader controller | RTTI `C_FaderController`, `C_BasicFader`; vtbl slots | yes (D-266…272, D-328) |
| `C_FastTravel`, `C_SkipTime`, calendar, State setter, port pulse, `FindNode` | strings and RTTI as in WO-145 | `C_SkipTime`, calendar, State setter (D-479/480), port pulse and `FindNode`: yes; `C_FastTravel`: no |
| `C_ReadinessService` | strings `Readiness observer '%s' with category bitmask …` | no |
| CryMovie sequences (`Movie.*`, `mov_skipSequence`) | Lua names; cvar strings | no (not a native row) |
| player switcher (`C_PlayerSwitcher`, `C_ShelverManager`) | strings "Switching to player %d", "Player switching can't be started …" | no |

Retail class for these pieces was **not** probed (the retail install is out of scope): that the scene classes survive with the
same vtable order is (inconclusive).

---

## 4. Phase 4 — the field evidence, by kind

### 4.1 The pack

Seven zips; **`HOSTINGPLAYER--…232810` is byte-identical to `HOST1-…232810`** (same SHA-256) and counts once. The six distinct
sets hold 13 game launches in four sessions. The maintainer's group labels cannot be derived from the logs; the best fit is
**group 2 = S2** (30 Sep early, 0.42.0, a new game, host side only; the game log for its first 80 minutes, the opening, is **not**
in the pack and there is no joiner log), **group 3 and the two-country pair = S3 + S4** (30 Sep evening, 0.42.2, host plus
joiner, same new-game save line) (inferred). S1 (29 Sep, 0.42.0, the maintainer's own pair, crash and relaunch) is the control:
about 75 minutes of joint play with no named story cutscene, no stuck scene, no `WAITING_FOR_TWINS`, no fader above 6 s except
loads. The two machines' wall clocks align to about 0.02 s per launch (the joiner is 92 s ahead in S3/S4 and 0.1 s in S1).

### 4.2 Every scene-like event, classified by kind

13 launches yielded **30 named cutscenes** (Ingame 15, Fader 10, Text 3, Rendered 2), **14 skip-time edges**, **78 interactive
scenes** (every dialogue that went through the scene queue), 40 faders of 2 s or more and 15 loads (99 + 78 tabulated events;
the table is in the field report and `field/master_events.csv` in the scratchpad). No `FastTravel:` marker and no
`wh_ui_FaderSuspend` occur anywhere. By kind:

| kind | in the pack | how it ended |
|---|---|---|
| ingame_sequence | 15 (`socky_2_gate` … `socky_7_bergov`, `zachrana_prespani`, the contraband sack scene, …) | host: 8 played, all normally (3.2–19.9 s of scene, 3.5–6.9 s end fade); **joiner: 4 of its 7 ended late or never** (`zachrana_prespani` +41.5 s, `socky_3_tavern` +60.2 s, `socky_6_pillary` interrupted at 195.6 s, the contraband scene followed by a 67.75 s positioning wait) |
| rendered_video | 2 (`m03_trosky_journey`: host 2.4 s paused, joiner 3.9 s) | normally on both; the mod's pause flag only ever shows this kind |
| fader_cutscene | 10 (`dice_preMinigameFader` ×4, `fightClub_fightTeleport` ×3, `test_teleport`, `zachrana_afterHerbs`, `randomEvents_fader`) | normally, except `fightClub_fightTeleport` on the joiner: interrupted at 132.7 s |
| text_cutscene | 3 (`zachrana_burrying` ×3, 3.1 s each) | normally |
| skiptime_cutscene / skip-time edges | 14 `AfterSkipTime` edges (0.2–3.7 s); one **cancelled** after 0.33 s | the 0.42.2 agent kept its skip-time pause flag on for 2,737 s after that cancel (fixed in 0.42.5) |
| fader_dialogue / forced_dialogue | 78 scene dialogues | 4 stuck in `WAITING_FOR_TWINS` and left on the 20 s timer; the rest normally |
| fasttravel_cutscene | none | — |
| trackview_background, credits, set pieces | none visible as scenes | — |

### 4.3 The joiner's black screen while the host is in a scene: 8 cases, all on the joiner (log)

Joiner black time 20.1–204.5 s; the host's same scenes 2–20 s. Two mechanisms, both in the engine's own lines:

| # | scene | joiner black | engine's words | what ended it |
|---|---|---:|---|---|
| 1 | `zachrana_prespani` + forced `pavlena__dialog_po_probuzeni` | 42.1 s | positioning took 41.30 s | host autosave, joiner's `Cancelling FF` |
| 2 | `socky_3_tavern` | 60.4 s | positioning took 59.85 s | host save 2 s earlier |
| 3 | forced `forced_jindra_smrdi` | 21.1 s | NPCPauseRequests timed out after 20.0 s, `WAITING_FOR_TWINS`, 0 twins | the 20 s timer |
| 4 | `socky_6_pillary` | 204.5 s | no `PlayCutscene` ever; stuck at precaching | `Interrupt` with the rejoin load, after the host's manual save and reload |
| 5 | contraband sack cutscene (joiner started it alone) + forced `mlynar__konfrontace` | 87.7 s | positioning 67.75 s then twins 20.0 s | the host's exit save, then the timer |
| 6 | `fightClub_fightTeleport` (joiner started it alone) | 141.5 s | no `OnPositioningFinished` | `Interrupt` with the rejoin load |
| 7 | contraband again + `mlynar__konfrontace` | 20.2 s | twins 20.0 s | the timer |
| 8 | `pacholek_jenik__po_souboji` | 20.1 s | twins 20.0 s | the timer |

* **(a) Positioning** (cases 1, 2, 5; 4 and 6 never finish): the positioning after the scene's end call never finishes by itself
  on the joiner; it ends only when something cancels the NPC fast-forward (the joiner mod's Henry-snapshot `QuickSave`, written for
  every host world save, makes the engine log `FastForward synchronization is going on while save lock is requested … Cancelling FF
  for all NPCs`; the link is inferred from timing: 1.6–2.3 s after the host save) or the scene is interrupted by a load.
* **(b) Twins** (cases 3, 5, 7, 8): a dialogue the **game** started on a copy of an NPC (all four carry the mod's `WO137-TALK
  forced` line) never creates its twins; 4 of 4 left after 20.000–20.012 s with zero `DialogTwin` contexts, where the other 8
  joiner scene dialogues set exactly 2 and every normal host dialogue set 2–4.
* Why a copy cannot be positioned or paused is **(inferred)**, not logged: the joiner's NPCs are the host's stream-driven,
  `wh_ai_PauseNPC`-paused copies.
* Two joiner scenes (5, 6) were started by the joiner's **own** world while the host ran free (case 6: the joiner's own
  conversation outcome fired the fight's teleport); the scene's mirrored-step start is not the only start.
* The host is not blocked by the joiner's black screen: it saved, talked and reloaded.

### 4.4 The five known cases, placed

1. **Lake massacre (about 260 s host, 94 s joiner, then a 117 s black fader): (inconclusive), not in this pack.** No scene in any
   launch has those numbers (longest host cutscene 19.9 s; no 117 s fader). In the data it is `prepadeni_lakeMassacre` (with a
   Discovered variant chosen by a `Switch` node), an **Ingame sequence of 301.9 s with fades and a ScreenFader node** that sets the
   clock to 21:30, M01 (data-verified). A host 260 s against a joiner 94 s has the opposite shape to every stuck scene in the pack
   (where the joiner is longer by 3–10×); a joiner copy ended early by an `Interrupt`, or started late, would fit (inferred).
   WO-90's older field log timed this scene at 13.7 s apart on the two machines (an earlier session).
2. **"Henry falls" queued again after ending (groups 2 and 3): (inconclusive), not in this pack.** In the data
   `prepadeni_henryFalls` is a 28.4 s Ingame sequence started by `nasleduj_ptacka.OnDone` through the scene wrapper's
   `spousti_se_zaverecna_cutscena` port, ending the quest (`AfterPlay` → `jindra_kolabuje_ve_skalach`) (data-verified). The
   handler **accepts `EnqueueCutscene` again as soon as it is back at idle** (§3.3), so any second `OnDone` of that State after the
   scene ended queues it again (code-verified for the acceptance; the second `OnDone` is inferred). Candidate sources in a session: a
   mirrored host step arriving after the joiner's own copy already ran it, the checkpoint correcting a State to a different value
   (the WO-144 §4.1 loop was one such, fixed), or a load. What the pack does show are re-plays that are *not* re-queues (§4.6).
3. **Joiner black while the host is in a scene: confirmed**, 8 cases (§4.3).
4. **Herbalist wake-up with `WAITING_FOR_TWINS`: the wake-up is there, the twins are not.** The wake-up (12 h sleep, then
   `zachrana_prespani` and the forced `pavlena__dialog_po_probuzeni`): host 17.1 s cutscene, joiner 48.6 s with a 42.09 s fader;
   the cause is mechanism (a). `WAITING_FOR_TWINS` occurs 7 times in the pack, none in that window (4 stuck ones elsewhere, 3
   transient notices). Refuted for this pack; the maintainer's pairing may come from a log not in the pack (inconclusive).
5. **Opening siege played separately: (inconclusive)** for the opening itself. **Separate play of one scene on two machines is
   confirmed** on other scenes: the contraband cutscene ran **alone on the joiner** (24.1 s) while the host did not play it for
   another 14 minutes, then the joiner played it again 11.4 s after the host's. The `socky` chain is the "together" case, the
   joiner's cutscene starting 0.1–1.5 s after the host's; `socky_4` started 38.9 s late behind the stuck `socky_3`; `socky_5` and
   `socky_7` never started on the joiner.

### 4.5 What each player's screen did (log)

* **Host**: in-game cutscenes are **not visible as a cutscene state** to the mod (only Rendered shows `paused: cutscene`); scene-end
  fades are 3.5–6.9 s; the host's skip-time flag was wrong for 45.6 minutes in S3 (stuck after a cancelled skip).
* **Joiner**: a long black fader with the player barely moving (1.4 m in 42 s, 0 m in 20 s); the mod's own data stream keeps
  flowing at 27–40 samples per second, so the joiner's Lua timers run; the joiner's state shows `paused: dialogue` for the
  dialogue waiting behind the stuck scene. **The mod's cutscene "end" edge fires at `OnCutsceneEnd`, 41.5 s and 60.2 s before
  the engine's release in the two measured cases, so the host's leash reads `joiner in-world` for the whole black screen.**
* **Far away / loading / another scene**: `socky_3_tavern` teleports the host 846 m; the host's leash logs a teleport, "joiner
  comes along", and the joiner's own copy jumps 843 m by itself; the joiner's countdown was "held at 0 s by the host" 209 times
  in one launch. Each join pauses the host's world for 17.6–81.5 s while the joiner loads. When the host is in a dialogue the
  joiner is told "the host is paused: dialogue" and its clock stands with the host's; its world keeps running.

### 4.6 Re-plays that are not re-queues, and queue blocking

`zachrana_burrying` three times (3.1 s each, different graves); `fightClub_fightTeleport` twice on the host (two teleports of
one fight); `dice_preMinigameFader` twice 17 s apart; tutorial conversations re-started by the player 4–5 times. Each start has its
own `Soul 'Dude' requested dialog` line: re-talks, not re-queues. The one real queue pattern is head-of-line blocking: scenes
enqueued while a joiner scene is stuck wait and then run in order once it is released (queue waits of 48.9, 78.7, 104.2 and 315.1 s).

---

## 5. Phase 5 — the quest-state side

### 5.1 What sets a State (data and code)

8,917 State nodes (2,963 in the 32 main quests), 29,078 edges into them (8,910 in the main quests). Every change goes through the
State's own In ports (`Exec`/`Value`, `Set<Value>`, `SetTrue/SetFalse`, `Increment/Decrement`) and one native setter
(`0x29C870`, called from every value path; an equal value does nothing and fires no edge) (code-verified):

| first-hop source into State set-ports | all | % | main | % |
|---|---:|---:|---:|---:|
| logic node (`If`, `IfFunction`, `TriggerSequence`, `Function`, …) | 7,760 | 26.7 | 2,032 | 22.8 |
| module In port (set by the parent graph or Haste; incl. 93 engine lifecycle signals) | 6,634 | 22.8 | 1,898 | 21.3 |
| sub-module out port | 4,492 | 15.4 | 1,539 | 17.3 |
| world trigger / interaction (`AreaTrigger`, `SoulDeathTrigger`, …) | 3,813 | 13.1 | 1,398 | 15.7 |
| **dialogue outcome** | 2,313 | 8.0 | 717 | 8.0 |
| `HasteTrigger` | 1,580 | 5.4 | 486 | 5.5 |
| another State's `On<Value>` | 1,301 | 4.5 | 378 | 4.2 |
| **cutscene handler out port** | 351 | 1.2 | 159 | 1.8 |
| `Timer` | 344 | 1.2 | 116 | 1.3 |
| `PlayTrackView`, `SceneFinishedWaiter`, `Scene` wrapper | 154 | 0.5 | 116 | 1.3 |

**Which scenes change a State**: dialogue outcomes (2,313) and cutscene `AfterPlay`/`OnFinished`/`BeforePlay` (351): the two scene
sources; the dominant landing port is `SetDone` (4,278 uses). A joiner that skips a scene never fires that edge. Loose upper
bound: of the 2,963 main-quest States 2,038 have a scene node somewhere upstream; **257 are set only by scene outputs**; 141 States
in the game have no incoming edge at all (46 in the main quests) and change only on a save restore.

### 5.2 "Without a port": what the field's number means

The DLL records a port name only when the State's `Execute` (vtbl slot 33) is the call in progress on that node
(`wo137.cpp`: `g_execNode == self && g_execPort`); a change that reaches the setter another way (the neighbouring value handlers
`0x29E010`, `0x29E0AA`, `0x29E1B3`, `0x29E28C`, `0x29E3B0`, `0x29CC90`, a restore) is logged as port `-` (code-verified). The
quest data has **no** State written without one of its ports (§5.1). So the field's "no port" is **a missing attribution, not a
missing port** (inferred from the two code reads); the joiner cannot apply such a change only because it has no name to pulse.
The observed share: **31–36 % of host changes sent per launch in the 0.42.2 sessions** (44–57 % in the small 0.42.0 samples),
about half of them inside a scene window in S3 and a third in S4:

* scene-internal flags and counters inside cutscene or dialogue modules (`cin_*` modules, `stopcrime`, `fightstop`, Katerina and
  fight-club behaviour states, `stocks_dialogue` flags);
* objective counters (`carryingBags`, `pickupedBags`, `numberOfActiveFans`) mostly outside scene windows;
* time control and bed flags (`ovladani_casu.pauseTime`, `hrac_jde_spat.onBed`);
* ambient systems that run on both machines anyway (the village bull and day cycle, the smith line).

The joiner's **uncorrectable mismatches** cluster in the stuck scenes' own modules: 29 of 31 in `socky.hibernable` (the
`stocks_dialogue` and pillory States of the stuck scene) in one launch; 28 of 41 in `mlynaruvUcen.stealth_takedown_tutorial` in
another.

### 5.3 The "inactive" drops

Group 3's "403 of 473 dropped inactive" could **not** be reproduced under any counting definition (14 counters, per launch, pair
and triple, tried). What the logs show: every game load after the DLL attaches produces **one burst of 614–640 State records
within 5 ms (host) to 0.9 s (joiner), all with port `-`, all flagged `silent`** (the load restoring every quest State through the
setter without notification), and the agent's `inactive` veto drops them (621, 615, 640, 640). The burst is 67–84 % of all records
in a launch, close to the maintainer's 85 %, so the 403 is plausibly one load burst of a session whose total cannot be identified
(inconclusive which launch). In 0.42.0 no record carries `silent` and the vetoes are 0: the counter exists from 0.42.2. So the
"steps lost during loads and rejoins" are **not** lost steps: the load carries the exact State of the host's save (WO-124/125);
what drifts afterwards are the host's later steps and the joiner's own scene consequences.

### 5.4 Is there a safe way to set a State so its consequences run?

Yes, one: **pulse the State's own In port** (`FindNode` → `C_Node::GetPort` → `I_Port` slot 15) which runs `ExecuteNode` → the
setter → slot 42 `OnStateChanged` → every `On<Value>` edge. WO-137 fired it live ("pulse equals the real step"). It is
idempotent per State (an equal value fires nothing) but **not** consequence-safe: the edges from the same State also reach rewards,
teleports, time advance, saves and — through a handler's `EnqueueCutscene` — scenes. WO-97 withdrew 5 of 22 objective fixes for
exactly that reach; `Audit-ObjectiveFixHazards.py` classifies the hazards (CUTSCENE, TELEPORT, ITEM, DIALOG, SAVE, CLOTHING,
MOVE) by name and tag, so a scene started by a node named like nothing scene-like is missed. Haste is unsafe (it replays console
commands). There is no engine setter that writes a State without firing its edges. `C_Node::ActivateNodeWithoutSideEffects` exists
(vtbl `[0x90]`, exported); its semantics are unread (inconclusive). The port a pulse needs is exactly what the DLL fails to record
for about a third of changes: **recording the port at the port pulse (the port's `Trigger`, vtable slot 15, already in WO-145
D-445/D-460/D-461) instead of at `Execute`** is the fix that makes those changes appliable (inferred; live gate needed).

### 5.5 What a catch-up after a join would need

(1) The State comes with the host's save at the join; drift starts after it. (2) A **per-State comparison** (the checkpoint already
does it) with a port attribution that works for all value paths. (3) A **class per scene** deciding whether the joiner replays only
the State edge (no scene), plays the scene locally (pulse `EnqueueCutscene`: needs the stream profile, the participants and a free
scene queue), or skips it (`SkipActiveCutscene` after it starts, Ingame and Rendered only). (4) A guard that a replayed step does
not trigger **other** consumers on the joiner (rewards, time, saves, switches; the native time gate already covers `AdvanceWorldTime`
and `PassLongTime`). The data says only 1.2 % of State sets come from cutscene ports and 8 % from dialogue outcomes, so most of a
catch-up is State pulses, not scenes (inferred). Pieces already in WO-145: `FindNode`, port pulse, State detector and setter,
quest enumeration (D-141…152, D-242…249, D-439…481); not in it: scene-end knowledge on a joiner (the handler's `+0x338`, the cutscene
player's state signal, the manager's `IsSceneRunning`).

---

## 6. Phase 6 — what two players need, per kind

### 6.1 The three options and their costs

| option | what the partner does | what it needs | cost and risk |
|---|---|---|---|
| **A. both watch** (each machine plays its own copy) | plays the same scene from its own copy of the graph, starring its own Henry double, 0.1–1.5 s later | the mirrored step starts it (works today); a **scene guard** so the joiner's copy finishes: scoped resume of the scene's NPC copies, a bounded positioning wait, a clean release; the same end spot for both | the stalls of §4.3; the scene's world effects (time, weather, layers, corpses) happen only on the playing machine; the other player's avatar is not in the shot; two machines can finish at different times |
| **B. triggerer only** | keeps playing; the world and the clock run; sees the other avatar stand or vanish | a **start gate** on the joiner's copy (the handler `EnqueueCutscene` port or the dialogue start gate, D-428…430); the outcome mirrored as State steps | the scene's non-State consequences (items, rewards, saves) happen only where it plays: host-only is the rule for those; the partner has to be brought along at the end for teleports |
| **C. hybrid** | placed at the scene and watches the host's figure, or waits behind a short fader | a safe parking spot, a hold, the fader | nothing in the engine lets a player watch another's camera: "watch the host's figure" means standing near the staged scene in the world, not seeing its camera; a fader costs a second or two |

### 6.2 What happens to each body, during and after

* **During, on the scene's own machine**: input off (`cutscene`, `fader`, `text_cutscene`, `no_input` maps), camera taken
  (Ingame, forced dialogue), the player stance/outfit possibly overridden by the quest (`PlayerOutfitOverride` 61 nodes; stash
  confiscations); `death_protection_cutscene` and `mute_cutscene` buffs apply; saves are locked.
* **The other player's avatar on the playing machine** is an NPC with no holder link: neither positioned nor hidden (inferred),
  so it stays where the stream puts it, in shot or in the way; for option A it is useful to hide it for the length of the scene
  (hold-not-hide is the WO-138 rule for pauses; a scene is not a pause).
* **After**: holder `teleport` links place participants (610 holders, 4.7 links on average), `fastForward` links move them
  (272 holders), and the player is repositioned by `PlayerLinkRerouter` on **92 holders** (Ingame 34, Fader 27, FastTravel 12, SkipTime 8,
  Text 7, Rendered 4; 76 of 731 handlers). With option A both copies run the same links, so both end at the same place; with
  option B a quest teleport of the triggerer is followed by the leash ("a teleport -- the joiner comes along", observed) or an
  explicit placement. 846 m (`socky_3`) is the longest observed.

### 6.3 What breaks if a player is far away, loading, or in another scene

* **Far away**: the holder's entities and layer profile must be streamed on the joiner's machine; a scene started from a mirrored
  step on a far-away joiner loads its stage layer (a visible hitch) and teleports the player 100–850 m; the host-owned NPC copies
  it positions are overwritten by the host's stream (a scripted relocation of 847 m is indistinguishable from a speed hack by
  displacement, WO-95/WO-66 claim gate). Recommendation: a distance threshold, beyond which option A falls back to B.
* **Loading**: a scene enqueued while the readiness service is not ready waits for it; each join pauses the host's world 17.6–81.5 s
  and defers joins while a cutscene or dialogue runs (WO-123); a load **interrupts** the running scene silently (state 4: no
  `OnFinished`, the quest chain behind it never runs) — which is how the pack's stuck joiner scenes ended; the host's reload and the
  joiner's rejoin are the observed releases. After a load, M30's finale is enqueued again by the data itself.
* **In another scene**: the queue is serial per machine, so a scene mirrored onto a joiner that is in its own scene waits (observed:
  48.9–315.1 s behind a stuck one), and an **AI-forced dialogue is refused** if any scene is running. A joiner in a conversation
  with the host's NPC copy can hold the whole queue.

### 6.4 Per kind: what each option does

| kind | A both watch | B triggerer only | C hybrid / note |
|---|---|---|---|
| rendered_video | works today; no NPC positioning unless the holder has links; local skip only | partner keeps playing; the host's video freezes only the host's machine (VideoMode declined) | the cheapest to keep as is |
| ingame_sequence | needs the scene guard (4 of the joiner's 7 stalled in the pack); AnimChar doubles copy the local Henry | a start gate on the joiner's handler; outcome by State steps | recommended: A with B as the fall-back |
| fader_cutscene | trivial once the guard exists; plumbing for teleports | fine for pure teleports (the leash brings the partner) | same guard |
| skiptime_cutscene | the clock is the host's; the joiner's skip shortened to 1 s (WO-139 pattern) | only the host's clock moves | sleep is the vote (WO-140) |
| text_cutscene | trivial | n/a | 2–7 s |
| fasttravel_cutscene | the joiner's own travel is refused | the triggerer travels | leash pull brings the partner |
| trackview_background | no | host-only | no copy on the joiner |
| fader/forced dialogue | the joiner's copy of a game-started dialogue never creates twins (4 of 4) | **works**; the WO-137 talk route already runs a joiner-started one | the partner cannot sit in on the dialogue (no camera spectate; the ghost cannot be spoken to) |
| set pieces | fights: existing combat sync; escorts: not built | leader-led | surrender pose not built |
| player switch | not possible (Godwin is the host's `$__player`) | host plays, joiner held | maintainer's call |

---

## 7. Phase 7 — the decision table and the plan

The decision table is §0.3. The build plan below is ordered by story covered and by how badly each breaks today; sizes are
rough (S/M/L). Every WO should be port-aware per the permanent rule: where a piece is named here, the anchor is §3.8.

| WO | scope | kinds / rows | engine pieces (anchor; in WO-145?) | live gate |
|---|---|---|---|---|
| **A. Scene guard** (S–M) | bound the positioning wait; scoped resume of the participants' NPC copies for the scene; a clean release; fix: the end edge fires at the release, a reset on load, the echo spelling; read `IsSceneRunning` | Ingame, Fader, Text, forced dialogue (the 8 stalls) | scene manager `IsSceneRunning`/`EndScene`/`Interrupt` (RTTI, strings; **no**), `[ScenePositioningManager]` warning (string; **no**), handler `FinishCutscene` port (generic rows) | two machines: `socky_3`-type scene ends in < 5 s with the partner's NPC copies resumed; no 20 s twin stall |
| **B. Scene bus** (M) | native subscription to the cutscene player's state signal and the scene queue; one wire message {type, name, holder, module path, state 0–4} to the partner | all cutscene kinds; retail-proof | cutscene player state signal (vtbl `[0x70]`; **no**), `C_GUIModule` accessors (fader slot only) | start/end seen natively on both machines, retail class probed |
| **C. Dialogue = triggerer only** (L) | gate the joiner's copy of a host-triggered forced/fader dialogue; joiner-triggered ones through the WO-137 talk route; the port-attribution fix | fader/forced dialogue (about 85 % of scripted time), dialogue twins | dialogue start gate (D-428…430, yes), `C_DialogInstance` state strings (no), `MakeTwin` (no), `I_Port::Trigger` (D-445, yes) | 10 forced dialogues across M01–M03 on two machines, outcomes mirrored, no black screen |
| **D. Cutscenes both watch** (L) | mirrored start + guard + same end spot for both; hide the partner's avatar for the scene; far-away fallback to B | Ingame, Rendered, Fader, Text, credits | handler ports (generic), `PlayerLinkRerouter` links (data), layer profile streaming | M01–M03 opening sequence played by both |
| **E. Time and travel scenes** (M) | skip-time (host clock, joiner 1 s), fast travel, quest teleports, sleep/wake | SkipTime, FastTravel, teleports, bed, wake | `C_SkipTime` (yes), `C_FastTravel` (no), calendar (yes) | a skip-time quest, a fast-travel quest, one wake |
| **F. Set pieces** (M–L) | surrender pose on copies, escorts follow their leader, quest items for the joiner | surrender 233, escorts 97, `AddQuestItem` 433 | AI/surrender (data), item sync (WO-134/148) | an escort and a surrender in M01/M42 |
| **G. Catch-up after join** (M) | per-State compare with working attribution; scene classes (replay State only / play / skip) | all State-bearing scenes | `FindNode`, port pulse, State detector/setter (yes), handler state `+0x338` (no) | join mid-quest at three points |
| **H. Non-Henry stretches** (M, policy-bound) | the host plays Godwin, the joiner is held, told and caught up | player switches 21, 8 stretches (about 1.4 h) | `C_PlayerSwitcher` strings (no) | one stretch (M10) |

Order: A and B first (no policy needed, fix measured breakage, give every later WO a native signal), then C (most story), D, E, F,
G, H. A and B could be one WO. About 30 % of what the host sends today is un-appliable without the attribution fix (in C).

---

## 8. Known unknowns: what only a live run settles

1. Whether a joiner's copy of a scene finishes its end positioning once the participants' NPC copies are resumed (the guard's
   central assumption).
2. Whether the engine's `Cancelling FF for all NPCs` (a save lock requested during fast-forward) can be used as the release on
   purpose, rather than by accident through the joiner's snapshot `QuickSave`.
3. Whether `SkipActiveCutscene` on an Ingame scene reliably ends in `OnCutsceneEnd` → `AfterPlay` → `OnFinished`, and what happens to
   the holder's `fastForward` positioning.
4. Who calls `C_InteractiveSceneManager::Interrupt` (a load? game over?) — the callers were not identified (indirect calls).
5. What blocks `WAITING_FOR_TWINS` for a hidden or externally driven participant, and what "EDDA" abbreviates.
6. Whether the cutscene player's state-signal subscription ABI (vtbl `[0x70]`) is stable, and whether `mov_skipSequence` ends the
   owning handler normally.
7. Whether retail keeps `C_CutscenePlayer`, `C_InteractiveSceneManager`, `C_CutsceneHandler` and `C_DialogInstance` with the same
   vtable order (not probed: the retail install is out of scope).
8. What each screen shows when the other player is far away, loading or in another scene (nothing in the pack covers a far-away
   joiner during a scene).
9. How the 43 rendered clips with no holder and no literal quest reference are played, and where the 201 story sequences keep their
   camera tracks.
10. Whether a quest-driven Rendered video with the VideoMode decline on lets the world run (WO-138 §5, still unproven).
11. Whether the 0.80 s entry offset of WO-95 is a constant (the 0.42.2 pack shows 0.1–1.5 s with an exact clock).
12. The lake massacre, "Henry falls" queued again, and the opening siege on two machines: not in the attached pack; the opening's
    game logs (S2 before 02:12) are missing. A new two-machine run of M01 would settle all three.
13. Whether the dialogue world-time pause (`DialogInstance` handle, DialogModule `0x684C3`) should be removed (WO-112 T2).
14. Whether `ActivateNodeWithoutSideEffects` is a usable side-effect-free State setter.

---

## 9. Decisions made unattended

1. **Duplicate zip.** `HOSTINGPLAYER--…` is byte-identical to `HOST1-…` (same SHA-256); counted once.
2. **Group labels.** The logs cannot name "group 2 / group 3 / the two-country pair"; I mapped them to S2 and S3+S4 by version,
   game state and date (inferred, §4.1). The five known cases that fall in the unattached opening are reported (inconclusive)
   rather than forced.
3. **Counting.** Rows are nodes or module roots (dialogues once, with an instance column); the CSV replaces the auto-extracted
   rows of M30, M01, M02, M03 and S14 with the 142 curated first-two-hours rows (which group some nodes), and adds one definition-only
   row for each of the 526 cutscene-table entries no handler plays. Row totals in the CSV are therefore not the node totals of §1.1.
4. **Both/host rule** is WO-126A §5.1's, reported but not applied to the recommendation.
5. **The estimate** of scripted time (4 s a line, a floor for sequences) is a measure of scripted time, not play time; the constants
   are inferred and stated.
6. **"Interactive scene"** is treated as the engine's queue, not a content kind, because no data defines one.
7. **`SkipTime` handlers vs rows.** 63 handlers play skip-time cutscenes; 39 more rows are skill-teacher lesson managers; both are
   reported.
8. **Recommendations are recommendations.** The policy is the maintainer's; where two options are close (dialogue: triggerer-only vs
   a start gate with a placed partner), the table says why and names the risk.
9. **Retail** was not opened (out of scope); retail class of each piece is (inconclusive) unless WO-145 already states it.
10. **No external project is named**; no log line, name, path or address is quoted.

---

## 10. Corrections to earlier docs found on the way

* WO-145 §6 Q2: `WAITING_FOR_TWINS` is state 2 and comes **before** `WAITING_FOR_NPC_FREEZE_EDDA` (3).
* WO-126A §4.1 steps 2–3: positioning is **after** `OnCutsceneEnd`, not a pre-roll.
* WO-145 Q1: `Interrupt` does **not** raise `OnFinished`. WO-145 Q3: an internal State setter exists (`0x29C870`); equal values are a no-op.
* WO-126A §5: its 29 unresolved holders now resolve (four mechanisms): its headline 123 Ingame / 115 Fader / 23 SkipTime plus the
  29 become 138 / 124 / 24 (Text 30, Rendered 10) among the same 337 handlers; its table's column sums (119 / 113 / 21) did not
  reconcile with its own headline, and I could not rerun its method to say why.
* WO-141A §5: `MusicCutscene` is 3,063, not 3,062.
* WO-126A Appendix A omits `prepadeni_lakeMassacre` (its holder is chosen by a `Switch`); it is the M01 handler WO-126A could not resolve.
* WO-147/WO-114: the leash docs still say a host cutscene holds the leash; the code (WO-147) holds only a joiner's.
* Tester pages (TEST-0.42.2/0.42.5/0.42.7) say "a cutscene can stay black on the partner's screen until the host's next step":
  this is now evidenced (§4.3) and the cause named (positioning of paused copies; released by the host's save).
* The agent's "no-port" changes are a missing attribution, not a missing port (§5.2).
