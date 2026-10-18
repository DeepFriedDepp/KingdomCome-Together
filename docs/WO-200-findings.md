# WO-200 — One world, one story: actions, not results (0.61.5)

> **Actions go to the authority; values come back.** The host's world decides. The joiner's game shows, and asks; it
> never decides. What the host must simulate is sent as an *action*. What the joiner only displays is sent as a
> *value*. Never the other way round.

Code: native `wo200.cpp` / `wo200.h` / `wo200_rules.h` (the two gates, the replay, the research probes), `wo137.cpp` (closure-marked
changes, the talker flag on an apply), `wo131.cpp` (stamina in the health follow), `wo141.cpp` (copies kept neutral), `wo189.cpp` and
`motion.cpp` (two read guards); agent `Wo200.cs` (the rules, the quest graph's closures), `GameBridge.Wo200.cs` (capture, replay, quiet
clocks, scoped twins' bound, Act IV's dialogue kinds), `GameBridge.Wo200Values.cs` (health, stamina, mount, clock, the clinch end, the
noise), `GameBridge.Wo200Items.cs` (quest items), hooks in the WO-121/125/137/143/160/164/179/191/197 bridges; protocol (append-only)
`QuestAsk` kind 5 `OutPortReplay`, `QuestHost` kinds 7 `ReplayAck` and 8 `ItemCount`, activity-extra kinds 5 `Vitals` and 6 `Mount`,
`W164` kinds 18 `Clock` and 19 `Noise`, DLL ops 70–75; `kdcmp.lua` (the switches, scoped twins, quest items, stand-ins, the host's
values, riders, save waits, noise). Tests: native `wo200_tests`, agent `Wo200Tests`, `tools/Test-WO200Synthetic.ps1`.

All live evidence is one machine: the Modding Tools game as the host of a synthetic joiner or the joiner of a synthetic host, through a
local relay, on the `playline4` throwaway (copies of the field host's saves at Semine's yard, and Troskowitz by day). The saves folder was
backed up byte-equal first. Nothing here is two-player evidence.

## 1. The two gates

* **Gate 1, the out-port tap (`WO200-TAP`).** One function: `C_ActiveTriggerPort::Trigger`, slot 15 of the four trigger-port classes that
  share it (Active, Output, Interface, Static; a dialogue's ports are Interface ports). Found by RTTI and verified by what the slot holds;
  the slot is swapped, nothing is patched inline. It sees a node's out-port before its pulse runs downstream: it captures this player's
  conversation steps, drops the joiner's quest clocks, and continues a scene waiter the replay marked.
* **Gate 2, the node gate (`WO200-GATE`).** `C_Node::Execute` (the export every node class calls), hooked through the hook-prologue
  registry. 24 node classes in seven modules are classified by RTTI name. It decides by class, by the node's soul input (the `player`
  alias or another soul, read through the port's own value) and by membership of a replay closure. An active effect's Execute (a switch-off)
  is never refused.

**O7 (can a dialogue out-port be fired directly?): yes.** Firing `jindra_ma_nasledovat_semina_a_suka` on the host's own node runs its
closure: the scene waiter is enqueued and the departure's States follow. The direct path won; no edge-target pulsing is needed. A waiter
whose scene plays only on the joiner is finished on the host by the joiner's continuation (the same closure id).

## 2. What runs where (the switches; all on unless said)

| switch | what it does |
|---|---|
| `mp_s1_replay` | the joiner's conversation out-ports go to the host as actions and run on the host's own nodes, in order, behind any catch-up or load; the joiner's requests for States inside that closure are held 5 minutes (`WO200-SUPPRESS`); a refused replay falls back to the old requests |
| `mp_s1_route` | rewards, money and body effects of a replayed conversation reach the talker only (host: not run for its own player; joiner: not run in his mirror of the host's conversations); `SaveGame` runs |
| `mp_qitem_mirror` | quest-item counts compared with the host's (see §5) |
| `mp_joiner_quiet_clocks` | the joiner's quest timers and time-of-day watches never fire in a joined world; each dropped fire is compared with the host's mirror for 30 s (`WO200-QUIET agreed / host-differs`) |
| `mp_joiner_no_puppet_quests` | no quest behaviour, entity context, scheduler link, teleport interrupt or health write runs on a host copy on the joiner |
| `mp_joiner_scoped_twins` | every copy in this player's own conversation runs for exactly its lifetime (the host holds its NPCs); WO-179's "no twins" bound counts from the engine's start attempt |
| `mp_copy_health_from_host` | the joiner applies no blow to a copy; health and stamina are the host's |
| `mp_rider_from_host` | a copy rides exactly when the host says so |
| `mp_copy_neutral_always` | a copy demands nothing of the planner, every tick |
| `mp_world_clock_from_host` | the joiner's clock is slewed to the host's (at most 25 %, never a jump) |
| `mp_stream_dogs` | village dogs travel with the host's people (WO-191's dog stream) |
| `mp_joiner_noise` | the joiner's lockpicking, fights and doors are the engine's own perception sounds in the host's world |
| `mp_copy_hands_from_host` | registered, nothing to do yet (the held-items gap in §4; §6 row 8) |
| `mp_joiner_receive_only`, `mp_figure_is_player`, `mp_nearest_player_triggers` | **off**: not built (§7) |

**Routing classes met in real closures** (logged the first time each was seen): State variables, timers, time-of-day watches, area,
distance, perception, item-descriptor, soul-death, unconsciousness, numeric-property, combat-history, skirmish and entity-context
triggers; `EnableBehavior`, `SchedulerLinkActivation`, `SetEntityContext(Preset)`, `SetGameContext`, `SetRelationContext`, `SetOwner`,
`ProfileEffect`, `Objective`, `SaveGame`, `ActorMountedTrigger`, `DisableRandomEvent`, `BuffEffect`, `ClothingPresetOverride`,
`AddSoulDefaultItem`, `LinkEffectNoData`, `AIConceptSignalTrigger`, `AddReward`, `Function` (e.g. `DespawnRandomEvents`). Talker-only
on the host: `AddReward`, the player-reward functions, `MoveMoney_*` / money items, `AddBuff`, `BuffEffect`, `ClothingPresetOverride`,
`WeaponPresetOverride`, `PlayerOutfitOverride`, `HealTo`/`SetState` on the player. Never run on the host inside a replay: forced, fader
and urge-a dialogues, monologues, focus cameras, tutorials, track views. Refused on the joiner's copies: behaviours (duels, tours),
entity contexts, scheduler links, the teleport interrupts, `KillNpc`/`SetState` health writes.

## 3. The scorecard (the harness, before vs after; the 0.61.0 field beside it)

| | before (main) | after (0.61.5) | field 0.61.0 |
|---|---|---|---|
| H1 the Jaunt, host, timed (13:00 reached by itself) | the host's own 13:00 watch fired: `CekaniNaNavratSeminaSuka` Started, `TourADistance` Active, `meetWithSeminAndSuk` 3→4 | the departure ran in the host's world inside the replay's closure, the watch finished with it; nothing after 13:00 — **no `hrac_se_na_to_vykaslal` change after 13:00: yes** | ride 1 at the host's 13:00 |
| H1, joiner | 7 requests, all from its own logic | 1 (a player-distance trigger); both clock fires dropped as quiet | 8 of 12 logic-made |
| H2 the ride | 14 mounts, 12 mod dismounts, 11 game dismounts | 2 mounts, 0, 0; no overflow | 50 vs 14 dismounts; 363 overflows (148 horses) |
| H3 the bout | 30 of 30 host blows applied locally; health off by ≤ 12 (mean 5.4); stamina 160 → 113, never corrected | 0 of 20 applied locally; ≤ 8 (mean 5.0); stamina follows the host | 70 local hits; 18 of 18 copy downs right after one |
| H4 rejoin, the figure's swings | not stageable unattended; the figure's clinch, staged: 0 of 4 swings started after a refused sync (the field's case) | 4 of 4 (armed, with and without the clinch's end) | 0 of 22 |
| H5 a working village | "Required state" 5, WO-141 placements 16, WO-160 contexts 11 | 0, 0, 0; 395 puppet executions refused in 90 s | "Required state" 3,546; duel errors 173 |
| H6 night and morning | — (no host clock existed) | a 115-game-s lead closes to 29 in 25 s, never a jump, no `LongTimePassed`; the skip itself stays WO-133's | the joiner 30 game-s ahead |
| frame rate | 75.4 / 75.9 / 84.3 / 74.6 fps (H5 / H3 / H2 / H6) | 75.2 / 75.7 / 84.0 / 73.8–74.2 (within 1.1 %) | |

## 4. The stream (what the joiner shows on a host copy or figure)

| property | 0.61.0 | 0.61.5 |
|---|---|---|
| position, gait, combat rows, KO and death, the figure | from the host | from the host |
| health | local hits, corrected later | **from the host** |
| stamina | local (never streamed) | **from the host** |
| mount | guessed (two streams' geometry) | **from the host** |
| world clock | local (synced on skips only) | **from the host** (slewed) |
| weather | from the host (WO-166) | from the host |
| dogs | local, pinned only when attacking | **from the host** |
| quest behaviours on copies | local (its own duels, tours) | **none** |
| stance / unstance (NPC state) | local (planner demands) | neutral (no demands) |
| held items | local (planner, 50–70 %) | **gap**: neutral (empty) — no item-to-hand attach exists without the planner |
| equipment and appearance | local | **gap**: local (no host stream yet) |

## 5. Quest items

A quest item exists exactly while its `AddQuestItem` effect is active, and all 433 such nodes take `IsActive` from a quest value. The
engine refuses to create, delete or move a quest item any other way ("cannot be created manually", "disappear when their effect is
deactivated"). So quest items follow the host through the quest-state mirror on the game's own path, and `mp_qitem_mirror` measures it:
the host streams its Henry's count of every quest-item class, the joiner compares (`WO200-QITEM agreed / differs`, "pending" while his
own replay may be confirmed, at most 5 minutes). Live: a host claim the joiner lacked was reported as a difference, nothing written.

## 6. The ledger

| # | mechanism | outcome |
|---|---|---|
| 1 | the State mirror as the only channel for the joiner's conversations | **replaced** by the out-port replay (H1); the mirror stays for State values |
| 2 | logic-made requests and their holds | 7 → 1 (H1 joiner); **kept** (receive-only not built) |
| 3 | the joiner's time gate | **kept** as a backstop; 0 hits in the harness and in the field session |
| 4 | twins release, scene release and toast, fader bound | **kept** (a joiner-started two-NPC conversation needs a real key press: not run); the twins bound counts from the start attempt; the toast reworded |
| 5 | the arranged-fight guard (WO-176), joiner half | **off** while `mp_joiner_no_puppet_quests` is on (marked for removal); the host half stays |
| 6 | local blows on copies | **off** while `mp_copy_health_from_host` is on (H3: 0 applied), marked for removal |
| 7 | WO-164's geometric dismount | **off** while `mp_rider_from_host` is on (H2: 0 dismounts), marked for removal |
| 8 | planner placements for copies' items and work | **kept**: held items as values (II.3) not built; `mp_copy_neutral_always` bypasses them (off restores WO-197's 50–70 %) |
| 9 | context sweeps and WO-160 releases | **off** while `mp_copy_neutral_always` is on (H5 "Required state" 0); the talk's own sweeps stay |
| 10 | stand-in juggling after reloads | **simplified**: a stand-in carries a token, is never saved, is forgotten at every load and retired when the world's NPC is back — the field's lords were deleted by a by-name removal after the rejoin |
| 11 | dogs pinned only when attacking | **replaced** by the dog stream; the attacker pin stays |
| 12 | the hand-calibrated witness rule | **kept** (the figure as a player not built) |

**Net lines:** shipped code +3,257 / −50 (native +1,382 / −10, agent and protocol +1,368 / −30, Lua +507 / −10); tests +862 / −3. No old
path is deleted yet: each stands down behind its replacement's switch until two players have run the replacement (rows 5, 6, 7, 9).

## 7. Act V — how far it went

* **V.1 receive-only (off).** O1 passed: hibernating one module's 40 effects, timers and watches with the engine's own `C_Node::Hibernate`
  stopped its decisions (13:00 passed, nothing fired) while a State pulse still landed and the journal objective moved Active → Done;
  pulses into hibernated nodes are dropped with the engine's own error. Hibernated effects reset (profiles, contexts, behaviours) — the
  feeds V.1 lists would have to replace them. O8 passed: notification kinds 2, 5 and 7 reach hibernated nodes (their names are not
  registered as strings). **O4 failed**: writing a watch's state byte directly does not stop it from firing at its hour. V.1 stops at O4;
  O2, O3 were not run, O6 needs an attended key press.
* **V.2 the figure as a player (off).** Not built: P3 needs a port-aware read of the soul flag word and its clear on all four save paths,
  and the save-decode gate makes "on" impossible without that.
* **V.3 the nearest-player rule (off).** Not built: P8's trigger target-resolution site was not located.
* **V.4 combat leftovers.** StartSyncGuard refused on puppets: **fixed** — WO-197's clinch rows held the figure when the engine refused
  the sync; they are no longer played, and the engine's own guard is asserted at the clinch's end or the figure's next swing. The
  knockout get-up through the engine's stand-up and pose-modifier vs FailedAttack: not done (the figure's knockout is a ragdoll and a
  rebuilt figure; no engine stand-up was reachable for it).

## 8. Act IV

* A save waits for a real conversation, a scene, a fight or a mod transaction — never for the player's own monologue or a bark; a waiting
  save logs `WO200-SAVEWAIT reason=…` every 5 s and its toast names the reason.
* The scene toast reads "Your partner's game is playing this scene — carry on."
* A rejoin's Join banner reads "Continue with your character (saved N min ago)" from the per-world character file's time.
* The joiner's lockpicking (the engine's lockpick sound, four pulses), his door use (door sound) and his swings (combat sound at his
  figure) are noise in the host's world (live: all `ok`).
* The host's NPC stream does not stop for a menu (WO-138 declines the in-game menu pause in a session; the field joiner kept receiving
  samples through a 13-s host menu).
* Three guarded read sites that faulted are fixed (the 0.61.0 field session had 12,808 faults at one of them): WO-189's scans and
  the motion reads check an address before reading it. A 20-minute host session afterwards (a synthetic joiner circling the host's
  Henry, a swing every 30 s, a lockpick every 2 minutes, every stream running): no fault line, 73–76.5 fps.

## 9. The build

0.61.5. Gates: the native build and its 9,741 unit checks, the relay round trip (64), the agent (1,838), setup (77), the log bundle
(14), all 73 synthetic suites (4,686 checks), the static checks, the save tools; the 20-minute session above; frame rate within 1.1 %
of before in every harness case. The installer is a release candidate for the maintainer's two-player session: the frame-rate soak
against the game without the mod was not run.

## 10. Open (WO-201)

Held items and stances as values (an engine path to put an item in a copy's hand); equipment and appearance from the host; the joiner's
two-NPC conversation checked with a real key press; receive-only's O2/O3/O6 and a clock write the engine honours; the figure as a
player with its save gate; the nearest-player rule; the knockout get-up; a mark key that takes a short note.
