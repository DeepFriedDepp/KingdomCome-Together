# WO-154 — 0.45.0, the first public beta: findings

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L1]…[L10]**, **[L9b]**, **[M1]**, **[JT]** a live solo session on this machine, on a throwaway copy of a save
(§9.3 lists them), with frames where it is visual; **[L-inst]** the install pass (§9.4); **[log]** read in the field logs (the maintainer's zips, never committed);
**[data]** read from the game's own files (read-only); **[unit]** / **[syn]** / **[native]** a unit, synthetic or
native test; **[code]** read in the code; **[not live]** no run in the game. The host and the joiner are the two
machines of the field sessions; no names, paths or addresses of theirs are in this file.

## The answer

**0.45.0 is built from what was proven.** Every phase's fix ran in the game on this machine, solo, against a
scripted partner or a synthetic host on throwaway saves, except the launcher's Phase 7 messages and the CONNECT gate
(unit-tested; they need a blocking Windows policy or clicks) and the keys of the mod menu (no keystrokes here). What
needs two people is `docs/TWO-PLAYER-CHECKLIST.md` §WO-154. The most important finding came last: **the field's hung
join is the game's own video player deadlocking at the loading screen**, reproduced and walked thread by thread; the
join now stops the menu's video first and tells the player plainly if the game still freezes.

| Phase | What was wrong (the evidence) | What 0.45.0 does | Proof | Ships |
|---|---|---|---|---|
| 1 Questing | Quest steps made on the game's AI threads carried no port: 49 of 54 host steps never applied on the joiner, none of his fight steps reached the host | Ports on every thread; a State each world derives is contested, not bounced; a joiner's AI steps judged as their net result | [L1] 0 of 35 records without a port; [L4] the evening's 43 host steps replayed: **0 failed to apply** | on |
| 2 The figure | The crime chain ran once, in a launch without the DLL; barks fell when an unbind cleared the avatar's contexts | Identity from spawn, kept through unbinds, fail closed; a knocked-down player's figure falls, lies, rises | [L2] 33 ok at spawn, kept through 3 unbinds; fall/lie/rise on the host's screen [L2] and the joiner's [L6] (frames) | on |
| 3 Fighting | Our own locks (WO-136's forced target, WO-139's pursuit) held enemies on the avatar; the scene guard's copy resume ran the "teleports" | The host's blow frees the NPC; a partner's death and respawn end his fights (120 s respite); fair crime; no copy resumed; end-combat in `mp_unstuck` | [L2] the first host blow cleared both locks, the NPC turned (frames); death ended every fight; [L4] scene rescued with no resume | on |
| 4 Joins | The bar's timer stops in the engine's hold; the agent gave up at 20 s and deleted the file; exact-build version text; host-seed saves | The game's own panel pushed through the hold; never given up while busy; plain reasons + "Join with a new character"; **the freeze**: menu video stopped first, a frozen load told plainly, the host released, the file kept | [L3] panel frames start/middle/end; [L3] busy probe; **[L7] the freeze reproduced and walked**; [L9b] the simulated freeze told; [JT] 10 real joins all loaded (5 with the stop-video step) | on |
| 5 Riding | The ridden horse was the last body on the 20 ms Lua path; the gallop clip fought the rider | The DLL writes the horse every frame from the rider's stream with the engine's own gaits; gait hysteresis | Host's screen [L5] and joiner's screen [L8]: frozen frames **50 % → 0 %**, clip mismatches ~60/s → ~0 | on |
| 6.1 Fast travel | the host's fast travel with the partner pulled along was the risky path | off in a session unless the host turns it on | [L6] joiner held + locked in the menu; [L10] host: held when a partner joins, given back by the menu and at the end | off |
| 6.2 Binds | 287 refusals were far bodies without physics; 34 cart sitters | bound anyway (entity-written / held in the seat) | [L6] both, before and after | on |
| 6.3 Skips | the "53 drops" were clock announces; a joiner's own skip set back silently | the set-back is told; the host's log tells announces apart | [L6] the line on screen (frame) | on |
| 6.4 Talk | the game's own reputation refusal | a wrong log line corrected | [log] | — |
| 6.5 Minigames | the rule also kept the knight's dice (a quest outcome) per machine | a State named after a minigame is shared; the module stays per player | [data] 21 States listed, 0 main-quest objectives | on |
| 6.6 Exit error | a waiting command met the closed reply channel | "not connected" | [unit] reproduces the field's order | on |
| 6.7 Voice | — | off unless chosen | [unit] [L6] | off |
| 7 Windows | 4551 and quarantine unexplained | plain messages; the relay reused | [unit] [log] | on |
| 7b Menu | dozens of console switches | the mod menu (Insert) | [L4] [L5] [L6] [L10] frames; keys not pressed | on |
| 9 Proof | — | gates, soaks (mod only), install pass, installer | §9 | — |

## Decisions made unattended

1. **"Join with a new character"** is offered only when this computer has a new game's first save to start the
   character from (`CanFresh`); otherwise the message says to start a new game in the Modding Tools first. No
   ready-made character from a save file is shipped (a save is the game's content).
2. **The join's save lock** for one test ([L3]): the test's own save lock blocked the host's join save, so it was lifted
   for that run; the extra saves went to the throwaway playline only, and all 368 real save files were checked
   unchanged after every session (they were, every time).
3. **`mp_scene_resume` off**: 0.44.0's resume of the host's copies for a stuck scene is what ran the end-of-cutscene
   FastForward ("teleports"); the rescue save runs at once instead, and no copy is resumed (proven [L4]).
4. **No safe write was found for a script-only quest value**; none was invented. A State without a port that differs
   is left to the next join, which loads the host's world exactly (the WO's own fallback); bool States are corrected
   with their own SetTrue/SetFalse.
5. **`mp_minigame_outcome` on**: the knight's dice in the attack on Nebakov is a quest outcome that WO-151's rule kept
   per machine by its name only; it is now shared like any quest State. Proven against the game's data and in tests;
   not run live (that quest lies far beyond the test save). Off restores WO-151's rule.
6. **The four journal objectives of the blacksmith's tutorial stay each player's own**: they are views over the
   tutorial module's States, and mirroring those States is exactly what looped the joiner at the anvil in WO-151. The
   WO's line "must not exclude any real quest objective" holds for every main-quest objective (0 of 626) and every
   tutorial objective outside that minigame module; these four follow each player's own anvil, by design.
7. **`mp_join_stopvideo` on**, though not proven to prevent the freeze: the freeze came once in 14 natural joins this WO (1 of 8 without the step, 0 of 6 with it) joins this
   WO, too rarely to measure a difference in the time; stopping the menu's video removes the component the stacks show
   racing, and it is proven harmless (every join with it loaded, and a manual load after it). Off restores 0.44.0's
   flow.
8. **`mp_join_frozen` on**: proven against a simulated freeze (the game suspended during a join's load: the freeze's
   own signature). A real freeze cannot be produced on demand.
9. **Fast travel off by default** for a co-op session (the WO's own line); the host turns it on in the menu.
10. **The menu key's alternatives (Numpad + and -) are kept**, Insert the default; no key was pressed in this session
    (no keystrokes, by the rules): the two-player checklist presses all of them.
11. **An NPC fighting an avatar is not "in combat" for the game** (`IsInCombatMode` false): the fair-crime exemption
    covers fights with the host's Henry (the field's brawl) and not fights with an avatar; recorded, not changed.
12. **The female NPC (6.4)** was the game's own refusal; only the misleading log line was changed.
13. **Voice**: a player who had chosen voice on before 0.45.0 turns it on once more (the old default cannot be told
    from a choice; `docs/WO-154-voice-decision.md`).
14. **A rider on a horse is not "parented"** in the engine (the DLL binds and writes it like any copy); the seated case
    was reproduced with a test NPC attached to a test horse the way a cart attaches its sitters (no cart sitter was
    loaded in the test world).
15. **The maintainer's Tier 3 result** (a WO-150 build on a machine with no Kingdom Come) never reached this session;
    `docs/* **The player's settings across an upgrade** (`tools/wo154/Test-SettingsUpgrade.ps1 -Launcher`, against the 0.45.0 candidate built from this tree): 0.43.0 and 0.44.0 (the first WO-150 build) installed into throwaway folders against a fixture Steam tree; a player's `settings.json` (a custom game path, changed relay and network choices, a voice choice from the old default, a Steam code, unknown keys — one nested —, its own key order, CRLF, four-space indents), `custom_servers.json`, `favorites.json` and `kcdmp-client.json` (a player name) written by hand; the 0.45.0 Setup installed over each: **every file byte-identical**, Setup's own verdict PASS; then the upgraded launcher started from the folder and ran 25 s (alive throughout; it loaded the hand-written servers and the custom master address, ran its read-only checklist, kept the saved game path — "settings.json keeps the saved path" — and exited cleanly): **every file still byte-identical. 24 / 24.** [L-inst]
* **WO-150's four cases**: `tools/wo150/Test-SetupCases.ps1` (a Setup compiled from the real script, isolated from any real install) and `tools/Test-InstallerDetect.ps1` run inside `tools/Build-Installer.ps1` and passed for the candidate and the shipping build (§9.5).
* **A finding about where to build**: a build or a clone under `%LocalAppData%` fails 16 of the 64 setup tests on this machine (the sandbox redirects that folder for this session's processes, and hard links made there do not read back); the candidate and the shipping build run in a fresh clone inside the repository's git-ignored `release\` folder instead.
* `docs/INSTALLER-TESTING.md`: the Tier 2 and 3 boxes rewritten to the current wizard (the page informs, the launcher's checklist finishes); the settings test added; Tier 3 records the maintainer's run on a machine with no Kingdom Come as awaited (Decision 15).ER-TESTING.md` records it as awaited instead of inventing it.
16. **The soaks ran on the mod version only**, on the maintainer's instruction during the WO ("Only run the soak on the
    mod version. No need to do a vanilla comparison."); `soak.py verdict --mod-only` records the waiver.

## E. The evidence, confirmed

Six read-only investigators read the zips by topic (quests, the set piece, the avatar's reaction, fights, joins,
riding and Phase 6); every number below was measured in a log, and where the work order's text differs, the log
wins (**differs**). Both machines ran 0.44.0 that evening; the joiner's clock read about 0.18 s **ahead** of the
host's (the brief had it behind).

| WO line | the logs | mark |
|---|---|---|
| the joiner's mirror applied 5 of 54 host changes; 27 `came through no port`; 29 of his own steps `not sent (no-port)` | yes. Every record without a port in every native log of both evenings was made **off the main thread** (AI behaviours: distance triggers, the duel's own logic, a farmhand's evening): the DLL attributed a port only on the main thread (§1.1) | [log] [code] |
| at the `cutscene`/`odd` markers the two worlds were in different states of one set piece | yes: each world ran the whole Moravian set piece by itself, the joiner's 16.2 s earlier; on the joiner the duel opened and closed in 106 ms with no hit; none of the duel's 32 States crossed the wire either way (all port-less). The lasting difference was a **crime**: the host's world judged the joiner's first punch an assault 2.9 s before its own quest switched on the fight's protections | [log] |
| the host's friendly fire made the joiner's avatar run the crime reaction, arm a skirmish and attack | **once**, 21:26:34–21:27:07, in a launch where **KCDMP.dll was not injected** (no native lever existed). With the DLL: 18 friendly-fire hits that evening, 0 barks, 0 arming, 0 attacks. A second weakness: every avatar dialogue fell in a window when a writer unbind had cleared the avatar's contexts | [log] |
| the joiner was ganged up on | yes: 3 pursuits by the host's guards on the avatar (2, 3 and 3 guards), each ended only by his death; **our own** WO-139 chase: a stop while he stood in a loot screen counted as fleeing, and the forced target held the guards on the avatar so they ignored the host | [log] |
| the Moravians' copies ran their quest behaviour `teleport` | **differs**: the lines record the scheduler interrupting `teleport` for the end-of-cutscene **FastForward**, which ran because WO-151's scene guard resumed 21 copies for 0.54 s | [log] |
| in the host's world the enemies kept targeting the avatar after his death and respawn | **differs** (mechanism): at every death the fights ended within ~0.1 s; the "keeping on" was the guards' pursuits and the forced target, re-armed by the crime record | [log] |
| the host's bar vanished during the 77 s hold | yes: the rows were drawn by a Lua timer the engine's hold suspends; they were on screen 1.7–2.3 s, during the world save only | [log] [code] |
| a join hung at the load; the agent gave up after 20 s and deleted the world file | yes, and **reproduced solo** ([L7]): the game's own video player deadlocks at the loading screen (§4.2) | [log] [L7] |
| "Your saves are from 1.5.5 … host 1.5.5" | the version check compared the exact build text: the Modding Tools' `1.5.5-release_1_5` against the regular game's `1.5.5-15315-release_1_5`. Another joiner's only Modding Tools saves were copies of the host's world (excluded as host seeds) plus a prologue save | [log] |
| ridden horse jittery | the one body still on the 20 ms Lua path: the DLL unbound the riding avatar, Lua moved the horse every second frame, its own brain ran, and a forced gallop clip fought the rider's idle ("segments do not have the same duration", 24–41 a second) | [log] |
| `refused: not-living` / `parented` | 287 of 324 refusals were bodies **without physics** (median 635 m away), 34 parented (caravan men in the cart stance), 3 ragdolls; the Lua path then wrote them, parented ones included | [log] |
| 53 time skips dropped | **differs**: 52 of 54 were the joiner's once-a-minute **clock announces**, not skips; the one real joiner skip was his part of a **shared** wait (applied correctly) | [log] |
| a joiner couldn't talk to a female NPC | **differs**: `tzel_woman_5` talked to him: it was the **game's own low-reputation refusal** (the host got the same refusal from her twice); one earlier request waited the engine's full 20 s on a busy seated copy while the host was talking to her original | [log] |
| `ChannelClosedException` and the DLL connection loss | at **every game exit** only, never mid-session: WO-153's reader-exit `Drop()` completes the reply channel under a command still waiting | [log] [code] |
| Application Control 4551 | yes: the injector was blocked by an Application Control policy (the host with no agent at 16:56–17:05) | [log] |

## 1. Phase 1 — questing together

### 1.1 The port attribution (fixed, proven live)

* **Cause.** The State setter's hook (`native/KCDMP/wo137.cpp`) recorded the port that changed a State only when the
  change ran on the main thread inside the node the DLL was tracking. Quest steps made by the game's AI run on
  **worker threads** (distance triggers, a duel's own logic, a farmhand's evening): across every native log of both
  evenings, every record without a port was a non-cascade change off the main thread [log]. A static read of
  ConceptModule.dll 1.5.5 confirmed the setter is reached with notify=1 only from the State variable's ExecuteNode
  (slot 33) [data].
* **Fix.** The executing node, its port and the change/apply depths are thread-local; a worker flag (`kFWorker`) and
  the thread id ride every `CHANGE` line; the time and punish gates run on every thread. [native]
* **Proof.** [L1] the host's own worker-thread changes carry ports: 0 of 35 records without one (12 main, 10 root and
  1 cascade worker, `celedinTakingBull SetOnWayWithBull ... root worker`). [L4] the evening's **43 host→joiner root
  steps** replayed into a real joiner game (a synthetic host serving a reseeded copy of the evening's save):
  **apply_fail = 0** (24 applied, 26 already in place, of 47 in). [L1] the evening's 45 joiner root steps replayed
  at a real host: 8 applied, 2 refused (the host's value was further on), 1 contested, the rest already; none
  unanswered.

### 1.2 What the port fix made possible, and the two guards it needed

* **A State each world derives from its own player** (how close he stands to something) would bounce once both
  directions mirror. `Wo154Contest`: when the other world's value was put on a State here and this world's own graph
  puts it straight back within 30 s, the State is **contested** for the session and neither world asks or corrects
  it any more (a story step never goes back like that). [unit] [syn]
* **Coalescing a joiner's AI-behaviour steps** (`mp_quest_coalesce`, on): [L1] the field's duel replayed step by step
  put the host's own duel into InProgress, which its own logic resolved Lost at once, and the joiner's Won 106 ms
  later was refused. A joiner's worker steps on one State are merged into their net result (0→1→2 is one Won, 0→1→0
  is nothing) before the host judges them. [unit]
* **Corrections that can't use a port** (2): bool States are corrected with their own SetTrue/SetFalse; the
  Quests/Testing State types are ignored; anything else without a port is left to the next join, which loads the
  host's world exactly (the quiet catch-up route the WO allowed). No safe way to set a script-only value with its
  consequences was found; none was invented. [code] [unit]
* **The joiner's own fight steps** (3) reach the host because they now carry ports (the duel's States were all
  port-less in the field). [L4] [code]

## 2. Phase 2 — the partner's figure never turns on you

* **Identity at spawn, kept.** The avatar's contexts (crime, assault, behavioural reactions, the quiet groups, the
  speech gate) are set the moment it spawns and are no longer cleared by a writer unbind (the field's second
  weakness). Without them (no DLL, or the isolate call failed) the avatar's brain is paused until they are on:
  it fails closed. [L2]
  `WO154-IDENTITY ... SET at spawn: 33 ok, 0 failed`, kept through three writer unbinds (no "avatar contexts
  cleared"); [L6] the same on the joiner's side for the host's avatar. It stays hittable: the knockdowns stay.
* **Knocked down together** (the maintainer's request), `mp_avatar_falls` (on): a player's knocked-down state (the
  vitals flag and the Downed bit, debounced 150/300 ms by the sender's DLL) makes their avatar fall on the other
  screen (`RagDollize`),
  lie, and stand when they do (`Revive`; `StandUp` does not lift a ragdoll [L1]). [L2] on the host's screen: fell,
  lay 64.6 s, rose (frames `L2_p2_lying`, `L2_p2_up4`); [L6] on the joiner's screen: fell, lay 40.6 s, rose (frames
  `L6_p2_lying`, `L6_p2_up`). The name badge stays at standing height while the figure lies (cosmetic).

## 3. Phase 3 — fighting together

* **3.1 The host is a real target** (`mp_host_target`, on). Two locks held enemies on the avatar: WO-136's forced
  target and WO-139's pursuit target. The host's own blow now clears the forced target on that NPC (read back), a
  guard already fighting the host is never pulled onto the avatar, and WO-136's arbitration counts host hits. [L2]
  `08:04:39.953` the host's first blow (the engine's own combat automation, no input) and `WO136-FORCED ... cleared
  (read back) -- the host struck it` in the same frame; the NPC fled from the host, was knocked down, and
  `TargetChanged ... (target Dude)`; `WO136-TARGET -> the host taken (read back 163 ms)`. The pursuit lock: first host
  blow at `08:08:14.313` and `WO139-PURSUE host-struck ... cleared (read back)`; the guard then attacked the host.
  Frames `L2_p3_before`, `hit2`, `hit5`, `hit9`.
* **3.2 Copies run no quest behaviour.** The field's "teleports" were the end-of-cutscene FastForward the scene
  guard caused by resuming copies. `mp_scene_resume` is **off**: a scene stuck at its end gets the engine's own rescue
  (a save request) at once and no copy is resumed. [L4] `zbranePanaSemina_playerSukDialog` not positioned at 6 s →
  "no copy is resumed (mp_scene_resume off); the rescue now" → the FastForward cancelled → positioned 6.0 s after
  its content → released.
* **3.3 A partner's death and respawn clear his fights** (`mp_guard_respite`, on). At the partner's death every
  forced target and threat on his avatar is forgotten (`WO136-FORGET`), his pursuits end whatever his crime record
  says, and for 120 s after he is up no guard stops or attacks him (30 s after his `mp_unstuck`). [L2] at the death
  `LEAVEFIGHT + WO136-FORGET 2 pairs`, `TargetChanged (target -)`, nothing re-engaged the avatar for 10 s+; at the
  respawn "up again -- no guard ... 120 s".
* **Crime judged on what happened** (`mp_fair_crime`, on). A murder only on the victim's death, once; an assault
  judged 5 s later and dropped if the victim is fighting by then (the field's quest brawl); a stop the guard turned
  into an attack while the player stood still is "attacked", not "fled" (the field's both "fled" results came from
  a player standing in a loot screen). [syn] Limit found [L2]: an NPC fighting an avatar reads
  `IsInCombatMode() false`, so the 5 s exemption covers fights with the host's Henry (the field's brawl), not fights
  with an avatar (recorded, not changed).
* **3.4 The joiner ends a stuck fight**: an end-combat step in `mp_unstuck` (and the menu's "I'm stuck") stops his own
  fight and asks the host to end every fight against his avatar (crime-ask kind 4). [L2] peer `crime endfights
  unstuck` → the host ended them: the avatar left the skirmish, `TargetChanged (target -)`, 0 blows in 8 s; the host's
  own `mp_unstuck fight` → `WO131-STOPFIGHT eid=0x0 -> called`.
* **3.5 The proof** the WO asked for is the [L2] run above: a scripted partner fought two NPCs in the host's world, the
  real host attacked one from behind (it reacted and turned, frames), the partner died (every fight against the
  avatar ended).

## 4. Phase 4 — joins that work, and look like they work

* **4.1 The host's bar through the whole join** (`mp_join_panel`, on). The engine's hold suspends every Lua timer
  (both chains froze in [L1]'s pause probe), but a console call still runs and the game's own tutorial panel still
  renders: [L1] `hud.ShowTutorial` pushed from the console showed and updated 40 % → 70 % during the pause. The agent
  now pushes the panel itself (`KCD2MP_JoinBarText`) on a stage change, at 25 % steps of the transfer and every
  15 s while the partner loads, flushing the panel's queue before each push, and hides it when the join ends.
  [L3] a 97.4 s hold with a synthetic joiner: pushes at the save (0/15/30/45 s), the send (0 %), the load
  (0/15/30 s), hidden at "join ready"; frames `L3_join_start` (save), `L3_join_load21` (the gilded panel "…is loading
  your world… 15 s", the ladder, "The world is paused until your partner is in"), `L3_join_end` (gone).
* **4.2 Never given up while the game is busy** (`mp_join_patient`, on). The agent waited 20 s for a log line that a
  buffered `kcd.log` delivers late, gave up, and deleted the world file while the game was still loading (the field's
  02:17:50). Now: past 20 s (90 s for the end of the load) the agent asks the game where it is
  (`KCD2MP_W154Where`: menu / world / loading); no answer within 4 s is "busy" and is waited out; the load is given up
  only on three answers from a responsive menu or world; the world file goes only when no load can read it. [L3] the
  probe through a reload: "world" in 31–106 ms, one "busy in 4011 ms" while the load held the main thread, "world"
  right after.
* **4.2 The hung join, found** [L7]. One of the joins of this WO froze the game for good, exactly like the field's:
  the menu's last frame on screen, the log silent for 9.5 minutes, the game using no CPU. No debugger is installed
  here, so a stack walker on Windows' own `dbghelp.dll` (scratchpad) walked every thread twice, 9 minutes apart,
  identical: the **main thread** waits for the render thread in `CD3D9Renderer::StartLoadtimeFlashPlayback` →
  `SRenderThread::FlushAndWait`; the **render thread** waits inside the game's video player
  (`CD3D9Renderer::RT_RenderVideoWH` → `bink2w64` `BinkDoFrameAsyncWait`) for its decode threads; the four Bink decode
  threads wait too. **No KCDMP frame is on any stuck stack** (the DLL's three threads idle in their own waits). The
  field's hung join has the same last main-thread line before the silence (WO-138's pass-through of the engine's own
  `VideoMode` unpause at the load's start). So: the game's own video player deadlocks when a load starts from the main
  menu while the menu's video plays — and a join always loads from the main menu, by console.
  * **The menu's video stopped first** (`mp_join_stopvideo`, on): the game's own `wh_ui_StopMovie` stops it (the
    menu stays, the background goes black: 159 changed pixels between two frames before, 0 after, frames
    `M1_menu_a/b`, `M1_stop_a/b`), and a load after it works ([M1]: "Gameplay started" in 65 s). The agent stops it,
    waits 1.5 s, then loads.
  * **A frozen load told plainly** (`mp_join_frozen`, on): busy (no console answer) for 90 s with under 1.5 s of the
    game's CPU in the last 60 s is the freeze (a slow load uses the CPU throughout: L6's took 46 s); the join is
    aborted to the host (it resumes at once) and the player reads: "Your game froze while loading your host's world
    (the game's own video player stopped at the loading screen; your own saves are untouched). Close the game - if it
    won't close, end it in Task Manager - then start it again and join again." The world file stays until no load can
    read it. [L9b] the game suspended 10 s into a join's load (the freeze's own signature): `the game is FROZEN -- busy for 90 s and it used 0.00 s of CPU in the last 60 s`; the launcher's status carried the plain line; the host: `abort load-failed after 186.8 s -- THE HOST RESUMES` (before its own 300 s timeout); the world file `still kept -- the game is busy`, and after the resume `removed ... now that the game answers from a world`. The first run [L9] found that the detector never fired: at the probe's real spacing (a look every ~6.05 s) its window kept 54.5 s of a required 55; fixed (a full window whatever the spacing) and tested at the live spacing.
  * **A host's abort during the load** no longer deletes the world file while the game is busy (it did in [L7]).
  * **The trials** (`jtrials.py`): 10 real joins from the main menu, alternating the old flow and the stop-video step: all 10 loaded ("Gameplay started" 57–63 s after the load command; the stop-video joins start 1.5 s later by design), the 368 real save files intact after each. No freeze in either flow: the freeze came once in 14 natural joins this WO (1 of 8 without the step, 0 of 6 with it), too rarely to show a difference (Decision 7).
* **4.3 CONNECT is gated** (the launcher): the host's until the agent reports him in the world and settled, the
  joiner's until the host is ready, the reason in plain words under the button. [unit] (`Wo154ConnectTests`) [not live]
* **4.4 Plain messages and "Join with a new character"**. The agent counts why a joiner has no usable save
  (`Wo154Rules.NoSaveReason`: no saves / only copies of this world / regular-game saves / another build / no Henry) and
  hands the launcher the reason and whether a new character can work (`/join-status` `Reason`, `CanFresh`); the
  launcher shows "Your saves are from the regular game, not the Modding Tools", "Your only Modding Tools saves are
  copies of this same world", and the button (`mp_join_henry fresh`). [unit] [L3] (the status carries both fields)

## 5. Phase 5 — riding, smooth on both screens

* **Cause** [log]: the ridden horse was the last body on the 20 ms Lua path (the DLL unbound the riding avatar, Lua
  moved the horse every second frame, its brain ran, a forced gallop clip fought the rider's idle).
* **Fix** (`mp_ride_native`, on): once the avatar is mounted, its horse is a puppet like any other: brain held, the DLL
  writes it every frame from the **rider's** stream (fed under the horse's name, the saddle height taken off), and
  the DLL's gait gives it the engine's own locomotion; the gallop clip and the Lua writes stand down. **Gait
  hysteresis** (`mp_gait_hysteresis`, on): the gait class changes only past a band (a trot on the 2.4 m/s boundary
  kept flapping): [native] the field's trot sequence flips the class once instead of 7 times.
* **Proof, the host's screen** [L5] (a scripted rider on a proxy horse, straight ping-pong at 3.0 and 7.5 m/s, traced
  per frame, `tools/wo118/jitter118.py`): before (both off) **50.1 % frozen frames**, step 5.26 ± 6.87 cm (max 42.6),
  4,281 clip-mismatch lines in 63.5 s (67/s); after (both on) **0.0 % frozen**, step 5.28 ± 2.90 cm (max 19.1), 5
  mismatch lines in 70.8 s (at the mount). An adopted **world** horse: 0 % frozen, 3.00 ± 0.04 m/s. Frames
  `L5_before_gallop` (an upright rider on a forced gallop clip), `L5_after_gallop` (the rider leans with the horse).
* **Proof, the joiner's screen** (the host's mount: a world horse its NPC stream also carries, adopted by the host's
  avatar): [L8] a synthetic host whose own NPC stream carries `tsem_horse_2` rides it on the joiner's screen (the joiner's copy a host puppet first; the host's avatar adopts it: `WO151-RIDE avatar takes npc=tsem_horse_2 puppet=true`, its NPC stream ignored). Before (both off): **50.1 % frozen frames**, step 5.56 ± 7.21 cm (max 38.2), 164 steps over 3× the mean, 2,612 clip-mismatch lines in 47 s (~56/s). After (both on, `WO154-RIDE native on ... saddle_m=1.51`, `native=bound`): **0.0 % frozen**, step 5.39 ± 2.86 cm (max 16.5), 2 steps over 3× the mean, render = written, 9 mismatch lines in 46 s (at the mount). Frame `L8_after_gallop` (the host's figure galloping on the world horse).

## 6. Phase 6 — the rest of the session list

* **6.1 Fast travel off by default** (`mp_fast_travel`, the host's, **off**): while a co-op session is up the game's
  own switch `wh_pl_FastTravelEnabled` is held at 0 on every machine (the host's choice reaches the joiners), with a
  one-line notice; the host can turn it on in the menu; the session's end gives the switch back. [L6] the joiner's
  menu shows "Fast travel: Off [set by the host]" and `cvar_held=true`; [L10] on the host: no partner → the switch untouched (1); a partner joins → held at 0 with the notice "Fast travel is off in this co-op session (you can turn it on in the mod menu)"; the host's menu on → 1, off → 0; the partner gone → given back (1). Nothing desynced could be
  reproduced solo (the risky path is the host's fast travel with the partner pulled along; it is now opt-in).
* **6.2 Far and seated copies on the native writer** (`mp_bind_far`, on): a body with no physics yet is bound and
  written through the entity until it has physics; a body parented to a cart is bound and held in its seat, written
  once it is free. [L6] far: `tvez_man_13` refused "not-living, no physics" with the switch off (the field's case),
  `tvez_man_14` bound "physics=none-yet" with it on, and moved 10.3 m at 1.5 m/s with no physics; seated (no cart
  sitter was loaded, so a test NPC was attached to a test horse the way a cart attaches its sitters; a rider on a
  mounted horse is **not** parented): off → "refused reason=parented", on → "seated=held"; the stream moved 1 m/s and
  the body held its seat; detached, it was written to the stream's spot at once (1747.8 → 1757.1).
* **6.3 A joiner's own wait or sleep, set back, is told** (`mp_skip_tell`, on). The field's "53 dropped skips" were
  52 clock announces and one part of a shared wait (§E); the host's log now tells announces from skips. A joiner's own
  skip that ran anyway (the vote off, a way the gate does not hold) is set back to the host's clock the moment it
  ends: now the set-back that follows his own non-shared skip within 60 s shows "Time was set back to your host's
  clock: in a shared world only the host's clock moves time. Ask your host to wait or sleep, and it passes for you
  both." A set-back after a cutscene's own time jump (the tutorial evening's 2 h at the lake) is not his doing and
  stays quiet. [L6] the joiner's own 2 h wait (the game's own wait, started through the DLL's skip op) → set back
  7,141 s → the line on screen 8 s later (frame `L6_skip_told`). [unit]
* **6.4 The female NPC**: the game's own low-reputation refusal plus one request the engine held its 20 s on a busy
  seated copy; not ours. The agent's line claiming every cancel happened "at once, nothing waited" was wrong for that
  request and no longer says so. [log]
* **6.5 The minigame-tutorial exclusion** against the game's own quest data (Scripts.pak, 21,649 quest files, 8,919
  States, 1,626 journal objective views) [data]: it kept per machine **21 States**: the 16 States of the blacksmith's
  tutorial module (`kovar…blacksmithing_minigame.*`), the four tutorials' progress drivers (Blacksmithing,
  Masterstrike, Combat, Alchemy `TutorialProgress`), and — by its name only — **the knight's dice in the attack on
  Nebakov** (`…kostky_s_rytirem.dice_minigame`, a DiceState None/InProgress/Won/Lost that the quest branches on). That
  one is a real quest step: a State whose **own** name ends in `_minigame` is now the quest's and shared; only a
  `_minigame` **module** stays each player's (`mp_minigame_outcome`, on). **No main-quest objective** is excluded (0 of
  626); the combat and alchemy tutorials' journal objectives are views over their own step States, which are shared.
  **Four journal objectives** of the blacksmith's tutorial (heat the forge, to the anvil, finish the falchion, quench
  the blade) are views over the module's States and so follow each player's own anvil (Decision 6). [unit]
* **6.6 The DLL connection loss and `ChannelClosedException`**: every loss in the field was the game's exit; the
  exception was a command still waiting when WO-153's reader-exit `Drop()` completed the reply channel. It now comes
  back "not connected". A test reproduces the field's exact order (reader exited, connection lost, the exception)
  against a test-only pipe and passes with the fix (and fails without it). [unit]
* **6.7 Voice chat off by default** (the launcher stream): voice is on only when the player chose it
  (`VoiceChatChosen` and `VoiceChatEnabled`, `docs/WO-154-voice-decision.md`): a new install, and anyone who never
  chose, starts with no microphone opened, nothing captured, sent or started; the launcher passes `--voice` or
  `--no-voice` explicitly; the mod menu's switch is the same choice. The old default `true` cannot be told from a
  chosen one, so a player who had chosen voice on turns it on once more (recorded, accepted). [unit] [L6] (the menu
  shows "Voice chat: Off")

## 7. Phase 7 — Windows blocking the mod (the launcher)

* A blocked injector (Win32 **4551**, "An Application Control policy has blocked this"), a quarantined or missing
  mod file: recognised at every start (`StartChecked`) and told in plain words by the file's name — what happened,
  what the player can do, and Smart App Control's catch (once turned off it cannot be turned back on without
  resetting Windows) (`LaunchBlock`). [unit] (`Wo154BlockTests`) [log] (the field's 4551) [not live]
* A second launcher whose relay ports are held by an earlier launcher's relay **reuses** it (same build) or replaces
  it; another program's ports are never touched (`RelayReuse`). [unit] (`Wo154RelayTests`) [not live]
* **Code signing** is not in this WO: it needs an OV or EV certificate the maintainer buys; what signing would
  cover and why it does not by itself pass Smart App Control: `docs/WO-154-code-signing.md`.

## 7b. Phase 7b — the mod menu

* One in-game menu (**Insert**; PgUp/PgDn choose, End changes; Insert or Esc closes) instead of the `mp_*` switches:
  Display (name badges, the ping and clock line, a clean screen), Gameplay (friendly fire, crime, fast travel, keeping
  together, sleeping and waiting together, the whistle, the partner's herb picking), Voice and keys (voice chat, the
  menu key: Insert, Numpad + or Numpad -), Help ("I'm stuck", "Something's wrong here", the version, the connection).
  Plain names, the current value, a one-line explanation. `docs/MOD-MENU.md`.
* The host's settings show on the joiner's screen as "[set by the host]", locked. Remembered in the mod's own
  `mod-settings.json` (never a game save); never opens in a cutscene, a dialogue, a load or the game's own menus; the
  console commands keep working.
* **Proof**: [L4] the menu open on the joiner with the host's settings locked, the clean screen on and off (0 vs 492
  bright pixels in the ping corner), a choice saved (`MP-MENU-SAVE NameBadges=off`); [L5] restored after an agent
  restart (`MP-MENU-RESTORE 1 saved choice(s)`); [L6] the joiner's menu with a host's real values (friendly fire On,
  crime Shared, fast travel Off, each "[set by the host]", frame `L6_menu_hostvalues`); [L10] the host's menu unlocked with the session's values, "Version: 0.45.0", "Connection: Hosting -- your partner is here" (frame `L10_host_menu`). **Not proven
  live**: pressing the keys themselves (no keystrokes from this session, by the rules); the keys are in our keybind
  patch like the dice keys, and the game's own key files bind none of Insert, PgUp, PgDn, End, Numpad + or Numpad -
  (WO-6 once noted the Numpad operator keys as debug toggles; only `*` and `/` are bound in the game's debug map).
  The two-player checklist presses them (§WO-154 item 122).

## 8. Phase 8 — the public release's docs

* `docs/releases/RELEASE-NOTES-0.45.0.md` — what is new since 0.42.x (the setup that does itself, the mod menu,
  questing and fighting together, joins, riding), what changed (fast travel and voice off unless turned on), the
  fixes, and the known issues honestly: shared cutscenes (the next big update), the tutorial the roughest part, lip
  sync (the Modding Tools build lacks the data), the herb animation off by design, fast travel off, conversations not
  heard by the other player, a frozen load's restart, the blacksmith's tutorial per player.
* `docs/QUICKSTART.md` and `README.md` checked against the build: the partner waits at the main menu; the start-save
  advice (a save is for the host; the partner brings a character from a Modding Tools save of another world or uses
  "Join with a new character"); the mod menu.
* `docs/TEST-0.45.0.md` (the tester page), `docs/TWO-PLAYER-CHECKLIST.md` §WO-154 (items 111–126, the tester page's
  markers registered in the mod), `docs/DISCORD-TEXT.md` (the 0.45.0 announcement), `docs/MOD-MENU.md` (the menu's
  page, from the menu stream); every player-facing page points to the menu and its key, the console commands stay
  documented for advanced players. `docs/WO-154-menu-findings.md`, `docs/WO-154-voice-decision.md` and
  `docs/WO-154-code-signing.md` are folded in here (§6.7, §7, §7b) and kept as the streams' own records.
* `docs/* **The player's settings across an upgrade** (`tools/wo154/Test-SettingsUpgrade.ps1 -Launcher`, against the 0.45.0 candidate built from this tree): 0.43.0 and 0.44.0 (the first WO-150 build) installed into throwaway folders against a fixture Steam tree; a player's `settings.json` (a custom game path, changed relay and network choices, a voice choice from the old default, a Steam code, unknown keys — one nested —, its own key order, CRLF, four-space indents), `custom_servers.json`, `favorites.json` and `kcdmp-client.json` (a player name) written by hand; the 0.45.0 Setup installed over each: **every file byte-identical**, Setup's own verdict PASS; then the upgraded launcher started from the folder and ran 25 s (alive throughout; it loaded the hand-written servers and the custom master address, ran its read-only checklist, kept the saved game path — "settings.json keeps the saved path" — and exited cleanly): **every file still byte-identical. 24 / 24.** [L-inst]
* **WO-150's four cases**: `tools/wo150/Test-SetupCases.ps1` (a Setup compiled from the real script, isolated from any real install) and `tools/Test-InstallerDetect.ps1` run inside `tools/Build-Installer.ps1` and passed for the candidate and the shipping build (§9.5).
* **A finding about where to build**: a build or a clone under `%LocalAppData%` fails 16 of the 64 setup tests on this machine (the sandbox redirects that folder for this session's processes, and hard links made there do not read back); the candidate and the shipping build run in a fresh clone inside the repository's git-ignored `release\` folder instead.
* `docs/INSTALLER-TESTING.md`: the Tier 2 and 3 boxes rewritten to the current wizard (the page informs, the launcher's checklist finishes); the settings test added; Tier 3 records the maintainer's run on a machine with no Kingdom Come as awaited (Decision 15).ER-TESTING.md`: the stale pre-WO-150 Tier 2/3 boxes rewritten to the current wizard; the settings
  upgrade test; Tier 3 records the maintainer's run as awaited (Decision 15).

## 9. Phase 9 — the proof and the build

### 9.1 Gates

On the final tree (the code of the shipping build): **native 388, agent 1,088, relay 62, setup 64, the launcher's
build, the three static checks (console placeholders 7, Lua locals 6, native guards 7) and all 45 synthetic suites —
0 failed**. The same gates run again inside `tools/Build-Installer.ps1` for the shipping build (§9.5).

### 9.2 The two soaks (the mod only, window focused)

Both on the throwaway save the 0.43.0 soak used (playline4/autosave111), 10 minutes each, the game as host with
the relay and its agent, the WO-151 scene (three bystanders 3 m around the player from the start, an AI fight beside
them from minute 3, a new pair whenever one goes down or stalls). The window took the foreground by itself after the
load and was not touched by this session; the maintainer kept it focused. **The comparison with the game without the
mod was not run: the maintainer's instruction mid-run ("Only run the soak on the mod version. No need to do a vanilla
comparison.")**; `soak.py verdict --mod-only` records the waiver (Decision 16).

| | soak 1: the mod | soak 2: the mod + a joined partner through the fights |
|---|---|---|
| the first 2 minutes | 71.6 FPS | 72.4 FPS |
| the fights (minutes 3–8) | 72.7–73.8 | 72.0–73.2, but for two dips below |
| the last 2 minutes | 72.9 FPS | 72.6 FPS |
| the stat stack | 0 in 55 of 56 rows; one row 2 (see below) | 0 in all 53 rows (the settled read) |
| `FAULT` lines | 0 | 0 |
| the DLL's own cost (`FRAME ours_us_mean`) | 0.65–0.67 ms a frame | 0.65–0.67 ms a frame, the dips included |
| fight pairs | 4 | 6 (the partner landed an attributed hit on each attacker every 15 s) |

* **Soak 2's dips, explained.** 222–243 s (57.9 / 43.8 / 48.2 FPS, then 73.2): the maintainer submitted a chat message
  at 14:16:19.6, inside that window (14:15:52–14:16:33) — the game was not the focused window while it was typed
  (an unfocused game caps its own frame rate, WO-151 §0.3); the DLL's world meter logged the re-activation's long
  frames at 14:16:26 and 14:16:31, and the next row was back at 73.2. One more row at 393 s (52.5 FPS) has the same
  signature (a long frame logged at 14:18:58, the next row 73.0). The DLL's own per-frame cost stayed flat through both
  (672 µs that minute against 646–668 around it), and soak 1 — the same scene without the partner — had no dip at all.
* **Soak 1's one stack row.** The reader took a single, unsynchronised snapshot of the main thread's stat stack, which
  is pushed and popped during every frame's stat calls: one read in 56 caught it mid-frame (2; the next row 0). A leak
  stays up in every read (0.42.5: thousands). The reader now takes the smallest of a few reads milliseconds apart
  (`statstack.Reader.settled_depth`, the raw first read noted when it differs); soak 2 ran with it: 0 in every row.
* **The record** (`tools/perf/soak-record.json`): soak 2, PASS (the last 2 minutes within 10 % of the first 2, the stack
  0 in every row, no `FAULT`, enough rows, the code committed); the run files are `tools/perf/runs/mod045*.{json,csv,md}`.
  The maintainer accepted both soaks ("Im happy with the soak test, finish up").

### 9.3 The live passes (solo, both roles)

| run | role | what it proved |
|---|---|---|
| L1 | host | worker-thread ports (0 of 35 records without one); the engine's hold: timers stop, console Lua and the tutorial panel run; knockdown levers (`RagDollize`, `Revive`); the unbind clearing contexts (found, fixed) |
| L2 | host + scripted partner | identity at spawn and through unbinds; fall/lie/rise; the host's blow frees both locks and the NPC turns; the partner's death ends every fight; respite; `mp_unstuck` end-fights |
| L3 | host + synthetic joiner | the join panel through a 97.4 s hold (frames); the where-probe answers busy during a load |
| L4 | joiner of a synthetic host | the 43 host steps applied (0 failed); the stuck scene rescued with no copy resumed; the menu locked on the joiner; clean screen; a choice saved |
| L5 | host + scripted rider | riding before/after on the host's screen (50 % → 0 % frozen); the adopted world horse; the menu's choice restored |
| L6 | joiner of a synthetic host | the host's figure falls/lies/rises on the joiner; the menu with a host's real values; fast travel held on the joiner; the 6.3 line; far and seated binds |
| L7 | joiner | **the freeze**, reproduced and walked thread by thread; the host-abort deleting the file (found, fixed) |
| M1 | the game alone (no DLL) | the menu's video stopped by `wh_ui_StopMovie`; a load after it works |
| JT | joiner ×10 | 10 real joins, the old flow and the stop-video step alternating: all loaded, the real saves intact after each |
| L8 | joiner of a synthetic host | riding before/after on the joiner's screen: the host's mount (a world horse its stream also carries) |
| L9 | joiner | the simulated freeze: the detector's window never filled at the live spacing (found, fixed); L9b with the fix: FROZEN at 90 s busy, the host released at 186.8 s, the plain line in the launcher's status, the file kept and removed after the resume |
| L10 | host + scripted partner | 6.1 on the host (held when a partner joins, the menu switch, given back at the end); the host's menu frame |

What needs two players: `docs/TWO-PLAYER-CHECKLIST.md` §WO-154 (items 111–126).

### 9.4 The install pass

* **The player's settings across an upgrade** (`tools/wo154/Test-SettingsUpgrade.ps1 -Launcher`, against the 0.45.0 candidate built from this tree): 0.43.0 and 0.44.0 (the first WO-150 build) installed into throwaway folders against a fixture Steam tree; a player's `settings.json` (a custom game path, changed relay and network choices, a voice choice from the old default, a Steam code, unknown keys — one nested —, its own key order, CRLF, four-space indents), `custom_servers.json`, `favorites.json` and `kcdmp-client.json` (a player name) written by hand; the 0.45.0 Setup installed over each: **every file byte-identical**, Setup's own verdict PASS; then the upgraded launcher started from the folder and ran 25 s (alive throughout; it loaded the hand-written servers and the custom master address, ran its read-only checklist, kept the saved game path — "settings.json keeps the saved path" — and exited cleanly): **every file still byte-identical. 24 / 24.** [L-inst]
* **WO-150's four cases**: `tools/wo150/Test-SetupCases.ps1` (a Setup compiled from the real script, isolated from any real install) and `tools/Test-InstallerDetect.ps1` run inside `tools/Build-Installer.ps1` and passed for the candidate and the shipping build (§9.5).
* **A finding about where to build**: a build or a clone under `%LocalAppData%` fails 16 of the 64 setup tests on this machine (the sandbox redirects that folder for this session's processes, and hard links made there do not read back); the candidate and the shipping build run in a fresh clone inside the repository's git-ignored `release\` folder instead.
* `docs/INSTALLER-TESTING.md`: the Tier 2 and 3 boxes rewritten to the current wizard (the page informs, the launcher's checklist finishes); the settings test added; Tier 3 records the maintainer's run on a machine with no Kingdom Come as awaited (Decision 15).

### 9.5 The installer, the privacy sweep, the tag

The shipping installer is built from a fresh clone of `origin/main` at the commit that adds this page, after it is
pushed; its record (the size and sha256, the gates inside the build, the privacy sweep of the payload, the tag) is the
next commit.

## Self-review — every item of the WO

"Fixed and proven" = ran in the game with the result shown; "behind a switch" = shipped with a switch defaulted as
stated; "not done" = with the reason. Every new behaviour has a switch (named).

| # | WO item | status | evidence |
|---|---|---|---|
| 0 | push WO-150/153 leftovers; `v0.43.0` tag if missing | done (the tag already existed) | — |
| 1.1 | record the port for every State change both worlds make | fixed and proven | [L1] 0/35 port-less; [native] |
| 1.2 | corrections without a port: a safe way, or a quiet catch-up | done: bool States corrected by their own ports; the rest left to the next join's exact load (no safe script write found) | [unit] [code], Decision 4 |
| 1.3 | the joiner's own fight steps reach the host | fixed and proven (they carry ports now) | [L4] [L1] replays |
| 1.4 | replay the evening's failing changes: apply-fail 0 or named | proven: **0** failed to apply (43 host steps) | [L4] |
| 2a | an avatar never runs crime/assault/behavioural reactions, never arms a skirmish; stays hittable | fixed and proven (identity at spawn, kept, fail closed) | [L2] [L6] |
| 2b | a knocked-down player's figure falls, lies, rises (both directions) — `mp_avatar_falls` | fixed and proven both directions | [L2] [L6] frames |
| 3.1 | the host is a real target — `mp_host_target` | fixed and proven | [L2] frames, read-backs |
| 3.2 | copies never run quest behaviours — `mp_scene_resume` off | fixed and proven (the teleports were the resume's FastForward) | [L4] |
| 3.3 | death/respawn clear every fight; no hunting a respawned partner — `mp_guard_respite` | fixed and proven | [L2] |
| 3.4 | an end-combat step in `mp_unstuck` | fixed and proven | [L2] |
| 3.5 | the proof (partner fights two NPCs, host attacks one from behind, partner dies) | proven | [L2] frames |
| 4.1 | the host's bar through the whole join (frames start/middle/end) — `mp_join_panel` | fixed and proven | [L3] frames |
| 4.2 | never give up while busy; never delete the file while a load may run; abort only on a responsive menu — `mp_join_patient` | fixed and proven; **the freeze found** [L7] and handled: `mp_join_stopvideo` (on, Decision 7), `mp_join_frozen` (on, proven [L9b]) | [L3] [L7] [M1] [L9b] [JT] |
| 4.3 | Connect gated, the reason in plain words | built and unit-tested; not live (needs the launcher's clicks) | [unit] |
| 4.4 | plain messages + "Join with a new character" | built and unit-tested; the agent's reasons live in `/join-status` | [unit] [L3] |
| 5 | riding smooth on both screens, measured before/after — `mp_ride_native`, `mp_gait_hysteresis` | fixed and proven on both screens | [L5] [L8] frames + traces |
| 6.1 | fast travel switch, default off — `mp_fast_travel` | done and proven (host and joiner); no desync reproduced solo | [L6] [L10] |
| 6.2 | not-living / parented binds — `mp_bind_far` | fixed and proven (far: real copies; seated: a reproduced attachment) | [L6] |
| 6.3 | tell the joiner when his wait or sleep is refused — `mp_skip_tell` | fixed and proven (the "53" were clock announces) | [L6] frame |
| 6.4 | the joiner and the female NPC | found: the game's own refusal; a log line corrected | [log] |
| 6.5 | list every State the minigame exclusion excludes; no real objective — `mp_minigame_outcome` | listed (21) and narrowed (the knight's dice shared); 0 main-quest objectives; the blacksmith's four tutorial objectives per player (Decision 6) | [data] [unit] |
| 6.6 | the DLL connection loss and `ChannelClosedException` | fixed (exit-time only; reproduced in a test) | [unit] |
| 6.7 | voice off by default, one click away | done (chosen-only) | [unit] [L6] |
| 7 | 4551 / quarantine / missing file explained; Smart App Control's catch; relay port reuse; code-signing notes | built and unit-tested; not live | [unit] [log], `docs/WO-154-code-signing.md` |
| 7b | the mod menu (one key, plain names, groups, host-locked settings, remembered, safe, console kept) | done and proven except pressing the keys | [L4] [L5] [L6] [L10] frames |
| 8 | release notes, known issues, quick start/README, tester page, Discord text, docs point to the menu | done | §8 |
| 9.1 | every gate | all green: native 388, agent 1,088, relay 62, setup 64, launcher, 3 static, 45 suites | §9.1 |
| 9.2 | two soaks, window focused (the mod only, by the maintainer's instruction) | done: both 10-minute soaks of the mod hold 71–74 FPS, no FAULT, stack 0 (settled); the partner soak's two dips were the window losing focus; the vanilla comparison waived by the maintainer | §9.2 |
| 9.3 | a full solo live pass, both roles; the two-player list | done | §9.3, checklist §WO-154 |
| 9.4 | the install pass (WO-150's four cases; the upgrade over 0.43.0 byte for byte); Tier 3; stale boxes | done: 24/24 (0.43.0 and 0.44.0, the launcher alive); the four cases in the installer's own gates; Tier 3 awaited (Decision 15); boxes rewritten | §9.4 |
| 9.5 | the self-review | this table | — |
| 9.6 | the installer from a fresh clone of `origin/main`; the privacy sweep; the tag; the push | see §9.5 | §9.5 |
| 9.7 | `docs/WO-154-findings.md` (answer first), `docs/WO-154-progress.md` | done | — |

## What a follow-up needs

* **Cutscenes shared** (the next big update): one plays per player today.
* **The game's video-player freeze** at a load from the main menu: the stop-video step removes the racing component;
  if a frozen load is ever seen again with it on, the stacks (`stackwalk`) and the log of that join are the evidence.
* **The menu's keys pressed live** and the joiner's half of the fast travel pull, the caravan sitters, the
  knight's dice and the blacksmith's tutorial with two players (§WO-154 items).
* **A small flinch on an ordinary friendly-fire hit** is not shown (the knockdown is).
* **An NPC fighting an avatar** reads not-in-combat to the game: the fair-crime exemption does not cover it.
* **Code signing** needs the maintainer's certificate (`docs/WO-154-code-signing.md`).
* **The maintainer's Tier 3 run** (no Kingdom Come installed) to be written into `docs/* **The player's settings across an upgrade** (`tools/wo154/Test-SettingsUpgrade.ps1 -Launcher`, against the 0.45.0 candidate built from this tree): 0.43.0 and 0.44.0 (the first WO-150 build) installed into throwaway folders against a fixture Steam tree; a player's `settings.json` (a custom game path, changed relay and network choices, a voice choice from the old default, a Steam code, unknown keys — one nested —, its own key order, CRLF, four-space indents), `custom_servers.json`, `favorites.json` and `kcdmp-client.json` (a player name) written by hand; the 0.45.0 Setup installed over each: **every file byte-identical**, Setup's own verdict PASS; then the upgraded launcher started from the folder and ran 25 s (alive throughout; it loaded the hand-written servers and the custom master address, ran its read-only checklist, kept the saved game path — "settings.json keeps the saved path" — and exited cleanly): **every file still byte-identical. 24 / 24.** [L-inst]
* **WO-150's four cases**: `tools/wo150/Test-SetupCases.ps1` (a Setup compiled from the real script, isolated from any real install) and `tools/Test-InstallerDetect.ps1` run inside `tools/Build-Installer.ps1` and passed for the candidate and the shipping build (§9.5).
* **A finding about where to build**: a build or a clone under `%LocalAppData%` fails 16 of the 64 setup tests on this machine (the sandbox redirects that folder for this session's processes, and hard links made there do not read back); the candidate and the shipping build run in a fresh clone inside the repository's git-ignored `release\` folder instead.
* `docs/INSTALLER-TESTING.md`: the Tier 2 and 3 boxes rewritten to the current wizard (the page informs, the launcher's checklist finishes); the settings test added; Tier 3 records the maintainer's run on a machine with no Kingdom Come as awaited (Decision 15).ER-TESTING.md`.
