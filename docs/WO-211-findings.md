# WO-211 — The 0.63.9 beta patch

Version **0.63.9** (unchanged, the maintainer's); builds are told apart by a **build id** (Part 0.2). Measured on the bench (two copies
of the Modding Tools game on one PC, joined through our own relay, started the launcher's way) against session F's field bundles.
Every new mechanism ships **on** with its own switch (the flight permission ships off), or a fix with its bench twin (the same build
with it off). Nothing built here reads input: `tools/Check-NoInputCapture.ps1` passes on every binary and script, first and last.

Roles only: "the host", "the joiner". "A copy" is the joiner's local body of one of the host's NPCs; "the figure" is the host's local body
of the joiner.

## Part 0: ground

- **0.1** `main` (d4f2cb4, the 0.63.9 release candidate) built as the baseline; every offline gate passed (98 of 98). The real saves
  folder was backed up byte-equal with a new hash list (1,122 files), checked before and after every bench run (every check a match;
  mirror roots only).
- **0.2 the build id.** `tools/Write-BuildId.ps1` writes the short commit and the build time (UTC) into a git-ignored `BUILD_ID` before
  a build (`-dirty` when the tree has uncommitted code). Every assembly carries it (`Directory.Build.props`), the DLL too
  (`KCDMP_BUILD_ID`); it names the installer (`KingdomComeTogether-Setup-0.63.9-<id>.exe`), shows in the launcher's About, heads the
  agent's log (`MP-BUILD version=... build=...`) and the DLL's (`KCDMP.dll attached ... build=...`), the crash report's header and the
  bug report's briefing (its first lines). The handshake carries `0.63.9+<id>`; the relay (always the host's own build) refuses any
  other, and the joiner reads "Your partner is on a different 0.63.9 build (<id>); install the same one."
- **0.3** the baseline, per part, below.

## Part A: a dropped connection never costs the joiner his game

**Cause (the field).** The joiner's link closed (`reason=5003`); 16 s later the agent left the host's world. Its final snapshot -- the
game's own save of his Henry in the host's world -- **was written 1.6 s after the drop** (the game's quicksave, verified), but the agent
routes a joiner's save to the snapshot's wait only while its relay session is up: the file was read as "no session as the host" and
ignored, the wait cancelled at 15 s ("not-ready tasks: none": the engine was ready all along; no save lock in the way), the snapshot
was not paired, and the leave loaded his own older autosave (212 m away, health 10.5). The link was back 44 s after the drop.

**Built.**
- **A.1 the hold (`mp_link_hold`, on).** A joiner in the host's world whose link drops (a relay close, a timeout, a Steam close -- the
  same teardown) stays in it: the world's clock stands and his movement is held the action-map way (the co-op menu and the console
  still work), "Connection to your host lost -- reconnecting... (n s)" counts down, the save lock stays, the host's figure stays. The
  agent reconnects with growing gaps (1, 2, 3, 5, 8, then 10 s) over the configured link for up to 60 s. When the link is back, the
  host's world epoch (WO-206 E) decides: the same world -> the hold ends where he stands (no load, no jump) and every body's state is
  asked afresh; a world the host reloaded meanwhile -> the in-world rejoin of a host reload (WO-202 III). **On the host**
  (`mp_link_hold`): the partner's figure stays where it stood with "(reconnecting)" on its badge for the same 60 s and carries on when
  the same partner is back; the host's world does not pause.
- **A.2 leave only then, never without his character.** After 60 s, or on **Leave the host's world** in the co-op menu (Insert; no
  new key; `mp_leave` in the console), the hold ends and the leave runs: **the final snapshot first** -- now routed to its wait when
  it comes after the session ended (the fix `snapshot_after_drop`) -- paired with the host's newest save; then his own newest save is
  loaded **with the snapshot's Henry spliced in** (the join's own splice, check and verify; his own world's quest items kept, the host
  world's left there: the fix `drop_henry_home`), and he is put where he stood at the drop. A snapshot that cannot be written is said
  in words: "Your last progress in your partner's world couldn't be saved (<reason>)."
- **A.3** the host's own drop runs the same: the joiner's hold and reconnect.

**A.4 numbers (the pair, d2; the relay's fault mode cuts the joiner and refuses his reconnects for the cut's length).**

| cut | the joiner | reconnect | his position | loads | the host's figure |
|---|---|---|---|---|---|
| 5 s | held in the host's world | 6.5 s (gaps 1, 2, 3 s), the host's world the same: resumed in place | 0.0 m | 0 | held "(reconnecting)", back after 6.5 s |
| 30 s | held | 39.6 s (gaps up to 10 s), resumed in place | 0.0 m | 0 | held, back after 39.9 s |
| 90 s | held 60.5 s, then left: the final snapshot written 1.7-1.9 s later, **paired**; his own save spliced with it (check PASS, verify ok) | -- (the link came back at 107 s, in his own world) | home at the drop's spot (0.02 m off) | his own world, once | held 60 s, then removed |

**The drop moment's Henry at home** (the final build, the 90 s cut again): his health in the host's world was held at 63 up to the drop
(his own save: 100); at home it read **63**, where he stood. Belongings: the splice's own check compares the spliced Henry's items with
the snapshot's (PASS); the bench's attempt to add two torches before the cut did not take, so no item count differed to compare.

The Steam close is the same teardown as the relay's (the bench runs over the relay; a Steam `closed-by-peer` cannot be made on it).
**Twin** (`link_hold`, `snapshot_after_drop`, `drop_henry_home` off): the field's behaviour, line for line: the leave at once (no hold), the final snapshot
written 1.9 s later but read as "no session as the host", the wait cancelled at 15.4 s "(no file)", **not paired**, and his own older
save loaded -- he stood about 170 m from where he had been.

## Part B: the host's crash in the shared conversation

**B.1 cause (the dump, with the build's own modules).** The crash thread is the render thread. Its frames, from the top: the script
system's `Release` of a table whose reference count reached 0 while its Lua reference was already gone ("already released") -- the
table CryAction's element-listener Lua dispatch (`CryAction+0x137c50`) creates at its own start (`pScriptSystem->CreateTable`) for
the event's arguments and releases at its end; that dispatch was called by the element listeners' `OnUnload` (`+0x10f550`), from
`CFlashUIElement::Unload` (`+0xefd60`) and `DestroyBootStrapper` (`+0xf0fd0`), when the render thread dropped the last reference to a
flash element's bootstrapper (`+0xee340`). So a menu element unloaded **on the render thread**, and the dispatch built and freed a Lua
table there while the main thread ran Lua: a race on the script system's table pool.

The dispatch builds that table for **every** element event whenever **any** Lua element listener is registered -- before it compares
the element and event names. The game itself registers none (its scripts: 0 calls of `RegisterElementListener`), so in the game alone
this never runs; the mod registers listeners on the map, the modal dialog, the item transfer and the main menu. The field host had the
map open 27 s and closed it; the player's hunger bark started on the main thread in the same frame; the crash followed. The joined talk
with the blacksmith had ended 38 s earlier: the conversation's re-sends (WO-210 F.2) are not on this path. It is not a table the mod
handed to the UI, and not a re-send rate.

**B.3 fix (`mp_ui_lua_main_only`, on).** A gate at the dispatch's entry (found from `OnUnload`'s own call, the one function it calls
that reads `__ui_action_name`; its 14-byte prologue checked, port-aware): on any thread but the game's main thread the event is counted
and not dispatched to Lua (none of the mod's listeners wants a map's hide or unload: each off-main event is checked against them and
logged as "no listener takes it"). On the main thread nothing changes.

**B.2 numbers (the pair, d2).** On each copy, 20 map and 20 inventory opens and closes with the pair joined: **every map close put 2
element events (`ApseMap/OnHide`, `ApseMap/OnUnload`) on a non-main thread** -- 40 on each machine; the guard skipped all 40, none was
a listener of the mod's; 221-241 events ran on the main thread as before. The inventory's closes stay on the main thread. On 0.63.9
each of those 40 built and freed a Lua table on the render thread: one race per map close. **Twin** (`KCDMP_TEST_W211_OFF=ui_lua_main_only`, the old
behaviour, the same storm): **the host's game crashed** on its 10th map close -- an access violation inside the script system on the
main thread, in the very millisecond the twin let the map's `OnHide` / `OnUnload` Lua dispatch run on the render thread
(23:49:36.335, the crash at .336): the same race as the field's, seen from the main thread's side. **Joined talks on the final build** (`convvote_both`, the ring and the
pick on): the host as starter -- 4 talks, 5 decisions voted (changed, cleared, not followed, followed), every one ended cleanly, no
crash; the joiner as starter -- the host never joined as listener within 120 s (the bench's failure WO-210 recorded too), no crash.
The bench cannot play 30 talks in a row in one run; the menu storms above are the cause's own reproduction.

**B.4:** not needed -- the cause is fixed and is not the conversation HUD: `mp_conv_hover` and `mp_conv_pick_hold` ship **on**.

## Part C: seated NPCs don't T-pose

**Cause.** WO-210 A's hand-over released a copy's seat whenever the host NPC's whole body returned to its base ("path 15") and its
activity row changed -- but a seated drinker's gesture or dialogue pose changes only the unstance on the same seat (the field's
`ttkc_man_10`: sitting@0D02... unstance - -> 119 -> -). The copy was let go, WO-160 sat it down again, every ~30 s. Across the field's
joiner bundles 153 of 371 path-15 releases kept the very seat or workstation, 46 were re-seated within 5 s. The 3,712 queue overflows
were nine of the copy's idle-state actions (`BaseAction`) behind its own movement and look actions -- one look action each, not a stack
of them: the hand-over and the watchdog's reseats queued behind a head that never yielded.

**Built.**
- **C.1** a copy's seat, bed or workstation is released only when the host's state no longer holds that object (the NPC stood, moved
  to another seat, the work ended); never on path 15 alone (the fix `seat_same_object`). WO-160's placement into the very object the
  copy was released from in the last 2 s waits for the next apply.
- **C.2** a hand-over (and so every watchdog reseat) on a copy whose queue is already long withdraws its own still-pending action first
  -- one pending action of ours per copy -- and installs nothing more while the queue stays long (the fix `handover_queue_cap`).
- **C.3 (`mp_overflow_reseat`, on)** the engine's own "attempting to queue too many actions on entity" line for a copy makes the owner
  reseat it the clean way at once: every action of ours on it withdrawn the engine's way, one hand-over (at most every 2 s); counted.

**C.4 numbers (the pair, the bench's tavern at night, 10 minutes).**

| | releases | seat loops (release + re-seat in 5 s) | queue overflows on copies | empty bodies | stale postures |
|---|---|---|---|---|---|
| 0.63.9 (m11) | 166 | **30** | **10,831** (three seated villagers) | 0 | 0 |
| this build (d2) | 111 | **0** | **0** | 0 | 2 (10.8 s, two villagers getting into bed) |
| the twin (C.1 and C.2 off) | 184 | **46** | **118,235** | 0 | 0 |

The twin brings the loop back; it overflows even more than 0.63.9 because its C.3 reseats (still on) hand over behind the long queue with
C.2's cap off -- C.3 relies on C.2, and both ship together. Seated while the host NPC sits: 79.9 % of 3,281 samples in the twin (0.63.9:
97.5 %); the twin's joiner fell 18 s behind on his game's queue in the tavern (the shipped build: within the step's limit).

Seated while their host NPCs sit: 97.5 % of 12,856 samples on 0.63.9; on this build's tavern run the two machines' animation probes
overlapped only for its last two minutes (the probe rotates its file), too little to measure it. The link runs (seated villagers by the
inn) kept them seated: 100 % of 4,200 samples. The tester-field tavern (Tachov's) is not reachable on the bench save.

## Part D: no fine for hitting the partner

**Cause (the field, both machines' logs).** The hit made no crime anywhere: the partner's figure is kept out of the crime system
(WO-139's isolation), no crime line followed it on either machine. **The fine was the shared crime setting at work**: 5 s before the
joiner's swing, the host lockpicked a door in Troskovice, two witnesses and a guard saw it, and the joint mode put it on the joiner's
record too ("the host's own lockpick -- ghost 1 answers for it too"); the guard stopped the joiner 16 s after his swing for "lockpick:
1" -- 60 groschen. Nothing told him it was his partner's.

**Built (`mp_joint_crime_told`, on).** A stop for a crime kind this player never reported himself says so: "Shared crime is on: the
guard holds you to account for your partner's lockpicking." The host, when the partner's crime is raised in his world under the joint
mode, is told it counts against him too. With friendly fire on, a deliberate hit stays the engine's own call; nothing changed there.

**Numbers.** The pair (d2), friendly fire off and shared crime on, in the bench town by the inn with
townsfolk and a guard about: the host swung 10 times at the joiner's figure 1.3 m in front of him, the joiner swung at the host's
Henry (his swings went out -- the host replayed his figure's strikes -- though the bench's send-only input lost the joiner's window
for each button release): **0 crime lines and 0 stops on either machine**. With friendly fire off a partner's blow is dropped before
any line logs it, so whether each blade connected is not recorded; the field (above) is the stronger evidence. The friendly-fire-on
run (the engine's own behaviour, recorded) was not made. **Twin:** `mp_joint_crime_told off` -- the stop comes with no notice (the field's behaviour).

## Part E: the flight is off for players

The Modding Tools' flight is the engine's `flymode` action on F3 in the "debug" action map; its state is the player's own
`GetFlyMode` / `SetFlyMode` (the game's `DebugUtils.FlyMode` cycles off / on / on without collisions). While a session runs the mod reads
that state ten times a second (never the key) and sets it back to off, with "Flying is turned off in co-op: it breaks the people around
you." `mp_allow_fly on` (off by default) lets testers fly and says so. **Numbers (the pair, d2):** F3 pressed on each machine: with the
switch off the flight state read back off 1.5 s later and the refusal was logged (host, joiner); with it on the flight stayed (mode 1).
The rest of the debug map (F4 and the others) is untouched.
## Part F: the release text and the briefing

- The beta's notes are `docs/BETA-0.63.9-release-notes.md` (renamed from WO-210's): "every switch is safe to flip" is now the ten
  switches WO-210's live run actually flipped (in a tavern and a fight, 80 flips), the rest named as not flipped live; the joiner's
  kills are "fixed; confirmed on our bench in an earlier build": the final build's `joinerkill` run swung 86-96 times at each of the bandits without a kill -- the bench's send-only input lost the joiner's window on every button release this session (the partner-hit run shows the same), so it neither confirms nor refutes the fix; the five fixes with how each behaves now; the known list in plain words; how to report, with the build id.
- **The briefing's role.** The second pair's host bundle said "role: joiner": every agent logs "MP-JOIN joiner: the game is at a
  loaded world" at its start, and WO-210 H.4 took any "MP-JOIN joiner:" line for the joiner. Now the role is the one the agent recorded
  with each mark (`MP-MARK-SNAP ... role=host`), shown beside each mark too; then the first authority line; the start-up question no
  longer counts. The second pair's host bundle reads "host" with it.
- The tester script (`docs/BETA-0.63.9-tester-script.md`) starts with the five fixes.

## Gates and the installer

- **Offline gates on the final source: 99 of 99 pass** -- native unit tests 10,224 (WO-211's own rules and the new prologue among
  them); relay round trip 199 (one new: another build of the same version is refused); agent 2,033; setup 77; log bundles 77; every
  synthetic Lua suite (WO-211's own 26; the co-op menu's suite updated for its new row); the static checks; the relay message table (97)
  and fault-mode (37) gates; the digest analyzer and rig tests; the save tools. **Found on the way:** with a build id stamped, the relay
  tests' peers (which sent the plain version) were all refused -- the tests now send the release string the agent sends.
- **No-input-capture gate** (`tools/Check-NoInputCapture.ps1 -SelfTest`), first and last over every binary and script built here, the
  dev builds and the bench's own tools included (the input helper gained three keys to send: the map, the journal, the flight key):
  PASS both times, the self-test catching every planted capture.
- **The real saves:** backed up byte-equal with a new hash list (1,122 files); checked before and after every bench run -- every check
  a match, mirror roots only.
- **The installer** `KingdomComeTogether-Setup-0.63.9-<build id>.exe` is built from this commit with `tools/Build-Installer.ps1`
  (the soak waived as the WO says -- the safety gates are hard, the rest reported): the publishing gate, the payload's no-input-capture
  check and the live save round trip run inside it. Installers stay local; its name, size and hash are in the commit that follows.

## Switches

Console: `mp_<name> on|off` (either machine; `mp_<name>` alone says its state); `mp_w211_status` lists them with their counts.

| switch | part | what it does | default |
|---|---|---|---|
| `mp_link_hold` | A | a dropped link holds the joiner in the host's world up to 60 s while it reconnects; the host keeps the figure "(reconnecting)" | on |
| `mp_ui_lua_main_only` | B | the game's menus never call the mod's Lua listeners off the main thread | on |
| `mp_overflow_reseat` | C.3 | a copy the game says has an over-full action queue is reseated the clean way | on |
| `mp_joint_crime_told` | D | under shared crime, a stop or a crime that is the partner's says so | on |
| `mp_allow_fly` | E | the Modding Tools' flight in a session (testers only) | **off** |

`mp_leave` (A): during a link hold, leave the host's world now (the co-op menu's "Leave the host's world" calls it). Fixes with their
bench twins only (`KCDMP_TEST_W211_OFF=<name>`): `snapshot_after_drop`, `drop_henry_home`, `seat_same_object`, `handover_queue_cap`;
the switches above are twins too.

## Still open

- **The second pair's joiner crash** (a null read in the entity module on the main thread under a Lua call, session F): not this WO's
  five. Its shape -- the main thread failing inside script calls -- is the face B's twin crash showed on the bench, so it may be the same
  render-thread race; not proven.
- **A:** the reconnect uses the link the session used (no Steam-then-relay ladder); a Steam `closed-by-peer` is the same teardown but
  was not made on the bench; a host reload during a cut (the in-world rejoin path, a load) was not run. The home splice carries the co-op
  world's Henry -- skills, belongings, money, health -- into the joiner's own playthrough after a drop, as asked; his own quest items stay
  his, the host world's stay there.
- **C:** two short stale postures at bedtime on the fixed build's tavern run (10.8 s, villagers getting into bed); the seated share was
  not measurable on that run (the animation probe rotated); the field's own tavern (Tachov) is not on the bench save.
- **D:** the friendly-fire-on run (the engine's own behaviour, recorded) was not made; with friendly fire off a blow's contact is not
  logged, so the bench run shows no crime, not that every blade connected.
- **E:** only the flight is turned off; the debug map's other keys (F4 and the rest) still work in a session.
- **A lead for WO-210 H.3 (the host's memory while it waits alone):** the field host's crash report lists the engine modules' own memory,
  and CryNetwork held 4.1 GB (`CryNetwork.dll Used in module: 4145712094`), the largest of them -- the climb may be the engine's network
  module, not the renderer. Not followed here.
- **WO-210's open items stand:** weapons appearing late, T-poses on some fleeing NPCs, no clinch between players and NPCs, carts and
  riders, the listener's head and some conversation shots, the host's memory while waiting alone.

