# WO-201 — Every bug from the first 0.61.5 field session, at its root (0.61.9)

> **Actions go to the authority; values come back.** The host's world decides. The joiner's game shows, and asks; it never decides.

The first two-player session on 0.61.5 (host and joiner, about 30 minutes: Semine's yard, the departure, the ride, the duel,
Troskowitz, the crossroads, the patrol fight) found two dozen problems. This WO fixed each at its cause, added two features
(hearing each other's conversations, voting on the partner's choices) and built the first parts of "both of us are Henry".

All live evidence is one machine: the Modding Tools game as the host of a synthetic joiner, or the joiner of a synthetic host,
through a local relay, on the `playline4` throwaway (copies of the field host's own saves). The saves folder was backed up
byte-equal first. Nothing here is two-player evidence; the tester script in `TWO-PLAYER-CHECKLIST.md` (392-410) is.

Every new mechanism has its own switch, all on except `mp_figure_is_player` (§3). `mp_w201_status` prints them all.

## 1. Crashes and stalls

| | What it was | The fix | Switch |
|---|---|---|---|
| **B5** the host's crash | The joiner looted a body the host was looting. A pass of the mod that removes the host's copy of an item the joiner had already taken from his own loot screen ran **under the host's open loot screen**; the host moved an item and the game died. | No mod path adds, removes or moves an item in an inventory whose transfer, loot or trade screen is open, on either machine. The change waits for the close (`WO201-INV deferred`) and is applied once after it. Every inventory-changing path was audited (below). If the host carried that copy off while his screen was open, it goes after the close ("Someone already took that."). | `mp_inv_screen_guard` |
| **B1** scenes waited for puppets | On the joiner, a scene with the host's people waited for them to walk into place. They are puppets and never arrive: 95 s, then 336 s, each ended only by a save request. | The moment the engine's NPC manager reports a fast-forward on the joiner, every host copy gets the engine's own per-NPC fast-forward cancel, the very call the save path makes (found in its own code). The copies stay where the host's stream puts them. No save is requested. | `mp_joiner_scene_noposition` |
| **B2** a late conversation | A talk queued behind B1's stall started 5.5 minutes later, far away, and its end moved the joiner 209 m. | A conversation that waited in the scene queue longer than 10 s is cancelled the engine's way: "That conversation was cancelled - talk again." | `mp_scene_queue_stale` |
| **B8** the twins race | The end of one conversation re-paused its people 14 ms before the chained one needed them. | A conversation's people are re-paused only when no conversation of this player wants them (a pending request, a scene hold), plus a 2 s grace. | `mp_twins_grace` |
| **B6** rescues cut monologues | The "couldn't start" rescue cut off one-person monologues (escort barks, a camera line). | Rescues ignore dialogues whose only participant is the player; the toast is shown only for a conversation the player started. | `mp_monologue_rescue_exempt` |
| **B7** the joiner's own story barks | The joiner's own quest logic started escort barks when it thought he strayed. | On the joiner, in the host's world, the node gate refuses quest nodes that start a player monologue or forced dialogue, unless the chain began in his own conversation or interaction (his own searches still speak). | `mp_joiner_no_story_barks` |
| **B19** toast truth | "Your partner's game is playing this scene" showed for the joiner's own stuck scenes; an em dash was lost. | Shown only when the scene is the partner's; every toast in plain ASCII (" - ", "..."). | `mp_scene_toast_truth` |

## 2. Puppets take values, not guesses

| | What it was | The fix | Switch |
|---|---|---|---|
| **B3** riders | The host's lords mounted 9 times; the host sent "mounted" 0 times (the horse is a WUID, read as an entity id). | The host reads the real mount (the horse through its WUID) and sends it the moment the engine says "starts mounting". On the joiner a copy the host calls on foot is never written onto a saddle (`WO201-RIDE disagree`). In the harness: 3 engine mounts -> 0 sent before, every one sent after. | `mp_rider_from_host` (WO-200's) |
| **B4** a fight that never ended | Semine's copy kept a local fight with the host's figure after the duel and attacked him when he talked to Semine. | A copy fights exactly when, and whom, the host's NPC fights; when the host's NPC leaves combat the copy's fight ends the engine's way (StopFight); a copy never starts one by itself. | `mp_copy_combat_from_host` |
| **B9** the look-pose flood | 5,967 animation-queue overflows on the joiner (0.61.0: 363), "Agent is stuck" 53 -> 328. | Two causes, found by switching WO-200's joiner parts off one at a time in the market scenario: WO-200's "copies demand nothing" rebuilt every copy's loaded state from new objects every 0.5 s, so the planner re-planned twice a second; it is now written only when it changes something. WO-143's one-shots on paused copies were ticked for up to 15 s although most never started (a woodcutter's copy shown without its woodchopping cannot play its chop): ticking is capped at 10 Hz and a one-shot that started nothing on the body in 3 s is dropped. A host activity that owns its place keeps the writer off the copy. The market replay (150 s): 3,122 overflows / 165 "stuck" before, 310 / 9 after (0.61.0's field rate was 9.8 a minute; this replay is a far denser market). The rest is one woodcutter's copy (§6). | `mp_copy_neutral_always` (kept on), `mp_oneshot_tick_cap` |
| **B10** gliding | Walkers stepped and then slid; the host sent about one row a second per NPC, the joiner wrote 6 of 28 copies. | The host sends a moving NPC every emit tick (10 Hz); the joiner's writer walks a copy on through a gap under 2 s on the host's own speed and heading, never hands a moving copy to the slower path, and takes the gait from the host's speed. **Not fixed:** the copies' feet still slide (the replayed market: 1.45 m/s at the median, 3.26 at the 95th percentile, against 1.47 / 3.19 before; the host's own walkers about 0.14) -- the writer moves them without a stepping animation, and the cause of that is open (§6). | `mp_walk_from_host` |
| **B11** the crossbow | The joiner's quest crossbow was refused on his figure ("cannot be created manually"); his shots were never sent. | The figure shows a weapon the engine refuses as a visual-only prop of its own model in its hand. The joiner's shot, reload and let-down are read from his own shooting (no hook) and shown on his figure as the game's own animations — never a shot of the figure's (no projectile). The draw and the aim hold are not shown (§6). | `mp_figure_ranged` |
| **B23** hands and looks | Copies' hands empty; a copy undressed for bed stayed undressed all day. | What the host's NPC holds is shown as a visual-only prop of its model in that hand; the host's day/night dress is applied even under "demand nothing". | `mp_copy_hands_from_host`, `mp_copy_look_from_host` |

## 3. Both of us are Henry (Act III)

* **V.1 receive-only** (`mp_joiner_receive_only`): the joiner's own quest logic asks the host for nothing — only his own conversations
  and his own interactions do (`WO201-RECV kept ...`). Nothing is hibernated, so every quest item's effect stays awake. The field's nine
  requests: seven are now kept, the two coal-sack / cart searches stay his.
* **V.2 nearest player** (`mp_nearest_player_triggers`, host): a quest's distance trigger on "the player" measures from the nearer of the
  host's Henry and the partner's figure (`WO201-NEAR`), so the escort fails only when both stray. Area triggers are not covered yet (§6).
  The `player` alias itself always resolves to the host's own soul (read in the engine: a copy, a twin or a second player soul can
  never be returned for it).
* **V.3 Dude2** (`mp_figure_is_player`, **off**): the flag word is read out of the engine's own per-soul save step; setting only
  `IsPlayer` on the figure's soul, a save gate on that step (the bit is written as 0 on every save path), a watchdog every second and a
  census after a load are built and tested. The audit of every `IsPlayer` read found no "first player soul" pick, but four side effects:
  the soul leaves the engine's soul update scheduler, copies of it inherit the bit, skirmish logic skips it, and the bit is saved (the
  gate handles that one). The part ships off until the first two are guarded and its save-decode check has run.
* **V.5 both are Henry** builds on V.3: blocked with it (rule 3 is V.2; rule 6, one party, is today's state).
* **V.4 the knockout get-up** (`mp_knockout_engine`): the partner's figure never fell because its own guard (immune and
  "unconscious-proof", put back every 5 s) killed the game's unconsciousness on its first update, and it never got up because the
  old fallback only switched its physics to a ragdoll, which nothing can stand up. Now the guard is swapped for the immune-only one
  the copies already use and held off while he is down, the game's own unconsciousness lays the figure down, and at his wake the
  engine's own StandUp stands it up once the soul is conscious again. A recoil stays WO-197's: the engine's recoil is a failed-attack
  action, and a puppet's guard pose blocks it at equal priority.

## 4. Smaller things

| | Fix |
|---|---|
| **B12** | The quiet-clock compare runs only for a pulse that carries a State; the rest is reported "no State". |
| **B13** | Above 30 game seconds behind, the joiner's clock slews up to 50 % faster until within 5; never a jump (`mp_clock_catchup`). |
| **B15** | While the host loads, reloads or saves, the joiner sees "Your host is loading - please wait", not the connection toast (`mp_host_load_toast`). |
| **B16 / B17** | Two of WO-200's events (quest-item counts, the joiner's noise) were never routed by the agent ("unknown game event"); routed now. |
| **B20** | The joiner's unresolved assets: host-spawned NPC arrays and random events the joiner's world does not have; no visible effect found. |
| **B21** | The host-only save lock is added once per load and never re-added to test it (60 engine errors in the field). |
| **B22** | The host's own instant monologue cancels are the engine's: each is cancelled in the frame it is made, one during the host's load, and no mod path restricts the host's Henry. A run without the mod would confirm it. |
| **B24** | `mark <what you saw>` puts a note with the mark into the logs and the bundle's list (`mp_mark_note`). No in-game text box: the game has none, and nothing that reads keys is shipped. |

## 5. New: hear each other, vote on choices (Act V)

* **Proximity conversations** (`mp_speech_share`; `mp_speech_radius` 20 m, `mp_speech_subtitles_m` 8 m): every line the engine voices is
  read where the speaker manager plays it (the voice file the engine already resolved). The partner hears it from the right body —
  an NPC's line from that NPC, the talker's Henry lines from the talker's figure — through that body's own audio object, at the game's
  bark range. Subtitles inside 8 m, marked "(overheard)", never during the listener's own conversation. A line plays once, never back
  to the talker, never late; one line is under 200 bytes (the text rides only inside the subtitle radius). The joiner sends his own
  conversation's lines only; the host also sends its NPCs' chatter.
* **The dialogue vote** (`mp_dialogue_vote`, `mp_vote_radius` 30 m): the talker's options are read where his own dialogue HUD receives
  them (display only, never the choice). The partner sees them in a small numbered panel and votes with F2 / F4 / F5 / F6 (the mod's own
  interaction-map keys, live for the vote only while the panel is shown); the talker sees "<> <partner> votes N: ..." beside his options;
  both see "<talker> chose: ..." afterwards. The vote never holds the conversation; timed decisions keep their own timer.

## 6. Open, and why

* **B10, the gliding** is not fixed (§2): the writer's new path runs (thousands of extrapolated frames, copies kept through silences)
  but the feet slide as before, so the stepping animation a copy plays while it is moved is the open question, not the stream.
* **B9's remainder:** 302 of the 310 overflows left are one woodcutter's copy (its look pose re-queued about every 50 ms while the
  writer moves it); holding the writer off it for 3 s after each swing did not help (399), so that change was not kept.
* **B5 in the harness:** the field crash did not reproduce on one machine (the base run's "crash" was a harness false alarm: the game
  kept logging). The fix is shown live once: the duplicate removal came due under the open loot screen, waited (`WO201-INV deferred`)
  and was applied 14.8 s later at the close, with no crash in 10 runs. Runs 2-10 do not count: the console-opened loot screen stopped
  the game's Lua timers for good, so nothing of the mod's could run; the at-close path is covered by its synthetic test.
* **B11, the draw and the aim hold** are not shown on the figure: those fragments need the shooting's own tags (`prime`, `charge`),
  which only the game's shooting sets. Driving the figure's own shooting component would show them, but it would also fire a real
  bolt on the partner's machine, outside the host's authority; it needs a live check against the figure-blow suppression first.
* **V.2's area triggers** and the "is it near" checks (`GetDistance`) are not covered yet; only distance triggers are.
* **V.3 / V.5:** off and blocked as said in §3; V.1's probes O2/O3 were not needed (nothing is hibernated) and O6 needs a tester's
  key press.
* **Act V:** an overheard line plays without lip movement (the file plays on the body's audio object, not through its speak manager);
  the partner's vote shows as a line beside the talker's options, not inside the option row (re-sending the rows resets the
  game's own highlight).
* **The harness** could not reproduce four field bugs on one machine; their fixes stand on their unit and synthetic tests, and the
  tester script checks them in the next two-player session:

  | Scenario | Field bug | Why it did not reproduce | Tester step |
  |---|---|---|---|
  | F1, the departure talk | B1 (95 s) | a scripted talk is not a key press: it opened another dialogue, never the departure | 1 |
  | F3, the duel's end | B4 (16 shouts) | the field's leftover fight began in the duel's behaviour on the joiner's machine; the engine's own attack start does nothing on a paused copy | 4 |
  | F4, the trialog talk | B1 / B2 / B8 | the same scripted-talk limit | 6 |
  | F5, straying from the lords | B6 / B7 | the escort's own distance check did not fire at 120 m in either save | 3 |

## 7. Act VI: retired

| WO-200 ledger row | Outcome |
|---|---|
| 6, local blows on copies | deleted: a host blow is never applied to a copy on the joiner (the duel showed right both ways in the field) |
| 5, the arranged-fight guard's joiner half | deleted: the joiner's copies run no quest behaviour; the host's half stays |
| 9, the context sweeps and WO-160 releases | deleted: the after-skip sweep, the focus sweep, the after-skip settle and a copy's release before an apply (a copy is kept neutral every tick); the talk's own steps stay |
| 7, the geometric rider rule | deleted once B3 passed F2: the host now sends every mount the engine makes (3 of 3, against 0 before), and the joiner's copies mount on that word; the rule and its switch `mp_rider_unit` are gone |

## 8. The B5 inventory audit

| Path | Guard |
|---|---|
| WO-195 host-dup (the field crash) | deferred to the close; a copy the host carried off goes after it |
| W134 host take (a joiner's take from a host body) | refused "busy" while the host's screen is open on it; taken back from the joiner |
| W134 take-back off the joiner's Henry | deferred, unless the joiner's own screen on that body is the open one |
| W134 host put, body state, chest rows | deferred |
| W143 temporary tools (in, out, all out) | deferred; a tool whose screen is open stays |
| W167 copy weapons | deferred |
| Henry's weapon re-equip (WO-174) | waits while his inventory screen is open; after a load nothing is open |
| Dice wager, a lost pickup race's roll-back | deferred |
| Ground items, `mp_item_spawn`, `mp_spawn_armor` | no screen can be open on them |
| Figures' own outfit, torch and lights | exempt on purpose: a figure is never looted |
| WO-136 take-backs | the verdict carries "busy"; applied through the deferred Lua path |
| WO-148 carry, WO-200's quest-item comparer | change no inventory |
| Shops and trades | the engine's own; an open trade screen guards Henry like a loot screen |
