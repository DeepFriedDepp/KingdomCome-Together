# WO-153 — the 0.43.0 tutorial session: findings

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **(log)** read in the field logs; **(code-verified)** read in the code; **(unit)** a unit or
synthetic test; **(not live)** no run in the game this session; **(inconclusive)**; **(not built)**
with the reason. The host and the joiner are the two machines of the field session; no names,
paths or addresses of theirs are in this file.

## The answer

**Four of the five reported symptoms were not what they looked like, and the fixes follow the
evidence, not the symptoms.** Everything below is checked by unit and synthetic tests only; none of
it ran in the game this session (§D 2). Two-player checks are in `docs/TWO-PLAYER-CHECKLIST.md`
§WO-153.

| Reported | What the logs show | What was done |
|---|---|---|
| The joiner's game crashes on herbs | Both crashes end the moment the host avatar's `PickingHerbs` loop is stopped, about a second after the joiner's own herb minigame started. Our code ties the loop to no plant, object, alignment or tag; the engine's fragment is what breaks. | The avatar **stands** while its player gathers herbs (`mp_avatar_herbs`, **off by default**). A pick-up or a dig can never be aligned to a world object. The cause inside the engine is an Opus follow-up. |
| Copies fight and move by their own AI | The 28 `MP-AUTHORITY-VIOLATION` lines are the mod's own native and legacy writers meeting after a hand-over, not an NPC's brain (every one of them was suspended). The dog that bit the joiner was a free brain: domestic dogs are neither streamed nor guarded. The "stays in combat" claim is not supported (4 skirmish handles created, 4 surrendered). | Dogs are suspended under the guard; Lua waits for the DLL to let go before it writes a body; a copy is not engaged with this player for the host's own fight; the "own hit reactions off" contexts go only on copies in a fight; the discard table holds 256, not 64; the violation notice is log-only. |
| Commands dropped | A timed-out batch is not lost: the game ran some of them late. They cluster at level loads, one 24.9 s GPU hang and the game's end. Quest steps and deaths do not ride this path. | A batch the game does not answer is kept and sent again in order (newer state replaces older, one-off statements run once, bounded), with a hold instead of hammering a stalled game. The pipe's loss is declared at the reader's exit, not 4.5 s later. |
| An NPC stood lootable after its host death | The kill landed. A stale "alive" sample, in flight when the death was applied, cleared the owner-death request, so only the `applied=dead` line and the corpse placement were skipped. No second body in these logs. | The race is closed in the mod and the agent; unresolved requests say so; a stand-in is refused over an existing body. |
| Faults, temp-tool churn, door flap, missing dog | Faults: reads through a stale cached combat actor, 78–110 a second. Temp tools: the churn is across classes, and every take logged a script error. Doors: the host's door history replayed in one millisecond after the load hold. Dog: engine noise after a layer unload. | Pointer check and per-frame re-read; tools held 90 s and the count passed to `DeleteItem`; one door state per door in the hold; dog not ours (§5.4). |

## E. The evidence, confirmed

Six read-only investigators read the nine zips; every number below was measured in a log, and
where the work order's text differs, the log wins (marked **differs**). The host's clock is
60 minutes 1.7 s ahead of the joiner's. Folder names under `history\` are run END times.

| WO line | the logs | mark |
|---|---|---|
| both joiner crashes end on `WO143-SHOT kcd2mp_0 'PickingHerbs' finished (request N, result 3 …)` | yes: request 11 (crash 1) and request 9 (crash 2). In both, the joiner's own `Herb gathering minigame started` was about 1 s old and `MasterSlaveManager is clearing context '5' for entity 'Dude'` is the last game line. Earlier overlaps of the two minigames (5 in crash 1, 3 in crash 2) did not crash | (log) |
| "at a plant the joiner was picking himself, right after the host had picked it" | **differs**: herb gathering is per player and untouched by the item sync; the only link between the machines is the avatar's loop. Both players were picking herbs in one patch | (log, code) |
| 33 `MP-AUTHORITY-VIOLATION … path=legacy` | **differs**: 28 violation lines + 5 toast lines. 21 NPCs, every one with `pause_issued=1 pause_exec=ok`. All 28 follow a native-to-legacy writer flip by 0.3–6.7 s (19 at loads after `npc_native … off tick`, 9 after a DLL `drop reason=silence`). Toasts were throttled to one per 300 s, so 5 showed | (log) |
| `CartMount` 104 times | 104 `Slot 'horseLeft|horseRight' is occupied`: 4 horses × 26 tries, 4 at 1.5 s and then every 15 s for 340 s, in the combat run only. The mod's own WO-141 apply, not the horses' brains | (log, code) |
| the joiner "in combat" from the cliff to the tutorial's end | **not supported**: `Skirmish created` 4 / `Handle surrendered` 4; about 32 s of 2,888 s in combat; `SoulAdded on Dude` 21 / `SoulRemoved` 13 is normal engine accounting (the host's log: 15 / 6). Nothing logged between the fall and the first skirmish (t=1308 → 2025) | (log) |
| the dog's copy attacked the joiner | 3 `HitTarget` and 2 `SkirmishVictory` by `korenarka_dog` on the joiner; health 22.8 → 12.0 → 1.25 → 1.0, then a downed state. The dog was the only non-player attacker in all five runs; it is absent from every guard, puppet and context list; its own behaviour tree ran. The host never streams it | (log) |
| the bandit leader's copy `SkirmishVictory … (target kcd2mp_0)` | the copy was suspended and its combat terminated each frame; the engage was the DLL's `target Host` engage within 0.1 s. It never attacked | (log) |
| "own hit reactions off" on many non-fighters | 216 copies (184 people, 20 horses, 13 animals) in the tutorial run; 4 ever fought | (log) |
| 15–43 `MP-BATCH-DROP` per session | **differs**: 9–21 log lines per run (throttled to one per 5 s); the true totals are the counter on the last line: 80 batches / 412 statements (tutorial), 51 / 264, 142 / 841 (crash 1, 92 of them after the game died), 98 / 600 (crash 2), 50 / 266. 85 of 86 lines are the 0.8 s timeout, 2 are the game's death | (log) |
| they cluster at stalls | 43–49 batches in the 35–45 s after the join's own load, in all five runs; 30 in one 24.9 s GPU hang (123 fence timeouts); one 1.3–1.5 s hitch per run | (log) |
| a timeout means the batch is lost | **differs**: the game ran about a third of them late (8 `MP-NPCWRITE` lines 0.72 s after the agent gave up; three `WO102-TOGGLE` re-arms 9 ms apart after the hang) | (log) |
| `[combat] lost the connection to KCDMP.dll` about 3 times a session | **differs**: exactly once per run, at the game's end, 4.3–4.6 s after `pipe reader exited`; never mid-session; no `PIPE: agent disconnected` in 11 native logs. The DLL answers a busy main thread with a failure at 3.5 s, before the agent's 5 s | (log) |
| `local equipped-set read failed` | 1–2 per run, each on a stall; healed itself | (log) |
| conversation requests `cancelled by the engine (timed out)` 14–19 per session | **differs**: 13 / 0 / 1 / 0 / 1 on the joiner; every one was refused in the same frame (0.00–0.03 s), the 20 s timeout never ran. 8 of the 15 were this player's press with nobody to talk to (the host's log has the same without any copy); 7 NPC-to-NPC. The host's tutorial run has 671, 634 of them one soul | (log) |
| `prepadeniNaCeste_bandit_10` had no `applied=dead` | the kill landed on the joiner (`Soul died` 45 ms after the request; one entity id throughout; no stand-in, no second body). The stale sample 12 ms later cleared `ownerReq`, so the landing branch was skipped. 4 requests, 3 `applied=dead` lines, 4 kills in the joiner's logs | (log) |
| `FAULT motion::rd` / `copy_cstr` | joiner tutorial 3,556 faults in 60 s (copy_cstr 3,345, rd 211), host tutorial 5,694, the restart run 4; `reading 0xffffffffffffffff`; onsets at a world jump (a sleep, a clock write). Read sites are never switched off, by design | (log) |
| temp-tool churn | 248 gives / 223 takes in the tutorial (99 NPCs); only 10 of 248 gives repeat a pair, 277 s apart at the nearest. The host's hand rows do flap (one villager changed hands 235 times in 1,055 s). **Every** take logged `DeleteItem(id, count) … Provided type Null` (223 of 223) | (log) |
| door flapping | the host's door history replayed through the load hold in one millisecond, 54–78 s late; no bounce loop (15 joiner asks, one answer each) | (log) |
| `Dog 'tvez_vorech' exists as a soul but the actor is gone` ×86 | **differs**: 56,604 lines in the tutorial run only, from the game's own profile layer unload to the end; engine text; a joiner-only `SetCompanion: Companion is not a valid soul` 1× | (log) |
| `mark_odd` at 12 moments | 11 distinct joiner markers (6 / 3 / 2) and 4 on the host. See §6 | (log) |

## 1. Phase 1 — the herb crash

* **Cause (inside the engine):** the avatar's `PickingHerbs` request is stopped by the agent's
  `(stop)` request as the host's minigame ends (result 3, interrupted). In both crashes the
  joiner's own herb minigame was a second old at that moment. From our side the fragment carries
  nothing: `WO143-HANDS`/`SHOT` show `obj=0000000000000000`, tags empty, alignment off. The
  fragment is a player fragment (`Cannot select bone camera while selector wasn't inited (Dude)`
  errors start right after the first full herb minigame on both machines, 243 / 177 / 6,974 in
  three logs). **(log; code-verified)**
* **Shipped:** `mp_avatar_herbs` (**off by default**; `on` brings 0.43.0's loop back). With it off
  the avatar stands where its player picks herbs; nothing else changes. `Wo143Rules.MayAlignAt`:
  only a station (grindstone, alchemy, lockpicking, smithing) can be aligned at an object, and only
  under `mp_minigame_align`. Tests: `Wo143Tests.WO153_*`, the Lua suite (A: the switch).
  **(unit; not live)**
* **Opus follow-up:** why the engine's fragment crashes when stopped next to a local herb minigame:
  the fragment's procedural clips (the camera bone selector), the MasterSlave context 5 on `Dude`
  (`DudeCharacterCameraAttachment`), and the clear that runs when the local minigame ends. A
  once-per-second probe of the avatar's mannequin during a herb pick would settle it.

## 2. Phase 2 — copies never act on their own

What the logs prove, and what was changed (every item has a test in `tools/Test-WO153Synthetic.lua`,
`Test-WO131/118/102Synthetic.lua` or the C# suites):

* **The `path=legacy` violations are the mod's own writers.** Lua unbinds a puppet from the DLL when
  the agent's heartbeat is more than 3 s stale (a join burst: "the agent runs 2,636 ms behind its
  reader"); the unbind reaches the DLL through the agent while the DLL keeps writing every frame,
  and Lua already writes the body. Three seated guests read exactly 3.582 m and then 7.050 m off the
  last Lua write: two writers, not a brain. **Changed:** after an unbind Lua waits
  `TUNE.NPC_NATIVE_UNBIND_HOLD_S` (1.5 s) before it writes the body; a DLL-side drop still hands the
  body to Lua at once. The notice is **log-only**: the on-screen toast is gone. **(unit; not live)**
* **The dog.** `korenarka_dog` (class `Dog`, LIKELY) is neither streamed nor guarded; its own tree ran
  and it took the joiner to 1 hp. **Changed:** `KCD2MP_W131PauseOnly` suspends classes in
  `KCD2MP_W131_PAUSE_ONLY_CLASSES` (`Dog`) under the guard — paused where they stand, **not hidden**
  (nothing of the host's replaces them), given back when the guard goes off. **(unit; not live; the
  class name is a LIKELY: if it is wrong the effect is nil, not harmful)**
* **Engaging this player for the host's fight.** 11 of 22 engages had `target Host`: the copy was
  put into this joiner's combat mode for a fight between the NPC and the host's own Henry.
  **Changed:** `Wo132Rules.JudgeEngage` engages only a fight that is this player's (`mine`) or a
  fight whose target is not the host. **(unit)**
* **The contexts.** `Wo151Rules.CopiesNeedingContexts`: the "own hit reactions off" contexts go only
  on a guarded copy that is in a fight (the host's NPC in combat, or held in combat here, plus 10 s).
  **(unit)**
* **The damage discard table** holds 256 attackers (`hits.cpp`), not 64: it sat at its cap in four
  of five runs against 88–229 guarded copies. **(not live)**
* **`CartMount`.** Not a copy's brain: the mod's WO-141 apply asking for a cart stance the game
  refuses (the slot's occupant is the horse itself), at 15 s for ever. **Changed:** after eight
  misses the retry settles at one a minute (`wo141::next_delay`; a new host row starts the count
  again). **(unit)**
* **Not changed (decisions, §D):** WO-147's hostile engage (the joiner's own drawn weapon near an
  enemy engages that enemy's copy); the talk/scene/guard-stop resumes; the guard's radius (220 m),
  the sweep's dialogue skip, and the class list beyond `Dog`.

## 3. Phase 3 — no dropped commands

* **The transport.** `HttpGameTransport` + `BatchQueue` (`dotnet/KcdMp.Client`): one ordered queue of
  statements, one batch in flight at a time. A batch the game does not answer within the timeout goes
  back to the **front** of the queue in order:
  * a **Level** statement (a tick, a session setter, one NPC's or ghost's latest sample: 24 named
    functions, key = function + first argument) is replaced by a newer one and never replayed
    stale (15 s time to live);
  * an **Edge** (everything else) is kept for 120 s and runs **once**: it carries
    `KCD2MP_Once("<epoch>-<n>", …)`, so a batch the game ran late and we sent again cannot run it
    twice (the table is bounded, ids are strings: this Lua's numbers are float32);
  * a **Stale** relay (the talk lines, the native scan) is dropped, not replayed;
  * the queue holds 256 statements; a state is pushed out before an edge, and every count is logged;
  * after two unanswered batches the transport **holds** (`MP-BATCH-HOLD down`): callers return at
    once, one batch is tried every second, `MP-BATCH-HOLD up after N s` resumes. This ends the 0.8 s
    waits that backed the agent up 16 s behind its reader at the GPU hang.
  `ClientConfig.BatchRetryEnabled` (default true; false = 0.43.0's drop). **(unit: 12 tests incl. a
  fake game that answers late, never, or only after N calls; not live)**
* **What rides this path.** Quest steps go over the pipe with their own retry queue, deaths over the
  pipe and idempotent in the DLL: 8 NPC-death applies landed, none failed, and the quest increments
  across the GPU hang ran 3→4 … 7→8 with no doubles. **(log)**
* **The pipe.** Exactly one loss per run, at the game's end: the reader found it, but the waiting
  caller sat out its 5 s deadline. `CombatPipe.ReadLoopAsync` now drops the connection at its exit
  (only if it still serves the current pipe), so the loss is declared at once. **(code-verified; unit
  none: needs a pipe)**
* **The conversation timeouts.** Not ours: the engine refuses a request in the same frame when a
  participant is not free; its text says "timed out". The agent's line now says whose request it was
  and that nothing waited (`Wo144Rules.CancelSoul`); other souls' cancels are counted, not printed.
  One real small defect remains: the primary pre-request route (`BeforeTalk`, the wrapped entry
  points) never fired for a player talk in 8 of 8, so the late fallback decided the one failure
  (request 254). **Opus follow-up.**
* **The equipped-set read** failed on stalls only: the line now says so, after three misses.
* **The yaw loop** (two REST round trips ~12–16×/s for the agent's whole life) now starts only in HTTP
  mode; in log-tail mode its output was unused.

## 4. Phase 4 — one body per NPC

* **The race (proven by the log order and the code).** t=249.250 `MP-OWNERDEATH … request=1`; the
  agent's `ApplyDeath` landed within 45 ms (`Soul died`); t=249.295 a sample sent before the death
  said alive (hp 1.8) and `KCD2MP_OwnerDeathCheck` cleared `ownerReq` on that one sample; the landing
  branch needs `ownerReq`, so `applied=dead` and the corpse placement never ran. **(log, code)**
* **Changed:** (a) the agent drops an ALIVE sample of an NPC for 10 s after a death was applied from a
  peer (`Wo153DeathHeld`); (b) the mod keeps the dead bit for 10 s after an applied death, and only
  while this world's copy really reads dead (a host that is truly alive again is never fought);
  (c) the request is no longer cleared by one stale sample, and the landing no longer needs the
  current stream bit; (d) a request that is still unresolved says `UNRESOLVED` at the 8th request and
  once a minute after, and goes on asking every 20 s; (e) `KCD2MP_W131StandIn` refuses to spawn over
  a body that already answers to the name. **(unit: Lua suite A–A4, WO-131 i2)**
* **Not built:** removing the stand-in by entity id; the dead-resync stand-in that no puppet tick shows
  or removes ("leaks until the guard goes off", LIKELY, not seen in these logs).

## 5. Phase 5

1. **Faults.** `motion::rd` / `copy_cstr` read through the avatar body's cached combat actor
   (`Body::ca`, set once), every frame, after the engine freed or rebuilt it. **Changed:**
   `fault::plausible_address` (null page, non-canonical: refused before the read, no fault),
   `apply_combat` re-reads the combat actor through its owner each frame and resets the applied
   state when it changed, a failed read backs off for a second. Native tests: +8.
   **(unit; not live)** *Opus follow-up:* who writes `C_Actor+0x300` / `C_CombatActor+0x2F0` and what
   frees it on a time skip or streaming; the guard still names no caller.
2. **Temporary tools.** Give once, take once: a tool no row asks for is kept 90 s
   (`Wo143Rules.TempHoldMs`), a row that asks again cancels the take, no second give for a class that
   is still there; `DeleteItem` now gets its count. **(unit)** *Not changed:* the native 10 s ask
   throttle and `g_needAsked`.
3. **Doors.** The load hold keeps the newest `DoorState` per door (`Wo136DoorKey`); the host's door
   history no longer replays as open/shut flaps. No echo loop exists. **(unit)**
4. **The dog `tvez_vorech`.** Engine noise (`C_Actor::GetCompanion`, EntityModule, Modding Tools
   build only) after the game's own profile layer unload, 56,604 lines at ~30/s; LIKELY from the
   joiner's brought Henry (first join: the splice keeps the joiner's companion link; later joins show
   0). **Not ours; not changed.** *Opus follow-up:* where the splice stores that link.

## 6. The `mark_odd` moments (11 joiner, 4 host)

Read by an investigator against the logs; none sits at a dropped batch, a fault or a connection loss.
* **Known issues:** T4 (copies fighting, the dog, skirmish, 97 animation-queue overflows, the
  joiner's death and respawn), R3 (a brawl: skirmish, KO divergence), C1 (a road ambush: horse/cart).
* **New findings (not fixed):**
  1. **Quest steps with no port on the joiner** at T3, T5, T6, R2: `treti_den.sitDown` (the joiner
     stays seated after a 125 s sleep fader and `mp_unstuck` does not free him), the sack counters
     (`pickedSacks 1 / 2`, `depositedSacks 2 / 1`). The WO-97/99.5 port line.
  2. **Look-pose animation-queue overflow storms**, one NPC copy at a time, 100+ lines in 5–8 s
     (`tvez_bozena`, `ttkc_man_34`, `ttkc_man_30`): the engine's own look layers (the mod skips the
     look write for puppets).
  3. **A spurious trespass level 255 → 3** at the hut after a scene teleports the player into the
     property volume (`GameBridge.Wo139.cs:170`).
  4. **A host-only dialog-request storm**: 634 requests from one soul in 29 s.
  5. **Pub scene pick-up/place event errors** (84) from copies' one-shots with no object.
  6. A joiner lock-on drop loop on a slope (66 in 9 s: the engine's, probably vanilla); the joiner's
     sleep fader of 125 s against the host's 0.3 s.

## D. Decisions made unattended

1. **The version, and the soak waived.** `VERSION` was blank, so the first pass was commits only. The
   maintainer then chose `0.44.0` and, after being told that `Build-Installer.ps1` refuses without a
   passing soak for this code, instructed "build the new installer, ignore the soak". The installer
   was built that way: **the frame-rate soak did not run for this code**, and no comparison against
   the game without the mod exists. Standing rule 5 (an unexplained frame-rate drop blocks the
   installer) is about a drop that was seen; none was seen and none was measured. The only evidence
   is by reading: the DLL changes are per-frame reads that got cheaper (a pointer check before a
   guarded read; one fewer read of a stale pointer) and add no new per-frame work. The waiver is on
   the record in `tools\Build-Installer.ps1 -SoakWaiver` and `release\SOAK-WAIVED-0.44.0.txt`
   (beside the installer, not in git). **The first live session should read the `FRAME` line of
   `kcdmp-native.log` (fps, `ours_us_mean`) against the 0.43.0 numbers (70.9-71.4 FPS, 0.69-0.76
   ms) and run the soak then.**
2. **No live runs.** The game was not started. Every fix is verified by unit and synthetic tests
   against the real `kdcmp.lua` and the real C# classes; the live behaviour is on the two-player
   checklist. *Why:* the machine's game launch needs the maintainer's session (focus, saves), and
   no throwaway save was prepared.
3. **`mp_avatar_herbs` ships OFF**, the one piece of WO-153 that does. The "new mechanisms ship on" rule
   is about additions; this removes a path both field crashes ended on, and a crash costs the
   joiner's inventory. Flip it on in a controlled test.
4. **Batch retry ships ON** (`BatchRetryEnabled`), as a fix of a loss that is proven.
5. **WO-147's hostile engage is unchanged** (the joiner's own drawn weapon near an enemy): it is the
   joiner's act, and "nothing proven by WO-131 to WO-151 may regress". The follow-up is to gate it on the
   host NPC's combat or the joiner's first blow.
6. **The WO-150 commits are not pushed** (`origin/main` is two commits behind them); the WO-153 commits
   are not pushed either. The tag `v0.43.0` (on `b28ac78`, lightweight) is pushed.
7. **Quest NPCs** were not touched: no special case anywhere in these changes.
8. **The dog class** is a LIKELY from the engine's script directory, not a log line; the pause list is a
   table, one word to extend or empty.

## F. Follow-ups (what each needs)

1. **Opus — the herb fragment** (§1).
2. **Opus — `Body::ca` lifetime** (§5.1).
3. **Opus — the skirmish manager** ("Skirmish is getting armed because of …": what "active" means, who
   is pulled in, the target pick) and the data-verified script contexts that look like levers
   (`combat_alwaysWithdrawSkirmish`, `combat_doNotJoinSkirmishesWithoutPlayer`,
   `combat_disabledAsTarget`, `crime_animal_*`, `dog_meleeCombat`): their effect needs a live probe through
   `sctx::set_soul_context`; and why the dog chose `Dude`.
4. **Opus — `CLivingEntity::Action(action_move) … dir is invalid`** (172 in the restart run, only on four
   seated guests): the caller; and what relocates suspended bodies on a clock jump (up to 128 m).
5. **Opus/live — the game's console server:** how it queues a request that timed out (why about a third
   ran late), and the trigger of the 24.9 s GPU hang and the one 1.4 s hitch per run.
6. **Live — the talk wraps** (`BeforeTalk` never fired for a player talk).
7. **Quest ports** for the steps in §6 (WO-97/99.5).
8. **A hook on the DLL pipe's connect** that clears the agent's DLL-state caches (`_nativeBound`, the
   WO-138 hold edge): latent, never seen.
9. **Horse detach vs the cart stance** (`KCD2MP_NpcDetach` clears `Stance` on a puppet horse; WO-141 then
   cannot restore it).
10. **Known and out of scope here:** the cutscene props at the world's origin (61,128 `Item shieldCuman…
    was moved to [0,0,0]!` in one marker window, 73,000 in the run: Part B), the tutorial respawn spot,
    lip sync (the Modding Tools build lacks the data).

## B. The installer

Built by `tools\Build-Installer.ps1 -SoakWaiver "<the maintainer's instruction>"` inside a fresh
clone of the repository (at `a5da586`), so nothing from the working folder is in it:
`release\KingdomComeTogether-Setup-0.44.0.exe`, 97.3 MB, SHA-256
`13f8fb0b9580128a524444770979356bb1986992f40c7938ef16ec0620b2bb28`, in the git-ignored release
folder with `SOAK-WAIVED-0.44.0.txt`. No GitHub release. **The soak did not run** (§D 1).
The payload smoke, the install markers and the privacy sweep passed on the final payload; the sweep
found one real leak on the way (the maintainer's Windows user name in the NativeAOT `KcdMpSetup.exe`,
from the native linker's PDB path) and it is fixed (`/PDBALTPATH`) and rebuilt. Details:
`docs/WO-153-progress.md`, work log item 9.

## G. Gates

| gate | result |
|---|---|
| every synthetic suite (`Test-*Synthetic.ps1`, 43) | 0 failed |
| the three static checks | exit 0 |
| agent tests | 852 passed |
| relay tests | 62 passed |
| native tests | 377 passed |
| the frame-rate soak | **waived by the maintainer; not run** (§D 1) |
